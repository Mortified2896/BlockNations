# Automatic private AI evaluations

## Direct champion selection correction, 2026-10-14

The installed run now enables `headToHeadPromotion: true`. Champion selection uses
direct games against the exact incumbent, rather than requiring a higher aggregate
score against the older fixed references. The reference pack still runs and is
published for diagnosis. Its regressions do not create a training-review stop in
this mode. Existing manual callers and configurations without this option retain
their earlier gates. An existing review hold requires explicit inspection and
resolution; enabling this option alone does not clear it.

Each candidate plays 128 balanced games against the incumbent in Hard sampling
and another 128 in unmodified Policy sampling. A second pack uses different seeds.
Promotion requires at least 55% wins in every mode/pack, at least 50% wins in each
starting role, and no interruptions. Complete per-match receipts verify both actor
identities, rules, sampling, colours and starting roles. The selection retains
direct evidence paths and hashes; retention protects those receipts and archives
the previous champion. These are empirical requirements on fixed geometry, not
independent statistical trials or a claim of superiority against every opponent.
Nontransitive matchups remain possible, so the reference diagnostics still matter.

The defect was demonstrated with previously rejected checkpoint 163,349,943:
it beat retained checkpoint 33,749,992 in 128/128 Hard games and 107/128 Policy
games. The old gate had no direct incumbent comparison; a reference-score decrease
was being presented as evidence that the incumbent was stronger and had stopped
training after three such decreases.

The complete repaired job then selected the final saved checkpoint 163,463,523.
Its direct results against 33,749,992 were:

| Sampling | Primary pack | Confirmation pack |
| --- | --- | --- |
| Hard | 128/128 wins | 128/128 wins |
| Policy | 84/128 wins | 88/128 wins |

All 512 direct games completed without interruption and every starting role passed.
The same candidate completed all 1,280 reference-pack games, winning 642/704
external-reference games versus the previous incumbent's 659/704. That reference
tradeoff remains visible; it does not overturn the measured direct result.
The incumbent pointer was updated by the verified selection function, without
changing learner weights or optimizer state. The old review and controller fault
receipts were preserved under ignored `Logs/Validation/EvaluationAudit-20261014/`
before resolving the reference-score hold and rearming the configured power
controller after a successful saved shutdown and a fresh AC/80% reading.

Future automatic jobs use the direct criterion. Newly launched private playtests
select the promoted actor; an already-open frozen session keeps its own weights.
This correction establishes stronger direct play against the previous champion;
human difficulty and broader generalization still need their own evidence.

Implemented for the owner's 2026-10-12 request to replace manual checkpoint checks with recurring evaluation and promotion. This changes local training tooling and the private frozen-opponent selection; it does not publish a game release, alter learner weights/recipes, or modify saves/PBp.

The optional `automatic_evaluation.py` scheduler watches **cumulative decisions** in the run's existing `board-7-progress.json`. It starts an immediate evaluation of a recent completed checkpoint, then checks a new checkpoint after each configured interval. The installed interval is **2,000,000 decisions**. A new session's counters do not reset the schedule. Training Elo is not a trigger or promotion criterion because its changing training opponents make it an unreliable strength comparison.

Each job freezes a complete immutable PT/ONNX pair and trainer/recipe evidence, verifies the current evaluator, worker, fixed references, rules and benchmark identity against the selected incumbent, and runs the same 13-case, 1,280-game pack. Both colours and first/second starting roles are balanced. The existing `playtest_champion.select` gate requires no increased interruptions, no fewer wins against any fixed opponent in either starting role, and at least one improvement. Same-policy matches are diagnostic. Regressions preserve the incumbent and record why; incomplete, incompatible or failed evaluations cannot promote a candidate. This finite benchmark does not establish human strength or guarantee that newer models will qualify.

A successful promotion updates the existing `playtest-champion.json` pointer atomically. Subsequent Easy/Medium/Hard human launches load the promoted checkpoint with their existing sampling presets. Already-open games and rematches retain their frozen actor. Sampling presets are not automatically retuned or claimed to have calibrated human difficulty.

Configuration is an optional generated file in the owned run:

```json
{
  "owner": "BlockNations.AutomaticEvaluation.v1",
  "version": 1,
  "runId": "mac-7x7-core3-gentle-20261011",
  "enabled": true,
  "intervalDecisions": 2000000,
  "suite": "/absolute/path/inside/the/training/root/to/fixed-suite.json"
}
```

The explicit suite is source-pinned and must match the incumbent's benchmark. Changing evaluator/worker/reference identity requires re-evaluating both candidates under a new compatible pack, rather than comparing incompatible results. Other boards and observation contracts are not automatically activated.

`automatic-evaluation-status.json` reports the current candidate, job path, last result/reason, cumulative decision milestone and next trigger. Each job retains full match records and an `evaluations/automatic/<id>/job.json` receipt. The scheduler uses an exclusive OS file lock, runs one job at a time, coalesces missed milestones rather than creating a backlog, and avoids repeating the same checkpoint. Recent three completed jobs plus selected evidence are retained; only explicitly marked automatic artifacts are eligible for its cleanup. Manual, pinned and selected models remain protected.

Evaluation shares CPU with learning. Jobs use one PyTorch CPU thread and scheduling priority +10 on macOS. They wait while training is stopped/paused, the supervisor heartbeat is stale, or the shared artifact/free-disk reserve lacks headroom; an active job is cancelled on those conditions or after its 30-minute bound. Cancellation affects only the evaluation process group and leaves partial receipts ineligible for promotion. The shared 20 GB allowance and trainer save reserve still apply.

An evaluator can attach to existing training without restarting it:

```sh
python Tools/Training/automatic_evaluation.py --run /path/to/owned-run --worker /path/to/matching/worker
```

Future supervisors start/reuse this optional scheduler when the configuration is enabled. A separately attached scheduler stays independent and waits across training stops/resumes. Missing/disabled configuration preserves previous training behavior.

## Installed validation, 2026-10-12

The active `mac-7x7-core3-gentle-20261011` run was configured with the exact retained benchmark, and the evaluator attached without restarting its supervisor or learner. Its first full automatic pack evaluated step **77,549,971**: all **1,280 matches** completed. Across the 704 fixed-reference games, the candidate won **536**, versus **659** for incumbent step **33,749,992**. Per-opponent/starting-role regressions rejected promotion and preserved the incumbent. The first decision milestone was 87,626,303; the next trigger is **89,626,303**, then another 2,000,000 cumulative decisions per scheduled evaluation. These are finite benchmark results, not human difficulty measurements.

All **53 targeted Python checks** passed, including real process-lock reuse, cancellation, resumed scheduling, storage guards, artifact retention and the actual champion validation/gate. All **27 Unity viewer/progress/replay checks** passed. The rendered live fixture was inspected with a 1,170,000 total in every board heading and the sidebar. The native inspector was rebuilt as `Build/TrainingInspectionTotalLive-20261012.app`, and the Desktop inspection launcher was updated. Owner training kept eight arenas and the same supervisor/learner processes throughout the update. Validation receipts are in ignored `Logs/Validation/live-training-total-20261012/`; complete automatic evaluation evidence remains under the owned run's `evaluations/automatic/`.

The scheduler was then restarted from the installed main-checkout entrypoint, preserving its candidate result and next decision milestone. The running controller's stable `Build/TrainingPlaytestExitFixed-20261012.app` path was also updated to the identical rebuilt player, after confirming it was inactive, so its future human/viewer launches cannot restore an older counter display. The former app remains recoverable at `Build/TrainingPlaytestExitFixed.before-total-live-20261012.app`. The open inspector and learner were left running during this app replacement.
