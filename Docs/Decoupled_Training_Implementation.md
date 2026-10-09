# Decoupled training and replay viewer

Implemented for the owner's approved goal on 2026-10-09. The same C# rules, schema-2 model/action contract, saved weights/optimizer, fair information, and shared 20 GB artifact budget are retained. This changes development training and its authored `LocalTraining` scene; product scenes, saves and PBp remain outside the change.

## Running and watching

Build through **Tools → Block Nations → Local ML Training → Build Mac Training Player**. This builds both the arm64 Unity viewer/human player and the self-contained .NET 8 simulation worker. Building needs the .NET SDK; running the published worker does not need a separate installed runtime. Python remains the scoped ML-Agents 1.1.0 environment.

In **Open Controls**, **Decoupled C# training** is the default. Choose 1, 2 or 4 **Parallel games**, then start a new run or resume its saved ID. Four is the measured default on this Mac. Resume restores the saved board, seed, network, optimizer and training step; it does not create fresh weights or automatically transfer between boards. The Unity/native scheduling option remains a diagnostic fallback.

The optional viewer opens into a recent completed game at 1.2 seconds per action. It finishes that replay before automatically selecting a newer one. Pause/single-step, previous/next game, and playback speeds remain available. **Watch live training** offers **One game** or **All 4 games** for a four-arena run. All games appear in a 2×2 grid, each with its own match/round and fixed Blue/Red gold rows; **Focus** enlarges that arena. The existing arena selector remains available in One game. **Watch recent games** returns to replay. Shared/Blue/Red/All fog views apply consistently to every panel and only affect presentation. Markers remain visible in slow replay and hidden in live viewing.

**Pause replay** controls playback only. **Pause training** holds every arena. **Stop & save** requests the existing graceful checkpoint stop and works while paused. Closing the viewer leaves learning running; **Show Training Viewer** in the Editor reopens it. Closing the Editor also leaves a decoupled supervisor running. Training time, shared storage usage, run folder, self-play Elo and first/second-player outcomes refresh independently of the displayed historical match. The folder is expandable/copyable in the spectator sidebar.

**Play against Easy / Medium / Hard AI** opens a separate human match against a frozen completed checkpoint. Learning continues independently; opening/closing that match does not change the training pause choice. Stopping the supervisor closes its owned viewer and human player.

## Local difficulty playtest with training stopped

In **Open Controls**, select the saved run and press **Open Easy / Medium / Hard Playtest**. This opens the standalone viewer without starting a learner or C# simulation worker. The overlay puts **Easy / Medium / Hard** at the top; choose a preset and press **Play against … AI**. A separate human match opens. **Back to training** returns to the selector; closing the viewer ends the local session. Training time and rated self-play results do not advance during these playtests. The public browser build is unaffected.

The controller copies the newest completed numbered checkpoint outside ML-Agents' rolling directory to `frozen-playtests/<checkpoint>/`, records its SHA-256 and fixes that source for the entire comparison session. Each human match gets an inference-only scratch copy. No optimizer updates, self-play swaps or rated training games run in that process. The controller uses the existing shared storage allowance, free-disk guard and exclusive root lock; stop and save an active run before opening it. Completed human demonstrations keep their existing recording path and are available for later training, not applied during a frozen comparison.

The initial presets share the same weights and fair seat observations. They match the existing browser sampler: Easy uses temperature 1.8; Medium 0.8; Hard 0.25 and discards actions below 70% of the highest policy probability. Legal masks remain enforced, and similarly preferred choices can still vary. These are **experimental sampling presets**, not measured or calibrated strength bands. A preferred move can be poor when the policy has learned a poor preference; the names alone do not establish a strength ordering.

Validation for this addition is under ignored `Logs/Validation/LocalDifficulty/20261010/`: 44 Python checks passed; 15 Unity human/bridge/layout checks passed, followed by a six-case bridge recheck for all three stopped launch and restored difficulty paths. The rebuilt native player connected Easy, Medium and Hard in an isolated copy of the saved 7×7 run with the same checkpoint SHA-256; returning closed each owned player and released the controller lock. The actual saved actor loaded strictly and produced legal choices for all three presets on a fair shared-simulation recruitment observation, without changing any tensor or completing a game. The selector was inspected in the native window. Native coordinate automation could not target that Unity window; a human click-through and playing-strength comparison remain the owner's playtest. No learning or watched tournament was started by these checks.

The stopped controller can also be opened directly with the configured Python environment:

```bash
/Users/Jo/.local/share/blocknations-ml/venv/bin/python Tools/Training/playtest_controller.py \
  --run "/Users/Jo/Library/Application Support/BlockNations/Training/runs/<saved-run>" \
  --env Build/LocalTrainingV2.app
```

## Shared simulation and learner boundary

| Component | Responsibility |
| --- | --- |
| `Training/Core/SelfPlayArena` | Independent plain C# match, fair observations/action encoding, owning-seat decisions, terminal rewards and interruption semantics. Uses the existing simulation; no Unity dependency. |
| `Tools/Training/SimulationWorker` | One console process with private arenas stepped in parallel, versioned batched pipe transport, match events and bounded spectator traces. |
| `dotnet_environment.py` | Public ML-Agents `BaseEnv`/`EnvManager` adapter. Batches inference/experience for the stock PPO/self-play learner; validates protocol, tensor dimensions, masks, agents and native-process ownership. |
| `rated_training.py` / `match_rating.py` | Whole-match results, immutable weight hashes and separate pending contexts per arena. Interrupted or changed-policy matches are excluded from win/loss Elo. |
| `decoupled_runtime.py` / supervisor | Independent optional viewer lifecycle, controls, sleep assertion, retention, shared storage and graceful save. |
| `TrainingViewer` / `TrainingTraceReader` | Read-only projection of data records to cached authored sprite geometry. Explicit serialized scene references; no environment/Academy initialization. |

One Python learner receives experience from all arenas. Each arena owns its match, RNG, fog history, source selection and agent IDs. Seeds are `seed + arenaIndex * 7919`; agent IDs are `2 * arenaIndex + seat`. First seat is randomized per opening. Shared tensors remain 3,120 observations and 259 masked choices. Both seats receive terminal outcomes, including a loser captured before making a decision. Round limits interrupt with zero terminal reward so PPO can bootstrap; the limit is still 100 complete rounds for standard openings.

The worker never loads Unity, scene objects, physics or assets. Its simulation sources are compiled directly from the same repository files used by gameplay. Unity rendering/frame rate does not schedule worker steps. The first transport uses several independent arenas in one C# process and one batched learner; multi-process/multi-machine transport is a later boundary, not implemented remote training.

## Data, compatibility and limits

`execution-settings.json` records backend, worker count, rules/model versions and seed convention separately from the existing run/trainer plan. Match events carry arena/session identities and policy assignments; concurrent openings do not overwrite one another. Saved native checkpoints remain resumable through the same pinned trainer. Starting a new process restarts environment RNG streams; bit-for-bit continuation of games/RNG/self-play history is not claimed.

The worker saves at most four completed spectator games per arena under `workers/<index>/replays/`, each capped at 256 states and 8,192 unit/city records. The final state is retained and omissions are labelled. The viewer imports only the four newest records across workers and keeps its selected replay fixed until completion. A reopened viewer can read existing recent games. Complete spectator gold/units are never policy observations.

Every arena publishes detached live snapshots at most twice per second, whether a viewer is open or closed. Optional live/replay file serialization and writing happen on a separate background thread. Its mailbox holds at most five pending records per arena plus one write in flight; newer records replace pending records for the same output path, and overflow drops the oldest pending output. No rendered-frame acknowledgement, viewer request, or file write is awaited by the simulation. Authoritative match events, experience and checkpoint progress retain their existing paths. Snapshot errors appear as spectator diagnostics without rejecting actions or failing the learner.

The viewer independently reads snapshots at 2 Hz, caches their projections, and rejects stale/different-session records. Current paused snapshots can remain visible. It skips full recent-game reconstruction while live viewing and skips action-marker queries in live projections. Four boards are drawn from cached records using the existing sprite/fog palette, without additional scene simulations or cameras. The optional Mac viewer runs at 15 frames per second with VSync disabled and a lower scheduling priority (`nice +10`). These choices bound presentation work; rendering and training still share the Mac's hardware, so zero resource competition or identical throughput with a viewer open is not guaranteed.

Replay files, match journals, logs and exports remain under the owned training root and count toward the shared allowance. Trainer diagnostics retain the existing bound; viewer diagnostics have a 2 MB rotation bound with at most four older logs. Match journals retain one rotated 8 MiB predecessor. Checkpoint/log retention, the 512 MB final-write reserve, the 20 GB free-disk guard and protected/pinned models retain their prior behavior. Hours zero means no timer; normal retention can keep the run below the allowance indefinitely. The safe storage stop threshold for the current defaults is 19.488 GB, reserving final checkpoint space.

### Checkpoint retention reliability (2026-10-10)

The installed ML-Agents 1.1.0 checkpoint manager deletes older numbered `.pt` and `.onnx` files after each save to enforce `keep_checkpoints: 5`. A supervisor inventory is concurrent with that deletion. On 2026-10-09 at 14:50 UTC, the supervisor enumerated a checkpoint that the trainer subsequently removed, then failed while reading its timestamp. The trainer handled the resulting shutdown and saved step 15,120,003, but the supervisor's final status incorrectly remained `running`.

Storage inventory now caches each surviving regular file's metadata once and tolerates a file disappearing before inspection or deletion. Other filesystem errors remain errors. The supervisor still leaves active-run checkpoint retention to ML-Agents; increasing the storage allowance or disabling trainer retention is unnecessary. Unexpected monitoring failures now publish `failed / supervisor_failure`, request a graceful trainer save, join log capture and release the owned process lock and sleep assertion.

Regression checks reproduce the enumeration/deletion race, deletion after inventory, concurrent log rotation, real permission errors and failure-time checkpoint saving/status/lock release. The original implementation fails the deterministic checkpoint race; the corrected implementation passes it. All 34 supervisor, runtime, run-limit, training-time and frozen-playtest checks pass in the configured Python environment. The existing 7×7 run remains stopped for the owner's requested human playtest; this repair has not been verified by resuming that run.

For an unattended CLI continuation, `--no-auto-viewer` keeps the optional Unity viewer closed while retaining explicit viewer requests and human-playtest availability. It does not change the learner or its model. Completed models selected for releases should be copied outside ML-Agents' rolling checkpoint directory. Supervisor pins cannot prevent the SDK from deleting files inside that directory. Keep those frozen copies inside the owned training budget, or export only the actor into the versioned game release.

## Acceptance evidence

Validation uses the installed Unity 6000.4.0f1 in an isolated snapshot and the scoped Python 3.10 / CPU PyTorch 2.2.2 / ML-Agents 1.1.0 environment. No watched strength tournament ran.

- 45 Python checks passed, including real C# four-arena captures/rewards, zero-decision losers, 100-round interruptions, masks/ownership, pause/resume/close, concurrent rating events, viewer lifecycle, bounded logs and existing storage/time/checkpoint behavior.
- 79 relevant Unity checks passed across the main run and focused spectator recheck. Final viewer/human checks passed as well; one human wiring case was rerun after an unrelated Unity Assistant relay connection error polluted its first test log. These checks cover shared simulation/policy parity, serialized state round trips, cached sprite/fog/marker presentation, replay refresh isolation and the existing human controls. The spectator fixture verifies replay as default, no Academy initialization, live switching, independent replay/training controls, stop requests and authored scene wiring. Its actual Game view capture was inspected.
- The arm64 Mac player and self-contained worker built successfully. A native viewer closed/reopened while the same learner advanced from 51,628 to 56,873 decisions. Pausing held every arena at 60,776 decisions; resuming advanced to 63,981.
- An isolated copy of the existing native 7×7 run resumed checkpoint step 964,627 through the new backend and saved at 989,478. Policy keys remained identical; six policy tensors changed. All 12 optimizer states were retained and their update counter advanced from 22,224 to 22,782. This proves continuation and learning, not playing strength.
- A native frozen human playtest connected while learning advanced from 81,451 to 96,256 decisions. Its copied checkpoint checksum stayed unchanged, no training match journal was emitted in the human session, and returning left training running.
- Stop/save from a paused four-arena run exported checkpoint 1,070,197 and exited with `stopped / user_stop`, code zero. The supervisor-owned native viewer closed as well. A subsequent bounded continuation also stopped/exported successfully.

Local receipts are under ignored `Logs/Validation/DecoupledTraining/20261009/`. Model weights, generated data, native apps and fallback copies remain outside Git.

### Four-board spectator validation (2026-10-09)

- 34 relevant Unity checks passed, including four distinct worker snapshots, a rendered 2×2 Game view capture, Focus/replay switching, shared simulation parity, no Academy initialization, and presentation-only controls. Layout bounds also cover 1/4/8/16 arenas and two laptop viewport sizes.
- A blocked-output check published 10,000 replacement snapshots while the sink remained blocked. The producer finished independently, the queue stayed bounded, and the newest snapshot was retained. Overflow and optional-output recovery checks passed as well.
- 10 Python runtime/environment checks passed against the actual published worker, including all four arenas publishing without a viewer, ignoring an obsolete viewer request, action masks, terminal outcomes, interruptions and owned viewer lifecycle.
- The Mac app and self-contained worker rebuilt and were installed with recoverable prior copies. The same `mac-7x7-20261008T171514Z` run saved gracefully at step 2,787,427 and resumed with four arenas, rules v3, no time limit and the shared 20 GB budget. A later numbered checkpoint reached step 2,829,998; all 12 optimizer states remained present, their update counters advanced from 64,337 to 65,297, and six policy tensors changed.
- The actual native viewer ran at scheduling priority +10. While its process alone was suspended for 12 seconds, training advanced from 84,640 to 98,940 decisions, every arena's live snapshot advanced, training controls stayed unchanged and no action was rejected. The viewer then resumed. This demonstrates that learning does not await the viewer; it is not a controlled throughput comparison. Subsequent connected telemetry reported 127,182 decisions, 4,148 completed matches and no viewer/arena failure. These are installation receipts, not current strength measurements.

Local receipts and the inspected screenshot are under ignored `Logs/Validation/FourBoardViewer/20261009/`. These checks establish presentation isolation and bounded publishing, not a claim of zero CPU/GPU cost or improved playing strength.

## Measured learning throughput

M1 Mac, 16 GB. Each fresh-seed case used the same 7×7 recipe and a 12,000-learner-step budget, including actual neural inference, PPO updates, self-play swaps, transport and replay recording. Viewer absent. These are short samples; training game lengths and other Mac workloads affect the result. Unity validation and other Mac workloads can compete for CPU.

| Parallel arenas | Decisions/s | Measured seconds | Completed games | Rejected actions | Peak trainer + worker RAM |
| --- | --- | --- | --- | --- | --- |
| 1 | 789 | 28.7 | 33 | 0 | 0.430 GB |
| 2 | 851 | 29.3 | 38 | 0 | 0.412 GB |
| 4 | 1,048 | 22.2 | 27 | 0 | 0.446 GB |

These totals include both learning and opponent decisions, rather than counting only optimizer learning steps. Whole-match rating totals exactly matched completed-game totals in all cases. The native scheduling baseline was around 50–60 decisions/s; the new path's short results are roughly 15–20 times that observed rate, not a controlled long-duration speedup guarantee. The resumed older model separately reached about 1,100 decisions/s, and a further session ran around 1,000 decisions/s with its viewer open.

The improvement gives more learning experience per hour. It does not establish a stronger opponent, a fixed-reference Elo, a better exploration schedule, faithful RNG continuation or phone/browser inference acceptance. Those remain in [General AI Training Direction](AI_Training_Direction.md).

## Installed continuation

The rebuilt viewer was installed at `Build/LocalTrainingV2.app`, with the prior native player preserved at `Build/LocalTrainingV2.before-decoupled-20261009.app`. The worker is `Build/TrainingWorker/BlockNations.TrainingWorker`.

The existing `mac-7x7-20261008T171514Z` run resumed its actual step-964,627 checkpoint with four arenas, standard 7×7 openings, seed 42, no time limit and the same shared 20 GB budget. The installed check recorded 215,825 decisions, 1,795 completed games, zero rejected actions and approximately 993 decisions/s with the replay viewer open. A fresh immutable step-1,069,945 checkpoint retained all 12 optimizer states. Shared usage was approximately 0.437 GB. The native viewer reported recent-replay mode and no errors; training remained connected and advancing. These counters are a deployment receipt, not a frozen current status. The supervisor owns its temporary sleep assertion; screen locking is supported with power connected and the lid open.
