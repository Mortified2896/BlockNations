# Capability policy feasibility trial

This opt-in development policy tests shared spatial and recruit-capability
processing. It does not change game rules, fog visibility, saved games, the
running learner, or selected opponents. Its weights are a new architecture;
legacy MLP checkpoint loading and function-preserving expansion do not apply.

The actor uses the existing fair observation-v4 vector and action-v2 indices.
Two small convolutions process current square capabilities and legitimate
last-seen history. A shared encoder processes every recruit's public capabilities.
Board context, the selected source, available-roster context and public globals
feed shared scores for square targets, recruit candidates and EndTurn. A shared
value head uses permutation-invariant context. No unit names or recruit-slot
embeddings enter the network. The authoritative legal-action mask still decides
which scores can be sampled.

Recruit permutation equivariance is an architectural property tested with all
16 slots, including unfamiliar slot positions. It does not prove good play
with unseen units. The existing 11×11 canvas, two-seat encoding, 16 recruit
capacity and two-stage source/action interface remain explicit constraints.
New abilities require shared rules and observable/action capabilities; an old
model cannot acquire them by accepting compatible tensor dimensions.

The trial entry point accepts an explicit registered `--rules-version` and
`--board-size` (5, 6, 7, 9 or 11). Both enter immutable trial identity. Changing
either starts a separate trial; it cannot silently relabel resumed weights.
The default remains the current 7×7 core-three profile.

`trainer.py` registers a distinct process-local `capability_ppo_v1` trainer
against audited ML-Agents 1.1.0. It uses stock PPO/self-play with the custom shared
actor/critic, without changing installed SDK files or the standard `ppo` entry.
The experimental architecture and checkpoints stay under the shared root's
`benchmarks/` directory, outside the ordinary frozen-model selector. There is no
automatic private selection or public export/promotion.

Run the focused tests in the existing ML training virtual environment:

```sh
python -m unittest discover -s Tools/CapabilityPolicy -p 'test_*.py' -v
```

After other learners save/stop, temporarily disable the ordinary auto-start
controller using its existing configuration, preserve that configuration, and
restore it after the bounded trial (including on failure). This prototype refuses
an enabled ordinary auto-starter so it cannot race a new learner or make that
controller interpret its lock as the wrong supervisor. Then run:

```sh
python Tools/CapabilityPolicy/train.py \
  --root "$BN_TRAINING_ROOT" \
  --destination "$BN_TRAINING_ROOT/benchmarks/capability-feasibility-001" \
  --worker "$BN_TRAINING_WORKER" \
  --steps 8192 --width 32 --seed 207 --hours 0.25
```

The entry point checks actual process liveness for the ordinary supervisor/study
locks, AC/battery/thermal limits, shared storage and wall time. It launches only
the standalone C# worker, with two arenas and one Torch CPU thread. Power/thermal,
storage or requested stops interrupt through the SDK's checkpoint-save path.
`prototype.json` records architecture/source/worker identity and ownership.
Explicit `--resume` requires those identities and a nondecreasing step target.
Keep stdout bounded when running a longer trial. Do not run concurrent training
or treat its changing self-play Elo as strength evidence.

Before a strength comparison: verify real C# observations/masks, actual PPO
updates, finite actor/value/Adam state, checkpoint save/resume and a probability
export against dynamic-batch Torch outputs. Then compare matched independent
seeds with a suitable initialization and frozen population, both roles, new
evaluation seeds and ordinary coordinated counterplay. Distillation from a
protected teacher is a possible initialization study; it must use a separately
declared training corpus, never the held-out counterplay journals or acceptance
packs. A from-scratch smoke only establishes feasibility.

The [explicit teacher-initialization pipeline](../../Docs/AI_Capability_Policy_Initialization.md)
now provides `collect.py`, `distill.py` and `train.py --initialize`. It creates new
fair training experience, splits whole matches, checks immutable inputs and
starts PPO with fresh Adam/step zero. Its first lower-KL v1 fit lost full-game
strength, so it remains unselected. Technical preparation is not an adoption gate.

Architecture identity is explicit and separate from observation/rules versions.
`shared-capability-v1` retains the original mean-pooled context;
`shared-capability-v2` adds a global layout projection and wider context. Select
the latter with `--architecture shared-capability-v2` in distillation and PPO.
The registry and strict loaders preserve v1 rather than guessing a network from
its tensor shapes. Global layout retention is a hypothesis to measure, not a
claim of stronger play.

Teacher collection can explicitly target the deployment sampler:
`collect.py --teacher-mode Hard --action-noise 0.05` records the preset and uses
the same distribution for labels and rollout actions. The original defaults
remain raw `Policy` and 0.2 noise. Value labels are still the raw teacher's fair
critic, so they are approximate for changed sampling. The first raw-teacher v1
and spatial-v2 transfers both lost full-game strength; neither is selected.
