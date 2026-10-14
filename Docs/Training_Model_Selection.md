# Private training and model selection

The native training inspector and **Tools → Block Nations → Local ML Training → Open Controls** provide two separate choices: the training run to inspect/manage and the frozen model to face. The model chooser can use checkpoints from another run without switching the active learner.

## Using the chooser

The training section reports the actual running run, any queued choice, its fog setting, saved step and state. Select a run to inspect its own replays, totals and strength receipts. **Start / resume** deliberately chooses the learner. Switching a configured active learner requests a graceful stop and save, verifies successful shutdown, then starts the selected saved run. **Pause / Resume** and **Stop & save** apply to the selected run. Historical runs remain available to inspect and play; a run without a configured launcher cannot be started accidentally.

The installed choices are the existing main fog learner, `selfplay-fog-20261014` and `selfplay-fullvision-20261014`. The two new continuations start from protected checkpoint 163463523 and its retained critic/Adam state, use pure learned self-play, eight arenas and one Torch thread, and have a one-million-additional-step budget and a six-hour session guard. They are independent manual continuations; the earlier three-seed matched study remains separate and is not automatically started or relabelled as completed.

Training starts only with AC power and at least 80% battery, and saves/stops when unplugged or at 75% or below. A selection made below the start threshold is visibly queued. Installing/opening the chooser does not start a learner. The first deliberate **Start / resume** action transfers training scheduling from the previous main-only power controller to the chooser. The previous controller configuration is preserved in `training-selector-previous-power.json`; main weights, optimizer, recipe and champion pointer are not replaced. The same shared 20 GB artifact allowance, final-save reserve and free-disk guard remain enforced by the existing supervisor. Unexpected exits and incomplete saves stop the switch with an error; unrelated supervisors are preserved.

In the model section choose **This run**, **Fog-trained** or **No-fog-trained**, then select an evaluated champion, a completed numbered save, or a frozen checkpoint. It shows the exact model name, step, originating run and training fog setting. A copied parent and retained input opponents are excluded from a continuation's trained outputs. Missing, changed, linked, partial and incompatible artifacts cannot silently fall back to another model. A run with no new completed checkpoint displays **No trained model yet** and cannot supply a playable no-fog actor.

**Match visibility (both players)** is independent of how the model was trained. Selecting a model defaults the match to its training setting. Choose **Fog** or **Full board** explicitly to test transfer. Choose difficulty sampling and **You first / AI first / Random first**, then **Play selected model**. The game sidebar reports the frozen checkpoint, model run, training visibility and actual match visibility. Rematches retain that frozen actor and visibility, while drawing the starting seat again. Playing does not start or pause a learner. The normal match exit closes only its own inference session; Back to training returns to the inspector.

## Full-board private matches

The private native player supports the existing V2, V3 and V4 observation contracts. Before its human agents initialize, it selects the exact behavior name and vector size. V4 history remains seat-local and resets with the match. Full-board human play configures an unpersisted flag on an explicitly external human match before reset. Observation, path masks, attack visibility, authoritative command validation and board rendering all use that same setting for both seats. Range, ownership, combat, economy and private enemy wallets/action counters retain their contracts. Ordinary single-player, saves and PBp cannot enable this flag; WebGL/mobile builds reject full-board diagnostic activation.

The explicit selector may load a diagnostic-trained actor for a private human test. Ordinary champion promotion and public model export retain their diagnostic rejection guards. Selector games, including cross-tests, do not feed the normal fog learner's human demonstration corpus. Feedback reuses the standard shared report form at the top right in the spectator and human views.

## Local implementation and installation

`model_catalog.py` discovers bounded owned artifacts and gives each checkpoint/configuration pair a content-bound identifier. The selector resolves that identifier again, strictly loads the actor and freezes its bytes. The native player must echo the selected observation/visibility contract; an older player that ignores full vision fails explicitly.

`training_selector.py` is an independent local process. It writes the bounded `training-selector.json` catalog and consumes explicit request IDs from `training-selector.request.json`. The native/Editor presentation reads those records; it never constructs arbitrary commands or chooses a replacement actor. The service validates configured argument arrays and process identities, and uses the existing supervisor for owned training. Closing the inspector leaves its selected learner running under the power guard.

Build the diagnostic console worker and private player, then install the owned controls using the scoped training Python:

```sh
python Tools/Training/install_training_selector.py \
  --root "$HOME/Library/Application Support/BlockNations/Training" \
  --player /absolute/path/to/TrainingModelSelector-20261014.app \
  --worker /absolute/path/to/TrainingWorkerSelector/BlockNations.TrainingWorker
```

The installation requires the previously preserved pure-self-play inputs and creates separate owned continuations without learning. The user LaunchAgent `com.blocknations.training-selector` keeps the small control process available across inspector restarts, using Standard process scheduling. Configuration, private weights, sessions and status stay outside Git. It does not publish a release.

## October 14 installation and validation

The chooser is installed on the owner's Mac. The Desktop **Inspect BlockNations Training.command** opens the private native player. Its catalog contains the existing fog champion and completed compatible V2/V3/V4 saves, plus both prepared self-play runs. Neither new continuation has performed a learning update or produced a trained output. A retained initialization checkpoint is deliberately absent from their playable model lists. No no-fog-trained opponent or human-strength result is claimed.

The Python regression run completed 243 tests with seven opt-in skips. The focused selector suite passed all seven tests, including the subsequent failed-save and service-restart guards. Unity EditMode validation passed 97 existing training/observation/scene checks and five new full-vision/agent-contract checks. The diagnostic .NET worker built without warnings or errors and passed the shared visibility, privacy, range, blocker, ownership and default-parity checks.

Actual native human matches selected V2 checkpoint 163463523 and V3/V4 checkpoints 34000006 with Full board for both players. Each echoed the exact selected contract, connected inference, applied two AI actions, and reported zero rejections and no failure. These are connection/action-path checks, not complete matches or evidence that the actors beat humans. The rendered V2 match visibly exposed the whole board and labelled the actor's original fog training separately from the full-board match.

Private receipts and native screenshots are under ignored `Logs/Validation/TrainingSelector-20261014/` and `Screenshots/`. The existing three-seed studies retain their original immutable input/worker/source pins and paused receipts.
