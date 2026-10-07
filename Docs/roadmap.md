# Block Nations Roadmap

Resume development through documentation cleanup, separately requested AI improvements, and renewed hosted playtesting. Keep each change bounded while preserving the ability to expand players and teams. [Current state](MVP_Current_State.md) describes implemented behavior; [development follow-ups](Development_Followups.md) records unresolved work and its validation.

Last reviewed: 2026-10-07.

## Product direction

- Product modes remain `VsAI` and `PlayByPost`, with mobile touch input and UITK UI.
- Multiplayer remains turn-based Play-by-Post. Active scenes are `MainMenu.unity` and `SampleScene.unity`.
- The intended near-term player range is **4 or 5**, currently without teams. PBp implementation presently supports **2–4**; five players is future work.
- Design for as many participants and teams as gameplay and performance can practically support. Keep player identity, seats/ownership, team relationships, turn ownership, viewer visibility, and transport state separate.
- Joining/leaving and team switching are intended future capabilities. Shared vision, friendly fire, replacement/reconnection, control of departed players' assets, team victory, and when membership changes take effect remain design decisions.

## Implemented foundation

- Grid gameplay, turn progression, cities/recruitment, combat, visibility/exploration, and save/load.
- Baseline/Rider Focus AI presets with local decision features and a tactical city-capture planner.
- Seat-based legal-action queries, alongside older two-side AI helpers still needing transition.
- Development AI-vs-AI batch/tournament evaluation and CSV logging.
- HTTP PBp relay with turn storage, seat claims, and batched menu status.
- Protocol 5 snapshots, protocol 4 migration, and per-seat ownership/metadata/gold/visibility support.
- UITK main menu and gameplay top/bottom HUD, unit panel, and city panel.

## Phase 1 documentation cleanup

Completed 2026-10-07: active instructions, compatibility documents, local/deployment setup, and test guidance now match the reviewed source. Planner preferences are separate from repository implementation rules. Known risks and future team direction are recorded without changing gameplay, server behavior, scenes/prefabs, serialized assets, credentials, or compatibility support.

## Phase 2 AI work

Start after the user requests phase 2. Plan the core-system boundary before implementation:

- Extract AI orchestration/decisions from `TurnManager` in a bounded, behavior-preserving step.
- Pass an explicit acting seat and its visibility through new AI paths, completing the transition from boolean side assumptions.
- Introduce an explicit relationship boundary that preserves current free-for-all behavior and can later support allies. Do not infer hostility from different ownership once teams are supported.
- Keep authoritative action validation/execution consistent with the seat-based legal-action service.
- Improve tactics/recruitment after establishing a regression baseline, using existing evaluation tooling and mobile performance checks.

This phase does not automatically authorize a complete team system, more player slots, or save-format changes.

## Hosted playtesting preparation

- Fix the confirmed concurrent-submit overwrite bug before reopening the relay to testers.
- Verify the actual deployed entry point/revision before retiring the divergent `server.prod.js` copy.
- Resolve the current public TLS/availability issue and validate create/claim/submit/fetch with supported client versions.
- Review distributed playtest credentials and rate limiting behind the selected proxy/tunnel.
- Choose between keeping the persistent Node relay behind Cloudflare and a separately designed Workers/storage migration.
- If browser play is desired, build and validate Unity WebGL separately from relay hosting.

## Later cleanup and decisions

- Decompose oversized menu controllers when the approved feature requires it.
- Decide existing-match retention before retiring protocol 4 or the missing-app-version bridge.
- Implement additional capacity, teams, and membership changes only after rules, limits, compatibility, and tests are agreed.
- Keep local transports and AI evaluation tools unless their removal is explicitly justified.

Tutorial/Hotseat reintroduction, real-time multiplayer, broad unrelated refactors, and reopening legacy UI/Calculus profiles remain outside the current work.
