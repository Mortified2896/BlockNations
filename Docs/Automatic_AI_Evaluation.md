# Automatic private AI evaluations

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
