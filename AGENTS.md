Create a new repo-level AGENTS.md at the repository root.

Use the finalized content below exactly as the base.
Do not add generic filler.
Do not expand scope.
Do not create it under Docs/.
Create it at repo root as: AGENTS.md

One required adjustment:
Update the "Definition Of Done" / diff expectations so they match this repo preference:

- For code or scene edits, include unified diff.
- For pure deletion/asset cleanup passes, unified diff is optional unless:
  - explicitly requested
  - something failed
  - or the change touched risky files.
- If no file was edited, do not fabricate a diff.

Final content:

# Block Nations – AGENTS.md

## Project Scope
- Unity project, mobile-first.
- Current MVP scope is `VsAI` + `PlayByPost` only.
- `Tutorial`, `Hotseat`, and `BottomStripController` are out of scope unless explicitly requested.
- Supported runtime scenes are `Assets/Scenes/MainMenu.unity` and `Assets/Scenes/SampleScene.unity`.

## Current Repo Reality
- Main menu and active gameplay UI are UITK-based.
- Supported gameplay UITK surfaces:
  - top HUD
  - bottom HUD
  - unit panel
  - city panel
- `MainMenuUITKView` is the active menu presentation path.
- `CityUIManager` and `UnitUIManager` still provide gameplay state/actions consumed by the UITK views.
- `TurnManager` is still a large, high-risk file. Keep edits narrow.
- Current `SampleScene` wiring points `TurnManager.turnTransportComponent` at `HttpTurnTransport`.
- `FileTurnTransport` still exists in repo/scene. Do not assume all non-HTTP PBp code is dead; verify scene wiring before changing transport assumptions.

## Default Workflow
- For non-trivial or risky work, start with a read-only plan and wait for approval before patching.
- Include:
  - likely root cause(s)
  - 1–2 solution options, with one recommended
  - exact files likely to change
  - tradeoffs
  - what could go wrong
  - assumptions
  - MVP-relevant edge cases
  - minimal manual test checklist
- When patching:
  - make the smallest safe change
  - preserve current behavior unless the task requires otherwise
  - avoid unrelated cleanup

## Scope And Change Rules
- Prefer small, incremental changes.
- Prefer minimal file count.
- Do not touch unrelated code.
- Do not do opportunistic cleanup.
- No prefab edits unless explicitly requested.
- No scene edits unless clearly required by the task.
- No broad refactors unless explicitly requested.
- Do not reintroduce legacy gameplay UGUI or hidden legacy menu flows unless explicitly requested.

## Unity / UI Rules
- Treat visual/manual smoke verification as first-class for UITK work.
- Do not claim visual verification unless it was actually run.
- If you cannot truly verify UI behavior, explicitly say:
  - manual visual smoke test required
- For UI/menu/gameplay changes, always provide targeted checks such as:
  - correct panel visible
  - no duplicate UI
  - expected overlay appears
  - handoff between default bottom, unit panel, and city panel works
  - top/bottom HUD visible
  - safe-area/layout still looks correct
- Blank or misaligned UITK UI is often a `UIDocument` / `PanelSettings` / safe-area issue. Check scene wiring before changing gameplay logic.

## PBp Guardrails
Keep these concepts separate:
1. viewer POV / visibility
2. turn ownership
3. local seat / command authority
4. transport / connectivity state

Important:
- `isPlayerTurn` is not a POV signal in PBp; it is turn-side only.
- Do not casually merge seat, transport, visibility, and UI concepts.
- Preserve existing PBp semantics unless explicitly asked to change them.
- If touching PBp menu or resume flow, protect:
  - create/join/resume behavior
  - protocol-version preflight checks
  - active-games list and badge behavior
  - return-to-multiplayer behavior
  - PBp endgame/menu-exit guards

## File-Specific Caution
### TurnManager
- Highest-risk file in the repo.
- Do not add new unrelated responsibilities to it.
- Avoid “while I’m here” edits.
- Regression-test the affected flows mentally and in the checklist:
  - turn flow
  - PBp submit/fetch/poll
  - save/load
  - endgame state

### MainMenuController / MainMenuUITKView
- Keep changes scoped; PBp create/join/resume/open flows are easy to regress.
- `MainMenuController` still contains compatibility and orchestration logic even though UITK owns the active menu presentation.
- Be careful with:
  - return-to-multiplayer behavior
  - PBp resume/open flows
  - version-blocked join/open handling

### SaveManifestService
- Be careful when touching PBp list, badge, or resume behavior.
- Manifest state drives active multiplayer list behavior in the menu.

## Build And Test Guidance
Use practical checks only.

### Default code smoke check
- `dotnet build Assembly-CSharp.csproj -nologo`

### Unity tests
- There is at least one editor UITK test at `Assets/Editor/Tests/MultiplayerScrollViewTests.cs`.
- Use it when changing multiplayer menu list/scroll behavior.
- If Unity batch/EditMode tests are attempted and the project is already open, say so clearly.
- Do not pretend Unity tests ran if they were blocked.

### Visual/manual smoke
- For gameplay/menu/UI changes, include targeted manual smoke checks.
- On UI work, check both mobile portrait and at least one wider layout if possible.

## Docs / Assumptions
- Prefer `Docs/MVP_Current_State.md`, active scene wiring, and current code over older docs.
- Treat `Docs/archive/*` and any pre-UITK / tutorial / hotseat notes as historical only.
- If a detail belongs to product/ops documentation rather than repo task guidance, keep it in docs instead of AGENTS.
- When architecture, MVP scope, or core workflows change substantially, update the relevant docs/status files as part of the task or explicitly call out the needed doc follow-up.

## Commit / Branch Policy
- Do not commit or push unless explicitly asked.
- Do not create or switch branches unless explicitly asked.
- If the user wants isolation for risky, broad, or throwaway work, use a short-lived feature/spike branch.
- If asked to commit:
  - use a concise scoped message
  - report the exact commit message used

## Definition Of Done
A task is not done unless the response includes:
- short explanation
- manual test checklist
- rollback plan

For code or scene edits, also include:
- unified diff

For visual/UI work, also include:
- targeted manual visual smoke checks

For pure deletion/asset cleanup passes, unified diff is optional unless:
- explicitly requested
- something failed
- or the change touched risky files

If no file was edited, do not fabricate a diff.

Return:
1) short explanation
2) rollback plan
3) unified diff only when required by the rules above