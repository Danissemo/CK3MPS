using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace CK3MPS.ParityRelay.Client;

public sealed class OnlineParityRelayClient : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private long _sequence;

    public OnlineParityRelayClient(Uri baseAddress, TimeSpan timeout, HttpClient? httpClient = null)
    {
        if (baseAddress.Scheme != Uri.UriSchemeHttps && !baseAddress.IsLoopback)
            throw new ArgumentException("Online parity relay requires HTTPS.", nameof(baseAddress));
        _ownsClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _http.BaseAddress = baseAddress;
        _http.Timeout = timeout;
    }

    public async Task<CreatedRoom> CreateRoomAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync("v1/rooms", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<CreatedRoom>(cancellationToken: cancellationToken))
            ?? throw new InvalidDataException("Relay returned an empty create-room response.");
    }

    public async Task<JoinedRoom> JoinRoomAsync(string code, string secret, string playerLabel, CancellationToken cancellationToken)
    {
        ValidateCode(code);
        using var response = await _http.PostAsJsonAsync($"v1/rooms/{code}/join", new { secret, playerLabel }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<JoinedRoom>(cancellationToken: cancellationToken))
            ?? throw new InvalidDataException("Relay returned an empty join response.");
    }

    public async Task SendAsync(RoomCredentials room, string payload, CancellationToken cancellationToken)
    {
        ValidateCode(room.Code);
        if (Encoding.UTF8.GetByteCount(payload) > 128 * 1024)
            throw new InvalidDataException("Parity payload exceeds 128 KiB.");
        var envelope = Sign(room, payload);
        using var response = await _http.PostAsJsonAsync($"v1/rooms/{room.Code}/messages", envelope, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<RoomSnapshot> GetSnapshotAsync(RoomCredentials room, CancellationToken cancellationToken)
    {
        ValidateCode(room.Code);
        var envelope = Sign(room, string.Empty);
        using var response = await _http.PostAsJsonAsync($"v1/rooms/{room.Code}/snapshot", new
        {
            envelope.ParticipantId,
            envelope.Sequence,
            envelope.Nonce,
            envelope.Signature
        }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<RoomSnapshot>(cancellationToken: cancellationToken))
            ?? throw new InvalidDataException("Relay returned an empty snapshot.");
    }

    public async Task CloseRoomAsync(string code, string secret, CancellationToken cancellationToken)
    {
        ValidateCode(code);
        using var response = await _http.PostAsJsonAsync($"v1/rooms/{code}/close", new { secret }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private SignedEnvelope Sign(RoomCredentials room, string payload)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var nonce = Base64Url(RandomNumberGenerator.GetBytes(18));
        var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        var canonical = $"{room.Code}\n{room.ParticipantId}\n{sequence}\n{nonce}\n{Convert.ToHexString(payloadHash)}";
        var secretHash = SHA256.HashData(Base64UrlDecode(room.Secret));
        using var hmac = new HMACSHA256(secretHash);
        var signature = Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
        return new SignedEnvelope(room.ParticipantId, sequence, nonce, signature, payload);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new OnlineParityRelayException(response.StatusCode, Redact(detail));
    }

    private static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Relay rejected the request.";
        return value.Length <= 512 ? value : value[..512];
    }

    private static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length is < 6 or > 12 || code.Any(ch => !char.IsAsciiLetterOrDigit(ch)))
            throw new ArgumentException("Invalid room code.", nameof(code));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Base64UrlDecode(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        value = value.PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');
        return Convert.FromBase64String(value);
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsClient) _http.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed record CreatedRoom(string Code, string Secret, DateTimeOffset ExpiresUtc, int MaxPeers);
public sealed record JoinedRoom(string ParticipantId, DateTimeOffset ExpiresUtc);
public sealed record RoomCredentials(string Code, string Secret, string ParticipantId);
public sealed record SignedEnvelope(string ParticipantId, long Sequence, string Nonce, string Signature, string Payload);
public sealed record RelayMessage(string ParticipantId, string PlayerLabel, long Sequence, string Nonce, string Payload, DateTimeOffset ReceivedUtc);
public sealed record RoomSnapshot(DateTimeOffset ExpiresUtc, int ParticipantCount, RelayMessage[] Messages);

public sealed class OnlineParityRelayException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
