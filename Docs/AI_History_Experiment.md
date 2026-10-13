# Replicated enemy-sighting memory experiment

Owner-approved experiment, 2026-10-13. The existing V4 history encoder was
implemented and smoke-tested, but the active learner and playable champion still
used V2. This study measures whether that history representation improves play.
It does not train a no-fog teacher or change the selected playing model.

## Matched design

The protected step-33,749,992 V2 champion is extended into an explicitly prepared
V3 parent with zero new actor/critic input columns and zero new Adam moments.
Actor/value parity and all retained optimizer counters are verified; no extra
learning selects this parent. Both arms start from that same function and state.

- Control: schema V3, public first/second role and attack-commitment capability.
- Treatment: schema V4, the same prefix plus per-seat legitimate enemy sightings
  and sighting age. This is a representation comparison, including the added
  input columns; it does not claim to isolate every effect of network size.
- Training seeds: 71, 72 and 73; 250,000 additional learner steps per arm.
- Eight arenas, one CPU Torch thread, unchanged core-three 7x7 rules, PPO/rewards
  and optimizer. Arm order reverses on the middle seed.
- Both arms use an immutable snapshot of the current training-only challenger
  weights and selected champion, with 50% fixed challengers and 50% self-play.
  Dynamic feedback and arriving human demonstrations are disabled in this study.
- Every arm receives the complete fixed benchmark and a separate confirmation
  seed pack, with both colours and starting roles. The parent is re-evaluated on
  both packs using the exact worker/evaluator before candidates are compared.

The shared 20 GB root, free-disk/save reserve and three-hour active per-arm guard
apply. Source, recipe, parent and worker identities are pinned. The study runs
from a stable isolated checkout so unrelated main-branch changes cannot mix its
evidence. The active main learner continues independently unless its own review
or power guard stops it.

## Power and resume

`--power-controlled` starts/resumes on AC at 80% or above and requests a graceful
save/pause when unplugged or at 75% or below. The controller checks every 15
seconds; an active study checks power every two seconds, including during
evaluation. A user LaunchAgent can resume that exact study with `--resume`.
Completed arms are skipped, unfinished arms retain their weights/optimizer, and
partial evaluation folders are preserved separately before re-evaluation. Resume
rejects changed study/arm inputs. Only whitelisted public execution settings are
saved; inherited credentials are excluded. SDK buffers/RNG streams are not fully
restored, so power interruptions are recorded as a limitation of the comparison.

Before strength testing, shared C# checks verified unchanged prefixes/actions,
both-seat history, hidden-state invariance, sighting age, clearing and resets. A
real 8,192-step-per-arm history smoke saved on a simulated unplug and resumed to
completion. Both arms advanced all twelve Adam counters by 144 with zero rejected
actions. That short smoke validates execution and resume; it is not the strength
result.

## Evidence

The shared study is `benchmarks/history-pilot-20261013/` under
`~/Library/Application Support/BlockNations/Training/`. Its `study.json` reports
state, baseline scores, each completed arm, optimizer advances, power pauses and
aggregate comparisons. Full per-opponent/per-role match records remain in each
arm's `fixed-results/` and `confirmation-results/` folders. An improvement must
repeat across training seeds and survive role/opponent review before any adoption.
Fixed geometry and deterministic repeated lines limit statistical independence;
human difficulty and device inference acceptance require separate evidence.
