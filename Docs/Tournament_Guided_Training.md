# Tournament-guided local training

Approved implementation on 2026-10-13. This connects measured opponent strength,
the retained private playtest champion, and training continuation. Terminal game
rewards remain +1 for a capture win and -1 for a loss. The learner trains against
the selected champion and a broad challenger pool; acceptance-test outcomes are
never injected into PPO rewards or challenger weights.

## Comparable evaluation and selection

Automatic evaluation refreshes the exact incumbent actor when evaluator source,
package, worker or suite identity changes. It verifies complete match records,
stores the new baseline under `evaluations/baselines/`, and archives the previous
selection in `champion-history/`. Changed weights cannot pass as a baseline
refresh. Partial/cancelled results cannot replace a baseline. Original evidence
and past champions are retained within the existing shared storage budget.

With `replicatedPromotion: true`, both the incumbent and candidate complete the
ordinary pack and a confirmation pack whose seeds are offset by 1,000,003. The
same opponents, rules, worker, sampling and evaluator apply within each comparison.
Self-matches remain balance diagnostics. Promotion requires:

- At least **one percentage point** more external-reference wins on **each** pack.
- No starting-role aggregate drop greater than **two percentage points** on either pack.
- No individual opponent-case drop greater than **five percentage points**.
- No increase in interruptions in any external case.

These are explicit empirical margins backed by a second seed pack. Repeated fixed
geometry and deterministic lines are correlated; no IID confidence interval or
human-strength guarantee is claimed. The legacy strict cell gate remains available
for existing manual callers. Promotion selects the private playtest champion;
ongoing human sessions retain their frozen weights, and public release selection
is independent.

## Training feedback and continuation

An opt-in `training-feedback.json` enables a runtime league separate from the
preserved `training-opponents.json`. It includes the currently selected champion
in the configured slot. Fixed challengers receive 50% of training matches; the
other 50% remain learned self-play. Each match keeps its selected opponent even
when weights or the selected champion change.

Only the authoritative journal of actual standard-opening training games informs
weakness weights. Interrupted, changed-policy, foreign-session and curriculum
games are excluded. Each challenger retains at most 256 outcomes for each starting
role. Weighting begins after every challenger has at least 32 results per role.
Roles contribute equally; half the sampling mass stays uniform, and a challenger
is capped at 25% of the fixed pool. The runtime receipt records identities, weights,
bounded training counts and the source recipe hash. Frozen evaluation manifests
record the effective feedback recipe. Acceptance seeds/results are not read by
this feedback path.

Three distinct evaluations showing a drop of at least **five percentage points on
both seed packs** create an owned `training-review.json` with `review_required`.
The supervisor then saves/stops. Resume and the power controller honor that guard;
the laptop cannot silently restart a regressing recipe. Review is explicit:
preserve the receipt, inspect the weakness/benchmark results, choose a recipe or
recovery checkpoint, then clear the guard deliberately. The code does not roll
back optimizer/global-step history automatically.

## Display and local controls

The training viewer's primary strength panel shows the latest evaluated score,
champion score, difference, both roles, checkpoint and update time. It reports
evaluation failures, stale evaluator status, baseline refresh, confirmation and
review pauses. The unanchored training-pool Elo stays behind its diagnostic toggle.

Local configuration lives in the owned run, outside Git:

```json
{
  "owner": "BlockNations.TrainingFeedback.v1",
  "version": 1,
  "runId": "mac-7x7-core3-gentle-20261011",
  "enabled": true,
  "championSlot": 2
}
```

The existing `automatic-evaluation.json` gains `replicatedPromotion: true` and
optional `promotionMargins` matching the bounded defaults above. Place an
`automatic-evaluation.request` file in the run to request the next comparison
without waiting for another two million decisions. Existing process locks,
pause/power cancellation, 20 GB shared storage, save reserve, legal seat observations
and saved-game/PBp formats remain in force. Changes install through a saved
supervisor restart; there are no shipping scene, prefab or layout changes.
