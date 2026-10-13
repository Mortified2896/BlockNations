# First ML training attempt: successful proof of concept

Recorded on 2026-10-14 following the owner's request to mark the first attempt
successful, continue overnight training, and investigate the apparent decline.

**Result: successful technical ML proof of concept and a usable learned opponent.**
The pipeline learns, saves/resumes weights and optimizer state, runs fair shared
rules, exports compatible actors, and retains tested opponents. This milestone
does not establish stronger-than-human play, general game balance, or reliable
transfer to Advanced or Poly. Those remain separate goals.

## Evidence and the apparent regression

The reviewed standard 7×7 core-three run is
`mac-7x7-core3-gentle-20261011`. At its saved review stop it recorded 197,089,504
all-seat arena decisions and 2,548,360 completed full-opening matches, including
six interruptions. These are training activity counts, not independent strength
tests or PPO learner steps. Its last resumable learner checkpoint is step
133,514,909. All 55 stored checkpoint tensors were finite at review.

The selected champion remains step 33,749,992, digest
`5ca6ac1b0183019a882788522829112fd27f91f74c51cf8679b3dcd77b89666b`.
The latest evaluated candidate is step 133,399,967. The primary comparison uses
identical rules, worker, evaluator/package identities, cases and starting roles.
Every full match record was reconciled with the primary pack's summary, including
uniqueness and win counts. Self-matches are excluded from the 704-reference score.

| Primary-pack measurement | Champion | Latest evaluated candidate |
| --- | ---: | ---: |
| All external references | 659/704 (93.6%) | 610/704 (86.6%) |
| Candidate uses raw Policy sampling | 355/384 (92.4%) | 305/384 (79.4%) |
| Candidate uses Hard sampling | 304/320 (95.0%) | 305/320 (95.3%) |
| Candidate moves first | 312/352 (88.6%) | 276/352 (78.4%) |
| Candidate moves second | 347/352 (98.6%) | 334/352 (94.9%) |

The overall score mixes inference settings; it is not an exclusively Hard score.
All 49 net lost wins reconcile to 50 lost wins in the raw-policy cases, offset by
one additional Hard win. First-player cases account for 36 of the 49 net lost
wins. The newer actor improves against Warrior-only tactical recruitment
(48/64 to 60/64), while losing wins against older neural policies. This is a
measured change in capabilities and reliability, not uniform deterioration.

The separate confirmation pack also declines: 671/704 to 608/704. Its complete
comparison and two earlier regressions caused the existing three-evaluation
review guard to request a graceful save/stop. Supervisor exit code was zero.
A subsequent five-second `pmset` timeout separately latched a controller fault;
it did not cause the original benchmark review stop.

## Fresh diagnostic comparisons

The review completed 1,024 additional games using frozen actors, the exact
installed worker, balanced colours/starting roles and one CPU Torch thread.
There were no interrupted games. These are private diagnostics; they do not
change model selection or the promotion benchmark.

- In the two historical-reference cases that had fallen from 120/128 and 116/128
  to 109/128 and 96/128 under raw sampling, switching only the candidate sampling
  to Hard produced **128/128 for both actors in each case**. The historical
  opponent stayed at its recorded Hard setting.
- Against the separate historical raw-policy reference, both actors scored
  **128/128 using candidate Hard**, versus the original 119/128 and 100/128 raw
  candidate results. The reference's Policy setting stayed fixed.
- Latest Hard versus champion Hard scored **106/128** and **109/128** on the
  primary diagnostic and its offset-seed confirmation. The latest actor won
  42/64 and 45/64 when first, and 64/64 on each pack when second.

Using candidate Hard throughout the primary external-reference pack, combining
the original Hard cases with these matched sampling probes, gives 688/704 for
the champion and 689/704 for the latest actor. This almost saturated benchmark
offers little discrimination between their strongest move-selection settings.
The newer actor can defeat the champion while still having weaker raw-policy
reliability. The unchanged conservative promotion gate protects both concerns.

Fixed geometry and repeated deterministic lines limit independence. For example,
64 retained champion traces against the unrestricted tactical reference contained
only four distinct action sequences. These traces are bounded spectator samples,
and may include games beyond an evaluator cell's counting quota; their totals
must not be substituted for the official match records. The results do not imply
a human win rate or an IID confidence interval.

## Likely mechanisms and their limits

**The learned distribution has become more diffuse.** TensorBoard entropy averages
about 0.218 near step 33.75 million and 0.620 in the recent 130–133.51 million
window. The windows contain different visited states/opponents. A separate
comparison on the same 103 non-forced observations from four short opening games
also gives higher mean entropy for the newer actor: 0.493 versus 0.232. Both
policies receive the same legitimate seat observation and legal mask in that check.

PPO retains a constant entropy coefficient `beta: 0.1`, alongside the unchanged
constant learning rate `7.5e-5`. That objective encourages diverse choices.
Hard instead removes actions below 70% of the largest probability and sharpens
the survivors with temperature 0.25. The sampling probes demonstrate that this
filter eliminates many losses in these cases. Entropy pressure is therefore a
specific experiment hypothesis, not a proven sole cause: the champion used the
same coefficient, and no beta-only training comparison has yet been completed.

**Training opponents change the objective.** Half the live matches remain learned
self-play; half use eight fixed challengers with bounded weakness weighting.
At review, Warrior-only tactician matches represented about 9.38% of all matches,
and champion Hard about 8.31%. The largest observed weakness receives extra
coverage, but no one historical strategy dominates training. Improvement against
the champion and Warrior reference alongside other regressions is consistent with
specialization and changing self-play incentives. Timing alone does not prove
that the newly installed adaptive feedback caused those changes.

**Movement and defense need examination rather than purchase quotas.** Retained
loss traces include leaving the home city undefended and choosing less decisive
movement. Passing with legal actions also occurs in the champion's successful
strategy, so an End Turn penalty would be unjustified. Test reachable defensive
positions and compare action choices/outcomes without naming a required recruit.

**Added information has not yet demonstrated a dependable improvement.** The
completed three-seed public-information pilot scored 1,900/2,112 for V2 controls
and 1,861/2,112 for V3 treatments. The separate history pilot has five completed
arms and a failed final arm; it is incomplete. Fair sighting memory remains a
reasonable hypothesis, but neither study justifies adopting it as the solution.

## Continuation decision and next experiments

The owner requested continued overnight work. Review supports a monitored resume
of the current saved weights and optimizer, with the current adaptive recipe and
selected champion preserved. Archive the review/fault receipts and explicitly
rearm their current states. Keep the existing three-regression review stop,
replicated promotion, AC/battery thresholds, eight arenas, one Torch thread and
shared 20 GB budget. Further guard failures can still pause the run; this is not
a guarantee that training will run uninterrupted overnight or improve.

The highest-value next controlled comparison is reduced exploration pressure
against the current `beta: 0.1`, from the same frozen parent/optimizer, with the
same rules and frozen opponent mix. Use multiple continuation seeds, report raw
and Hard results separately, test both roles and stronger diverse opponents, and
retain the parent until a replicated improvement is shown. A wider opponent
coverage or reachable defensive-start treatment should be a separate comparison.
Human games on protected positions/openings remain necessary for a human-strength
claim. These proposed recipe/benchmark changes are not installed by this review.

Advanced and Poly require their own training adapters. Advanced adds population,
food, upkeep, upgrades and recruitment/disbanding choices; Poly adds terrain,
technology, city development, additional units and different combat/capture rules.
Train against each versioned authoritative ruleset with its fair observations,
complete legal action set, relevant maps/openings and separate benchmark. Shared
features/weights may transfer, but the existing core-three actor cannot cover
missing action/observation interfaces by simply training longer.

Detailed private receipts and reproducible diagnostic scripts remain in the
owned training root's `benchmarks/overnight-diagnosis-20261013/` and ignored
`Logs/Validation/OvernightDiagnosis-20261013/`. No human recordings or PBp data
are included in this document. The public frozen release is unchanged.
