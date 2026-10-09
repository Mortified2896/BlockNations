# Block Nations MVP Current State

The current product paths are `VsAI` and HTTP `PlayByPost`, with a UITK main menu and gameplay HUD. This is the implemented baseline for returning to development. Future requirements and unresolved work are in [the roadmap](roadmap.md) and [development follow-ups](Development_Followups.md).

Last reviewed: 2026-10-07, against source revision `0d510b3`.

## Platform and scenes

- Unity 6000.4.0f1, as recorded in `ProjectSettings/ProjectVersion.txt`.
- iOS and Android first, with touch interaction and the New Input System.
- Active build scenes: `Assets/Scenes/MainMenu.unity` and `Assets/Scenes/SampleScene.unity`.
- Main menu, top HUD, bottom HUD, unit panel, and city panel use UITK.
- `TurnManager`, `CityUIManager`, and `UnitUIManager` still supply state/actions to gameplay views.
- `Tutorial`, `Hotseat`, `BottomStripController`, and the old gameplay UI route are outside the active product path.

## AI runtime

Experimental Hard Tactician is available in the regular VsAI setup; see [Hard AI design](Hard_AI_Design.md). It has a separate seat-observation/policy boundary, fair visibility, incremental fixed-work search, and an Editor analysis inspector. The watched version-2 comparison produced three wins and one round-100 abort. Version 3 adds generic feature/evaluator separation, bounded observed-opponent reply search, and optional versioned decision records; it has not had a tournament. Human strength, varied-position strength, and physical phone/browser timing remain unverified. Further tournaments require owner authorization.

All selectable game opponents run locally. The subscription/model experiment was retired on 2026-10-07 at the owner's request; its menu, selectors, bridge, remote policy, and live-model tests have been removed. Existing experiment saves resolve their retired opponent to local Hard without changing the save format. The direction is bounded local search followed by offline self-play training of a compact policy/evaluator for on-device inference.

The [local ML-Agents MVP](ML_Training_MVP.md) now provides a separate, explicitly wired development scene with 5×5, 6×6, 7×7, 9×9 and 11×11 boards under learned schema v2 and Editor controls for continuous fair self-play, a shared 20 GB artifact budget, stop/save/resume, and human playtesting of an exported local policy. A real Mac training run updated weights, resumed a checkpoint, and ran the 1.8 MB export through local CPU inference. This is development tooling; the early policy is not promoted to a regular game difficulty, and competitive strength/browser/physical phone acceptance remain unverified.

The [shared C# simulation](Shared_Simulation_Implementation.md) now owns movement, combat, recruitment, economy, capture and seat visibility. Gameplay uses a compatibility adapter for Unity objects; connected training retains plain match state and projects cached spectator frames. The same rules compile and run outside Unity. Save/PBp formats and model dimensions remain compatible; documented attack-budget, private-information and training-round corrections apply to continued play/training. The [decoupled training backend](Decoupled_Training_Implementation.md) now batches independent C# arenas outside Unity into one PPO learner. An optional Unity viewer defaults to recent-game replay; rendering does not schedule training. Short actual-learning benchmarks measured 789/851/1,048 decisions per second with 1/2/4 arenas on this Mac, including inference and optimizer updates. These measure throughput, not opponent strength.

The runtime uses the Baseline profile with `Default` and `RiderFocus` recruit variants, presented as Baseline and Rider Focus. In `VsAI`, the Default preset enables `OffensiveObviousWin`, `ExchangeScoring`, and `DefensiveVeto`; Rider Focus enables `OffensiveObviousWin`. Development AI-vs-AI settings can vary local feature combinations and compare them through batch/tournament tooling and CSV logs.

`LegalActionService` supplies seat-based legal unit/recruit actions. `AICityCaptureTacticalPlanner` searches for bounded tactical capture plans using explicit ownership seats. `AIPostCalculusLocalDecisionHelper` remains an active scoring helper despite its historical name; Calculus is not a selectable live AI profile.

AI orchestration and many decision helpers still live in `TurnManager`. `RunAIForSide`, defense planning, target collection, and visibility retain two-side boolean paths. These need a separately planned transition before expanding AI assumptions to additional participants or teams.

## PBp protocol and participants

- Current snapshot protocol: `5`; supported migration source: `4`. Protocols `3` and earlier are not accepted by the current PBp load gate.
- Current project app version: `1.0.3`. A present snapshot `appVersion` must match the current application version; a missing/blank value still passes a temporary bridge after the protocol gate.
- `GameSave.version = "3"` is a separate legacy save-schema field, not the PBp protocol number.
- Current PBp seat count: **2–4**, clamped in both Unity and `server.js`.
- Snapshots carry `seatCount`, `currentTurnSeatIndex`, `transportSeq`, per-seat gold, per-seat metadata, explicit unit/city ownership, and explored-seat data alongside legacy fields.
- Seat metadata includes `Unclaimed`, `Active`, `Eliminated`, and `Resigned`. Turn progression skips eliminated/resigned seats; an unclaimed seat can still be the waiting turn owner.
- The relay can claim an available seat and return the same claim for an existing player ID. This is not a general team, replacement-player, or join/leave policy.
- There is no team membership model. The user's intended near-term player range is **4 or 5**, with flexible players/teams as a longer-term direction. Five-player support and dynamic team changes are future work.

See [compatibility policy](PBp-Compatibility.md), [migration history](PbP_Migration_Ledger.md), and [the HTTP contract](HTTP_PBp_Transport_Contract.md).

## PBp transport and hosting

`npm start` runs the root `server.js` Node/Express relay. It implements turn submit/fetch, seat claiming, and batched status queries with disk persistence. `server.prod.js` is an older divergent copy lacking seat claiming and current status metadata; it is not the entry point selected by `package.json`.

In-game polling/submission lives in `TurnManager`; the main menu also queries status and fetches newer snapshots. File/in-memory/null transport support still exists for local/development uses. Keep it until removal is explicitly scoped.

The configured default URL is `https://blocknations.moneymattersmedia.com`. On 2026-10-07, two unauthenticated HTTPS health checks from the development machine failed during TLS negotiation. That does not establish the deployed process/version or origin health. Local relay validation is distinct from live acceptance. See [deployment guidance](VPS_Deploy_MVP.md).

## Visibility and combat

Tactical visibility is live and recomputed after movement and other relevant actions. It can shrink during the acting player's turn when a spotting unit moves away. Explored state is visual memory, not tactical visibility; there is no sticky "seen this turn" layer. Movement order therefore affects later targeting and expected Rider surprises.

Combat uses deterministic scaled integer storage with `CombatScale = 10`. Displayed `1.0`, `0.5`, and `0.1` correspond to stored `10`, `5`, and `1`. Protocol 4+ persists `currentHealthUnits`. Keep the displayed decimal rules distinct from their storage representation.

## Profile and display metadata

The generated profile username feature remains implemented but hidden. Multiplayer entry requires a recognizable manually typed name. Typed names are display metadata serialized by seat and used in game cards/details/waiting text; they must not affect identity, ownership, visibility, or transport. Current seat display helpers fall back to labels such as `Player 1` when a name is absent.

## Validation baseline

On 2026-10-07, at source revision `0d510b3`:

- Server lint and syntax checks passed.
- An isolated relay exercised submit, identical duplicate, sequential conflict, claim, and status successfully. Concurrent different submissions for the same game/sequence both returned success: an unresolved overwrite bug.
- The PlayMode adjacent empty enemy city capture test passed, exercising both acting seats and adjacent positions.
- All 11 EditMode tests passed with graphics enabled. The UITK test fixture cannot initialize under `-nographics`.

These checks do not establish complete-match balance, device acceptance, WebGL support, or live client/server compatibility. Local output is under ignored `Logs/RestartAudit/`.

## Large files and change boundaries

Source sizes at this review:

| File | Lines | Main concern |
| --- | ---: | --- |
| `Assets/Scripts/Core/TurnManager.cs` | 9,254 | Turn lifecycle, AI, PBp, save/load, visibility, endgame in one component. |
| `Assets/Scripts/UI/MainMenuUITKView.cs` | 4,692 | Multiple panes, profile, multiplayer, AI experiment controls. |
| `Assets/Scripts/UI/MainMenuController.cs` | 3,608 | Menu orchestration, remote refresh, snapshots, platform integration. |
| `Assets/Scripts/Utilities/AIVsAIBatchRunController.cs` | 2,955 | Evaluation scheduling and reporting; useful for phase 2. |

Plan bounded extractions around the next approved feature. File length alone is not a reason to rewrite gameplay, remove tooling, or change serialized fields. Gameplay UITK views, `HttpTurnTransport`, and `SaveManifestService` also require care when their behavior changes.
