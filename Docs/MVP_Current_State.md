# Block Nations MVP Current State

Current snapshot of the repo after the gameplay UITK migration. This is a factual status note, not a roadmap.

## Supported Modes

- `VsAI`
- `PlayByPost`

## Active Scenes

- `Assets/Scenes/MainMenu.unity`
- `Assets/Scenes/SampleScene.unity`

## Gameplay UI Status

- Main menu uses UITK.
- `SampleScene` gameplay UI uses UITK for the active MVP path.
- Current UITK gameplay surfaces:
  - top HUD
  - bottom HUD
  - unit panel
  - city panel
- `TurnManager`, `CityUIManager`, and `UnitUIManager` still provide gameplay state/actions consumed by the UITK views.

## AI Runtime Status

- Live AI runtime is Baseline-only.
- The current per-side AI model choice for AI-vs-AI setup is:
  - `Baseline`
  - `Rider Focus`
- `Calculus` is no longer part of reachable runtime AI behavior.
- The older AI Style/Profile split was simplified in the AI-vs-AI setup UI to per-side AI Model selectors.
- AI-vs-AI comparison tooling now reflects the active same-profile seat-bias control path rather than old Calculus-vs-Baseline analysis.

## Removed / Not Current

- `Tutorial`
- `Hotseat`
- `BottomStripController`
- Legacy gameplay UI path as the active MVP route
- `Calculus` as a live AI/runtime option

## PBp Truth Notes

- Current multiplayer scope is Play-by-Post over HTTP.
- PBp runtime sync/polling currently lives in `TurnManager`.
- Do not assume older menu-only polling notes are current.
- File-based PBp support still exists in code, but the MVP multiplayer direction is HTTP PBp.

## Visibility / Fog Rules

- Tactical visibility used for gameplay decisions is live only.
- Visibility is recomputed immediately after movement and other relevant actions.
- Visibility can shrink during the acting player's own turn if a spotting unit moves away.
- There is no separate "seen this turn" or sticky tactical visibility layer in the current MVP rule set.
- Explored state is visual memory only; it is not current tactical visibility.
- Movement order therefore matters for what can still be seen and targeted later in the same turn.
- Rider surprise after losing sight is expected under the current MVP rule, not a bug.

## Combat Number Representation

- Combat uses scaled integers for deterministic internal storage.
- `CombatScale = 10`.
- Player-facing combat values remain the real rules reference; scaled ints are the internal representation.
- Displayed `1.0` = internal `10`.
- Displayed `0.5` = internal `5`.
- Displayed `0.1` = internal `1`.
- UI formatting should stay consistent with the displayed decimal values rather than exposing the scaled storage.

## PBp Display Name Notes

- There are two separate local name concepts:
  - rolled/generated profile name
  - typed playtesting name
- The typed playtesting name is display-only metadata used to help players identify opponents during PBp testing.
- The typed playtesting name may appear in:
  - active game cards
  - selected game details
  - in-game waiting text
- The typed playtesting name is serialized in PBp snapshots by seat.
- The typed playtesting name must not affect viewer POV, turn ownership, or transport state.
- If no valid typed playtesting name is available, the UI falls back to `Opponent`.

## High-Risk Files

- `Assets/Scripts/Core/TurnManager.cs`
- `Assets/Scripts/UI/MainMenuController.cs`
- `Assets/Scripts/UI/GameplayTopHudUITKView.cs`
- `Assets/Scripts/UI/GameplayBottomHudUITKView.cs`
- `Assets/Scripts/UI/GameplayUnitPanelUITKView.cs`
- `Assets/Scripts/UI/GameplayCityPanelUITKView.cs`
- `Assets/Scripts/Networking/HttpTurnTransport.cs`
- `Assets/Scripts/Utilities/SaveManifestService.cs`

## Do Not Assume

- Do not assume `Tutorial` or `Hotseat` still exist.
- Do not assume gameplay HUD work is still mid-migration; UITK gameplay HUD is the current baseline.
- Do not assume legacy gameplay UI objects are still the primary runtime path.
- Do not assume older docs written before the UITK migration still describe runtime behavior accurately.
