# Block Nations – Development Roadmap

This document tracks **what exists**, **what’s next**, and **what’s intentionally deferred**. It is designed to be edited incrementally and to serve as a shared reference between the developer and ChatGPT/Codex.

---

## Principles

* Mobile‑first, clarity over complexity
* Prefer **small, shippable increments**
* Multiplayer is Play‑by‑Post first; real‑time is explicitly out of scope
* Instrumentation before optimization
* Avoid premature architecture refactors

---

## ✅ Phase 0 — Foundations (DONE)

### Core Gameplay

* Turn-based, tile-based core loop
* Hotseat support
* Vs AI mode
* Fog of war basics

### Persistence

* Save / load system
* Stable `gameId` per campaign

### Multiplayer Core (Local / File)

* Play‑by‑Post via file-based transport
* Turn snapshots
* Transport sequence numbers

### Telemetry v0 (Completed)

* `ITurnTelemetrySink` seam (off by default)
* Resolve / Submit / Fetch / EndTurnPressed events
* Transport decorator (`TelemetryTurnTransport`)
* Canonical error + op constants
* Debug telemetry sink
* Verified semantics for:

  * seqA / seqB
  * payload size
  * mode + gameIdHash

---

## 🚧 Phase 1 — PBp Hardening (NEXT)

### Transport

* HTTP transport (server-hosted)
* Auth-less game ID–based access (rate-limited)
* Server-side storage for turn files

### Validation

* Server-side sanity checks on turn payloads
* Conflict detection (already partially present)

### UX

* Clear “Waiting for opponent” state
* Disabled interactions when not your turn
* Explicit Sync Now feedback

### Telemetry v1 (Light Extension)

* HTTP latency visibility
* Transport availability errors from server
* Optional retry counters (no aggregation yet)

---

## 🧩 Phase 2 — Multiplayer Polish

### Player Identity

* Move from bool ownership to `PlayerId`
* Support >2 players (design only, no UI yet)

### Visibility

* Correct fog of war per player
* Spectator-safe serialization

### UX

* Better turn transition feedback
* Read-only board state when waiting

---

## 📊 Phase 3 — Analytics & Live Ops

### Telemetry v2

* UGS Analytics sink
* Session-level correlation
* Event sampling / throttling

### Insights

* Drop-off points
* Average turn duration
* Transport failure rates

---

## 🧪 Phase 4 — Content & Iteration

### Gameplay Variety

* Smaller maps (faster matches)
* Minor rule twists (e.g. limited actions per turn)
* More meaningful early-game decisions

### AI

* Simple heuristics improvements
* Difficulty tuning

---

## 🚫 Explicitly Out of Scope (For Now)

* Real-time multiplayer
* Chat system
* Matchmaking / public lobbies
* In-app purchases
* Large-scale content pipelines

---

## Notes

* This roadmap is **living**. Items can move between phases.
* ChatGPT is allowed to update this file when explicitly asked.

---

## How to use this roadmap

This roadmap is a living document.

- It reflects *current intent*, not fixed promises.
- Items move forward only when the underlying systems are stable.
- Scope may be reduced if clarity or robustness would otherwise suffer.

Before starting any larger change, this file should be reviewed to answer:
- Is this the right phase for this work?
- Does this build on already-stable systems?
- Can this be deferred without blocking progress?

Small, incremental steps are preferred over broad refactors.

Last reviewed: 2026-01-31