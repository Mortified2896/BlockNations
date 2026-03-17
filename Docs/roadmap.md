# Block Nations – Roadmap  
## Block Nations – Development Roadmap

This document tracks what exists, what’s next, and what’s intentionally deferred. It is designed to be edited incrementally and to serve as a shared reference between the developer and ChatGPT/Codex.

---

## Principles

- Mobile-first, clarity over complexity  
- Prefer small, shippable increments  
- Multiplayer is Play-by-Post first; real-time is explicitly out of scope  
- Instrumentation before optimization  
- Avoid premature architecture refactors  

---

# ✅ Phase 0 — Foundations (DONE)

## Core Gameplay

- Turn-based, tile-based core loop  
- Hotseat support  
- Vs AI mode  
- Fog of war basics  

## Persistence

- Save / load system  
- Stable gameId per campaign  

## Multiplayer Core (Local / File)

- Play-by-Post via file-based transport  
- Turn snapshots  
- Transport sequence numbers  

## Telemetry v0 (Completed)

- ITurnTelemetrySink seam (off by default)  
- Resolve / Submit / Fetch / EndTurnPressed events  
- Transport decorator (TelemetryTurnTransport)  
- Canonical error + op constants  
- Debug telemetry sink  

Verified semantics for:

- seqA / seqB  
- payload size  
- mode + gameIdHash  

---

# ✅ Phase 1 — Mobile PBp (COMPLETE)

**Definition of done achieved:**  
Play-by-Post works reliably on real mobile devices (iOS first, Android also validated) using HTTP transport.

Core multiplayer workflows were validated across:
- iPhone ↔ iPhone  
- iPhone ↔ Editor  
- Android ↔ Editor  

Recent follow-up work focused on backend hardening and did not change intended transport semantics.

---

## Transport (Implemented)

### ✅ HTTP PBp transport (Unity)

- HttpTurnTransport implemented (UnityWebRequest)  
- Canonical error mapping aligned with ITurnTransport  
- Telemetry verified for resolve / submit / fetch / NO_TURN  

### ✅ Standalone PBp server (Node/Express)

- POST submit / GET fetch  
- File-backed turn storage  

### ✅ PBp Server Hardening (MVP COMPLETE)

- Input validation (gameId, seq, json, after)  
- Payload size limits (Express + byte cap)  
- Rate limiting (per IP, per endpoint)  
- API key protection (X-BlockNations-Api-Key)  
- Production-safe error responses (no stack traces)  
- Stale temp-file cleanup (crash-safe writes)  
- Health endpoint (/healthz)  

Status:  
Backend is considered secure enough for MVP / TestFlight.  

---

## 🔄 Polling Strategy (MVP)

### ✅ Client-driven polling (menu-only)

- Poll only while Multiplayer screen is open  
- Exponential backoff:
  - 3s → 10s → 30s → 60s → 120s → cap at 300s  
- Reset delay to 3s if a new turn is received  
- Stop polling when leaving Multiplayer screen  

### ✅ Server load signaling

- Server may return:
  - `429 Too Many Requests`
  - `Retry-After: <seconds>`
- Client honors `Retry-After`  
- Fallback: increase backoff to cap if header missing  

---

## 📡 Networking Upgrade (Planned — Phase 1.5 / Phase 2 Candidate)

### ⏳ Option 3 — Long Polling (Deferred but Designed)

**Goal:** Reduce request spam while maintaining near real-time PBp updates.

Client:

GET /pbp/turn/next?gameId=...&after=...&waitSeconds=25  

Server:

- Holds request open up to `waitSeconds`  
- Returns immediately if a new turn arrives  
- Returns `NO_TURN` after timeout  

Client:

- Immediately re-issues request on `NO_TURN`  
- Uses backoff only on error conditions  
- Adds small jitter  

---

# UX

- Clear “Waiting for opponent” state  
- Disabled interactions when not your turn  
- Explicit Sync Now feedback  
- Throttled NO_TURN polling logs  
- Multiplayer list reflects:
  - “Your turn”
  - “Waiting…”
  - “Server offline”

---

# Validation

- Client-side validation before submit/fetch  
- Server-side sanity checks on turn payloads  
- Conflict detection  

---

# Server hardening

### ✅ Phase 1 (Completed)

- Core security layer implemented (auth, validation, limits, cleanup)  
- Stable error model  
- Safe file write handling  

### ⏳ Future improvements (only if needed)

- JSON-only responses (remove plaintext NO_TURN)  
- More granular rate limiting (per gameId / per user)  
- Distributed rate limiting (multi-instance)  
- Long polling / connection tuning  

---

# 🧩 Phase 2 — Multiplayer Polish

## Player Identity

- Move from bool ownership to PlayerId  
- Support >2 players (design only, no UI yet)  

## Visibility

- Correct fog of war per player  
- Spectator-safe serialization  

## UX

- Better turn transition feedback  
- Read-only board state when waiting  

---

# 📊 Phase 3 — Analytics & Live Ops

## Telemetry v2

- UGS Analytics sink  
- Session-level correlation  
- Event sampling / throttling  

## Insights

- Drop-off points  
- Average turn duration  
- Transport failure rates  

---

# 🧪 Phase 4 — Content & Iteration

## Gameplay Variety

- Smaller maps  
- Minor rule twists  
- More meaningful early-game decisions  

## AI

- Heuristic improvements  
- Difficulty tuning  

---

# 🚫 Explicitly Out of Scope (For Now)

- Real-time multiplayer  
- Chat system  
- Matchmaking / public lobbies  
- In-app purchases  
- Large-scale content pipelines  
- WebGL multiplayer support  

---

## Notes

This roadmap is living. Items can move between phases.

ChatGPT is allowed to update this file when explicitly asked.

Last reviewed: 2026-03-17