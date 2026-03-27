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

## Removed / Not Current

- `Tutorial`
- `Hotseat`
- `BottomStripController`
- Legacy gameplay UI path as the active MVP route

## PBp Truth Notes

- Current multiplayer scope is Play-by-Post over HTTP.
- PBp runtime sync/polling currently lives in `TurnManager`.
- Do not assume older menu-only polling notes are current.
- File-based PBp support still exists in code, but the MVP multiplayer direction is HTTP PBp.

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
