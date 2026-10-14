# Training investigation: retention, visibility and capacity

Investigated on 2026-10-14 with the actual C# simulator and frozen 7x7
core-three models. The selected model at the beginning was step 163,463,523;
the latest learner checkpoint was step 163,793,666. These are different roles:
the playable selection does not replace the learner's optimizer/checkpoint.

## Findings from actual games

`policy_metagame.py` ran 1,152 completed rollouts with Hard sampling on both
sides throughout. Three immutable models faced one another and four fair
tactical opponents on seeds 140201/140202, both colours and both starting roles.
There were no interruptions. Each directional table entry below aggregates 64
rollouts. Reverse model duels repeat the same assignments; deterministic layouts
and repeated lines mean these are empirical comparisons, not 64 independent maps.

| Candidate | Vs selected 163.46m | Vs archived 162.30m | Vs previous 33.75m | Mixed tactician | Warrior only | Rider only | Archer only |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Archived 162,299,959 | 61/64 | Same actor | 63/64 | 64/64 | 64/64 | 64/64 | 59/64 |
| Initial selection 163,463,523 | Same actor | 3/64 | 64/64 | 64/64 | 43/64 | 64/64 | 64/64 |
| Previous 33,749,992 | 0/64 | 1/64 | Same actor | 64/64 | 48/64 | 64/64 | 64/64 |

The initial selection's Warrior result splits into 11/32 when starting first and
32/32 when going second. The archive wins 32/32 in both roles. This is evidence
that this weakness can be improved under these rules. There is an Archer tradeoff,
but no rock-paper-scissors cycle among the three tested learned models. It does
not establish a total ordering over every possible strategy or human opponent.

The archived model was absent from the permanent training pool. The recent
self-play window has 40 snapshots at 25,000-step intervals, approximately one
million steps; the archive lies outside that recent interval. Permanent retention
matters across window changes and process restarts. Beating the selected incumbent
alone had allowed a newer model to be called champion while losing badly to an
earlier model that was not in that comparison.

An additional standard direct challenge confirmed the mode disagreement. The
archive won 121/128 and 120/128 Hard duels, but only 62/128 and 68/128 raw Policy
duels. It passed the new population review, with mean gains of 4.88 and 6.64
percentage points and worst-role gains of 40.63 and 34.38 points. It failed the
existing requirement to win the raw duels as well. Automatic selection therefore
remains at 163.46m. For Hard play, explicitly selecting frozen checkpoint
162,299,959 is the supported way to try the stronger tested Hard opponent.
The different sampling settings are part of the effective playing policy;
network weights alone do not provide a mode-independent strength ordering.

The console controller also played two complete matches against the initial
selection in normal fog, using only the fair observation and legal-action mask:
a loss in round 7 when starting first and a win in round 9 when going second.
The winning approach combined two Archers with a Rider, clearing a defender and
then moving into the enemy city. The AI coordinated attacks well in both games;
the decisive failure was its city defence. These are agent-operated diagnostic
games, not human win-rate measurements or physical UI acceptance tests.

## A visibility effect on the exact reachable board

`replay_policy_diagnostic.py` replays the completed winning game's action prefix
under normal fog and symmetric full visibility. The normal-fog vectors reproduce
the original journal exactly. At decision 118, the legal root choices remain
selecting the new city defender or ending the turn. One supporting enemy Archer
is hidden under fog and visible in the counterfactual.

| Same model and board | Raw probability of keeping the defender | Hard decision |
| --- | --- | --- |
| Normal fog | 16.18% | Select and move the defender off the city |
| Full visibility | 99.94% | End the turn with the defender on the city |

Thus missing visibility affects this specific error. This is a counterfactual
decision probe, not a full no-fog match, a result from training without fog, or
proof that the full-vision model beats humans. More capacity cannot reveal an
enemy that is genuinely unobserved; the policy needs uncertainty-aware defence
and experience with such combinations, potentially using legitimate history.

The existing synthetic position probes also found very low Archer shooting
probabilities, including a ranged-kill fixture at about 0.00265% raw probability
and zero under Hard filtering. These are focused regression probes, not a claim
that every attack is optimal. In the 384 metagame games played by the initial
selection, it recruited only three Archers out of 1,084 units. This measures Hard
evaluation behaviour; it is not the raw PPO training recruitment rate.

## Information and capacity audit

The active V2 observation already includes visibility/seen tiles, public city
ownership, visible unit health and combat/movement capabilities, remaining own
actions, recruitment capabilities/costs, gold, round and the legal-action mask.
It has 3,120 inputs and a 259-action, two-stage source/action interface. The
network is an unnormalized feed-forward MLP with 128 units in each of two layers,
with no persistent memory.

V3 adds explicit public starting role and attack-commitment information. V4 adds
legitimate per-seat last-seen enemy information and sighting age. Existing short
experiments do not establish a repeatable strength gain:

- V3 pilot: three 250,000-step paired seeds; control 1,900/2,112, treatment
  1,861/2,112 on the then-current mixed-preset benchmark. Seed-level treatment
  outcomes varied substantially.
- V4 history pilot: two complete paired seeds. Primary control 1,329/1,408 vs
  history 1,307/1,408; confirmation 1,339/1,408 vs 1,323/1,408. The third control
  completed, but its treatment stopped after a simulator reference RPC exceeded
  its 30-second response deadline. The unfinished arm is not a strength result.

These pilots use older parents and a different benchmark from the new Hard
population. They do not prove history is useless or that 250,000 steps is enough
to learn the new representation. Enemy hidden state must never be supplied as
ordinary fog-policy input. Recurrent inference would require an explicit adapter;
simply enabling the trainer's memory setting is not supported by the current
frozen-policy runtime.

## Installed training and evaluation changes

The stopped main learner's permanent pool now retains the 162,299,959 Hard actor
in place of the much older 18m Hard actor, and records the selected 163.46m actor
in its base recipe. Eight distinct challengers remain: four tactical variants,
the two recent learned actors, and two older raw policies. Half the games remain
learned self-play. Adaptive weighting starts with uniform coverage, then uses
completed live training losses from both starting roles. Benchmark wins do not
set adaptive weights. The original files are backed up in the investigation's
`retained-pool-update` receipt; trained weights, optimizer state and Elo history
were preserved. Referenced checkpoint/config/manifest files are also recorded
in the existing `pins.json` mechanism so automatic evidence retention cannot
delete an opponent that the new pool or reference suite needs.

A new `automatic-evaluation-archive-hard-suite.json` evaluates eight fixed
opponents with uniform Hard sampling, both colours/roles, 512 games per seed pack.
The old suite is preserved for historical comparisons and pinned older studies.
The new population is a new score series; its win rate must not be spliced into
the old mixed-preset curve. A fresh primary evaluation scored the initial
selection 438/512 and the archive 463/512. The archive faces its own frozen actor
in one case, which has the expected self-comparison ceiling; the panel stays
identical for all candidates.

The new optional automatic population gate supplements the existing replicated
direct-incumbent gate. It requires at least one percentage point of mean gain on
each of two seed packs, equal weight per opponent and starting role, and no more
than a five-point decrease in the worst opponent/role cell. Individual openings
may regress while the mean and weakest result improve. Ties, a gain confined to
one pack, incomplete/unbalanced matches and mixed sampling cannot establish
promotion. Rejected population candidates preserve the champion and skip the
extra direct challenges. Passing the population check still requires the existing
Hard and raw Policy direct challenges before automatic selection.

The simulator transport now gives batched tactical-reference searches a bounded
120-second deadline while ordinary steps/status remain at 30 seconds. Errors
identify the operation/deadline; actions with unknown outcomes are never retried.
The precise cause of the earlier timeout remains unproven. Older pinned failed
study evidence is preserved.

## Scheduled capacity experiment

`benchmarks/capacity-archive-20261014` is an isolated matched continuation from
the immutable 163,463,523 parent:

- Control: existing 128x2 network; treatment: 256x2 network.
- Seeds 84/85/86, 1,000,000 added learner steps per arm, middle seed in reversed
  arm order. Six arms, eight arenas, one Torch CPU thread, unchanged PPO settings,
  fog, rules, rewards and the same frozen strengthened challenger pool.
- Actor and critic start with the same function. Old units occupy the leading
  block; new hidden features are random, old-path cross-connections are zero,
  and new output-head columns start at zero. Old Adam moments/counters and the
  global step are preserved; added moment entries are zero.
- Actor trainable parameters grow from 449,411 to 931,331; critic parameters
  from 416,129 to 865,025. Total capacity grows about 2.08 times. Equal learner
  steps do not mean equal compute, so elapsed time is also recorded.
- Every arm gets the new fixed Hard population and a separate confirmation pack.
  Diagnostic games/positions remain evaluation-only. The study never promotes
  a release or mutates the main learner.

Preflight preparations for both sizes reproduced all 218 actual fair decisions
from the two manual games with exactly zero maximum probability/value difference.
Tests also exercise the real SDK resume path (which can otherwise silently
reinitialize failed modules), preservation of all optimizer tensors/counters,
legal actual-worker decisions and learning of the added hidden features.

The `com.blocknations.capacity-study` LaunchAgent checks every 15 seconds. The
existing AC/80% start threshold and AC/75% stop threshold apply, including during
evaluation. It waits for another live information study rather than racing its
shared lock. At installation the Mac was charging below the start threshold,
so the study is `paused_power` with no learned arms yet. Its dedicated source
checkout and recipe are pinned; main-branch work does not alter those pins.

The main continuation resumes independently under its existing power controller,
with the stronger retained pool and new evaluation setup. No measured capacity
gain is claimed before the scheduled arms finish. Continued training can now
target a demonstrated weakness and be measured against an older strong actor;
whether it reaches human-level play remains an empirical question.

## Local evidence and reproducibility

Under `~/Library/Application Support/BlockNations/Training/`:

- `benchmarks/deep-investigation-20261014/hard-metagame/`: complete suites and raw
  match records; `hard-payoffs.csv` and `verified-evidence.json` recompute totals.
- `benchmarks/deep-investigation-20261014/manual-first-hard/` and
  `manual-second-hard/`: session identities, fair decisions and complete results.
- `benchmarks/deep-investigation-20261014/city-defense-replay-v2/`: matched replay
  and frozen-policy scores. The earlier failed preparation is kept separately.
- `benchmarks/deep-investigation-20261014/capacity-preflight.json`: actual-position
  function parity, parameter counts and retained optimizer counters.
- `benchmarks/capacity-archive-20261014/study.json` and its sibling controller
  directory: pinned design, power status, eventual arm results and confirmation.

The source utilities are `manual_policy_play.py`, `replay_policy_diagnostic.py`,
`policy_metagame.py`, `capacity_expansion.py`, `information_study.py` and
`population_promotion.py` in `Tools/Training/`. The rendered local figure is
`Screenshots/ai-training-investigation-20261014.{png,svg,pdf}`.

The custom match Elo remains a diagnostic of a changing training pool. Neither
that Elo nor the SDK's self-play Elo is calibrated human playing strength. The
fixed population, per-role results, fresh opponents and human counterplay provide
the relevant checks for longer runs.

For the underlying ideas, see the primary papers
[PSRO and multiagent overfitting](https://arxiv.org/abs/1711.00832) and
[function-preserving network expansion](https://arxiv.org/abs/1511.05641).
