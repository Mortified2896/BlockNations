# Overnight AI results, October 15, 2026

The quiet overnight capacity study completed all six arms. It found useful
compact continuations and a small average benefit from greater capacity, but
inconsistent retention of the strongest opponent/starting-role matchup. The
compact seed-84 candidate still loses to ordinary coordinated console counterplay
in both starting roles. Human strength is not fixed, and no model was promoted.
A separate shared-capability architecture has verified PPO/save/resume/export
support. Expert labels on newly generated student/reference experience recovered
most of the teacher's tested strength: 230/256 versus 237/256 on a fresh matched
panel, compared with the earlier initialization's 142/256. It still trails the
strongest archived neural matchup and lacks independent replication, novel-content
and human validation. It remains unselected.

## Completed matched comparison

Every arm continued the same frozen step-163,463,523 parent/optimizer for a target
of one million additional learner steps, with small recorded batch overshoots.
The common recipe used eight arenas, one Torch thread, unchanged core-three
rules, opening, fog, observations, rewards/PPO and strengthened retained pool.
The treatment expanded 128×2 to 256×2 while preserving the initial actor/critic
function and old Adam state. Seeds 84, 85 and 86 used alternating arm order.
Each cell below is 512 completed external-reference games from the same frozen
Hard population; confirmation uses different evaluation seeds.

| Training seed | Compact primary | Wider primary | Compact confirmation | Wider confirmation |
| --- | ---: | ---: | ---: | ---: |
| 84 | 491/512 | 484/512 | 479/512 | 482/512 |
| 85 | 459/512 | 472/512 | 463/512 | 476/512 |
| 86 | 445/512 | 467/512 | 450/512 | 477/512 |

The frozen parent's baseline was 438/512 primary and 428/512 confirmation.
All six arms improved those aggregate totals. The wider-minus-compact paired
mean was **+1.82 percentage points primary** and **+2.80 points confirmation**.
Seed 84 lost primary aggregate strength; seeds 85 and 86 gained it. The strict
three-seed/two-pack positive-paired-means check is false. Exploratory paired
three-seed t intervals are −5.38 to +9.02 points primary and −3.05 to +8.65 points
confirmation (df 2; few independent seeds). These intervals are a limited view
of recipe uncertainty, not evidence that repeated deterministic openings are
IID samples. The experiment does not establish a reliable adoption benefit.

The protected step-162,299,959 Hard actor remains the difficult retained matchup:

| Candidate | Primary, starts first | Primary, starts second | Confirmation, starts first | Confirmation, starts second |
| --- | ---: | ---: | ---: | ---: |
| 84 128×2 | 23/32 | 28/32 | 19/32 | 20/32 |
| 84 256×2 | 12/32 | 25/32 | 11/32 | 26/32 |
| 85 128×2 | 3/32 | 17/32 | 4/32 | 19/32 |
| 85 256×2 | 15/32 | 24/32 | 16/32 | 29/32 |
| 86 128×2 | 11/32 | 23/32 | 8/32 | 27/32 |
| 86 256×2 | 9/32 | 26/32 | 16/32 | 25/32 |

The wider model regressed on that first-role primary matchup for seeds 84 and
86 despite its better overall mean. Keep role retention separate from aggregate
wins. Full results for every opponent, role and seed are in the verified
`benchmarks/capacity-night-20261015/morning-report.json` under the shared training
root. The raw packs retain all match records. Observed arm times, including
both evaluation packs, were compact 45.28/34.17/32.94 minutes and wider
56.70/41.44/39.59 minutes. They are operational elapsed times with concurrent
bounded diagnostics, not an isolated inference/throughput benchmark.

All 6,144 arm-evaluation games plus 1,024 parent-baseline games completed without
interruptions. Every arm recorded zero rejected training actions, and all twelve
retained optimizer counters advanced by 23,136. Independent completion checks
strictly loaded each actor/critic, found all 55 checkpoint tensors finite, and
checked all six ONNX graphs and finite initializers. No study power pause was
recorded. These checks do not establish phone/browser acceptance or balance.

## Candidate and counterevidence

Compact seed 84, step **164,463,559**, was the best compact continuation on this
panel: 491/512 primary and 479/512 confirmation. A separate direct Hard challenge
against the protected archive scored 96/128 and 84/128 on new evaluation seeds.
Raw Policy scored 68/128 and 73/128; retain the sampler distinction. The completed
direct criterion did not perform a new population selection or change a champion.
The frozen candidate is already available in the private model catalog.

Two fresh fair console matches, agent-controlled rather than physical human
trials, beat that candidate in both starting roles, in rounds 6 and 7. Their
141 total choices, observation vectors, masks, legal prefixes and final outcomes
were reproduced exactly in the standalone simulator. Coordinated ranged attacks
and city captures remain effective counterplay. The existing recorded defence
case also retains the same mistake under the wider seed-84 model. Neither a
higher benchmark mean nor greater capacity resolves this weakness.

A separate training-only screen gave the existing tactical reference four times
its former work budget: 2,048. The candidate won 123/128, including all games
against its Archer-only version, with zero interruptions. That screen does not
supply the missing coordinated teacher. Keep its results and the console games
separate from frozen acceptance and training data.

The compact candidate's private probability export passed source/checkpoint
checks and Torch parity across all 141 console decisions (maximum absolute error
below 0.000005; masked probabilities zero). It is an offline private artifact,
not a new public release. The protected selected actor remains step 162,299,959.
All these private receipts are under
`benchmarks/capacity-night-evidence-20261015/` in the shared root.

## Reusable representation work

The new [shared capability prototype](AI_Capability_Policy_Prototype.md) implements
shared spatial and recruit-capability scoring, with no unit names, slot embeddings
or purchase quotas. Sixteen checks passed, including 16-slot permutation
properties, actual C# legality under two board/rule profiles, stock PPO updates,
checkpoint/Adam round trips and dynamic-batch probability exports.

Its separate from-scratch feasibility run saved at 8,217 steps and resumed to
16,390. All 135 serialized checkpoint tensors were finite, and all 24 Adam
parameter states advanced by 336 updates on resume. It recorded 29 captures,
three interrupted early-training episodes and zero rejected actions across the
two segments. Its 155,414-byte probability export matched Torch over 64 new fair
C# decisions, with maximum error below 0.00000012. The exact ordinary controller
configuration was restored, and its saved main learner resumed. This is a working
architecture/training path, not a strong new opponent or faithful RNG continuation.

The [teacher-initialization pipeline](AI_Capability_Policy_Initialization.md)
subsequently generated 8,192 new fair training positions and split whole matches
between fitting and validation. It fits teacher probabilities/value, then starts
the new architecture's PPO at step zero with fresh Adam; no MLP tensors are
padded into incompatible weights. The v1 validation KL improved from 0.6562 to
0.1761, but a complete 768-game screen rejected strength preservation: teacher
229/256, distilled v1 100/256, v1 after 8,249 PPO steps 90/256 (one interruption).

A distinct spatial v2 retains full square layout in global context, with 209,773
parameters and explicit architecture identity. The same corpus, seed and fit
budget improved validation KL slightly to 0.1649. Its initialized PPO reached
step 8,203, with 155 finite checkpoint tensors, all 28 Adam states advanced,
189 captures and no interruptions/rejected actions. The complete 1,536-game
sampling comparison still rejected it:

| Candidate | Hard wins | Raw Policy wins |
| --- | ---: | ---: |
| Compact teacher | 229/256 | 137/256 |
| Distilled spatial v2 | 94/256 | 24/256 |
| Spatial v2 after 8,203 PPO steps | 99/256 | 33/256 |

References used Hard in every row. Both colours, starting roles and two seeds
were retained; all raw records, counts, identities, fair worker status and
source hashes were checked. These repeat the teacher conditions deliberately,
not as independent new evidence of teacher strength. Reduced imitation error,
more parameters and completed PPO do not qualify an architecture for adoption.

The optional teacher-corpus generator now records an explicit deployment sampler
and bounded action noise. The separate Hard-teacher trial uses 16,384 fresh fair
positions and 5% legal noise; its corpus completed 324 games without interruption
or rejection. Its selected fit achieved 91.9% internal multi-choice top agreement,
but a complete fresh-seed 1,280-game screen still rejected useful strength
preservation. The teacher won 234/256 Hard games, the prepared model 145/256 (one
interruption) and its 8,217-step PPO child 119/256. Raw Policy results were 109/256
and 81/256 for those two new models. All raw records, identities and per-role
counts were independently checked. Hard PPO's first/second split was 24/128 and
95/128. This combined data-distribution/fit-budget change is not an isolated
causal test, and lower internal error does not establish human competence.

The optional capability PPO now supports immutable copied retained leagues,
explicit conservative/feasibility recipes and arena counts. Thirty-two focused
checks passed, including real C# integration and copied-input/tamper checks.
The actual retained eight-opponent pool preflight validated 15 copied files and
four finite frozen actors. Its separate gentle eight-arena pilot completed a
real 8,207-step warmup: 155 finite checkpoint tensors, all 28 Adam states advanced,
261 captures, 5,244 challenger decisions across all eight entries and zero
interruptions/rejections. Independent verification of the resumed step-24,988
checkpoint found all 155 tensors finite, all 28 Adam counters advanced from
144 to 528, and unchanged copied inputs/source hashes. It subsequently finished
at 131,112 with all 28 Adam counters at 2,976, actual learning rate 0.000075,
3,416 captures, 80,455 opposing choices and zero interruptions/rejections in the
continued segment. The exact ordinary controller was restored.

Its complete independently verified 1,792-game screen found partial Hard recovery:
teacher 230/256, original initialization 148/256, warmup 191/256 and final 203/256.
Raw Policy fell from 100/256 at warmup to 69/256 at the final checkpoint.
There were no evaluation interruptions. Hard's gains were all second-role:
77/128 first stayed unchanged while second rose from 114/128 to 126/128; the
teacher scored 113/128 and 117/128. Final Archer-reference retention was 42/64
versus the teacher's 64/64. No model was selected. A separate 64-position dynamic
probability-export check passed with maximum error below 0.00000275, masked zero
and finite initializers; this remains a technical private export.

An independent fit diagnostic found 95.2% training top-choice agreement versus
91.9% internal validation for the Hard initializer, despite its much worse full
games. The next corpus therefore asks the teacher for labels on new student
experience, with a retained reference pool and an explicit 50/50 action mixture.
It generated 16,384 fresh positions/249 completed games with no interruption or
rejection and 4,573 reference choices across all eight entries. Independent
inference checked 128 new teacher labels within 0.000004. Thirty-six focused
checks passed for frozen student loading, aggregation/split/partition checks and
real C# integration. The combined 32,768-position fit completed at 4,096 updates,
selecting update 3,840. Its prepared checkpoint passed source/label/aggregate
identity checks, 71 finite tensors, zero PPO steps and empty Adam; dynamic export
parity was within 0.00000513. The exact controller was restored.

The complete fresh 1,280-game comparison showed useful transfer recovery:
teacher Hard 237/256, original initializer Hard 142/256, aggregated initializer
Hard 230/256. Raw Policy rose from 115/256 to 191/256. No games were interrupted.
Both-role Hard improved from 64/128 first and 78/128 second to 111/128 and 119/128,
versus the teacher's 115/128 and 122/128. The aggregate matched 64/64 against both
tactical Warrior/Archer references, but its strongest-neural-archive result was
38/64 versus the teacher's 51/64. This is one fit/seed with a combined data-size
and rollout-distribution change; independent replication, content tests and
ordinary counterplay remain required. It is not a selected model or human fix.

Fresh fair console counterplay also beat the earlier 131,112-step PPO model in
round 6. All 33 agent-controlled choices, 23 AI choices, observations, masks and
outcome reproduced exactly. This is a held-out agent diagnostic, not a physical
human test; the aggregate initializer is being checked separately. All experiments
preserve the selected compact model and original checkpoints. No evaluation or
counterplay journals entered fitting.

Further representation work needs retained opponent/role checks, independently
trained seeds, held-out roster/stat/rule variants and fresh coordinated
counterplay. Never ingest these held-out journals/acceptance packs.
The fixed canvas, two seats, 16 recruit capacity and two-stage action interface
remain explicit model-contract limits. New mechanics need authoritative rules
and observable/action capabilities. No prototype is silently compatible with a
legacy game loader merely because it has an ONNX file.

## Quiet operation and remaining review

Training and feasibility used standalone C# workers and Python, without opening
a Unity Editor, player or viewer. The independent quiet guard continues until
09:00 Hong Kong time: native output-mute readback, keyboard brightness/backlight
zero and 30-second idle dim, with no display/user-activity caffeinate assertion.
The audio driver retained its volume setting; mute succeeded. No instantaneous
Bluetooth-route guarantee is inferred from the 15-second backstop. The source of
game music was removed by headless operation/pre-scene training mute.

At the morning handoff, recheck current process/guard state and shared storage,
then unload only the owned overnight quiet guard after reporting. Preserve its
receipts and leave output muted/keyboard dark until the owner adjusts them.
Public promotion, physical human difficulty, difficulty calibration, intrinsic
starting-role balance and phone/browser inference acceptance remain separate
unmet gates. The wider Unity menu suite also retains its previously documented
unrelated Assistant-connection interruption; the audio tests/native silent build
passed. See [quiet setup and validation](Quiet_Overnight_Training.md).
