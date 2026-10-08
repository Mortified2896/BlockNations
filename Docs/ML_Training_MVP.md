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
