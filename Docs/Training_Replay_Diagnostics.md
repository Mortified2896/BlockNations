# Recorded decision diagnostics across observation contracts

`Tools/Training/replay_policy_diagnostic.py` compares compatible frozen V2, V3
and V4 actors on a completed console diagnostic's exact reachable decisions.
It supplies each actor only its supported observation prefix. V4 memory is
rebuilt by the actual C# simulator from that seat's observations along the
recorded actions; the tool does not reconstruct hidden units from replay labels
or append current enemy wallets/action counters.

The comparison is evaluation-only. Its positions and journal must stay outside
training, episode-start curricula and human demonstration ingestion.

## Same-board visibility comparisons

Replaying all past actions with full visibility can change action legality and
movement paths, particularly when a fog move encounters a hidden blocker.
That cannot establish a perceptual comparison on the same board.

The development worker now has an explicitly enabled, read-only `projection`
operation. It builds a full-view observation and mask from the current fog
state using a separate observer and fresh diagnostic memory. It does not apply
an action, move a unit, advance RNG or populate the normal seat's knowledge.
The Python adapter returns projection steps separately; ordinary `get_steps`
retains the normal observations and masks. The worker rejects projection
requests in ordinary sessions and requests containing actions/policy assignments.
This API exists only in the diagnostic C# compilation.

The tool replays one normal-fog match. At each scored decision it queries that
read-only projection, compares Policy and Hard preferences, and then applies
the original action to the normal game. An action can be legal under fog and
masked in the full-view counterfactual; its counterfactual probability is zero,
and the original game still advances under its original rules. A projection is
not an additional full-game trial.

## Evidence and compatibility

The source needs a completed, non-interrupted owned console diagnostic with
zero rejected actions and a preserved source-model manifest. The reader checks
source hashes, rules/opening provenance, every recorded vector and legal mask,
explicit seat/controller identity, and the actual terminal outcome. V4 journals
must reproduce their full history suffix, not merely the old V2 prefix. Linked,
oversized, non-finite, extra, incomplete and inconsistent records fail clearly.

The default target is core-three rules. `--rules-version` can explicitly select
another supported profile when the source journal and every actor have that
same recorded target. Equal tensor dimensions do not make different rules
comparable. Unknown observation/rule contracts need an explicit adapter.
Existing save and PBp formats are unaffected.

Scoring uses the last recorded frozen-controlled round, which can precede the
match's final round if the other player wins before the AI acts again. A match
with no recorded frozen decision has no model-preference samples. Stored
`observation` bytes retain the original journal's shape; the receipt separately
records journal, projection and model observation versions. Sighting metadata
contains last-observed capabilities, canonical positions, visibility and age,
not stable unit IDs or current hidden-state labels.

The CLI uses one Torch thread, no Unity/player/viewer, and the shared storage
reserve. `--power-controlled` adds the existing Mac AC/80% and nominal-pressure
start conditions, AC/75% and serious/critical-pressure stop conditions, with
checks between bounded simulator calls. Failures preserve a failed receipt and
close only the tool's own worker.

## Reproduce the overnight city-defence comparison

The retained diagnostic worker and its source snapshots are below the shared
training root, alongside `projection-worker.json`. Use that worker for this
operation; the overnight learner keeps its own immutable worker/source pins.
An older worker that does not acknowledge projection support fails explicitly.

```sh
BN_REPLAY_ROOT="$HOME/Library/Application Support/BlockNations/Training"
BN_REPLAY_WORKER="$BN_REPLAY_ROOT/benchmarks/capacity-night-evidence-20261015/projection-worker/BlockNations.TrainingWorker"
/Users/Jo/.local/share/blocknations-ml/venv/bin/python Tools/Training/replay_policy_diagnostic.py \
  --root "$BN_REPLAY_ROOT" \
  --session "$BN_REPLAY_ROOT/benchmarks/deep-investigation-20261014/manual-second-hard" \
  --manifest "$BN_REPLAY_ROOT/runs/mac-7x7-core3-gentle-20261011/frozen-evaluations/milestone-163463523/manifest.json" \
  --manifest "$BN_REPLAY_ROOT/runs/info-capacity-night-20261015-84-control/frozen-evaluations/milestone-164463559/manifest.json" \
  --worker "$BN_REPLAY_WORKER" \
  --destination "$BN_REPLAY_ROOT/benchmarks/new-city-defence-comparison" \
  --power-controlled
```

Repeat `--manifest` for up to eight compatible actors and choose a new output
directory. The actual five-model receipt is
`benchmarks/capacity-night-evidence-20261015/city-defense-observation-contracts/`.
It reproduced all 129 actions and the completed outcome, scoring 22 normal/
counterfactual positions for three V2 actors and the previous paired V3/V4
pilot actors. The earlier three actors' observation bytes and scores matched
the preserved city-case receipt exactly.

At decision 118, legitimate V4 history held two currently visible enemy
records and no stale enemy sighting. Full-view projection added one supporting
ranged enemy. Both previous V3/V4 pilot actors still selected the defender and
moved it off the city under Hard, as did the parent, archive and new control.
This does not establish that the supporting enemy was never observed earlier;
it establishes what the current last-seen representation retained at this
decision. Different actor lineages also prevent treating this comparison as a
causal test of memory. Generic experience with uncertainty remains necessary;
history alone cannot be assumed to fix threats it does not represent.

Fourteen diagnostic checks cover the actual completed simulator replay across
all three journal/model contracts, altered prefixes/history/outcomes, seat and
rule mismatches, source integrity, resource gates and ordinary-session rejection.
Projection-purity checks compare normal V4 observations/masks/counters through
shared actions and randomized continuation resets. Fifteen existing environment/
information checks also passed with the new worker, and eleven environment
checks passed with the unchanged installed overnight worker. Extra supported
recruit slots now receive a generic console description instead of raising an
index error; this does not relax model/roster compatibility gates.
