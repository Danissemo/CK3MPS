using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;

const int MaxPeers = 8;
const int MaxPayloadBytes = 128 * 1024;
const int RoomTtlMinutes = 30;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("relay", limiter =>
    {
        limiter.PermitLimit = 60;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

var app = builder.Build();
app.UseHttpsRedirection();
app.UseRateLimiter();

var rooms = new ConcurrentDictionary<string, RoomState>(StringComparer.OrdinalIgnoreCase);

app.MapPost("/v1/rooms", () =>
{
    string code;
    do code = CreateCode(); while (rooms.ContainsKey(code));

    var secretBytes = RandomNumberGenerator.GetBytes(32);
    var room = new RoomState(code, SHA256.HashData(secretBytes), DateTimeOffset.UtcNow.AddMinutes(RoomTtlMinutes));
    rooms[code] = room;
    return Results.Ok(new CreateRoomResponse(code, Base64Url(secretBytes), room.ExpiresUtc, MaxPeers));
}).RequireRateLimiting("relay");

app.MapPost("/v1/rooms/{code}/join", async (string code, HttpRequest request) =>
{
    if (!TryGetLiveRoom(rooms, code, out var room, out var error)) return error;
    var body = await ReadBodyAsync<JoinRequest>(request);
    if (body is null || string.IsNullOrWhiteSpace(body.Secret) || string.IsNullOrWhiteSpace(body.PlayerLabel))
        return Results.BadRequest(new ErrorResponse("invalid_request"));
    if (!SecretMatches(room!, body.Secret)) return Results.Unauthorized();

    lock (room!.Sync)
    {
        if (room.Closed) return Results.StatusCode(StatusCodes.Status410Gone);
        if (room.Participants.Count >= MaxPeers) return Results.Conflict(new ErrorResponse("room_full"));
        var id = Base64Url(RandomNumberGenerator.GetBytes(18));
        room.Participants[id] = new ParticipantState(id, body.PlayerLabel.Trim(), 0, new HashSet<string>(StringComparer.Ordinal));
        return Results.Ok(new JoinResponse(id, room.ExpiresUtc));
    }
}).RequireRateLimiting("relay");

app.MapPost("/v1/rooms/{code}/messages", async (string code, HttpRequest request) =>
{
    if (!TryGetLiveRoom(rooms, code, out var room, out var error)) return error;
    if (request.ContentLength is > MaxPayloadBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    var body = await ReadBodyAsync<MessageRequest>(request);
    if (body is null || body.Payload is null || body.Payload.Length > MaxPayloadBytes)
        return Results.BadRequest(new ErrorResponse("invalid_payload"));
    if (!TryAuthenticate(room!, code, body, out var participant, out var authError)) return authError;

    lock (room!.Sync)
    {
        if (room.Closed) return Results.StatusCode(StatusCodes.Status410Gone);
        if (body.Sequence <= participant!.LastSequence) return Results.Conflict(new ErrorResponse("replay_sequence"));
        if (!participant.Nonces.Add(body.Nonce)) return Results.Conflict(new ErrorResponse("replay_nonce"));
        if (participant.Nonces.Count > 256) participant.Nonces.Clear();
        participant.LastSequence = body.Sequence;
        room.Messages[participant.Id] = new RelayMessage(participant.Id, participant.PlayerLabel, body.Sequence, body.Nonce, body.Payload, DateTimeOffset.UtcNow);
    }
    return Results.Accepted();
}).RequireRateLimiting("relay");

app.MapPost("/v1/rooms/{code}/snapshot", async (string code, HttpRequest request) =>
{
    if (!TryGetLiveRoom(rooms, code, out var room, out var error)) return error;
    var body = await ReadBodyAsync<AuthRequest>(request);
    if (body is null || !TryAuthenticate(room!, code, body, out _, out var authError)) return authError;
    lock (room!.Sync)
    {
        return Results.Ok(new SnapshotResponse(room.ExpiresUtc, room.Participants.Count, room.Messages.Values.OrderBy(x => x.PlayerLabel).ToArray()));
    }
}).RequireRateLimiting("relay");

app.MapPost("/v1/rooms/{code}/close", async (string code, HttpRequest request) =>
{
    if (!TryGetLiveRoom(rooms, code, out var room, out var error)) return error;
    var body = await ReadBodyAsync<CloseRoomRequest>(request);
    if (body is null || !SecretMatches(room!, body.Secret)) return Results.Unauthorized();
    lock (room!.Sync) room.Closed = true;
    rooms.TryRemove(code, out _);
    return Results.NoContent();
}).RequireRateLimiting("relay");

var cleanupTimer = new PeriodicTimer(TimeSpan.FromMinutes(1));
_ = Task.Run(async () =>
{
    while (await cleanupTimer.WaitForNextTickAsync())
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in rooms)
            if (pair.Value.Closed || pair.Value.ExpiresUtc <= now) rooms.TryRemove(pair.Key, out _);
    }
});

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.Run();

static bool TryGetLiveRoom(ConcurrentDictionary<string, RoomState> rooms, string code, out RoomState? room, out IResult error)
{
    room = null;
    if (string.IsNullOrWhiteSpace(code) || !rooms.TryGetValue(code, out room))
    {
        error = Results.NotFound(new ErrorResponse("room_not_found"));
        return false;
    }
    if (room.Closed || room.ExpiresUtc <= DateTimeOffset.UtcNow)
    {
        rooms.TryRemove(code, out _);
        error = Results.StatusCode(StatusCodes.Status410Gone);
        return false;
    }
    error = Results.Ok();
    return true;
}

static bool TryAuthenticate(RoomState room, string code, IAuthenticatedRequest body, out ParticipantState? participant, out IResult error)
{
    participant = null;
    lock (room.Sync)
    {
        if (!room.Participants.TryGetValue(body.ParticipantId ?? string.Empty, out participant))
        {
            error = Results.Unauthorized();
            return false;
        }
    }
    if (string.IsNullOrWhiteSpace(body.Nonce) || body.Nonce.Length > 96 || body.Sequence <= 0)
    {
        error = Results.BadRequest(new ErrorResponse("invalid_auth_fields"));
        return false;
    }
    var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(body.PayloadForSignature ?? string.Empty));
    var canonical = $"{code}\n{body.ParticipantId}\n{body.Sequence}\n{body.Nonce}\n{Convert.ToHexString(payloadHash)}";
    using var hmac = new HMACSHA256(room.SecretHash);
    var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
    byte[] provided;
    try { provided = Base64UrlDecode(body.Signature ?? string.Empty); }
    catch { error = Results.Unauthorized(); return false; }
    if (!CryptographicOperations.FixedTimeEquals(expected, provided))
    {
        error = Results.Unauthorized();
        return false;
    }
    error = Results.Ok();
    return true;
}

static bool SecretMatches(RoomState room, string secret)
{
    try
    {
        var supplied = SHA256.HashData(Base64UrlDecode(secret));
        return CryptographicOperations.FixedTimeEquals(room.SecretHash, supplied);
    }
    catch { return false; }
}

static async Task<T?> ReadBodyAsync<T>(HttpRequest request)
{
    try { return await JsonSerializer.DeserializeAsync<T>(request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
    catch (JsonException) { return default; }
}

static string CreateCode()
{
    const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    var bytes = RandomNumberGenerator.GetBytes(7);
    return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
}

static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
static byte[] Base64UrlDecode(string value)
{
    value = value.Replace('-', '+').Replace('_', '/');
    value = value.PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');
    return Convert.FromBase64String(value);
}

sealed class RoomState(string code, byte[] secretHash, DateTimeOffset expiresUtc)
{
    public object Sync { get; } = new();
    public string Code { get; } = code;
    public byte[] SecretHash { get; } = secretHash;
    public DateTimeOffset ExpiresUtc { get; } = expiresUtc;
    public bool Closed { get; set; }
    public Dictionary<string, ParticipantState> Participants { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, RelayMessage> Messages { get; } = new(StringComparer.Ordinal);
}

sealed class ParticipantState(string id, string playerLabel, long lastSequence, HashSet<string> nonces)
{
    public string Id { get; } = id;
    public string PlayerLabel { get; } = playerLabel;
    public long LastSequence { get; set; } = lastSequence;
    public HashSet<string> Nonces { get; } = nonces;
}

interface IAuthenticatedRequest
{
    string? ParticipantId { get; }
    long Sequence { get; }
    string? Nonce { get; }
    string? Signature { get; }
    string? PayloadForSignature { get; }
}

sealed record CreateRoomResponse(string Code, string Secret, DateTimeOffset ExpiresUtc, int MaxPeers);
sealed record JoinRequest(string Secret, string PlayerLabel);
sealed record JoinResponse(string ParticipantId, DateTimeOffset ExpiresUtc);
sealed record AuthRequest(string? ParticipantId, long Sequence, string? Nonce, string? Signature) : IAuthenticatedRequest
{
    public string? PayloadForSignature => string.Empty;
}
sealed record MessageRequest(string? ParticipantId, long Sequence, string? Nonce, string? Signature, string? Payload) : IAuthenticatedRequest
{
    public string? PayloadForSignature => Payload;
}
sealed record CloseRoomRequest(string Secret);
sealed record RelayMessage(string ParticipantId, string PlayerLabel, long Sequence, string Nonce, string Payload, DateTimeOffset ReceivedUtc);
sealed record SnapshotResponse(DateTimeOffset ExpiresUtc, int ParticipantCount, RelayMessage[] Messages);
sealed record ErrorResponse(string Error);
