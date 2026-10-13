# Training throughput and evaluated playtest selection

Implemented on 2026-10-12 for the owner's request to accelerate and improve the current local training workflow. The changes affect development tooling and checkpoint selection. Match rules, observations/actions, gameplay scenes, save/PBp formats and the published frozen opponent retain their existing contracts. The approved [tournament-guided continuation](Tournament_Guided_Training.md), added on 2026-10-13, refreshes benchmark baselines automatically and optionally replaces the strict cell gate below with repeated seed packs and bounded regression margins. It also feeds actual training weaknesses into the challenger mix and requests saved stops after repeated measured regressions.

## Frozen AI selection

Previously the training-window human-play button selected the largest completed numbered checkpoint in the selected run. A paired PT/ONNX export at least two seconds old qualified; mutable `checkpoint.pt` and incomplete exports did not. Saving before playing was unnecessary once a completed checkpoint existed. A running human session and its rematches kept the same copied weights. Newer checkpoints were not ranked against stronger historical candidates, and Hard was an inference sampling preset rather than an automatic champion selector.

The optional `playtest-champion.json` now selects a retained checkpoint outside rolling trainer storage. The active and stopped playtest paths prefer this evaluated champion. Runs without a selection retain their latest-completed behavior. Missing, linked, changed or incompatible champion evidence produces an explicit error rather than silently replacing the opponent. The existing policy label includes the checkpoint, difficulty and selection source; `playtest-status.json` and the session receipt record the same information. A diagnostic request can explicitly choose `checkpointSource: "Latest"` without changing the champion.

`playtest_champion.py` verifies run ownership, rules, board/opening, network settings, weight/configuration digests, finite strictly loaded actor weights, completed benchmark metadata and every complete match record. It checks opponent identity, sampling, seeds, balanced colours/starting roles, counts and per-role wins. Initial selection is explicit. Subsequent promotion requires identical benchmark identity and cases, no increased interruptions, no fewer wins against any fixed opponent in either starting role, and at least one improvement. Self-matches remain balance diagnostics and never contribute to the fixed-opponent strength gate. This conservative gate preserves a tested incumbent; finite deterministic test packs do not establish human strength or statistical certainty.

Initialize an already evaluated candidate in its owned run:

```bash
python Tools/Training/playtest_champion.py --run /path/to/owned-run \
  --manifest /path/to/owned-run/frozen-evaluations/milestone-N/manifest.json \
  --evaluation /path/to/owned-run/evaluations/completed-pack/suite.json --initialize
```

For later candidates, use the same command without `--initialize`. It verifies the complete evaluation and rejects regressions before changing the pointer. It never changes learner weights, starts a tournament, or publishes a game release. Rebenchmark the incumbent and candidate together when evaluator source/worker/suite identity changes; do not pool incompatible receipts.

The strongest previously selected core-three candidate is step **33,749,992**, checkpoint SHA-256 `5ca6ac1b0183019a882788522829112fd27f91f74c51cf8679b3dcd77b89666b`. Its completed 13-case pack contains 1,280 games, including 659/704 external-reference wins. The native training-window owner's last recorded Hard playtest before this change instead used step **69,549,980**, with the human first. That newer actor was not the selected historical champion. These are recorded identities, not a claim that every later checkpoint is weaker.

## Parallelism and CPU scheduling

The existing C# worker already supports up to sixteen independent arenas stepped by worker threads; the supervisor and Editor controls now accept 1/2/4/8/16. One PPO learner batches all arenas. Adding independent learners would split experience and consume more CPU; it is a separate controlled-experiment or multi-machine design, not implemented by this setting.

`--torch-threads 1|2|4|8` explicitly sets PyTorch's CPU thread budget after ML-Agents imports, before constructing actors. ML-Agents 1.1.0 sets its own thread count at import, so the prior `OMP_NUM_THREADS=2`/`MKL_NUM_THREADS=2` environment did not prevent it selecting four threads on the eight-core M1. Execution settings record the explicit budget. The supervisor defaults to two threads; the matched current-recipe result supports one thread with eight arenas on this machine. More arenas/threads are options to measure, not universal speed guarantees.

`benchmark_decoupled.py --recipe-run` reads the source checkpoint once, preserves its optimizer/global step in isolated resumptions, and copies only the required opponent evidence. Every case starts from identical weights, optimizer and settings with a matched additional learner-step budget. Source training is untouched. Human recordings and viewer work are excluded. Receipts record source/recipe digests, actual learned steps, arena decisions, captures, interruptions, rated match accounting, memory and wall/arena timing. Repeated runs reverse case order. `--profile` produces a cProfile artifact and function timing summary; profiled samples are diagnostic and excluded from speed comparisons.

```bash
python Tools/Training/benchmark_decoupled.py --worker /path/to/worker \
  --recipe-run /path/to/owned-run --steps 12000 --cases 4:4 8:1 \
  --repetitions 2 --output /path/to/new-throughput-receipt.json
```

On this M1/16 GB Mac, two 12,000-additional-step continuations per case measured:

| Arenas | PyTorch threads | Mean learner steps/s | Mean arena decisions/s | Peak learner + worker RAM |
| --- | --- | --- | --- | --- |
| 4 | 4 | 306.86 | 606.36 | 0.501 GB |
| 8 | 1 | 384.05 | 763.24 | 0.513 GB |

The combined scheduling change improved these matched short-run learner rates by **25.2%**. Both repetitions agreed; no legal action was rejected or completed match omitted. Short 16-arena samples varied more, so eight was selected for this Mac. Concurrent workloads and evolving match lengths affect absolute rates. These results measure end-to-end learning throughput and do not demonstrate stronger play. Receipts are under ignored `Logs/Validation/TrainingSpeed-20261012/`.

## Supervisor housekeeping

The shared training root contained **32,145 files** during inspection; one inventory took **2.72 seconds**. The previous two-second supervisor loop performed separate full inventories for pruning/usage plus repeated run traversals for metadata/checkpoint counts. This work competed with learning as retained experimental evidence accumulated.

The monitor now shares one root inventory across retention, storage accounting and checkpoint/export counts. It groups files by owned run once and subtracts retired logs before deciding whether further model retirement is necessary. It refreshes at most every 30 seconds with more than 1 GB/two-reserve headroom, returning to the previous two-second cadence near the save threshold. Disk-free checks, process/control handling and graceful stop/save continue on the fast loop. Status reports the measurement timestamp and interval. The shared 20 GB budget, 512 MB final-save reserve, free-disk guard, retention protections and explicit permission-error behavior are preserved. This is periodic monitoring rather than an operating-system filesystem quota.

## Strength work still requiring evidence

The owner reported easy wins against a newer training-window actor; human difficulty of the selected historical champion remains unverified. Automated reference wins do not establish human difficulty. The existing human recorder/imitation path is active, but isolated successful imitation updates do not demonstrate generalized counterplay. The documented Hard identical-policy starting-role sweep and sparse unrestricted Archer use remain unresolved. Continue fair learning with held-out opponents, both-role human games, and generic defensive/tactical failure positions. Preserve fixed references and compare recipes through matched continuations. Faster training or a newer checkpoint alone cannot qualify a stronger opponent.

Validation includes the Python retained-opponent, environment, human ingestion, checkpoint/controller, regression-selection and supervisor checks, plus the graphics-enabled Unity training/viewer/human tests. Runtime installation receipts are recorded separately from these source/benchmark checks.

## Installation receipt, 2026-10-12

The scoped implementation was committed and pushed to `main` as `01462e8`. The existing `mac-7x7-core3-gentle-20261011` supervisor saved gracefully at step **70,547,835**, exit code zero. The final numbered pair and resumable checkpoint contained identical actor, global-step, critic and optimizer tensors; their Torch archive bytes differ because their archive names differ. The same run resumed with eight arenas and one PyTorch thread. Trainer configuration, run manifest and opponent recipe hashes remained identical. The owner's existing local resume launcher was updated to these settings and the matching native player, with its prior content retained in the validation folder.

The verified resumed numbered checkpoint reached **70,649,986** with finite actor tensors. All **12** retained optimizer states advanced, by at least **2,352** updates. At the receipt, 2,978 games had completed without rejected actions, storage was approximately **5.40/20 GB**, and training remained unpaused. A short live interval measured approximately 1,252 arena decisions/s; this is current-run telemetry rather than a controlled speedup comparison. Supervisor CPU was observed near 2% after installation; CPU percentages depend on measurement window and other workloads.

The selected champion remains step **33,749,992**. A separate temporary native Hard playtest connected under core-three rules with the exact selected checkpoint SHA-256 and displayed policy identity `BlockNationsSeatV2-33749992 / Hard / evaluated champion`. Main training advanced during that verification. The owned verification player then closed and its temporary data was removed; the owner's inspection viewer remained running. This proves native checkpoint loading/selection and lifecycle, not completion of a human match or human difficulty.

The broad Python run passed **103** checks, with two fixture-dependent skips. The final focused rechecks passed after storage-accounting and evidence-verification adjustments. All **44** graphics-enabled Unity training/viewer/human checks passed. Fresh XML, logs, benchmark receipts, the native connection receipt and runtime/optimizer evidence remain under ignored `Logs/Validation/TrainingSpeed-20261012/`. Generated models, runtime state, native apps and private recordings were excluded from the commit.
