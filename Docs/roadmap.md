# Block Nations Roadmap

This roadmap is the current planning view for the live MVP path in this repo. For a short factual snapshot of current architecture and scope, see `Docs/MVP_Current_State.md`.

## Direction

- MVP modes are `VsAI` and `PlayByPost` only.
- Multiplayer remains Play-by-Post first.
- Gameplay UI is UITK-based for the MVP path.
- `Tutorial` and `Hotseat` were removed and are out of current scope.
- Keep planning grounded in the active build scenes: `MainMenu.unity` and `SampleScene.unity`.

## Done

- Turn-based core loop, grid gameplay, combat, cities, fog-of-war, and save/load are in place.
- `VsAI` is supported as the main single-player mode.
- `PlayByPost` is supported via HTTP transport and the Node/Express relay server.
- Main menu flow is UITK-based.
- Gameplay HUD/UI for the MVP path is on UITK:
  - top HUD
  - bottom HUD
  - unit panel
  - city panel
- Legacy gameplay UI ownership was cleaned up after the UITK migration.
- `Tutorial`, `Hotseat`, and `BottomStripController` were removed from the active product path.

## Before MVP

- Stabilize the current `VsAI` and `PlayByPost` flows without reopening scope.
- Finish docs cleanup so repo guidance matches the live codebase and scene setup.
- Do targeted UITK gameplay regression testing across supported gameplay flows and device layouts.
- Tighten PBp runtime behavior where current code and docs still diverge.
- Keep scene/setup changes focused on MVP reliability, not feature expansion.

## After MVP

- Improve PBp UX and runtime robustness if MVP feedback shows it is needed.
- Revisit player identity and ownership modeling beyond the current bool-based setup.
- Consider PBp transport/server upgrades only after MVP needs are clear.
- Expand telemetry/analytics beyond the current MVP baseline.
- Revisit broader feature additions only after MVP is stable.

## Explicitly Out Of Scope For Current MVP

- Tutorial reintroduction
- Hotseat or other local pass-and-play modes
- Re-opening the legacy gameplay UI path
- Reintroducing `BottomStripController`
- Real-time multiplayer
- Broad architecture refactors that do not directly support MVP stability

Last reviewed: 2026-03-27
