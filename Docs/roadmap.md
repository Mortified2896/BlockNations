Block Nations – Roadmap
Block Nations – Development Roadmap

This document tracks what exists, what’s next, and what’s intentionally deferred. It is designed to be edited incrementally and to serve as a shared reference between the developer and ChatGPT/Codex.

Principles

Mobile‑first, clarity over complexity

Prefer small, shippable increments

Multiplayer is Play‑by‑Post first; real‑time is explicitly out of scope

Instrumentation before optimization

Avoid premature architecture refactors

✅ Phase 0 — Foundations (DONE)
Core Gameplay

Turn-based, tile-based core loop

Hotseat support

Vs AI mode

Fog of war basics

Persistence

Save / load system

Stable gameId per campaign

Multiplayer Core (Local / File)

Play‑by‑Post via file-based transport

Turn snapshots

Transport sequence numbers

Telemetry v0 (Completed)

ITurnTelemetrySink seam (off by default)

Resolve / Submit / Fetch / EndTurnPressed events

Transport decorator (TelemetryTurnTransport)

Canonical error + op constants

Debug telemetry sink

Verified semantics for:

seqA / seqB

payload size

mode + gameIdHash

🚧 Phase 1 — Mobile PBp (IN PROGRESS)

Definition of done for Phase 1: Play‑by‑Post works reliably on real mobile devices (iOS first, then Android) using HTTP transport.

Transport work in this phase exists to enable and validate mobile builds, not as an abstract backend effort.

Transport (Minimum Viable — MUST happen before mobile builds)

✅ HTTP PBp transport (Unity)

HttpTurnTransport implemented (UnityWebRequest)

Canonical error mapping aligned with ITurnTransport

Telemetry verified for resolve / submit / fetch / NO_TURN

✅ Standalone PBp server (Node/Express)

POST submit / GET fetch / health endpoint

File-backed turn storage

⏳ Device reachability smoke test

Server reachable from iPhone/Android (same Wi‑Fi/LAN or hosted URL)

baseUrl configured per build target

Mobile Builds (Core Goal of Phase 1)

📱 iOS build (primary focus)

Xcode build + signing

iPhone ↔ iPhone PBp works

iPhone ↔ Editor PBp works

Background / resume does not break PBp state

🤖 Android build (secondary)

Same HTTP PBp flow validated

No platform-specific regressions

UX

Clear “Waiting for opponent” state

Disabled interactions when not your turn

Explicit Sync Now feedback

Throttled NO_TURN polling logs (spam reduction)

Validation

Client-side validation before submit/fetch

Server-side sanity checks on turn payloads

Conflict detection (already partially present)

Server hardening (DEFERRED unless mobile testing forces it)

⏳ JSON-only responses (remove plaintext NO_TURN)

⏳ Stricter validation + clearer error mapping

⏳ Simple rate limiting per gameId

Telemetry v1 (Light Extension)

HTTP latency visibility (done)

Transport availability / IO errors surfaced

Optional retry counters (future)




🧩 Phase 2 — Multiplayer Polish
Player Identity

Move from bool ownership to PlayerId

Support >2 players (design only, no UI yet)

Visibility

Correct fog of war per player

Spectator-safe serialization

UX

Better turn transition feedback

Read-only board state when waiting

📊 Phase 3 — Analytics & Live Ops
Telemetry v2

UGS Analytics sink

Session-level correlation

Event sampling / throttling

Insights

Drop-off points

Average turn duration

Transport failure rates

🧪 Phase 4 — Content & Iteration
Gameplay Variety

Smaller maps (faster matches)

Minor rule twists (e.g. limited actions per turn)

More meaningful early-game decisions

AI

Simple heuristics improvements

Difficulty tuning

🚫 Explicitly Out of Scope (For Now)

Real-time multiplayer

Chat system

Matchmaking / public lobbies

In-app purchases

Large-scale content pipelines

WebGL multiplayer support (may be revisited later if there is strong demand)

Notes

This roadmap is living. Items can move between phases.

ChatGPT is allowed to update this file when explicitly asked.

Last reviewed: 2026-01-31