# Shared capability policy prototype

The October 15 overnight investigation now includes an opt-in trainable
spatial/capability policy in [Tools/CapabilityPolicy](../Tools/CapabilityPolicy/README.md).
It implements a bounded part of the adaptable representation in the
[training direction](AI_Training_Direction.md). The original matched capacity
study has completed with its pinned source, worker and conditions.

The current flat MLP assigns separate weights to each input/recruit index. The
new actor shares square processing and recruit-capability processing, then
scores the existing legal action candidates. A recruit's stats and abilities
enter the same function in every slot. No unit names, slot embeddings, purchase
quotas or named-unit rewards enter the model. The 32-channel prototype has
30,821 trainable parameters, including its shared value head.

The prototype consumes the existing fair v4 observations: public information,
current legitimate observations and bounded last-seen enemy history. It retains
the authoritative v2 legal-action mask and indices. It does not add hidden enemy
state, change rules or observation generation, or alter game saves/PBp. Its
distinct `shared-capability-v1` architecture has new weights; MLP padding is not
a valid initialization. Stock PPO/self-play runs through a distinct process-local
trainer registration against audited ML-Agents 1.1.0, with installed SDK files
unchanged.

Sixteen focused checks passed in the installed training environment. They cover
24 different permutations of all 16 recruit slots, equal scores for identical
capabilities in unfamiliar slots, unchanged non-recruit decisions/value under
permutation, padding isolation, finite normalized legal probabilities, malformed
contracts, actual SDK sampling, learning gradients, strict finite actor/value/
Adam validation, and ownership/autostart exclusions. The tests also performed an
actual stock-PPO optimizer update, SDK checkpoint/ONNX save, fresh-policy load and
a second update with advancing Adam counters. Dynamic-batch probability exports
matched Torch at batches 1 and 7. Real C# decisions stayed legal for both seats
on 7×7 core-three and 5×5 four-unit rules.

The separate bounded end-to-end trial completed after the six capacity arms.
Its first target of 8,192 learned steps saved at 8,217, then explicit resume to
16,384 saved at 16,390. It used two C# arenas, one Torch CPU thread, the existing
AC/battery/thermal/storage guards and no viewer. All 135 serialized checkpoint
tensors were finite; all 24 trained parameter Adam counters advanced from 339
to 675 on resume. This verifies model/value/optimizer/step continuation, not
faithful replay of RNG or the earlier opponent-pool trajectory.

The first segment recorded 11 captures and three round-limit interruptions;
the resumed segment recorded 18 captures and no interruptions. Neither rejected
an action. These are untrained/early-training feasibility episodes, not strength
evaluation. Its 155,414-byte probability ONNX matched Torch on 64 newly generated
normal-fog C# decisions from both seats, at dynamic batches 1, 7 and 64, with
maximum absolute error below 0.00000012 and masked probabilities zero. The exact
ordinary auto-start configuration was restored, and its power controller resumed
the saved main learner. Receipts are under
`benchmarks/capability-feasibility-20261015/` in the shared training root:
`verified-8192.json`, `verified-16384.json`, `sequence-status.json` and
`export-parity.json`. This proves feasibility, not stronger play or phone/browser
inference acceptance. Parameter count/export size do not establish inference
latency; the convolution processes many squares with shared weights.

The existing 11×11 canvas, two-seat encoding, 16 recruit slots and two-stage
source/action interface remain explicit limits. The trial records its board and
registered rules profile and rejects incompatible resume; a new ability needs
rules and observable/action capabilities. Permutation equivariance does not
prove strength or unseen-unit generalization. The prototype stays outside the
ordinary frozen-actor catalog and has no automatic promotion or public release
path. A useful next comparison needs suitable initialization, several independent
seeds, retained opponent/role strength, new evaluation seeds, held-out content
and ordinary coordinated counterplay. A short from-scratch feasibility trial
does not replace those checks.

The subsequent [teacher-initialization comparison](AI_Capability_Policy_Initialization.md)
completed its v1 fit/PPO smoke but lost fresh full-game strength, so v1 remains
an unselected prototype. A distinct v2 retains global board layout and a wider
context with shared recruit processing. It has explicit registry/checkpoint
identity and preserves v1 loading. Its controlled transfer also lost full-game
strength: 94/256 Hard wins after fitting and 99/256 after 8,203 PPO steps, versus
the compact teacher's 229/256 on the same panel. Both prototypes remain outside
selection. The initialization note records the complete sampling comparison and
the separate fresh Hard-teacher corpus trial.
