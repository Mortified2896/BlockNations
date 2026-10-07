# HTTP PBp Transport Contract

This reference describes the current `server.js` relay and `HttpTurnTransport` client. `npm start` selects this server. The older `server.prod.js` is not equivalent.

Last reviewed: 2026-10-07. Snapshot protocol is **5** with migration from **4**; see [compatibility policy](PBp-Compatibility.md). The HTTP API does not negotiate a separate transport version.

## Authentication and limits

Protected PBp requests require `X-BlockNations-Api-Key` matching the server's `PBP_SHARED_SECRET`. Missing/invalid client credentials or an unset server secret return HTTP 401 with `{"ok":false,"error":"UNAUTHORIZED"}`.

Public health endpoints:

- `GET /healthz` → HTTP 200, `{"ok":true}`.
- `GET /health` → HTTP 200, plain text `ok`.

Health success does not establish authentication or snapshot compatibility. See [local setup](Local_Server_Setup.md) and [deployment/platform provisioning](VPS_Deploy_MVP.md).

Protected-route rate limits are separate buckets per route/client IP:

| Route | Requests per 60 seconds |
| --- | ---: |
| `GET /pbp/turn/next` | 60 |
| `POST /pbp/turn` | 20 |
| `POST /pbp/turn/status` | 30 |
| `POST /pbp/game/claim` | 20 |

Exceeded limits return HTTP 429, `{"ok":false,"error":"RATE_LIMITED"}`, and `Retry-After`. Buckets are process-local. Behind a proxy/tunnel, verify the IP seen by Express; current source does not enable `trust proxy`.

Requests use JSON bodies except GET query parameters. The JSON request-body parser limit is `2mb`; the submitted snapshot string additionally has a 2,000,000-byte UTF-8 cap. Game IDs must be nonempty, unpadded strings of at most 128 characters. Sequence values are safe integers with at most 12 digits.

## Submit a turn

`POST /pbp/turn`

```json
{"gameId":"example-game","seq":4,"json":"<serialized snapshot JSON>"}
```

- `seq` must be positive; `json` must be a nonempty string.
- New stored turn: HTTP 200, `{"ok":true}`.
- Identical already-stored payload: HTTP 200, `{"ok":true,"alreadyHad":true}`.
- Different already-stored payload for the same game/sequence: HTTP 409, `{"ok":false,"error":"SEQ_CONFLICT"}`.
- Invalid input/JSON/body size: HTTP 400, `{"ok":false,"error":"INVALID_INPUT"}`.
- Storage failure: HTTP 500, `{"ok":false,"error":"SERVER_ERROR"}`.

The relay stores the client sequence and snapshot string; client load gates establish gameplay compatibility.

**Known correctness gap:** concurrent different submissions can both succeed because the current file rename can overwrite the competing turn. The conflict/idempotency behavior above is reliable for the checked sequential cases, not a concurrency guarantee. [Follow-up](Development_Followups.md) requires an atomic fix before renewed online playtesting.

## Fetch the next stored turn

`GET /pbp/turn/next?gameId=example-game&after=3`

`after` is an integer at least -1. The relay returns the smallest stored sequence strictly greater than `after`; gaps are allowed.

- Found: HTTP 200, `{"seq":4,"json":"<serialized snapshot JSON>"}`.
- No next turn or unknown game: HTTP 200, plain text `NO_TURN`.
- Invalid query: HTTP 400, `INVALID_INPUT`.
- Storage failure/empty stored file: HTTP 500, `SERVER_ERROR`.

The client also accepts a JSON `NO_TURN` error at HTTP 200. No turn is normal polling state.

## Claim an available seat

`POST /pbp/game/claim`

```json
{"gameId":"example-game","playerId":"example-player","typedDisplayName":"Recognizable name"}
```

`playerId` must trim to a nonempty string of at most 128 characters. Optional `typedDisplayName` must be a string when provided; the server trims it. The client applies its own recognizable-name entry rule.

- Claimed: HTTP 200, `{"ok":true,"seatIndex":1,"alreadyClaimed":false}`.
- Existing player ID claim: HTTP 200, same seat with `alreadyClaimed:true`.
- No stored snapshot: HTTP 409, `{"ok":false,"error":"NO_TURN"}`.
- No available unclaimed seat: HTTP 409, `{"ok":false,"error":"GAME_FULL"}`.
- Invalid input or storage failure: HTTP 400 `INVALID_INPUT` or HTTP 500 `SERVER_ERROR`.

The server reads the latest snapshot, imports known claims, and reserves the first eligible unclaimed seat in `claims.json`. Claims are serialized per game within a single server process. Both relay and Unity normalize seat count to 2–4. Claims do not implement teams, arbitrary replacement players, or a full join/leave policy.

## Query batched status

`POST /pbp/turn/status`

```json
{"games":[{"gameId":"example-game","knownSeq":3}]}
```

The batch must contain 1–50 entries. Each `knownSeq` must be a safe integer at least -1, with at most 12 digits when nonnegative.

```json
{
  "ok": true,
  "games": [{
    "gameId": "example-game",
    "knownSeq": 3,
    "hasAnyTurn": true,
    "latestSeq": 4,
    "nextSeqAfterKnown": 4,
    "hasNewerThanKnown": true,
    "turnSeat": 0
  }]
}
```

An unknown game yields `hasAnyTurn:false`, zero sequence fields, and `turnSeat:-1`. Status describes transport progress and the latest snapshot's turn seat; it does not supply viewer POV or fresh opponent/team display metadata.

Invalid batches return HTTP 400 `INVALID_INPUT`; snapshot/storage scan failures return HTTP 500 `SERVER_ERROR`.

## Sequences and Unity transport interface

The existing `ITurnTransport` parameter names are `turnNumber` and `afterTurnNumber`, but the PBp caller passes **transport sequence values**, not round numbers.

For current snapshots, `ComputeTransportSeq(GameSave)` uses a positive explicit `transportSeq` when present. The fallback for current seat-based snapshots is:

```text
seq = roundTurn * seatCount + currentTurnSeatIndex
```

A round advances when turn progression wraps. Eliminated/resigned seats can be skipped, so the next stored sequence need not equal the last sequence plus one. Legacy two-seat fallback maps the old `isPlayerTurn` bridge to seats 0/1. Do not use that boolean as viewer POV or as a general multiplayer/team model.

## Current client error mapping

For submit/fetch, `HttpTurnTransport` currently maps:

| Result | Callback error |
| --- | --- |
| Valid success | `null` |
| Invalid local game/sequence/empty submit payload | `INVALID_GAME_ID`, `INVALID_TURN`, or `EMPTY_JSON` |
| No configured transport URL | `UNAVAILABLE` |
| HTTP 401 | `UNAUTHORIZED` |
| Submit HTTP 409 with `SEQ_CONFLICT` | `CONFLICT` |
| Fetch HTTP 200 with `NO_TURN` | `NO_TURN` |
| Network/timeout/data-processing failure | `IO_ERROR` |
| Invalid HTTP 200 response, HTTP 5xx, or otherwise unhandled status (including 429) | `BAD_RESPONSE` |
| Server HTTP 400 after local input passed validation | `IO_ERROR` |

Status and claim methods have their own callbacks/error parsing; do not apply the submit-conflict mapping to seat-claim errors. Each transport request is issued once. Polling/backoff belongs to callers; receipt of `Retry-After` does not imply the submit/fetch transport automatically retries.

## Persistence and deployment boundary

The relay persists `turn_<seq>.json` and `claims.json` under `data/PlayByPost/Turns/<sha256-game-id>/`. Rate limits and claim locks are in memory. Preserve live data separately from Git and keep deployments within the intended single-process/storage model unless concurrency coordination is redesigned.

Cloudflare routing, persistent hosting, browser/WebGL distribution, team rules, and stronger participant authorization are separate design/deployment work. The current shared key and seat claim are playtest mechanisms, not proof that the server validates a player's legal turn.
