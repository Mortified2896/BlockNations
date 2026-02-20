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

# 🚧 Phase 1 — Mobile PBp (IN PROGRESS)

**Definition of done for Phase 1:**  
Play-by-Post works reliably on real mobile devices (iOS first, then Android) using HTTP transport.

Transport work in this phase exists to enable and validate mobile builds, not as an abstract backend effort.

---

## Transport (Minimum Viable — MUST happen before mobile builds)

### ✅ HTTP PBp transport (Unity)

- HttpTurnTransport implemented (UnityWebRequest)  
- Canonical error mapping aligned with ITurnTransport  
- Telemetry verified for resolve / submit / fetch / NO_TURN  

### ✅ Standalone PBp server (Node/Express)

- POST submit / GET fetch  
- File-backed turn storage  

### ⏳ Health endpoint (recommended)

- `/health` endpoint returning JSON  
- Used for server reachability checks  

---

## 🔄 Polling Strategy (MVP)

### ✅ Client-driven polling (menu-only)

- Poll only while Multiplayer screen is open  
- Exponential backoff:
  - 3s → 10s → 30s → 60s → 120s → cap at 300s  
- Reset delay to 3s if a new turn is received  
- Stop polling when leaving Multiplayer screen  

### ⏳ Server load signaling (Option 1 — Phase 1)

- Server may return:
  - `429 Too Many Requests`
  - `Retry-After: <seconds>`
- Client must honor `Retry-After`
- Fallback: increase backoff to cap if header missing  

This allows server-side throttling without client updates.

---

## 📡 Networking Upgrade (Planned — Phase 1.5 / Phase 2 Candidate)

### ⏳ Option 3 — Long Polling (Deferred but Designed)

**Goal:** Reduce request spam while maintaining near real-time PBp updates.

Instead of short-interval polling:

Client calls:

GET /pbp/turn/next?gameId=...&after=...&waitSeconds=25

Server behavior:

- Holds request open up to `waitSeconds`
- Returns immediately if a new turn arrives
- Returns `NO_TURN` after timeout

Client behavior:

- Immediately re-issues request on `NO_TURN`
- Uses backoff only on error conditions
- Adds small jitter to avoid thundering herd

**Benefits:**

- Fewer HTTP requests  
- Near real-time responsiveness  
- Scales better than aggressive polling  

**Requirements:**

- Stable HTTPS hosting  
- Proper request timeout handling  
- Cancellation safety  

Explicitly not required for MVP.

---

## ⏳ Device reachability smoke test

- Server reachable from iPhone/Android (same Wi-Fi/LAN or hosted URL)  
- baseUrl configured per build target  

---

# 📱 Mobile Builds (Core Goal of Phase 1)

## iOS build (primary focus)

- Xcode build + signing  
- iPhone ↔ iPhone PBp works  
- iPhone ↔ Editor PBp works  
- Background / resume does not break PBp state  

## Android build (secondary)

- Same HTTP PBp flow validated  
- No platform-specific regressions  

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

# Server hardening (Deferred unless mobile testing forces it)

- JSON-only responses (remove plaintext NO_TURN)  
- Stricter validation + clearer error mapping  
- Simple rate limiting per gameId  
- Connection limits tuning (for long polling phase)  

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

Last reviewed: 2026-02-20