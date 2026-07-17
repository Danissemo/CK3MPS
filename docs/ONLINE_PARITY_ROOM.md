# Online Parity Room

Online Parity Room replaces the LAN listener with an outbound-only HTTPS relay. CK3MPS clients never accept public inbound connections and no port forwarding is required.

## Security model

- TLS is mandatory in production. Terminate TLS at a trusted reverse proxy or directly in Kestrel.
- A room has a 7-character random code and a separate 256-bit secret.
- The secret is returned only in the JSON body of room creation. It must never be placed in a URL, query string, access log, analytics event, or crash report.
- The relay stores only SHA-256(secret), not the original secret.
- Each authenticated request contains participant id, strictly increasing sequence, random nonce, and HMAC-SHA256 signature.
- Canonical signature input is `code\nparticipantId\nsequence\nnonce\nSHA256(payload)`.
- The HMAC key is `SHA256(roomSecretBytes)`, matching the server-side stored key.
- Duplicate/non-increasing sequences and reused nonces are rejected.
- Rooms expire after 30 minutes, allow at most 8 participants, support explicit close, and are removed by background cleanup.
- Request rate is limited to 60 requests/minute per limiter partition. Production deployment should additionally rate-limit by source IP at the reverse proxy.
- Message bodies are limited to 128 KiB. CK3MPS should send only parity manifest fields and consented OOS summaries. Save files and personal filesystem paths are prohibited.

## API

### Create room

`POST /v1/rooms`

Returns `{ code, secret, expiresUtc, maxPeers }`. Show the code and secret separately in the UI. Clipboard/share text must not embed the secret in a URL.

### Join

`POST /v1/rooms/{code}/join`

Body: `{ "secret": "...", "playerLabel": "..." }`

Returns participant id and expiry. Wrong code returns 404, wrong secret returns 401, expired rooms return 410, full rooms return 409.

### Submit parity payload

`POST /v1/rooms/{code}/messages`

Body contains participant id, sequence, nonce, signature, and payload. The payload should be the existing locally signed CK3MPS parity envelope with sensitive paths removed.

### Read snapshot

`POST /v1/rooms/{code}/snapshot`

Uses the same authenticated envelope without a payload. Returns participant count and latest message per participant.

### Close room

`POST /v1/rooms/{code}/close`

Body: `{ "secret": "..." }`.

## Client integration contract

1. Online is the default mode in the Parity Room UI.
2. Host calls create, joins its own room, periodically submits local parity, polls snapshot, and can close the room.
3. Peer enters room code and secret, joins, submits parity, and polls snapshot.
4. All requests use a bounded timeout and cancellation token.
5. Logs may include room code, HTTP status, request duration, participant count, and expiry. Logs must redact secret, HMAC, nonce, raw payload, save content, and personal paths.
6. Existing local payload signature verification remains mandatory after relay transport validation.
7. Legacy LAN code must not be reachable from the default UI. If retained, expose it only under an explicitly labelled Advanced/Legacy mode.

## Deployment

Required environment and platform controls:

- .NET 8 runtime.
- Public HTTPS endpoint, for example `https://parity.example.com`.
- Reverse proxy with TLS 1.2+, request-body limit at or below 128 KiB, connection/request timeouts, IP rate limiting, and query-string logging disabled.
- At least two instances require shared room storage (Redis recommended) and a distributed replay/sequence store. The included implementation is intentionally single-instance/in-memory.
- Do not enable request-body logging.
- Health check: `GET /healthz`.

Example:

```bash
dotnet run --project relay/CK3MPS.ParityRelay/CK3MPS.ParityRelay.csproj --urls http://127.0.0.1:5088
```

Place a TLS reverse proxy in front of the local Kestrel endpoint for production.

## Required test matrix

Client: wrong code, wrong secret, expired room, replay, tampered payload, oversized payload, peer limit, reconnect, slow relay, unavailable relay, cancellation, and log redaction.

Relay/integration: TTL cleanup, rate limiting, room isolation, concurrent rooms, authentication, replay rejection, payload validation, and graceful shutdown.
