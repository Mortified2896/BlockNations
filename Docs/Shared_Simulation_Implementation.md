# Shared C# simulation extraction

Approved overnight implementation scope, 2026-10-09. The extraction is implemented and the existing 7×7 weights have resumed in a validated native player. The previous scene-driven player remains a local fallback outside Git.

## Intended result

One scene-independent C# rules assembly owns occupancy, paths, combat, recruitment, income, turn resources, capture outcomes and seat visibility. Human commands and seat-aware AI commands use a Unity adapter to that assembly. ML-Agents trains directly on its match state, without constructing tiles and units for every match. Unity input, sprites, sound, saves and PBp transport remain adapters. The existing spectator and recent-game replay remain available.

The simulation supports variable board dimensions, seat counts and capability-defined rosters. The current learned policy still has its separately versioned two-seat, 11×11 canvas and 16-recruit-slot contract. New units must not require type-name conditions in the rules kernel.

## Sequence and acceptance

1. Extract pure definitions, state, legal actions and transitions. Test paths, hidden blockers, attack resources, damage, recruitment, income, capture and multi-seat turns.
2. Route human commands, seat-aware AI, recruitment and visibility through a scene adapter. Preserve supported save/PBp formats and their authorization/transport boundaries. Compare legal actions and transition results with characterized existing behavior.
3. Drive the training arena directly from the shared state. Project immutable spectator frames using cached sprite templates, retaining live/replay fog and markers. Maintain fair observations and the existing model input/action dimensions.
4. Validate in an isolated Unity checkout, exercise complete native training matches and checkpoint resume, measure throughput, then deploy and resume the existing 7×7 weights. Keep hours at zero and the shared artifact ceiling at 20 GB. Do not run a watched comparison tournament without Jo's approval.
5. Document actual results and remaining migration boundaries, commit validated changes and push them.

## Characterized discrepancies to resolve

The old human attack consumes one movement step; the seat-aware AI attack does not. Existing reply-search regression tests document ranged firing followed by movement. The shared transition uses each unit's capabilities for its attack/move budgets: an Archer can fire and then move but cannot move and then fire. Following the Warrior rule correction on 2026-10-09, a Warrior can move and then attack, but attacking consumes all remaining movement. Its single attack is spent whether the defender survives or dies. A melee kill still advances the attacker onto the defender's tile as part of that attack; it does not grant another action. `AttackEndsMovement` is a capability, not a type-name branch, and is enabled for the official Warrior. Imported counters from older saves cannot allow a Warrior with a spent attack to move. The kernel, scene adapter, tactical search and learned legal-action masks share this arithmetic.

A hidden blocker during a multi-step human move consumes the attempted step and the remaining attacks; the AI executor only consumed the step. The shared transition uses the human hidden-blocker rule for every caller. These corrections affect behavior but do not change save fields or policy tensor dimensions. Existing learned weights remain tensor-compatible and can resume, although policies trained under the previous movement rule need further learning in the corrected environment. Runtime rules telemetry identifies the Warrior correction as `blocknations-simulation-v2`.

A training round now counts a complete cycle from the randomized starting seat. Previously a Red start incremented the round after its first half-turn, making the nominal limit depend on colour. Product/PBp save round numbering remains compatible with its existing Blue/seat-zero anchor.

Public starting city locations remain available to each policy. Enemy gold and hidden units remain private. Enemy per-turn action counters and recruitment flags must not enter the policy merely because the authoritative simulator knows them. Unit identities in policy observations remain local indices, not persistent identities.

## Operational boundary

Stop Codex development when an available Codex usage window falls below 35% remaining. Leave the validated 7×7 trainer running independently, or cleanly stopped at the shared storage ceiling with a saved checkpoint. Training logs, snapshots and weights remain outside Git. This extraction does not authorize changes to product scenes, UI layouts, network architecture or save-protocol retirement.

## Implemented boundary

| Component | Responsibility |
| --- | --- |
| `Assets/Scripts/Simulation` | Pure definitions, dense occupancy, match state, capability arithmetic, BFS paths, fair legal actions, transitions and turn/capture rules. Its assembly has no Unity dependencies. |
| `SceneSimulationAdapter` | Imports existing Unity objects into the shared state, applies a revalidated command, projects the result and retains sound/capture/UI/save boundaries. Product gameplay still uses this compatibility adapter per query/command. |
| `SimulationObservationSource` | Observer-owned visibility/history, public starting cities, private-information filtering and policy-local unit/action indices. The scene observer delegates to this same implementation. |
| `TrainingArena` | Retains plain match state between decisions for connected training. ML-Agents episodes/rewards, source/action choices and policy dimensions remain the existing adapter. |
| `SimulationReplayProjector` | Caches authored sprite/health/marker geometry once, then creates bounded spectator frames from state. The viewer's omniscient statistics never enter the policy. |
| `Tools/Simulation` | Compiles the same rules and policy sources into a .NET executable without Unity, for reproducible environment benchmarks. |

Human movement/attacks, seat-aware AI execution, older opponents' execution, city recruitment, income and visibility now use the shared kernel. Older opponents retain their candidate-selection policies; their previously inconsistent execution budgets follow the shared rule. Existing save/PBp serialization, migration gates and transport ownership remain adapters; no scene, prefab or persistent gameplay-format change was made. The moved definition/value files retain their Unity meta GUIDs.

The kernel accepts rectangular boards, arbitrary configured seat counts, explicit relationships and capability-defined rosters. Tests exercise a five-seat turn cycle and an unfamiliar custom unit on a 9×6 board. This does not expand the current two-seat learned encoding or implement teams, new mechanics or five-seat PBp. A custom ability still needs an engine rule and an appropriate observation/action contract.

## Validation receipts

Validation used the installed Unity 6000.4.0f1 in an isolated local project snapshot, with graphics for Editor tests. No comparison tournament ran.

- 146/146 Editor tests passed. Eight seeded complete-match fixtures compared legal masks, policy tensors and full state after every command: 2,136 actions, both starters, recruitment, moves, combat, captures, income and round transitions. Cached spectator geometry/fog/markers matched actual scene frames. A human-input fixture verified firing an Archer and then moving it through the shared rule.
- 21/21 focused PlayMode gameplay tests passed, including capture and the existing ML-Agents in-flight reset/shutdown cases. Destruction now clears surviving city links even when a scene reset bypasses `Die()`. Third-seat fog-memory fixtures explicitly configure a three-seat PBp state instead of placing owner 2 in a two-seat VsAI state.
- 29/29 Python checks passed in the installed trainer virtual environment, covering storage retention, continuing learning, match-result rating and training time. Running them with the system Python correctly failed the actual-SDK test because ML-Agents is installed only in the isolated environment; the environment run passed.
- The native arm64 development player built successfully. Live and recent-game replay were visually inspected, including fog, health, remaining-move markers and returning to live while training continued.
- Graceful stops exported saved weights and exited with code zero. The direct player resumed step 112,439; after validation it saved at 121,991 and the scheduling build resumed that exact checkpoint. Its fresh 124,984 checkpoint changed six policy tensors (maximum absolute change 0.00791249) and six critic tensors. This confirms learning updates, not playing strength.

Local test/build receipts are under ignored `Logs/Validation/SharedSimulation/20261009/`. Generated training data, native apps and their local fallback copies are outside Git.

The Warrior action correction was validated separately with 171/171 Editor tests passing, including Play Mode human-input fixtures. Eight pure transition cases cover both seats, moving first or attacking first, and killing or leaving the defender alive. Four Blue human-input cases exercise the actual playtest controls. Regression checks also cover older imported counters, replay markers, tactical lookahead, learned legal-action masks, a custom unit using the same capability, and ranged units retaining their existing budgets. Complete scene/direct parity covered 2,129 actions across eight seeded matches. The native Mac player rebuilt successfully; correction receipts are under ignored `Logs/Validation/WarriorActions/20261009/`.

## Measured performance

M1 Mac, 16 GB, CPU PyTorch 2.2.2 / ML-Agents trainer 1.1.0. These are different measurements and should not be divided into a claimed training speedup.

| Measurement | Observed result | Included work |
| --- | --- | --- |
| Previous native scene-driven trainer, 7×7 | 54.52 decisions/s over 2,108 active seconds; 3,595 completed matches, zero rejection | Actual self-play decisions, inference, PPO updates, communication and viewer |
| Direct C# native trainer with the original two-decision scheduling | 52.49 decisions/s over 365.5 seconds; 641 completed matches, zero rejection | Same actual learning pipeline, compatible continued weights |
| Direct C# native trainer with bounded frame batches, initial sample | 56.35 decisions/s over 110.9 seconds; 208 completed matches, zero rejection | Same pipeline; at most 16 decisions or 25 ms per rendered frame, yielding after the current decision |
| Standalone .NET, 100 random 7×7 games, seed 42 | 41,862 actions in 2.011 seconds, approximately 20,815 actions/s; 99 captures, one limit | Rules, fair observations, action masks and both policy encoding stages; no neural inference/PPO/RPC/viewer |

The extraction removes scene construction/search from training, but this measurement shows no large increase in total learning throughput. Profile neural inference, two-stage decisions, communication, rendering and optimizer time separately before choosing further batching or parallel arenas. The standalone driver allocated approximately 2.52 GB cumulatively during those 100 games; that is allocation traffic, not retained RAM. Its unsupported macOS peak-RSS result is `null`. The native supervisor continues to report real trainer/player resident memory.

The subsequent [decoupled training implementation](Decoupled_Training_Implementation.md) removes Unity scheduling/RPC from the learning loop, supports batched parallel arenas, and supplies a separate replay-first viewer. Its actual-learning benchmarks and resume evidence supersede the native scheduling measurements above for the current training backend.

## Continued overnight run

The resumed run is `mac-7x7-20261008T171514Z`, using standard 7×7 openings, seed 42, randomized first seat and the existing schema-2 weights/optimizer. It began the extraction on `blocknations-simulation-v1`; the Warrior correction rebuilt the player and resumed checkpoint 913,538 on `blocknations-simulation-v2`, with completed matches and zero rejected actions. The untouched human opening was reopened with that frozen checkpoint while training continued. It has no wall-clock limit and uses the same shared 20 GB artifact budget across all runs. Early continuation used approximately 0.418 GB; fresh checkpoints and exports were verified. The supervisor retains the 512 MB final-write reserve and free-disk guard, so the safe stopping threshold is 19.488 GB. Normal checkpoint/log retention can keep usage below that threshold indefinitely; filling the allowance is not required.

The supervisor runs independently of Codex and owns its macOS sleep assertion. Locking the screen or letting the display sleep is supported; keep power connected and the lid open. Stop through **Stop and Save Checkpoint**, wait until `stopped / user_stop`, then close the player. **Pause live run** only pauses gameplay. The native app must be relaunched to load source/build updates; its weights do not reset on resume.

## Remaining long-term work

The pure rules are now the shared authority; product gameplay still imports/projects scene state per command for compatibility. A later persistent in-memory product state can remove that adapter cost without creating a second rules implementation. PBp serialization/header logic and UI/transport authorization retain their supported interfaces.

Policy capacity, faithful random-generator continuation, immutable export/recipe lineage, recurrent information memory, stable held-out evaluation and cross-machine training remain the separately scoped phases in [General AI Training Direction](AI_Training_Direction.md). Runtime telemetry now reports backend/rules version, but old checkpoints/rating history do not become a clean fixed-reference benchmark by continuing on the new environment. Tensor compatibility is preserved; corrected private inputs and turn accounting can change behaviour. Phone/browser inference, battery use and competitive strength still require actual acceptance checks.
