# HTTP PBp Transport Contract v0

This document defines the HTTP contract for Play‑by‑Post transport so it matches `ITurnTransport` and `FileTurnTransport` semantics.

Scope: 2‑player PBp, localhost only, no auth, no retries.

## Interface Alignment (Unity)

- `SubmitTurn(gameId, turnNumber, json)` → `done(ok, err)`
- `TryFetchNextTurn(gameId, afterTurnNumber)` → `done(ok, err, fetchedTurnNumber, fetchedJson)`

Canonical errors (Unity expects these exact strings):

- `INVALID_GAME_ID`
- `INVALID_TURN`
- `EMPTY_JSON`
- `UNAVAILABLE`
- `CONFLICT`
- `IO_ERROR`
- `NO_TURN`

## Endpoints

Keep current paths:

- `POST /pbp/turn` (submit)
- `GET /pbp/turn/next` (fetch next)
- `GET /healthz` (availability)

All requests and responses use JSON, except where explicitly noted below.

## Submit: POST /pbp/turn

Request JSON:

```json
{
  "gameId": "string",
  "seq": 123,
  "json": "string"
}
```

Rules:

- `gameId` must be non‑empty.
- `seq` must be a positive integer (> 0).
- `json` must be non‑empty string.

Success response JSON (HTTP 200):

```json
{ "ok": true, "alreadyHad": false }
```

Conflict (same seq, different payload):

```json
{ "ok": false, "error": "SEQ_CONFLICT" }
```

Invalid input:

```json
{ "ok": false, "error": "INVALID_INPUT" }
```

Server error:

```json
{ "ok": false, "error": "SERVER_ERROR" }
```

Conflict semantics:

- If the same `gameId` + `seq` already exists:
  - If stored payload bytes match `json` exactly → treat as success (`ok: true`, `alreadyHad: true`).
  - If different → treat as conflict (`SEQ_CONFLICT`).

## Fetch: GET /pbp/turn/next

Query params:

- `gameId` (string, required)
- `after` (int, required; may be `-1` to request the first turn)

Success response JSON (HTTP 200):

```json
{ "seq": 124, "json": "{...}" }
```

No turn available:

- The server may respond with JSON:

```json
{ "ok": false, "error": "NO_TURN" }
```

- Or (legacy) with plain‑text body `NO_TURN` (HTTP 200).

Invalid input:

```json
{ "ok": false, "error": "INVALID_INPUT" }
```

Server error:

```json
{ "ok": false, "error": "SERVER_ERROR" }
```

## Availability: GET /healthz

Response JSON (HTTP 200):

```json
{ "ok": true }
```

## Mapping to Canonical Transport Errors

The client must map server errors/status to the canonical transport errors:

Submit mapping (`SubmitTurn`):

- HTTP 200 + `{ ok: true }` → `done(true, null)`
- HTTP 200 + `{ ok: true, alreadyHad: true }` → `done(true, null)` (idempotent)
- HTTP 409 + `{ error: "SEQ_CONFLICT" }` → `done(false, "CONFLICT")`
- HTTP 400 + `{ error: "INVALID_INPUT" }` → `done(false, "INVALID_GAME_ID" | "INVALID_TURN" | "EMPTY_JSON")`
  - Prefer specific validation on the client side before request to choose the correct canonical error.
- HTTP 500 + `{ error: "SERVER_ERROR" }` → `done(false, "IO_ERROR")`
- Any network failure / timeout / parse error → `done(false, "IO_ERROR")`
- If `/healthz` not reachable or transport disabled → `done(false, "UNAVAILABLE")` (if used)

Fetch mapping (`TryFetchNextTurn`):

- HTTP 200 + `{ seq, json }` → `done(true, null, seq, json)`
- HTTP 200 + `{ error: "NO_TURN" }` → `done(false, "NO_TURN", 0, null)`
- HTTP 200 + plain‑text `NO_TURN` → `done(false, "NO_TURN", 0, null)`
- HTTP 400 + `{ error: "INVALID_INPUT" }` → `done(false, "INVALID_GAME_ID" | "INVALID_TURN", 0, null)`
  - Prefer client‑side validation to map correctly.
- HTTP 500 + `{ error: "SERVER_ERROR" }` → `done(false, "IO_ERROR", 0, null)`
- Any network failure / timeout / parse error → `done(false, "IO_ERROR", 0, null)`
- If `/healthz` not reachable or transport disabled → `done(false, "UNAVAILABLE", 0, null)` (if used)

## Turn Numbering & Sequence

- Unity transport sequences use `ComputeTransportSeq`: `seq = turnNumber * 2 + (isPlayerTurn ? 0 : 1)`.
- The HTTP server must accept and store this `seq` as‑is.
- Fetch should return the smallest `seq` strictly greater than `after`.

## Notes

- `NO_TURN` must map to `done(false, "NO_TURN", 0, null)` and should not be treated as an error in polling.
- This contract preserves `FileTurnTransport` behavior with HTTP replacing file I/O.
