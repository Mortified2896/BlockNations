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

## Installation and verification, 2026-10-13

Implementation was merged into `main` at `c03f7a4`. The existing learner saved
successfully before installation and resumed through the power controller, with
the V2 model, eight arenas and one Torch thread. Run manifest, trainer recipe and
original opponent recipe hashes were preserved. The native arm64 training player
was rebuilt as `Build/TrainingTournamentGuided-20261013.app` and installed in the
controller's resume command; the previous app remains available.

The first repaired job completed both full 1,280-game packs. Its frozen step
126,249,979 scored **572/704** external-reference wins on the primary pack and
**569/704** on confirmation. Re-evaluating the selected champion reproduced
**659/704** on the primary pack. The job retained the champion, published the fresh
strength scores, and recorded one substantial regression without triggering the
three-evaluation review hold. Training feedback reached its both-role sample
threshold and activated nonuniform bounded weights. At acceptance all twelve
retained Adam counters had advanced by at least 4,896 updates, and live training
reported zero rejected actions and no failure.

Validation passed 43 graphics-enabled Unity checks, the Python suites (including
112 checks in the broad run, with two optional PPO integrations skipped), focused
final guard rechecks, and a separate real adaptive-PPO save/resume integration.
Current-source C# worker checks passed. An earlier information-telemetry check
against the older installed worker failed because it lacks those newer counters;
the original worker was preserved, and actual live evaluations passed against its
pinned identity. Native build/connection passed; OS window capture was unavailable,
so there is no rendered-window screenshot acceptance receipt. Detailed ignored
artifacts are in `Logs/Validation/TournamentGuidedTraining-install/` and the
implementation worktree's `Logs/Validation/TournamentGuidedTraining/`.
