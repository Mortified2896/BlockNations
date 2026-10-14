# Reproducible paired study reports

`Tools/Training/study_report.py` prepares the same evidence summary for capacity,
public-information, fair-history and other owned matched continuation studies.
It reads their existing receipts; it does not alter learning, experiment inputs,
model selection or public releases. This makes the morning comparison repeatable
as units, rules, observation contracts and benchmark opponents change.

Run with the scoped training Python, keeping private output inside the existing
shared-budget study directory:

```sh
python Tools/Training/study_report.py \
  --root "$HOME/Library/Application Support/BlockNations/Training" \
  --study "$HOME/Library/Application Support/BlockNations/Training/benchmarks/capacity-night-20261015" \
  --output "$HOME/Library/Application Support/BlockNations/Training/benchmarks/capacity-night-20261015/morning-report.json"
```

The reporter verifies the protected parent's manifest hash, frozen checkpoint
and configuration hashes, benchmark-definition hashes, and the exact candidate
in both evaluation packs. It recomputes wins/losses/interruptions from every raw
match, rejects duplicate match identifiers, checks balanced colours and starting
roles, and verifies the stored role summaries. Changed rules, workers, evaluator
sources, packages, opponent panels or probability samplers cannot silently enter
one paired comparison. Confirmation must use new seeds with the same panel and
exact actor. Completed arms must reach the recorded step budget and retain
positive optimizer-counter deltas and zero rejected actions.

The JSON retains every training seed, control/treatment, primary/confirmation
pack, opponent and starting-role cell. Mean scores give equal weight to each
opponent/role cell; interruption games remain non-wins. A running study with
zero or some completed arms stays incomplete. An aggregate gain on one lucky
training seed cannot become a claim of repeatable improvement. The optional
`replicatedPositivePairedMeans` flag requires all three paired seeds, both
packs, and no interrupted evaluation games; it is not a promotion gate.

Even positive paired means require inspection of weaker opponent/role cells,
performance against the preserved champion, independent challenges and human
counterplay. Deterministic repeated openings are not independent samples, so
the reporter does not manufacture a pooled per-game confidence interval for a
training-recipe gain. It also does not infer a unit's usefulness merely from
purchases on a winning side. Diagnostic/full-vision packs retain that label.
The raw-record validator covers the current two-seat arena protocol. Additional
learning seats need an explicitly versioned validator rather than an assumed
permanent two-player limit.

Reports are snapshots. Check actual process identities and advancing arena
state separately before claiming a learner is live. Finite actor/critic/optimizer
checks remain in the study runner; report hashes alone do not establish tensor
finiteness, fair inference, phone latency or human strength.

Seven focused tests exercise complete three-seed comparisons, unfinished arms,
one-seed gains hiding other regressions, changed models/rules/pack identities,
missing/duplicated/wrong raw outcomes, inconsistent study totals and missing
seed pairs. The first actual overnight report revalidated both 512-game baseline
packs and all sixteen external opponent/role cells in each, while correctly
reporting zero completed training arms and no treatment-strength conclusion.
