# Controlled information and experience training

The October 13 implementation follows the owner's request to implement the ChatGPT advisory plan. It preserves the existing CPU learner, eight arenas/one thread, selected v2 champion, rules, economy, saved games and public release. New treatments run separately. The unfinished final arm of the previous exploration study was stopped by request; its weights and failed/partial receipt remain preserved, and it is not silently restarted.

## Contracts and fairness

Observation version and action version are separate. All contracts retain the original 259 action indices and two-stage selection semantics.

| Observation version | Size | Additional information |
| --- | ---: | --- |
| 2 | 3,120 | Existing numeric contract |
| 3 | 3,259 | Public first/second starting role and generic attack-commitment capability |
| 4 | 4,712 | Version-3 prefix plus 12 historical sighting channels per canvas tile and a version marker |

Version 4 takes only an already filtered `AIObservation`. It records observed enemy capabilities/health and the sighting's round, clears a remembered location when that tile is visible again, and keeps each seat's history separate. It never receives a live hidden unit, enemy wallet, enemy action counters or persistent unit IDs. Two old sightings may belong to the same unit: these are historical evidence, not a current enemy count or a claimed live position. Age is measured in complete game rounds, capped at 100 and scaled by 100. Memory resets with the match and is updated at the seat's decision boundaries in both training and Unity.

The worker and Python adapter require the selected behavior and tensor size. Frozen v2 challengers receive the unchanged 3,120 prefix of a v3/v4 observation; they do not receive invented suffix fields. Frozen evaluation chooses an environment contract wide enough for both policies and explicitly projects older actors. New models must export with their observation schema and `actionSchema: 2`. Unity validates that manifest and encodes the matching input. Existing v2 release assets remain selected.

Actor, critic and Adam input matrices extend with zeros. All existing values and counters are retained. Migration supports the audited unnormalized, memory-free 128×2 PPO model, and rejects arbitrary architecture or optimizer associations. This is function-preserving initialization, not evidence of stronger play.

## First experiment

`Tools/Training/information_study.py` runs control and treatment arms sequentially, reversing arm order on the middle seed. It uses eight arenas and one explicit PyTorch thread, CPU, identical parent weights/optimizer, unchanged rules/opponents/rewards, and no viewer. Human ingestion is disabled in both arms instead of allowing new recordings to confound their comparison. The original main learner continues independently.

The first experiment compares v2 and v3 from the protected v2 champion. Start with three seeds and 250,000 additional learner steps per arm. An 8,192-step real-PPO run checks integration only; a 2,048-step run can stop before the copied PPO buffer produces an optimizer update. The study re-evaluates the parent with the exact new worker before evaluating its candidates. Full benchmark records, per-role results, source/worker/recipe pins, actual optimizer advances, interruptions and decision audits remain in the shared root. The study never promotes or publishes an actor.

```sh
python Tools/Training/information_study.py \
  --root /path/to/marked-training-root \
  --parent /path/to/protected-parent/manifest.json \
  --recipe-run /path/to/owned-baseline-run \
  --suite /path/to/preserved-fixed-suite.json \
  --worker /path/to/new-built-worker \
  --destination /path/to/marked-training-root/benchmarks/new-v3-comparison \
  --name new-v3-comparison --treatment observation \
  --steps 250000 --seeds 61 62 63 --hours-per-arm 3
```

The shared 20 GB root, final-save reserve and free-disk guard still apply. Inputs and output must be owned and bounded. A source/worker change, failure, time limit or `stop.request` at the study destination stops owned training with a final save and preserves its evidence. The main learner is not stopped. Trial rates under concurrent main training are not controlled throughput measurements. Restarts still do not restore every SDK snapshot or RNG stream exactly.

After early results, compare full per-opponent/per-role records before committing to million-step continuation. The `--steps` option accepts a bounded budget up to one million for a fresh matched study; it does not relabel a 250,000-step study as a completed million-step experiment. Do not promote on aggregate wins alone or silently weaken the existing champion gate. Human difficulty and actual phone acceptance remain separate checks.

## Subsequent separate treatments

For fair history, first choose and freeze an explicit v3 parent. Use `--treatment history` with that parent to compare v3 and v4. Both start with the same preserved v3 function and optimizer. Do not combine history with a new reward, opponent recipe, recurrent network or game balance change.

For failure-position training, `prepare_episode_starts.py` reads only owned live-training replay files. The C# authoring tool checks a complete standard-opening game and verifies every transition against legal commands. It chooses several reachable losing-seat positions before the capture, preserving command prefixes. At reset, the worker replays those commands and rebuilds each seat's legitimate observations/history. No omniscient observation snapshot is used as policy input. The initial recipe uses 15% such starts and 85% normal openings, with a maximum permitted fraction of 20%. These outcomes are marked as curriculum and excluded from whole-standard-game Elo; they retain the normal 100-round guard. Decision histograms count only the played continuation, not the replayed prefix.

```sh
python Tools/Training/prepare_episode_starts.py \
  --run /path/to/owned-baseline-run --worker /path/to/new-built-worker \
  --destination /path/to/marked-training-root/benchmarks/new-corpus/episode-starts.json
```

A source replay is not an assertion that a useful defense exists. The corpus is a diagnostic treatment; review/measure counterplay rather than claiming every sampled state is recoverable. Interrupted/truncated/evaluation games cannot enter through this tool. The worker rejects non-legal prefixes and terminal starts. Evaluation does not accept this curriculum. Test it through `--treatment episode-starts --episode-starts /path/to/corpus.json`, with the same frozen parent in both arms. Standard opening evaluation still measures whether any gain transfers.

For challenger weighting, `prepare_weakness_diagnostic.py` runs a distinct training-only diagnostic against every recorded challenger, with both starting roles. The optional `--treatment weakness --weakness-diagnostic /path/to/diagnostic.json` retains the 60/40 self-play/challenger split and changes only the challenger weights. Half of the challenger distribution remains uniform; the other half follows smoothed failure rates, with a maximum 25% of the challenger share per entry. The weights remain frozen throughout that study. This is bounded adaptive selection between studies, not changing opponents during an arm.

Held-out acceptance data must never be relabeled as training diagnostics. Existing regression references remain valuable but are development data after repeated recipe selection. Use a separately protected acceptance set and both-role adaptive human games to assess generalization. New human demonstrations remain in their existing private run; the controlled studies do not ingest arriving data.

## Credit assignment diagnostics and conditional work

The worker records bounded histograms of actual selections per seat game/turn, root versus conditional selection counts, conditional End Turns, and executed board actions. `decision_audit.py` reads these counters and the exact saved gamma/horizon. It labels them as all-seat arena decisions, including assigned opponents, rather than PPO learner steps. Counts measure the interface overhead; discount arithmetic does not by itself diagnose a learning failure.

```sh
python Tools/Training/decision_audit.py --run /path/to/owned-study-run --output /path/to/new-audit.json
```

Distillation, new gamma/horizon values, recurrence, a larger or spatial encoder, neural search and MPS are conditional next experiments. This implementation does not activate them without the observations and diagnostic comparisons that the advisory plan calls for. In particular, adding recurrence needs per-seat/arena memory and export support; changing a YAML field alone is insufficient. Historical examples must never enter PPO as fresh on-policy experience.

## Validation

Shared C# checks cover unchanged v2/v3 prefixes, masks/actions across five board sizes, both-seat history, hidden-state invariance, aging, clearing/reset and reachable curricula. Python checks cover the actual worker protocols, legal choices, older-prefix projection, sampling caps and evaluation/curriculum separation. Existing migration tests preserve Adam state and prove new columns can receive updates. Real PPO smoke studies verify saved/resumed optimizer advances and complete balanced fixed packs. Unity checks cover the release manifest and filtered-history boundary, alongside existing learned-action/fairness checks.

Passing integration and export checks does not demonstrate stronger play. Installation, completed study outcomes, and any future promotion are recorded separately from source validation.

### October 13 integration receipts

The full Python regression run passed 193 tests with six opt-in/fixture skips. Ten observation-migration checks passed. The shared .NET checks passed, and the new worker built without warnings/errors. Unity compiled the changed runtime and passed all 27 focused observation, learned-policy and release-contract checks. A trained v3 export then passed 24 actual C# fixture comparisons in Unity CPU inference with maximum probability error `1.788139e-7`. The original release/model bytes and editor-generated project changes were restored afterward; the main release was never changed.

Two one-seed, 8,192-added-step real-PPO integration studies completed. The public-information smoke scored 666/704 external wins for v2 control and 655/704 for v3 treatment. The subsequent history smoke, from that frozen v3 test parent, scored 662/704 for v3 control and 667/704 for v4 treatment. All four arms advanced every one of the twelve retained Adam counters by 144, had finite actor/critic/optimizer states and zero rejected actions, and completed their full balanced reference packs. These are short plumbing checks, not replicated evidence for adopting either representation. Earlier startup and too-short-buffer attempts remain preserved as failed receipts rather than counted as completed strength comparisons.

The corpus tool verified 31 complete owned training games and produced 76 reachable starts. The actual v4 worker selected and replayed those starts successfully in a separate reset check. A distinct diagnostic covered all eight challengers with zero interruptions; its proposed weighting retained the 40% challenger share and respected the 25% within-pool cap. Neither corpus nor proposed weights were applied to the baseline learner. Local XML, logs and export receipts are in ignored `Logs/Validation/InformationTraining-20261013/`; private study/corpus data remain in the guarded shared training root.

### Installed pilot

Source implementation `a5a589d` was merged and pushed to `main`. The separately owned `information-v3-pilot-20261013` study was then launched from the stable information-training checkout. It compares v2 and v3 over seeds 61, 62 and 63, with 250,000 additional learner steps per arm, eight arenas, one PyTorch CPU thread and a three-hour per-arm guard. The repeated parent pack reproduced 659/704 external wins with zero interruptions using the exact final worker and evaluator identity. The first seed-61 control was verified connected and producing decisions/games with zero rejections. This is installation/startup evidence; the six-arm study is still in progress, and no winner or recipe improvement has been established.

Inspect `benchmarks/information-v3-pilot-20261013/study.json`, each arm's `fixed-results/suite.json` and `decision-audit.json` inside the shared root. The main eight-arena baseline remained connected and unpaused, with its original trainer process, zero reported rejections and the unchanged selected step-33,749,992 champion digest. Shared storage at the accompanying check was approximately 6.73/20 GB. No viewer was launched, the public release was not deployed, and the stopped earlier exploration arm remains preserved.
