# Local ML-Agents training MVP

The owner approved implementation on this Mac on 2026-10-07, including continuous live self-play, checkpoint saving/resuming, local inference, and a configurable total training-artifact budget of 20 GB. Training matches run automatically after Start; comparison tournaments still require separate owner approval. The Mac training loop, checkpoint resume, and local model playtest have been validated. Opponent strength and physical phone/browser inference remain unverified.

## Implementation plan

Use Unity ML-Agents 4.0.x with the matching Python trainer 1.1.0. Keep Python dependencies in a project-specific environment outside Assets and generated results outside Git. The initial target is one visible two-seat 11×11 arena and a small numeric-observation policy.

Extract the existing fair seat observation and authoritative action execution as shared services used by Hard and training. Add an explicitly configured externally driven match path to TurnManager, bounded to training scenes: use existing seat progression, income, movement/action resources, recruitment, and capture rules; bypass user save/resume, autosave, ordinary AI scheduling, and human commands. Existing gameplay/saves/PBp retain their paths.

Give each seat one learning Agent. Use an observation/action schema with unit and recruit statistics/capabilities, visibility/memory, and a stable two-stage source/action selection. Mask impossible choices from authoritative legal actions; no implicit actions outside the mask. The first schema supports the 11×11 board and a declared recruit roster capacity, with version checks for exported models. Hidden enemy information is never a policy input.

Use an isolated training scene with explicit references. Reset complete matches with a recorded seed, clear per-seat information memory, and request decisions only for the current owning seat. Genuine captures end both episodes with opposing win/loss rewards. Artificial round/action limits interrupt episodes and are recorded separately from wins/draws. Use a mixed curriculum: four in five openings start with varied roster units and nearby cities, increasing their distance after a 100-game capture-rate check; one in five uses the ordinary full opening. Curriculum episodes stop at 30 rounds, full openings at 100. Both use the full authoritative rules/action path. Capture rate measures task completion, not strength against a human or an existing opponent.

A native arm64 training player is also available for lower Editor overhead and unattended runs. The Editor remains the control surface; the standalone player renders the current board. Unity CLI validation uses an isolated project snapshot when the live Editor cannot refresh, never a second Editor against the same checkout.

An Editor training window launches the local Python supervisor and starts the wired scene. Show the current live board automatically; successive matches need no confirmation. Include Start/Resume/Stop, duration, seed, storage used/limit/free space, games/actions/rate, checkpoint/model selection, and local inference playtesting. Pin user-selected models and preserve the latest resumable checkpoint.

The supervisor owns an editable total 20 GB budget across training runs, bounded logs, checkpoint retention, reserved write space, a free-disk guard, and graceful shutdown. Keep the latest checkpoint and active inputs; remove only eligible generated artifacts in owned run directories. Use a temporary macOS system-sleep assertion tied to the run, support screen locking, and restore background execution when leaving training. Keep the lid open and power connected for overnight runs.

ML-Agents can reset the environment when switching the self-play training seat. Discard that partial match and its in-flight decision, clear seat knowledge/source selection, and start a fresh authoritative board on the next frame. Record trainer resets separately from completed matches and capture rewards. Batch validation/build Editors must not adopt or stop an independently running training session through shared Editor preferences.

## Dependencies

Python 3.10.1–3.10.12 is required by trainer 1.1.0. The Mac already has native Python 3.10.4; the isolated environment is `/Users/Jo/.local/share/blocknations-ml/venv`. Install with `uv pip install --python <environment>/bin/python --override Tools/Training/dependency-overrides.txt -r Tools/Training/requirements.txt`.

The grpcio override follows Unity's official release_23_tag dependency ceiling (1.53.2). Its native macOS universal wheel avoids compiling the stale PyPI dependency ceiling of 1.48.2. This is a scoped environment override, not a global package/config change.

## Acceptance checks

- Encoding/decoding and masks include every representable legal action, reject incompatible capacity/schema, and isolate hidden information.
- Training uses authoritative seat progression, recruitment/income/action effects, resets resources/memory, and does not touch user saves or PBp preferences.
- Supervisor storage retention, write/free-space reserves, pinned/active files, stop/resume, and process ownership are tested.
- A short actual trainer run collects experience, updates weights, completes/restarts episodes, writes a fresh checkpoint/ONNX model, and reports measured throughput/peak memory.
- Resume a real checkpoint; import its ONNX output and execute a legal local action. Verify a browser inference smoke build when tooling permits before claiming mobile support.
- Inspect the live arena and training controls in Unity. No comparison tournament is part of these checks.
- Commit/push validated changes before starting the authorized overnight run. Record the run id, limits, storage path, process state, and observed progress; do not claim an overnight run succeeded before it finishes.

## Current validation

Validated on the M1 Mac with Unity 6000.4.0f1, native Python 3.10.4, ML-Agents 4.0.3 / trainer 1.1.0, and CPU PyTorch 2.2.2. Fresh local receipts are under ignored `Logs/Validation/MLTraining/batch-20261007/`.

- 70/70 Editor tests and 19/19 PlayMode tests passed with graphics enabled. These include encoding/masks, hidden information, existing Hard/opponent compatibility, authoritative external seat progression/income/resources, spectator visibility isolation, disabled saving, and discarding the SDK's cleared in-flight buffer while accepting a fresh decision after reset.
- 8/8 Python supervisor checks passed, covering owned storage, foreign files/symlinks, latest/pinned checkpoint retention, log limits, and resumed environment/curriculum settings.
- The native arm64 Mac training player built successfully. A real standalone run collected 9,076 decisions across 16 matches and wrote checkpoints. Between steps 1,018 and 3,038, six policy tensors changed (maximum absolute change 0.01263), and TensorBoard reported policy/value losses.
- The Unity window resumed that saved run at step 4,580, continued to 6,642, and saved/closed cleanly with zero rejected actions. Its measured rate was 32.36 decisions/second across 8 further matches (5 captures, 3 interruptions). Initial development found and corrected a false masked-action rejection on SDK shutdown; the corrected stop receipt has no failure.
- The first longer continuation exposed a separate in-flight reset fault at the SDK's self-play team switch at step 10,000. Its saved checkpoint was preserved. After correction, a six-minute run with `team_change: 1024` resumed at 11,619 and saved at 14,891: 6,805 decisions, 3,642 actions, 9 completed matches (4 captures, 5 interruptions), 4 trainer resets, zero rejected actions. It stopped on its duration limit with exit code 0 and no arena failure. Measured throughput was 19.63 decisions/second while validation Editors also ran; sampled trainer/player peak resident memory was 496,877,568 bytes. The standard `team_change: 10000` configuration was restored afterward.
- The 6,642-step ONNX export is 1,802,591 bytes. With the trainer disconnected, local CPU inference executed 2,038 actions / 3,779 decisions with zero rejection. The two full-opening matches reached their 100-round limits, so this is functional validation rather than evidence of competitive strength.
- In the human playtest, city selection, recruitment, ending a turn and receiving the model's reply, and moving/exploring with the human unit worked in the live Game view. The detached phone-shaped preview is resized for laptop inspection.
- No WebGL build module is installed on this Mac. Browser/phone inference, latency, battery use, and comparative strength are still acceptance work before shipping this policy.

## Using the controls

Open **Tools → Block Nations → Local ML Training → Open Controls**. Set a run id (or leave blank for a new one), maximum hours, storage limit, seed, and curriculum. **Start Training** launches successive matches and opens the current board; it is not a tournament or a per-match selection flow. The window shows supervisor status, storage/free space, checkpoint counts, live match statistics, decisions/second, rejected actions, and sampled peak resident memory of the trainer/player processes.

For unattended Mac runs, first use **Build Mac Training Player**, then enable **Use standalone training player** and point it to `Build/LocalTraining.app`. The same Unity window starts/stops the run; the separate player shows the live training board. Editor mode remains available. Background execution is enabled for training, so locking the screen does not pause the match loop. The launcher owns `caffeinate -i -s -w <supervisor pid>` without a display assertion and releases it at the end. Unity's native player may independently keep an idle display awake; manual screen locking is supported. Keep the Mac plugged in with its lid open and leave the training applications running; quitting the control Editor requests a stop. No system power preferences are modified.

**Stop and Save Checkpoint** requests graceful trainer shutdown; let saving finish. **Resume Saved Run** needs the original run id and continues its network/optimizer/trainer configuration, original seed, and curriculum stage. Episode counters restart for each launch; the current partial game is not restored. Select a numbered `.onnx` checkpoint and press **Play Against This Model** after stopping training. This uses a fresh full opening, ordinary human inputs, and local CPU inference without Python. Imported models and their available resumable weights are copied to a content-addressed `pinned/` archive outside ML-Agents' own retention directory. The local imported asset is ignored by Git.

The default Mac storage root is `~/Library/Application Support/BlockNations/Training`. Its 20 GB limit is shared across all owned runs, including checkpoints, pinned models, metrics and logs. Experience buffers live in RAM; full action/snapshot/video archives are disabled. The supervisor stops at 19.488 GB to leave a 512 MB final-write reserve, or earlier if the disk has less than 20 GB plus that reserve free. This is a supervised limit with two-second checks, not a filesystem quota. The small fixed network/buffers and bounded checkpoint/log settings keep normal writes far below the reserve. Python installation, Unity caches, and the built player are outside this training-artifact budget.

The initial policy has schema version 1: a two-seat 11×11 board, 3,120 numeric observations, a 259-choice two-stage source/action interface, and up to 16 recruit definitions. It uses capabilities/costs rather than unit-name features. New abilities that need new observations/actions, a larger board, or a larger recruit roster require an explicit schema version and retraining. These are declared MVP limits, not permanent product/player/team limits. A stronger policy, stable held-out evaluation, multiple parallel arenas, and a lighter simulation core can follow measured learning/throughput results; they are not claimed by this first integration.

## Public map knowledge and live progress

Both starting city locations are public knowledge. The arena supplies immutable opening city records to both policies after placement. Enemy units, gold, recruitment and unseen ownership changes remain concealed from policy observations. Spectator gold is presentation only. Existing checkpoints retain the same tensor dimensions and can resume, but must adapt to this corrected public information contract.

The fullscreen spectator sidebar reserves camera space and scales with display height. It shows both seat balances and the actual opening distance. `fullboard-progress.json` records only completed full-opening matches, excluding curriculum episodes and SDK partial resets. The curve is rolling capture completion over at most 40 matches against training decisions, retaining at most 200 points across resumes. It starts when this tracker is first enabled; older mixed totals cannot supply a full-board breakdown. This measures decisive self-play completion, not playing strength. Fixed-opponent strength requires a separately approved evaluation tournament.

## Fresh small-board experiment (schema v2)

Start with a real 5×5 board and cities at (1,1) and (3,3), one tile inward from opposite corners. Use standard opening gold/income, empty starting cities, the entire official recruit roster, ordinary fog and public starting-city coordinates. The policy chooses recruitment and actions freely. Tactical opening curriculum and automatic winning-move overrides are disabled; rewards remain terminal capture wins/losses. Initial weights are random, in a new run directory.

The v2 policy centers 5×5, 7×7, 9×9 and 11×11 native boards on an 11×11 maximum canvas. Outside cells are unavailable, legal actions retain native coordinates, and both seats use the same mirrored encoding. Tensor dimensions remain 3,120 observations / 259 choices, but schema v2 and behavior `BlockNationsSeatV2` explicitly distinguish this contract from archived v1 weights. Resume keeps the saved run's board size; changing size later needs an explicit continuation experiment under the same v2 policy contract.

The Unity training controls include board size. Standard-opening results are tracked separately per board in `board-N-progress.json`. Human playtests use the exported model's run board size. The v2 Mac player is `Build/LocalTrainingV2.app`; the existing v1 player remains `Build/LocalTraining.app`. The prior v1 run keeps its checkpoints, exports, old chart and a `legacy-supervisor.py` snapshot for reproducing its old environment. Current v2 tools reject v1 model/resume imports rather than silently interpreting old weights under a changed contract. Product saves and PBp formats are unchanged.

Initial run recommendation: one hour with the existing shared 20 GB artifact limit. Observe completed games, turn-limit endings and actual tactical behaviour before claiming better strength. Evaluation tournaments remain separately approved.

Validation for this change: 62 Editor tests, 21 Play Mode tests and 9 Python supervisor tests passed. The separate native arm64 v2 player built successfully. The fresh run `mac-5x5-fresh-20261008` connected as `BlockNationsSeatV2`, began from random weights and completed standard-opening captures with zero rejected actions. These are functional checks, not a strength benchmark.

### Live training rating and seat outcomes

The spectator chart displays ML-Agents' reported **Training Elo**, with the trainer's configured 1200 starting reference and trainer steps on the horizontal axis. This is relative to the evolving self-play opponent pool, not calibrated human Elo or fixed-opponent evaluation. The supervisor backfills available bounded trainer logs, persists `training-elo.json`, and keeps the baseline plus the latest 199 reports across resumes. Missing chart telemetry does not halt training.

Standard-opening outcomes separately show blue wins, red wins, turn-limit interruptions, and the blue/red split among captures. Seat tracking starts when winning-seat telemetry is available; historical records without winners are excluded from that denominator. A limit is not counted as a draw. Capture completion is only the share of matches ending before the limit, not playing strength. Seat splits flag possible bias but changing opponent assignments, learning, and fixed starting positions can also affect them; controlled swapped-position evaluation is needed to isolate causes.

### Recent-match inspection

The live viewer's **Inspect recent games** button plays recorded states from the four latest completed matches, latest first, at 1.2 seconds per action by default. Inspection has pause, single-action step, previous/next game, and 0.6/1.2/2-second speed controls. **Back to Live** returns to the current training board immediately. Training continues independently; replay never executes game actions or feeds observations/rewards back to the trainer. The replay sidebar shows recorded gold and action descriptions; live statistics continue updating separately.

Replays are in memory only and begin after this player starts. Each match is bounded to 256 frames and 8,192 piece records; long matches explicitly report omissions while keeping a final state. The four-match inspection playlist is frozen while viewed, even if newer matches replace the live ring buffer. Returning to live and inspecting again selects newer matches. Trainer resets discard incomplete recordings. Spectator copies support the same selectable fog views as the live viewer, without changing policy visibility.

The development training player uses its own macOS app identifier (`com.blocknations.localtraining.v2`) to distinguish it from shipping and legacy players. The builder restores the project identifier after building.

### Spectator fog views

Live training and recent-match inspection share a **Shared / Blue / Red / All** vision selector, defaulting to Shared. Shared reveals the union of both seats' current sight: dark tiles are unseen by either seat. Blue and Red show only that seat's current visible tiles and conceal units outside them. These individual views are better for inspecting scouting and surprise attacks. All preserves the omniscient view.

Replay frames capture independent visibility masks through the same read-only `ComputeVisibilityForSeat` rules used by seat observations. The viewer draws copies over the camera; it never mutates game visibility, legal actions, observations, or rewards. Fog remembers no explored tiles in this viewer: it depicts current sight. Public starting city coordinates remain marked with a neutral City label under fog, without revealing hidden current ownership. Gold and match descriptions remain clearly labelled spectator statistics. Human policy playtests retain their normal player fog and do not expose this selector.

The masks are memory-only and remain bounded by the existing four-game, 256-frame replay limits; on 11×11 they add at most about 0.5 MB of mask payload across the frozen inspection playlist and live ring. The sidebar scrolls when necessary at smaller window sizes.
