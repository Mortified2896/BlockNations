HTTP PBp Transport Contract v1

This document defines the HTTP contract for Play-by-Post transport so it matches ITurnTransport and FileTurnTransport semantics.

Scope: 2-player PBp, localhost or VPS, API key authentication enabled, no retries.

⸻

Authentication

All PBp endpoints require the following header:

X-BlockNations-Api-Key: 

Behavior:
	•	Missing or invalid key:
{ “ok”: false, “error”: “UNAUTHORIZED” }
HTTP 401

Public endpoints (no auth required):
	•	GET /healthz

⸻

Interface Alignment (Unity)
	•	SubmitTurn(gameId, turnNumber, json) → done(ok, err)
	•	TryFetchNextTurn(gameId, afterTurnNumber) → done(ok, err, fetchedTurnNumber, fetchedJson)

Canonical errors (Unity expects these exact strings):
	•	INVALID_GAME_ID
	•	INVALID_TURN
	•	EMPTY_JSON
	•	UNAVAILABLE
	•	CONFLICT
	•	IO_ERROR
	•	NO_TURN

⸻

Endpoints
	•	POST /pbp/turn (submit)
	•	GET /pbp/turn/next (fetch next)
	•	GET /healthz (availability)

All requests and responses use JSON, except where explicitly noted below.

⸻

Rate Limiting
	•	GET /pbp/turn/next → 60 requests / 60 seconds / IP
	•	POST /pbp/turn → 20 requests / 60 seconds / IP

Exceeded:

HTTP 429
{ “ok”: false, “error”: “RATE_LIMITED” }

Retry-After header is included.

⸻

Submit: POST /pbp/turn

Request JSON:

{
“gameId”: “string”,
“seq”: 123,
“json”: “string”
}

Rules:
	•	gameId must be non-empty
	•	seq must be a positive integer (> 0)
	•	json must be non-empty string

Success response (HTTP 200):

{ “ok”: true, “alreadyHad”: false }

Duplicate (idempotent):

{ “ok”: true, “alreadyHad”: true }

Conflict (same seq, different payload):

HTTP 409
{ “ok”: false, “error”: “SEQ_CONFLICT” }

Invalid input:

HTTP 400
{ “ok”: false, “error”: “INVALID_INPUT” }

Server error:

HTTP 500
{ “ok”: false, “error”: “SERVER_ERROR” }

Conflict semantics:
	•	If same gameId + seq already exists:
	•	same payload → success (alreadyHad: true)
	•	different payload → SEQ_CONFLICT

⸻

Fetch: GET /pbp/turn/next

Query params:
	•	gameId (string, required)
	•	after (int, required; may be -1)

Success response (HTTP 200):

{ “seq”: 124, “json”: “{…}” }

No turn available:

Option A (JSON):

{ “ok”: false, “error”: “NO_TURN” }

Option B (legacy):

Plain text: NO_TURN (HTTP 200)

Invalid input:

HTTP 400
{ “ok”: false, “error”: “INVALID_INPUT” }

Server error:

HTTP 500
{ “ok”: false, “error”: “SERVER_ERROR” }

⸻

Availability: GET /healthz

Response (HTTP 200):

{ “ok”: true }

⸻

Mapping to Canonical Transport Errors

Submit mapping:
	•	HTTP 200 + { ok: true } → done(true, null)
	•	HTTP 200 + { ok: true, alreadyHad: true } → done(true, null)
	•	HTTP 409 + SEQ_CONFLICT → done(false, “CONFLICT”)
	•	HTTP 400 + INVALID_INPUT → client decides:
	•	INVALID_GAME_ID / INVALID_TURN / EMPTY_JSON
	•	HTTP 500 + SERVER_ERROR → done(false, “IO_ERROR”)
	•	Network / timeout / parse → done(false, “IO_ERROR”)
	•	Auth failure (401 UNAUTHORIZED) → done(false, “UNAVAILABLE”)

Fetch mapping:
	•	HTTP 200 + { seq, json } → done(true, null, seq, json)
	•	HTTP 200 + NO_TURN → done(false, “NO_TURN”, 0, null)
	•	HTTP 400 + INVALID_INPUT → done(false, “INVALID_GAME_ID” | “INVALID_TURN”, 0, null)
	•	HTTP 500 + SERVER_ERROR → done(false, “IO_ERROR”, 0, null)
	•	Network / timeout / parse → done(false, “IO_ERROR”, 0, null)
	•	Auth failure (401 UNAUTHORIZED) → done(false, “UNAVAILABLE”, 0, null)

⸻

Turn Numbering & Sequence

seq = turnNumber * 2 + (isPlayerTurn ? 0 : 1)
	•	Server must store seq exactly as provided
	•	Fetch returns smallest seq strictly greater than “after”

⸻

Notes
	•	NO_TURN is not an error → normal polling state
	•	Transport mirrors FileTurnTransport semantics
	•	HTTP replaces file I/O without changing game logic