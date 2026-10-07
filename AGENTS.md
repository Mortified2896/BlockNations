# Block Nations repository instructions

Block Nations is a turn-based, tile-based Unity game targeting iOS and Android first. Touch is core; Editor mouse input should approximate touch. Use the New Input System and the Editor version in `ProjectSettings/ProjectVersion.txt`.

## Project context

- Product modes: `VsAI` and `PlayByPost`. AI-vs-AI simulation is development tooling.
- Active scenes: `Assets/Scenes/MainMenu.unity` and `Assets/Scenes/SampleScene.unity`. Main menu and active gameplay UI use UI Toolkit (UITK).
- Design references: The Battle of Polytopia for clarity, pacing, and mobile UX; Civilization for broader strategy ambitions.
- See [current state](Docs/MVP_Current_State.md), [development follow-ups](Docs/Development_Followups.md), and [the documentation index](Docs/README.md). Historical audits are not active refactor plans.

## Change boundaries

- No scene (`.unity`), prefab, or UI layout changes unless explicitly requested.
- No broad refactors or persistent-format changes unless explicitly approved. Preserve supported saves and PBp migration paths until a retirement decision is made.
- Plan before changing core systems such as turn progression, AI ownership/visibility, input, and save/load, unless the conversation already approves the plan or implementation scope.
- Prefer bounded responsibility extraction over growing oversized files. Low file count is not an architectural requirement.
- Follow [the Git workflow](Docs/BlockNations_Git_Workflow.md) for completed changes.
- For OmniRoute operations, read the relevant OmniRoute Agent Skill and use its API/CLI/MCP workflow before considering browser interaction.

## Players, teams, and PBp

Keep player identity, match seats/ownership, team membership/relationships, viewer visibility, turn ownership, and transport progress separate.

- PBp currently supports 2–4 seats without teams. The near-term capacity direction is 4 or 5 players; five-player support is not implemented. These are not permanent architectural caps.
- Preserve a path to as many players/teams as gameplay and performance support, including joining/leaving and switching teams. Membership timing, shared vision, and other team rules remain unresolved.
- New AI/legal-action paths must use explicit `seatIndex`, `currentTurnSeatIndex`, `IsTurnOwnedBySeat(int)`, and `ownerSeatIndex`. Do not use `isPlayerTurn`, `isPlayerOwned`, `IsHumanTurn`, `CanControlUnit`, or `CanLocalPlayerIssueCommands()` as legality inputs.
- `isPlayerTurn` is a legacy turn-side bridge, not a PBp POV signal. Existing boolean AI paths are not patterns to copy.
- Determine hostility/cooperation through explicit relationships when adding teams; different owners alone do not imply enemies. Preserve current free-for-all behavior until team rules are approved.
- Reconnection, claiming an unused seat, resignation, elimination, and participant replacement are distinct operations. Current seat claiming does not implement a complete join/leave policy.
- See [compatibility policy](Docs/PBp-Compatibility.md) and [migration ledger](Docs/PbP_Migration_Ledger.md) for protocol gates and migration support.

## UI and inspection

- Use explicit serialized references and inspector wiring for UI additions; do not auto-wire through hacks.
- Use stable state rather than rendered UI strings for gameplay/flow logic.
- For PBp display metadata, use the latest locally known snapshot/header rather than widening lightweight polling unless live freshness is required.
- Keep the random username generator implemented but hidden in Profile. Multiplayer requires a recognizable manually typed name.
- Capture menu panes with `Assets/Editor/TakeScreenshotMenu.cs`, not camera capture. Use Play Mode with the Game view active/visible, settle after switching panes, capture one at a time into `Screenshots/`, and verify each file.

## Validation

- UITK Editor tests require graphics; see [Unity testing notes](Docs/Unity_Testing.md).
- Keep PBp credentials and private local match data out of output and commits.
