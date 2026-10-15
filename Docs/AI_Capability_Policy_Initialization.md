# Fair teacher initialization for capability policies

The [shared-capability prototype](AI_Capability_Policy_Prototype.md) has a different
parameter layout from the protected flat MLP. Copying or padding MLP tensors is
not a valid transfer. The opt-in initialization pipeline instead fits the new
actor and shared value head to a frozen teacher on newly generated fair game
positions, then starts PPO at step zero with fresh Adam. This preserves available
teacher knowledge approximately; it also inherits teacher weaknesses and needs
new full-game evaluation.

The three development entry points are under
[Tools/CapabilityPolicy](../Tools/CapabilityPolicy/README.md):

1. `collect.py` creates fresh C# normal-fog experience from an immutable, verified
   teacher. Both seats receive their own legitimate v4 observations. Teacher
   policy/value labels use only that teacher's supported observation prefix.
   By default twenty percent of actions are sampled uniformly from all legal
   choices to broaden state coverage. Explicit `--teacher-mode` selects raw
   Policy or the recorded Easy/Medium/Hard inference preset for both labels and
   rollout actions; `--action-noise` chooses a bounded 0–0.2 legal-action noise
   probability. The defaults preserve the original corpus procedure. No unit
   name, purchase quota, hidden state, human
   journal or benchmark outcome determines an action/label.
2. `distill.py` reads only a completed owned training corpus. It fits recorded teacher
   probabilities and fair critic values, records held-out-match metrics, retains
   the best joint validation score and checks its probability export. A prepared
   initializer requires at least a halving of multi-choice validation KL. This
   is an approximation gate, not a playing-strength gate. Value labels remain
   the fair critic of the unmodified teacher; they are approximate values for
   trajectories generated with a changed sampler.
3. `train.py --initialize` validates the explicit architecture/rules/board/width
   and checkpoint hash, requires zero PPO steps and empty PPO Adam state, copies
   immutable initialization inputs into a new run, and loads those weights into
   actual stock PPO. Later `--resume` checks the copied identities and the
   positive-step model/value/optimizer state. It does not require the original
   preparation directory to remain mounted.

The corpus's validation split hashes whole seed/arena/match/reset groups. Rows
from one match cannot enter both training and validation. These are validation
matches for teacher fitting, not acceptance games or independent maps. Metrics
separate forced decisions from root/conditional decisions with multiple legal
choices, so forced EndTurn agreement does not masquerade as a useful fit.
Held-out benchmark/counterplay journals are not accepted as corpus inputs.

Every corpus checks finite v4 vectors, schema markers, legal probability targets,
current-enemy visibility, complete tensor counts, split separation and content
hashes. Compressed arrays cannot use pickle or expand beyond the bounded tensor
allowance. Initializers have distinct owner/architecture identities; corrupt,
incompatible or positive-PPO-step checkpoints cannot be silently relabelled as
fresh prepared weights. Source/worker/teacher identities and package versions
remain recorded. The recovery `progress.pt` is an offline-distillation checkpoint,
not a PPO checkpoint; its optimizer moments never enter PPO.

Use the installed training environment and a new directory for every preparation.
The ordinary learner must first save/stop and its auto-start controller must be
held disabled for the bounded sequence. Preserve and restore its exact
configuration in cleanup, including failure; never race or signal another
process without verifying its identity. The entry points reject a live learner
or enabled ordinary auto-starter. Power, thermal, storage and time limits remain
in force, and all game execution uses the standalone worker without a viewer.

```sh
python Tools/CapabilityPolicy/collect.py \
  --root "$BN_TRAINING_ROOT" --teacher "$BN_FROZEN_TEACHER" \
  --worker "$BN_TRAINING_WORKER" \
  --destination "$BN_TRAINING_ROOT/benchmarks/capability-data-example"

python Tools/CapabilityPolicy/distill.py \
  --root "$BN_TRAINING_ROOT" \
  --corpus "$BN_TRAINING_ROOT/benchmarks/capability-data-example/manifest.json" \
  --destination "$BN_TRAINING_ROOT/benchmarks/capability-init-example" \
  --width 32 --seed 209 --updates 2048

python Tools/CapabilityPolicy/train.py \
  --root "$BN_TRAINING_ROOT" --worker "$BN_TRAINING_WORKER" \
  --initialize "$BN_TRAINING_ROOT/benchmarks/capability-init-example/initialization/manifest.json" \
  --destination "$BN_TRAINING_ROOT/benchmarks/capability-child-example" \
  --steps 8192 --width 32 --seed 210
```

The October 15 pilot's evidence is under
`benchmarks/capability-teacher-init-20261015/` in the shared training root. Its
fresh corpus contains 8,192 decisions: 6,396 training and 1,796 validation,
from 66 and 22 whole-match groups respectively. Its teacher is compact
step 164,463,559 under core-three rules. The first fit selected update 1,792:
multi-choice validation KL fell from 0.6562 to 0.1761, with 72.0% teacher top-choice
agreement. Its explicit initialized-PPO smoke reached step 8,249 with 135 finite
checkpoint tensors, all 24 fresh Adam states advanced, 260 captures and no
interruptions or rejected actions. The exact ordinary controller was restored.
Twenty-two focused checks passed before launch, including initialization copying,
fresh-PPO-state checks, profile/architecture rejection, split leakage, hidden
current enemies, masked labels, evaluation partitions and content tampering.

The subsequent two-seed Hard screen rejected this as a strength-preserving
initialization. Each candidate played four frozen/reference opponents, both
colours and starting roles, for 256 games:

| Candidate | Wins | Interruptions |
| --- | ---: | ---: |
| Protected compact teacher | 229/256 | 0 |
| Distilled capability v1 | 100/256 | 0 |
| Initialized v1 after 8,249 PPO steps | 90/256 | 1 |

All 768 raw match records, roles, counts, sampler/rules and candidate identities
were rechecked. Evidence is `strength-screen-v2/verified.json` under the pilot
directory. The earlier `strength-screen/` retains a report-assembly harness
failure after its first case; those partial games are not pooled into this
screen. No weaker candidate was selected. Lower imitation KL and completed
training therefore do not establish useful transfer.

A separately versioned `shared-capability-v2` now retains the spatial board
layout in global context, while keeping shared recruit processing. Its 209,773
parameters include a layout projection and a wider context; the earlier version
averaged the square embeddings. The architecture registry rejects unknown
versions or incompatible weights. Twenty-eight checks cover both versions,
including v2 PPO/checkpoint/export and real C# legality, while saved-v1 probability
outputs remain compatible. The controlled fit used the same immutable corpus,
seed and update budget under `benchmarks/capability-spatial-init-20261015/`.
Its best update was 1,280, with multi-choice validation KL 0.1649 and 71.6%
top-choice agreement. Its initialized PPO reached step 8,203, with 155 finite
checkpoint tensors, all 28 Adam states advanced by 336, 189 captures and no
interruptions/rejected actions. The exact ordinary controller was restored.
Pass `--architecture shared-capability-v2` explicitly to both distillation and
PPO; an initializer cannot silently change architecture.

Its complete sampling screen ran 1,536 normal-fog games, checking the teacher,
distilled v2 and short PPO continuation with both actual Hard and raw Policy
sampling. References always used Hard; the four references, two evaluation seeds,
colours and starting roles matched the first screen. Results are wins out of
256 per row, with no interruptions:

| Candidate | Hard | Raw Policy |
| --- | ---: | ---: |
| Protected compact teacher | 229 | 137 |
| Distilled spatial v2 | 94 | 24 |
| Initialized v2 after 8,203 PPO steps | 99 | 33 |

All 48 complete result files, 1,536 raw records, per-role counts, identities,
samplers, fair worker status and evaluator source hashes were independently
verified in `sampling-screen/verified.json`. Identical teacher conditions were
repeated; these games are not independent evidence of a new teacher improvement.
Preserving global layout and reducing raw-policy imitation KL did not preserve
playing strength. Neither architecture was selected.

The separate `benchmarks/capability-hard-teacher-20261015/` pilot now tests a
deployment-policy corpus: 16,384 new fair positions, actual Hard labels/actions,
5% uniform legal noise and 4,096 fitting updates for spatial v2. Its corpus
completed with 261 training/67 validation whole-match groups, 324 captures and
zero interruptions/rejections. This deliberately changes corpus size, sampling,
noise and fit budget together; it is a new initialization attempt, not an isolated
causal test. Its fresh experience contains no held-out benchmark or counterplay
journals. Its selected update 2,304 reduced its own multi-choice validation KL
from 1.3231 to 0.2072, with 91.9% teacher top-choice agreement. KL values across
different corpus/sampling targets are not directly comparable. Its initialized
PPO reached step 8,217, with 155 finite checkpoint tensors, all 28 Adam states
advanced by 336, 402 captures and no interruption/rejected action. An independent
input/checkpoint verification and exact-controller restoration succeeded.

The complete fresh-seed screen used 1,280 games on seeds 1,015,011/1,015,012,
the same four reference types, both colours and starting roles. It again rejected
strength preservation:

| Candidate | Hard wins | Raw Policy wins |
| --- | ---: | ---: |
| Compact teacher | 234/256 | — |
| Hard-teacher initialization | 145/256 | 109/256 |
| Initialized PPO after 8,217 steps | 119/256 | 81/256 |

The initialized Hard row had one interruption against the Archer reference;
all other rows had none. Source/actor identities, all 40 complete result files,
raw records, role counts and zero rejected actions were checked in
`sampling-screen/verified.json`. Hard PPO's role split was 24/128 first and
95/128 second, versus the teacher's 114/128 and 120/128. The fit still loses
important full-game decisions. Matching the teacher sampler does not by itself
solve transfer, forgetting or human difficulty. No candidate was selected.

## Retained opponents for longer PPO

The opt-in `--recipe gentle --parallel-games 8 --opponents-from` path now connects
the capability learner to the existing whole-match retained-opponent adapter.
It copies only verified frozen weights/configuration/manifests and a bounded
version-two recipe, checks finite actor identities and matching board/rules/opening,
and binds the copied files to immutable resume identity. At least half the games
remain learned self-play. The challenger replaces only the opposing seat's moves;
the learner keeps its entire legal roster, rewards and fair observations. Private
journals are excluded. Copied inputs survive original-source removal; mutations,
incompatible contracts or unreceipted pools fail closed.

Thirty-two focused checks passed, including actual C# environment checks. The
real eight-entry retained pool preflight validated all four frozen actors and
15 copied files. The separately queued `capability-retained-training-20261015`
pilot then completed a real eight-arena warmup to step 8,207, with 155 finite
tensors, all 28 Adam states at 144, actual saved learning rate 0.000075, 261
captures and zero interruptions/rejections. It recorded 5,244 challenger choices
covering all eight retained policies. It resumed and completed at step 131,112.
The continued segment recorded 3,416 captures, 80,455 opposing choices across all
eight entries and zero interruptions/rejections; all 28 Adam counters reached
2,976 and the actual learning rate remained 0.000075. The exact controller was
restored. A frozen warmup snapshot is preserved
outside ordinary selection. An independent check loaded that snapshot and the
resumed step-24,988 checkpoint: all 155 tensors were finite and all 28 Adam
counters advanced from 144 to 528, while copied initialization/league identities
and every pinned Python source hash matched. Parameter/value/Adam and copied
input continuation do not claim faithful environment/RNG/snapshot trajectories.

The complete fresh 1,792-game comparison checked all 56 result files, raw records,
identities, both roles, source hashes and zero rejected actions. All rows had zero
interruptions on seeds 1,015,021/1,015,022:

| Candidate | Hard wins | Raw Policy wins |
| --- | ---: | ---: |
| Compact teacher | 230/256 | — |
| Original Hard-teacher initialization | 148/256 | 118/256 |
| Retained-pool warmup, step 8,207 | 191/256 | 100/256 |
| Retained continuation, step 131,112 | 203/256 | 69/256 |

Hard improved over the warmup, but the first-role result stayed at 77/128 while
second-role wins rose from 114/128 to 126/128. The teacher was 113/128 and 117/128.
The final candidate's Archer-reference result was 42/64 versus the teacher's
64/64. This is partial recovery, not preserved strength or a solved human
weakness. The different recipe/retained pool, seed and learning budget are not
isolated causal factors. No model was selected. The final probability export
passed batches 1/7/64 on 64 genuine internal positions, finite graph/initializers
and zero masked probabilities, with maximum absolute error 0.00000275. Its
873,315 bytes do not establish native/browser compatibility or device latency.

## Expert labels on student experience

The subsequent opt-in collector accepts `--student` pointing to immutable
prepared weights or a completed positive-step prototype. It checks architecture,
profile, full checkpoint identity and finite weights. An explicit
`--teacher-fraction` mixes teacher/student action probabilities, while the stored
targets always come from the unmodified teacher. Optional `--opponents-from`
uses copied fair references on seat 1; first player is random and both seats'
legitimate observations receive labels. It does not read evaluation journals.

Repeated `distill.py --corpus` arguments aggregate one to four compatible owned
training corpora, up to 65,536 positions. Individual archives retain their 16,384
position/400 MB bounds. The original whole-match partitions are preserved;
duplicate corpora, different teachers/samplers/contracts, evaluation partitions
and cross-source split leaks are rejected. Multi-source identity explicitly
hashes the ordered manifest/content identities; it is labelled separately from
a single NPZ digest. Prepared weights still start PPO with step zero/fresh Adam.
This implements an iterative training-data option motivated by
[dataset aggregation](https://proceedings.mlr.press/v15/ross11a.html), not a proven
convergence or strength guarantee for this game.

Thirty-six focused checks passed, including real C# integration. The actual new
corpus under `capability-rollout-aggregation-20261015` completed 16,384 positions
from 249 games, with no interruption/rejection. Its 50/50 teacher/student Hard
mixture used the frozen step-131,112 student, 5% legal action noise and all eight
retained policies (4,573 reference choices). Independent inference rechecked
128 new teacher labels within 0.000004. The combined 32,768-position fit completed
with the same v2 width/seed/4,096-update budget. Selected update 3,840 achieved
0.2284 multi-choice validation KL and 89.6% top agreement on its broader
validation distribution. These internal metrics are not directly comparable to
the original corpus. Independent verification checked 474 training/107 validation
groups, both source/content identities, teacher labels, and a fresh prepared
checkpoint with 71 finite tensors, step zero and empty PPO Adam. Probability
export parity passed at batches 1/7/64 within 0.00000513. The exact ordinary
controller was restored.

The complete, independently verified 1,280-game matched screen on new seeds
1,015,031/1,015,032 showed substantially better transfer, with no interruptions:

| Candidate | Hard wins | Raw Policy wins |
| --- | ---: | ---: |
| Compact teacher | 237/256 | 133/256 |
| Original Hard-teacher initialization | 142/256 | 115/256 |
| Aggregated student/reference experience | 230/256 | 191/256 |

The teacher's raw-Policy row was independently completed and verified in a
separate 256-game diagnostic on the same seeds/reference pack. Together these
comparisons cover 1,536 completed games without interruption. Sampling presets
remain separate measures; the student's better raw Policy does not imply a
better Hard policy.

Hard first/second results improved from 64/128 and 78/128 to 111/128 and 119/128;
the teacher was 115/128 and 122/128. Warrior/Archer-reference wins rose from
48/64 and 6/64 to 64/64 and 64/64. The strongest neural archive remained weak:
38/64 versus the teacher's 51/64. The extra data and changed rollout/validation
distribution are a combined intervention. This single fit/seed is promising
transfer evidence, not replicated parity, novel-content generalization or a
human-difficulty fix. No model was selected or published.

A fresh fair console diagnostic against the earlier 131,112-step model won in
round 6 using coordinated ranged attacks and a capture. All 33 agent-controlled
choices, observation vectors, masks, 23 AI choices and the outcome were reproduced
exactly. This was not a physical human trial. Its evaluation-only journal is
excluded from both corpora. The aggregate initializer won a separate round-4
diagnostic after the agent moved its city defender away despite revealing an
approaching Rider. Its 12 agent choices, 23 AI choices, observation vectors,
masks and outcome reproduced exactly. This agent mistake and one game do not
establish reliable tactical defense or human difficulty. Both journals remain
evaluation-only and excluded from fitting.

Further strength work needs fresh full-game seeds, both starting roles, retained
opponents and coordinated counterplay. Better teacher approximation does not
establish stronger play or zero-shot content generalization. New unit stats may
need more training; new mechanics still require shared rules and appropriate
observable/action capabilities. The fixed canvas, recruit capacity, two seats,
two-stage actions and local inference acceptance limits remain explicit.
