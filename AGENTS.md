# Block Nations repository instructions

Block Nations is a turn-based, tile-based Unity game targeting iOS and Android first. Touch interaction is core; Editor mouse input should approximate touch behavior. The project uses the New Input System. Read the required Editor version from `ProjectSettings/ProjectVersion.txt` (currently Unity 6000.4.0f1).

## Project context

- Supported product modes are `VsAI` and `PlayByPost`. AI-vs-AI simulation is development tooling.
- Active build scenes are `Assets/Scenes/MainMenu.unity` and `Assets/Scenes/SampleScene.unity`.
- Main menu and active gameplay UI use UI Toolkit (UITK).
- Design references are The Battle of Polytopia for clarity, pacing, and mobile UX, and Civilization for broader strategy ambitions.
- The restart sequence is phase 1 documentation cleanup (completed 2026-10-07), phase 2 separately requested AI work, and preparation for renewed hosted playtesting. Phase 1 completion does not authorize gameplay changes or deployment.
- Start with [the documentation index](Docs/README.md), [current state](Docs/MVP_Current_State.md), and [development follow-ups](Docs/Development_Followups.md). Older audits under `Docs/archive/` are historical references.

## Scope and workflow

- Prefer small, incremental changes. Assume existing systems are in use and must remain stable.
- Implement small, clear, low-risk requests directly. For risky, unclear, or core-system changes, investigate and present a plan before patching unless the user already approved that plan or implementation scope.
- A core-system plan should identify likely causes, the recommended solution, exact files likely to change, tradeoffs, risks, assumptions, relevant edge cases, and a minimal manual test checklist. Offer alternatives only when they help the decision.
- Do not touch unrelated code or perform opportunistic cleanup.
- No scene (`.unity`) changes, prefab edits, or UI layout redesign unless explicitly requested.
- No broad refactors unless explicitly requested. Extract responsibilities in bounded steps when justified by the approved task.
- Avoid changing persistent data formats unless clearly necessary and explicitly approved. Preserve supported saves and PBp migration paths until a retirement decision is made.
- Follow existing patterns unless there is a strong reason to change them. A low file count is a preference, not a reason to keep growing oversized files or choose an inferior architecture. Explain broader scope when needed for correctness or maintainability.
- Follow [the Git workflow](Docs/BlockNations_Git_Workflow.md) for completed, validated changes. Keep unrelated local changes out of commits.
- For OmniRoute operations, read the relevant OmniRoute Agent Skill first and prefer its API/CLI/MCP workflow. Use a browser only when the supported workflow genuinely requires it.

## Behavior and compatibility

- If behavior changes, state what changes, why, and how it will be tested.
- Prefer additive changes. Changes to core systems should remain clean and consistent rather than patch around limitations.
- Identify temporary workarounds explicitly.
- Use stable state for logic; do not infer state from rendered UI strings.
- PBp snapshot protocol is currently `5`, with `4` as the supported migration source. See [compatibility policy](Docs/PBp-Compatibility.md) and [migration ledger](Docs/PbP_Migration_Ledger.md).
- Treat the source code as authoritative for implemented behavior. Distinguish implemented features, verified behavior, future requirements, and unresolved decisions in documentation.

## Players, teams, and PBp truth

Keep these concepts separate:

1. Player identity: who participates.
2. Match seat and ownership: a participant's seat and the units/cities it owns.
3. Team membership and relationships: alliances and hostility, independent of ownership.
4. Viewer POV and visibility: what the local viewer can see.
5. Turn ownership: which seat may act.
6. Transport state: last applied/submitted sequence and polling state.

- Current PBp code supports 2–4 seats without a team model. The user's near-term player-count direction is 4 or 5; five-player support is not implemented. Neither number is a permanent architectural cap.
- Preserve a path to as many players and teams as the game can practically support, including joining/leaving and switching teams. Do not implement those rules or choose their timing during an unrelated cleanup.
- Future AI and legal-action code must use explicit `seatIndex`, `currentTurnSeatIndex`, `IsTurnOwnedBySeat(int)`, and `ownerSeatIndex`. Do not use `isPlayerTurn`, `isPlayerOwned`, `IsHumanTurn`, `CanControlUnit`, or `CanLocalPlayerIssueCommands()` as AI/legal-action legality inputs.
- `isPlayerTurn` is a legacy turn-side bridge, not a PBp POV signal. Existing boolean AI paths are migration work, not patterns to copy into new code.
- As teams are introduced, determine hostility/cooperation through explicit relationships. Different owners alone do not establish hostility. A relationship boundary must preserve today's free-for-all behavior until team rules are approved.
- Reconnecting, claiming an unused seat, resignation, elimination, and a new player joining or replacing another player are distinct operations. Do not assume current seat claiming implements all of them.

## UI work

- Use explicit serialized references and inspector wiring. Describe new buttons/labels/panels and where to wire them; do not auto-wire through hacks.
- List player-facing implications of UI flow changes.
- For MVP PBp display metadata, prefer the latest locally known snapshot/header over widening lightweight polling or server payloads unless live freshness is explicitly required.
- Preserve requested wording exactly unless a technical or layout issue prevents it.
- Keep the random username generator implemented but hidden in the Profile panel. Multiplayer requires a manually entered recognizable typed name.
- For menu-pane screenshots, use `Assets/Editor/TakeScreenshotMenu.cs`, not Unity camera capture. Enter Play Mode with the Game view active/visible, allow a short settle delay after changing panes, capture one pane at a time into `Screenshots/`, and verify each file before continuing.

## Validation and communication

- Prefer practical checks relevant to the change. Documentation-only changes need source/link/diff checks, not new gameplay tests.
- For non-trivial code changes, report compilation/test results when relevant, a targeted manual checklist, and material limitations. Manual smoke checks are normally for the user to run; do not imply they were executed.
- Follow [Unity testing notes](Docs/Unity_Testing.md); UITK Editor tests require graphics.
- Provide rollback guidance and a unified diff when useful. Supply a copy-paste-ready commit message when a patch is ready to commit.
- Keep secrets and private runtime data out of output and new commits.
- Respond concisely and explain decisions explicitly. Spend effort on implementation rather than repeating established plans.
- Separate ChatGPT planner/prompt preferences are preserved in [Planner Workflow](Docs/Planner_Workflow.md); prompt-writing conventions are not mandatory endings for implementation reports.
