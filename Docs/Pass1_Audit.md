# PASS 1 Script Audit (Assets/Scripts only)

## Summary
- Total script count: 31
- Count by role:
  - Core: 5
  - Orchestration: 5
  - Input: 5
  - Presentation: 11
  - Data: 5
- Refactor-later candidates (ordered by impact):
  - Assets/Scripts/Core/TurnManager.cs
  - Assets/Scripts/Input/UnitSelectionManager.cs
  - Assets/Scripts/Input/TileHoverManager.cs
  - Assets/Scripts/Tutorial/TutorialOverlay.cs
  - Assets/Scripts/UI/MainMenuController.cs
  - Assets/Scripts/Cities/CityUIManager.cs
  - Assets/Scripts/Gameplay/GameMenuActions.cs
  - Assets/Scripts/Utilities/SpeechBubble.cs
- Quarantine candidates (ordered by confidence):
  - Assets/Scripts/Input/UnitClickHandler.cs
  - Assets/Scripts/Input/CityClickHandler.cs
  - Assets/Scripts/Tutorial/TutorialBoot.cs
- Recommended order for deeper review (ordered):
  - Assets/Scripts/Core/TurnManager.cs
  - Assets/Scripts/Input/UnitSelectionManager.cs
  - Assets/Scripts/Input/TileHoverManager.cs
  - Assets/Scripts/Cities/City.cs
  - Assets/Scripts/Units/Unit.cs
  - Assets/Scripts/Core/GridManager.cs
  - Assets/Scripts/Utilities/TileVisibility.cs
  - Assets/Scripts/Cities/CityUIManager.cs
  - Assets/Scripts/UI/UnitUIManager.cs
  - Assets/Scripts/Utilities/GridUtils.cs
  - Assets/Scripts/UI/MainMenuController.cs
  - Assets/Scripts/Utilities/SaveLoadRequest.cs
  - Assets/Scripts/Audio/SoundManager.cs
  - Assets/Scripts/Gameplay/GameMenuActions.cs
  - Assets/Scripts/UI/HotseatTurnOverlay.cs
  - Assets/Scripts/UI/GameplayUIScaler.cs
  - Assets/Scripts/UI/SafeAreaApplier.cs
  - Assets/Scripts/UI/ButtonHoverEffect.cs
  - Assets/Scripts/Tutorial/TutorialOverlay.cs
  - Assets/Scripts/Tutorial/TutorialGate.cs
  - Assets/Scripts/Tutorial/TutorialLaunch.cs
  - Assets/Scripts/Tutorial/TutorialBoot.cs
  - Assets/Scripts/Gameplay/GameModeSelection.cs
  - Assets/Scripts/Gameplay/AIDifficultySelection.cs
  - Assets/Scripts/Utilities/ClipboardUtility.cs
  - Assets/Scripts/Utilities/SpeechBubble.cs
  - Assets/Scripts/Utilities/TileHighlighter.cs
  - Assets/Scripts/Units/OwnedSprite.cs
  - Assets/Scripts/Input/CameraController.cs
  - Assets/Scripts/Input/CityClickHandler.cs
  - Assets/Scripts/Input/UnitClickHandler.cs

## Per-Script Notes
> Dependency and reference lists are best-effort from static text search in `Assets/Scripts/**` only; Unity inspector/prefab/scene references are not detectable here.

### Assets/Scripts/Audio/SoundManager.cs
- Role: Presentation
- Turn-outcome relevance: No — Audio feedback only; does not change game state or resolve actions.
- Dependencies (project scripts only): (none found)
- Referenced-by: Assets/Scripts/Cities/City.cs, Assets/Scripts/Cities/CityUIManager.cs, Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/Input/UnitSelectionManager.cs, Assets/Scripts/Tutorial/TutorialOverlay.cs, Assets/Scripts/UI/ButtonHoverEffect.cs, Assets/Scripts/Units/Unit.cs
- Responsibility quality: Single
  - Centralized audio playback (UI/SFX/music) behind a singleton.
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: SoundManager.

### Assets/Scripts/Cities/City.cs
- Role: Core
- Turn-outcome relevance: Yes — Recruitment spends gold and spawns units, directly changing actionable pieces on the board.
- Dependencies (project scripts only): GameMode, GridUtils, OwnedSprite, SoundManager, TurnManager, Unit
- Referenced-by: Assets/Scripts/Cities/CityUIManager.cs, Assets/Scripts/Core/GridManager.cs, Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/Input/CityClickHandler.cs, Assets/Scripts/Input/TileHoverManager.cs, Assets/Scripts/Input/UnitSelectionManager.cs, Assets/Scripts/Tutorial/TutorialGate.cs, Assets/Scripts/Tutorial/TutorialOverlay.cs, Assets/Scripts/UI/GameplayUIScaler.cs, Assets/Scripts/Units/Unit.cs, Assets/Scripts/Utilities/GridUtils.cs
- Responsibility quality: Mixed
  - Combines multiple concerns (logic + side effects and/or UI).
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: City.

### Assets/Scripts/Cities/CityUIManager.cs
- Role: Presentation
- Turn-outcome relevance: Yes — UI triggers recruitment and gates which side can act, affecting available actions this turn.
- Dependencies (project scripts only): City, CityClickHandler, GameMode, SoundManager, TurnManager, TutorialGate, UnitUIManager
- Referenced-by: Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/Input/CityClickHandler.cs, Assets/Scripts/Input/TileHoverManager.cs, Assets/Scripts/Tutorial/TutorialOverlay.cs, Assets/Scripts/UI/UnitUIManager.cs
- Responsibility quality: Mixed
  - UI panel state + gameplay action dispatch (recruit) combined.
  - Contains runtime UI discovery logic (bottom buttons root) shared with UnitUIManager.
- Status: Keep
- Pass-1 action:
  - Label for later refactor
- Notes: Primary type: CityUIManager.

### Assets/Scripts/Core/GridManager.cs
- Role: Core
- Turn-outcome relevance: Yes — Initial grid/city spawning defines the playable board state for all turns.
- Dependencies (project scripts only): City, OwnedSprite, TileVisibility, TurnManager
- Referenced-by: Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/Tutorial/TutorialOverlay.cs
- Responsibility quality: Mixed
  - Combines multiple concerns (logic + side effects and/or UI).
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: GridManager.

### Assets/Scripts/Core/TurnManager.cs
- Role: Orchestration
- Turn-outcome relevance: Yes — Owns turn flow, mode rules, and save/load; it determines which side can act and when turns end.
- Dependencies (project scripts only): AIDifficultySelection, City, CityUIManager, ClipboardUtility, GameModeSelection, GameplayUIScaler, GridManager, GridUtils, OwnedSprite, SaveLoadRequest, SoundManager, TileHoverManager, TileVisibility, TutorialGate, TutorialLaunch, TutorialOverlay, Unit, UnitSelectionManager, UnitUIManager
- Referenced-by: Assets/Scripts/Cities/City.cs, Assets/Scripts/Cities/CityUIManager.cs, Assets/Scripts/Core/GridManager.cs, Assets/Scripts/Gameplay/AIDifficultySelection.cs, Assets/Scripts/Gameplay/GameMenuActions.cs, Assets/Scripts/Gameplay/GameModeSelection.cs, Assets/Scripts/Input/TileHoverManager.cs, Assets/Scripts/Input/UnitSelectionManager.cs, Assets/Scripts/Tutorial/TutorialOverlay.cs, Assets/Scripts/UI/HotseatTurnOverlay.cs, Assets/Scripts/UI/MainMenuController.cs
- Responsibility quality: Mixed
  - Turn flow + mode rules + UI updates + AI hooks live together.
  - Owns save/load serialization and file I/O in the same class.
  - Also coordinates fog-of-war and autosave triggers.
- Status: Keep
- Pass-1 action:
  - Label for later refactor
- Notes: Primary type: TurnManager.

### Assets/Scripts/Gameplay/AIDifficultySelection.cs
- Role: Data
- Turn-outcome relevance: Yes — Selects AI difficulty consumed by TurnManager, influencing AI turn behavior.
- Dependencies (project scripts only): AIDifficulty, TurnManager
- Referenced-by: Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/UI/MainMenuController.cs
- Responsibility quality: Single
  - Focused on one primary behavior/service.
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: AIDifficultySelection.

### Assets/Scripts/Gameplay/GameMenuActions.cs
- Role: Orchestration
- Turn-outcome relevance: Yes — Save/load and menu actions control persistence and can restore or discard turn outcomes.
- Dependencies (project scripts only): TurnManager, TutorialGate
- Referenced-by: (none found in scripts; may be referenced via scene/prefab)
- Responsibility quality: Mixed
  - Button hooks + scene transitions + runtime-built confirm UI for tutorial leave.
- Status: Keep
- Pass-1 action:
  - Label for later refactor
- Notes: Primary type: GameMenuActions.

### Assets/Scripts/Gameplay/GameModeSelection.cs
- Role: Data
- Turn-outcome relevance: Yes — Selects the mode consumed by TurnManager, changing turn ownership rules.
- Dependencies (project scripts only): GameMode, TurnManager
- Referenced-by: Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/UI/MainMenuController.cs
- Responsibility quality: Single
  - Focused on one primary behavior/service.
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: GameModeSelection.

### Assets/Scripts/Input/CameraController.cs
- Role: Input
- Turn-outcome relevance: No — Camera movement/zoom affects view only, not turn logic or state.
- Dependencies (project scripts only): (none found)
- Referenced-by: Assets/Scripts/Tutorial/TutorialOverlay.cs
- Responsibility quality: Mixed
  - Combines multiple concerns (logic + side effects and/or UI).
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: CameraController.

### Assets/Scripts/Input/CityClickHandler.cs
- Role: Input
- Turn-outcome relevance: Yes - Routes city clicks into city UI, which can enable recruitment actions.
- Dependencies (project scripts only): City, CityUIManager, TileHoverManager, Unit
- Referenced-by: Assets/Scripts/Cities/CityUIManager.cs
- Responsibility quality: Mixed
  - Combines multiple concerns (logic + side effects and/or UI).
- Status: Quarantine
- Pass-1 action:
  - Label for later refactor
- Notes:
  - Duplicates click routing that also exists in TileHoverManager (raycast-based); confirm which path is used in scenes/prefabs.
  - Serialized usage found in: Assets/Prefabs/City.prefab — keep in Pass 1 (deletion deferred) to avoid breaking prefabs.

### Assets/Scripts/Input/TileHoverManager.cs
- Role: Input
- Turn-outcome relevance: Yes — Primary click router for selecting/moving/attacking, which resolves turn actions.
- Dependencies (project scripts only): City, CityUIManager, TileHighlighter, TurnManager, TutorialGate, Unit, UnitSelectionManager
- Referenced-by: Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/Input/CityClickHandler.cs, Assets/Scripts/Input/UnitClickHandler.cs
- Responsibility quality: Mixed
  - Hover/selection visuals plus click routing for units/cities/tiles.
  - Coordinates with UnitSelectionManager and CityUIManager; duplicates some state.
- Status: Keep
- Pass-1 action:
  - Label for later refactor
- Notes: Primary type: TileHoverManager.

### Assets/Scripts/Input/UnitClickHandler.cs
- Role: Input
- Turn-outcome relevance: No - Empty stub; selection is handled elsewhere.
- Dependencies (project scripts only): TileHoverManager
- Referenced-by: (none found in scripts; may be referenced via scene/prefab)
- Responsibility quality: Single
  - Intentionally empty compatibility component.
- Status: Delete-later
- Pass-1 action:
  - Label for later refactor
- Notes:
  - Empty stub kept so prefabs with this component do not break; selection is centralized in TileHoverManager.
  - Serialized usage found in: Assets/Prefabs/Warrior.prefab — keep in Pass 1 (deletion deferred) to avoid breaking prefabs.

### Assets/Scripts/Input/UnitSelectionManager.cs
- Role: Input
- Turn-outcome relevance: Yes — Resolves movement/attack/capture and triggers autosave/turn-end checks.
- Dependencies (project scripts only): City, GridUtils, SoundManager, TileHighlighter, TurnManager, TutorialGate, Unit, UnitUIManager
- Referenced-by: Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/Input/TileHoverManager.cs, Assets/Scripts/Tutorial/TutorialOverlay.cs
- Responsibility quality: Mixed
  - Selection state, movement rules, attack resolution, and UI highlighting intertwined.
  - Directly triggers fog updates, autosave, and city-capture callbacks.
- Status: Keep
- Pass-1 action:
  - Label for later refactor
- Notes: Primary type: UnitSelectionManager.

### Assets/Scripts/Tutorial/TutorialBoot.cs
- Role: Orchestration
- Turn-outcome relevance: No - Tutorial launcher helper; does not participate in normal turn resolution.
- Dependencies (project scripts only): TutorialLaunch
- Referenced-by: (none found in scripts; may be referenced via scene/prefab)
- Responsibility quality: Single
  - Focused on one primary behavior/service.
- Status: Quarantine
- Pass-1 action:
  - No action
- Notes:
  - File contains scene-setup instructions for a TutorialLauncher scene; confirm it is in Build Settings before relying on it.
  - Serialized usage found in: Assets/Scenes/TutorialScene.unity — keep in Pass 1 (deletion deferred) to avoid breaking the tutorial scene.

### Assets/Scripts/Tutorial/TutorialGate.cs
- Role: Orchestration
- Turn-outcome relevance: No — Tutorial-only gating that restricts input but does not resolve outcomes.
- Dependencies (project scripts only): City, TutorialOverlay, Unit
- Referenced-by: Assets/Scripts/Cities/CityUIManager.cs, Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/Gameplay/GameMenuActions.cs, Assets/Scripts/Input/TileHoverManager.cs, Assets/Scripts/Input/UnitSelectionManager.cs, Assets/Scripts/Tutorial/TutorialOverlay.cs, Assets/Scripts/UI/MainMenuController.cs
- Responsibility quality: Single
  - Focused on one primary behavior/service.
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: TutorialGate.

### Assets/Scripts/Tutorial/TutorialLaunch.cs
- Role: Data
- Turn-outcome relevance: No — Tutorial-only cross-scene flag and completion preference.
- Dependencies (project scripts only): (none found)
- Referenced-by: Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/Tutorial/TutorialBoot.cs, Assets/Scripts/Tutorial/TutorialOverlay.cs, Assets/Scripts/UI/MainMenuController.cs
- Responsibility quality: Single
  - Focused on one primary behavior/service.
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: TutorialLaunch.

### Assets/Scripts/Tutorial/TutorialOverlay.cs
- Role: Orchestration
- Turn-outcome relevance: No — Tutorial overlay/scripting; not part of baseline turn resolution outside tutorial mode.
- Dependencies (project scripts only): AIDifficulty, CameraController, City, CityUIManager, GameMode, GridManager, GridUtils, OwnedSprite, SoundManager, SpeechBubble, TileVisibility, TurnManager, TutorialGate, TutorialLaunch, Unit, UnitSelectionManager, UnitUIManager
- Referenced-by: Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/Tutorial/TutorialGate.cs
- Responsibility quality: Mixed
  - Builds UI, defines step content, and scripts gameplay events/spawns.
  - Reaches into many gameplay systems (TurnManager/Grid/Units/UI) from one class.
  - Large surface area makes regressions likely when changing tutorial behavior.
- Status: Keep
- Pass-1 action:
  - Label for later refactor
- Notes: Primary type: TutorialOverlay.

### Assets/Scripts/UI/ButtonHoverEffect.cs
- Role: Presentation
- Turn-outcome relevance: No — UI hover feedback only.
- Dependencies (project scripts only): SoundManager
- Referenced-by: (none found in scripts; may be referenced via scene/prefab)
- Responsibility quality: Single
  - Focused on one primary behavior/service.
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: ButtonHoverEffect.

### Assets/Scripts/UI/GameplayUIScaler.cs
- Role: Presentation
- Turn-outcome relevance: No — Runtime UI layout adjustments only.
- Dependencies (project scripts only): City, Unit
- Referenced-by: Assets/Scripts/Core/TurnManager.cs
- Responsibility quality: Mixed
  - Combines multiple concerns (logic + side effects and/or UI).
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: GameplayUIScaler.

### Assets/Scripts/UI/HotseatTurnOverlay.cs
- Role: Presentation
- Turn-outcome relevance: No — Hotseat handoff UI only; does not resolve moves or combat.
- Dependencies (project scripts only): GameMode, TurnManager
- Referenced-by: (none found in scripts; may be referenced via scene/prefab)
- Responsibility quality: Mixed
  - Combines multiple concerns (logic + side effects and/or UI).
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: HotseatTurnOverlay.

### Assets/Scripts/UI/MainMenuController.cs
- Role: Presentation
- Turn-outcome relevance: Yes — Chooses mode/difficulty and can import saves, which sets the starting state of a match.
- Dependencies (project scripts only): AIDifficulty, AIDifficultySelection, GameMode, GameModeSelection, SaveLoadRequest, TurnManager, TutorialGate, TutorialLaunch
- Referenced-by: (none found in scripts; may be referenced via scene/prefab)
- Responsibility quality: Mixed
  - Menu navigation + layout auto-fit + save import/export/file I/O combined.
  - Owns both presentation concerns and gameplay-start configuration.
- Status: Keep
- Pass-1 action:
  - Label for later refactor
- Notes: Primary type: MainMenuController.

### Assets/Scripts/UI/SafeAreaApplier.cs
- Role: Presentation
- Turn-outcome relevance: No — Screen safe-area layout only.
- Dependencies (project scripts only): (none found)
- Referenced-by: (none found in scripts; may be referenced via scene/prefab)
- Responsibility quality: Single
  - Focused on one primary behavior/service.
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: SafeAreaApplier.

### Assets/Scripts/UI/UnitUIManager.cs
- Role: Presentation
- Turn-outcome relevance: No — Displays unit stats; does not change game rules or outcomes.
- Dependencies (project scripts only): CityUIManager, Unit
- Referenced-by: Assets/Scripts/Cities/CityUIManager.cs, Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/Input/UnitSelectionManager.cs, Assets/Scripts/Tutorial/TutorialOverlay.cs
- Responsibility quality: Mixed
  - Combines multiple concerns (logic + side effects and/or UI).
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: UnitUIManager.

### Assets/Scripts/Units/OwnedSprite.cs
- Role: Presentation
- Turn-outcome relevance: No — Ownership tint/visuals only.
- Dependencies (project scripts only): (none found)
- Referenced-by: Assets/Scripts/Cities/City.cs, Assets/Scripts/Core/GridManager.cs, Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/Tutorial/TutorialOverlay.cs
- Responsibility quality: Single
  - Focused on one primary behavior/service.
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: OwnedSprite.

### Assets/Scripts/Units/Unit.cs
- Role: Core
- Turn-outcome relevance: Yes — Contains combat and per-turn state (moves/attacks), directly affecting board outcomes.
- Dependencies (project scripts only): City, SoundManager
- Referenced-by: Assets/Scripts/Cities/City.cs, Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/Input/CityClickHandler.cs, Assets/Scripts/Input/TileHoverManager.cs, Assets/Scripts/Input/UnitSelectionManager.cs, Assets/Scripts/Tutorial/TutorialGate.cs, Assets/Scripts/Tutorial/TutorialOverlay.cs, Assets/Scripts/UI/GameplayUIScaler.cs, Assets/Scripts/UI/UnitUIManager.cs, Assets/Scripts/Utilities/GridUtils.cs
- Responsibility quality: Single
  - Focused on one primary behavior/service.
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: Unit.

### Assets/Scripts/Utilities/ClipboardUtility.cs
- Role: Data
- Turn-outcome relevance: No — Clipboard helper; does not resolve or change gameplay state by itself.
- Dependencies (project scripts only): (none found)
- Referenced-by: Assets/Scripts/Core/TurnManager.cs
- Responsibility quality: Single
  - Focused on one primary behavior/service.
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: ClipboardUtility.

### Assets/Scripts/Utilities/GridUtils.cs
- Role: Core
- Turn-outcome relevance: Yes — Used to validate occupancy/city presence, which constrains legal moves/recruits.
- Dependencies (project scripts only): City, Unit
- Referenced-by: Assets/Scripts/Cities/City.cs, Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/Input/UnitSelectionManager.cs, Assets/Scripts/Tutorial/TutorialOverlay.cs
- Responsibility quality: Single
  - Focused on one primary behavior/service.
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: GridUtils.

### Assets/Scripts/Utilities/SaveLoadRequest.cs
- Role: Data
- Turn-outcome relevance: Yes — Controls whether the next gameplay scene load restores a saved state.
- Dependencies (project scripts only): (none found)
- Referenced-by: Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/UI/MainMenuController.cs
- Responsibility quality: Single
  - Focused on one primary behavior/service.
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: SaveLoadRequest.

### Assets/Scripts/Utilities/SpeechBubble.cs
- Role: Presentation
- Turn-outcome relevance: No — Tutorial/UX text display only.
- Dependencies (project scripts only): (none found)
- Referenced-by: Assets/Scripts/Tutorial/TutorialOverlay.cs
- Responsibility quality: Mixed
  - World-space UI construction + text sanitization + lifetime management combined.
- Status: Keep
- Pass-1 action:
  - Label for later refactor
- Notes: Primary type: SpeechBubble.

### Assets/Scripts/Utilities/TileHighlighter.cs
- Role: Presentation
- Turn-outcome relevance: No — Tile highlight visuals only.
- Dependencies (project scripts only): (none found)
- Referenced-by: Assets/Scripts/Input/TileHoverManager.cs, Assets/Scripts/Input/UnitSelectionManager.cs
- Responsibility quality: Single
  - Focused on one primary behavior/service.
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: TileHighlighter.

### Assets/Scripts/Utilities/TileVisibility.cs
- Role: Core
- Turn-outcome relevance: Yes — Stores fog-of-war state that determines what tiles/units are visible to the active side.
- Dependencies (project scripts only): (none found)
- Referenced-by: Assets/Scripts/Core/GridManager.cs, Assets/Scripts/Core/TurnManager.cs, Assets/Scripts/Tutorial/TutorialOverlay.cs
- Responsibility quality: Single
  - Focused on one primary behavior/service.
- Status: Keep
- Pass-1 action:
  - No action
- Notes: Primary type: TileVisibility.


