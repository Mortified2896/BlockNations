# Fog versus full-vision AI diagnostic

The October 14 owner request asks whether limited visibility explains weak learned-AI play. This experiment changes the information available to the policy in isolated development matches. The viewer's existing All fog display does not change policy inputs and cannot answer this question.

## What changes

The diagnostic worker can reveal every board tile, unit and city to seat 0, seat 1 or both seats. It uses the existing observation encoding and action indices. Known enemy blockers affect the legal path mask; attacks still need their normal range, turn ownership and attack budget. Execution uses the same diagnostic visibility as legal-action generation. Roster, combat, opening economy, income, rewards, turn progression and terminal limits retain their existing values. Enemy wallets, action counters and persistent unit IDs remain excluded from policy input.

The full-vision observation/execution entry points compile only when `BLOCKNATIONS_TRAINING_DIAGNOSTICS` is defined by the console worker or checks project. Ordinary Unity builds reject a nonzero diagnostic seat mask. The default worker mask is zero, and the Python adapter verifies the worker's echoed mask; an older worker cannot silently ignore a full-vision request.

## Frozen comparison

`Tools/Training/fog_diagnostic.py` evaluates one immutable actor under three conditions:

| Condition | Candidate sees | Reference sees | Question |
| --- | --- | --- | --- |
| `fog` | Normal fog | Normal fog | Current baseline |
| `candidate` | Whole board | Normal fog | Can the current actor exploit an information advantage? |
| `both` | Whole board | Whole board | How does it play when neither player needs to scout? |

Every case uses balanced colours and starting roles. Diagnostic stochastic sampling has a distinct RNG per seat, arena and episode, reset from the recorded seed; a longer match in another arena cannot consume that game's draws. The normal evaluator retains its original sampling streams. The receipts preserve each game's opening identity, winner or interruption, round, final candidate gold and recruitment counts. Pair transitions identify losses converted to wins, wins converted to losses, and turn limits. They do not establish statistical independence: opening geometry is fixed, and repeated deterministic Hard games can be duplicates.

```sh
python Tools/Training/fog_diagnostic.py \
  --root /path/to/marked-training-root \
  --parent /path/to/owned-frozen-actor/manifest.json \
  --suite /path/to/immutable-fixed-suite.json \
  --worker /path/to/diagnostic-worker \
  --destination /path/to/marked-training-root/benchmarks/new-fog-diagnostic \
  --power-controlled
```

A frozen fog-trained actor may encounter unfamiliar inputs when every tile becomes visible. A poor frozen full-vision result therefore does not prove that additional information is useless or that scouting is the only problem. The symmetric comparison also gives references more information. Neither aggregate should be described as a human difficulty score.

## Matched continued learning

The existing information-study runner accepts `--treatment fog`. Control and treatment start from the same frozen actor, critic, Adam state and training recipe. Control retains fog; treatment gives both seats full vision. Both disable arriving human demonstrations. The study uses eight arenas and one PyTorch CPU thread, reverses arm order for the middle seed, and checks actual optimizer advances, finite tensors and zero rejected actions. Freeze a recipe snapshot before starting so the ongoing learner's adaptive recipe cannot change the experiment inputs.

Each resulting actor is evaluated in both normal fog and symmetric full vision against the same fixed references. This produces a two-by-two comparison:

| Trained with | Evaluate with fog | Evaluate with full vision |
| --- | --- | --- |
| Fog | Control strength in the real information setting | Control's ability to use extra information |
| Full vision | Transfer back to normal play | Playing strength after learning without scouting |

```sh
python Tools/Training/information_study.py \
  --root /path/to/marked-training-root \
  --parent /path/to/owned-frozen-actor/manifest.json \
  --recipe-run /path/to/owned-immutable-recipe-snapshot \
  --suite /path/to/immutable-fixed-suite.json \
  --worker /path/to/diagnostic-worker \
  --destination /path/to/marked-training-root/benchmarks/new-fog-study \
  --name new-fog-study --treatment fog \
  --steps 32768 --seeds 73 74 75 --hours-per-arm 1 --power-controlled
```

This is a continued-learning pilot, not a fresh-from-zero training comparison. A short budget tests integration and early adaptation. Larger matched budgets and more diverse openings/opponents would be required before attributing long-term learning limits to fog. Better performance only in full vision points toward information/scouting/memory issues; persistent tactical failures under full vision justify examining policy representation, search, credit assignment, training coverage and opponent selection. Inspect role and opponent cells before interpreting an aggregate difference.

The normal learner continues independently. Studies share the existing 20 GB storage budget and checkpoint reserve. Power-controlled studies save and pause when AC/battery conditions fail; resumption requires the original command with `--resume`. A `stop.request` in the study destination stops its owned trial with a final save. No recurring launcher is installed by this diagnostic.

Both study arms and their frozen manifests carry `diagnosticOnly`. Ordinary evaluation, human-playtest selection, champion promotion and release export reject these diagnostic actors; diagnostic suites are also ineligible for champion promotion. The experiment does not publish a release or change normal saves/PBp.

## October 14 pilot inputs and validation

The pilot freezes selected checkpoint `BlockNationsSeatV2-163463523`, SHA-256 `d22081cb418f604061f3cc931c07a413ad6719e1f9b17bd4fc284949e96716ba`. It uses core-three rules on 7x7 and the eight external cases from the existing reference suite, reduced to eight games per colour/starting-role cell (256 games per visibility condition). The separate recipe snapshot is `runs/fog-recipe-20261014`; it receives no training. The frozen diagnostic is `benchmarks/fog-frozen-20261014`, and the three-seed, 32,768-added-step-per-arm continuation is `benchmarks/fog-training-pilot-20261014`, under the guarded local training root. Private weights and complete match receipts remain outside Git.

The worker built with zero warnings/errors. Shared checks passed default parity, both-seat visibility, private-state invariance, unchanged schema/range, blocker paths and authoritative action execution. A separate build without the diagnostic define passed normal fog parity and rejected full vision. The existing shared information/history/curriculum checks passed. The Python regression run completed 238 checks with seven opt-in or fixture skips, including actual-worker masks, isolated sampling streams, resume preservation and rejection of perfect diagnostic promotion evidence.

### Interrupted runtime receipt

The Mac switched from AC to battery at 80% during the baseline evaluations. The normal learner's existing controller stopped its own run; the new experiment did not override that decision. The frozen normal-fog pack completed: 232/256 wins and zero interruptions. Only the first three candidate-full-vision cases completed (21/32 unrestricted, 15/32 Warrior reference, 23/32 Rider reference, all without interruptions). Those partial cells are preserved but excluded from an aggregate full-vision comparison. The symmetric full-vision baseline for continued training was also interrupted. The study is `paused_power`, with no learner arm started and no optimizer update claimed.

The local ignored `Build/ResumeFogDiagnostic-20261014.command` verifies AC power and at least 80% battery, resumes the frozen comparison, then resumes the existing three-seed pilot with the original inputs and limits. Complete packs are verified/reused; partial packs are retained in interrupted directories. This is an explicit launch command, without a recurring scheduler.

The Unity 6000.4.0f1 EditMode attempt exited 2 before tests could run because the fresh project import reported `UnityEditor.GUID` compilation errors in Unity serialization/ShaderGraph packages. The same package failure was recorded in earlier isolated-project validation. No Unity test pass is claimed; the .NET checks, actual-worker Python checks and package-free ordinary-build compilation passed independently. The generated package-lock/model-metadata changes were excluded from the source change. Logs are under ignored `Logs/Validation/FogDiagnostic-20261014/`.

There is no completed trained fog/full-vision result or established causal diagnosis yet. The frozen normal-fog baseline is the only completed strength pack at this interruption.

## Pure self-play continuation for human strength testing

The owner's follow-up asks whether both players should learn with full-board vision, then face humans under the same visibility. This needs actual continued learning and an equal-information human match. The earlier frozen input experiment does not measure that, and the short pilot retained 40% fixed challengers in its recipe.

A separate study, `benchmarks/fog-selfplay-million-20261014`, is prepared from the same frozen checkpoint 163463523. Its private snapshot `runs/fog-selfplay-recipe-20261014` sets the fixed-challenger probability to **zero** for both arms. The existing ML-Agents learned-opponent snapshot pool remains active, with window 40, 25,000-step snapshot saves and alternating learning seats; pure self-play includes current and historical snapshots of the learning policy. Fixed references remain evaluation opponents only. Adaptive league feedback and arriving human examples are disabled.

The new study compares fog against symmetric full vision over seeds 81, 82 and 83, with **1,000,000 additional learner steps per arm**, eight arenas, one PyTorch thread, unchanged optimization settings and a six-hour guard per arm. Both actors are evaluated under both information settings. This is a larger continuation budget, not a promise that one million steps will produce human-level play. An eventual fresh-from-zero comparison can separately check dependence on the fog-trained initialization if continuation remains inconclusive.

Preparation verified the preserved checkpoint byte-for-byte and checked 4,800 seeded worker/match/learning-seat assignments across the three seeds: none selected a fixed challenger. The protected parent is unchanged. On the fresh check the Mac was on AC at 15%, so the runner recorded `paused_power` before baseline evaluation or learner launch. There are zero completed learning arms. All original study receipts remain separate.

The ignored local `Build/ResumeFullVisionSelfPlay-20261014.command` resumes this exact study at AC/80% using its pinned inputs. It retains the shared storage and saved-stop guards. This launcher is explicit; it does not install a recurring job or replace the normal learner.

Human acceptance requires a frozen full-vision-trained checkpoint, full-board visibility for **both** the human and AI, unchanged range/combat/economy, and games with each starting role. Record complete outcomes and repeated tactical mistakes before claiming human strength. This setup still needs a dedicated private diagnostic human client: the existing ordinary Unity playtest rejects diagnostic actors and uses its normal fog path. The ordinary-playtest/export/promotion guards remain in place. The human client is a later implementation step; no playable full-vision human session or human victory result is claimed by preparing the study.
