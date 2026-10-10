# 7×7 balance and learned-AI continuation

Approved by the owner and implemented on 2026-10-10. This experiment changes the shared rules and new single-player opening, continues the existing learned weights, and measures whether stronger policies can use several viable strategies. It does not authorize publishing the learned-AI candidate to the public site.

## Rules and opening

The authoritative definitions remain in `UnitRegistry`; Unity gameplay, the C# training arenas and tactical references use those definitions.

| Unit | Cost | HP | Attack | Movement | Attack range | Vision |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Warrior | 2 | 3 | 1 | 1 | 1 | 1 |
| Rider | 2 | 2 | 1 | 2 | 1 | 1 |
| Archer | 2 | 2 | 1 | 1 | 2 | 1 |
| Scout | 1 | 1 | 0 | 1 | — | 2 |

Defence remains zero. Attacks and new-unit health use whole display numbers. The internal combat scale remains 10, so supported old saves can still contain fractional health without a format migration.

Warriors and Riders may move and then attack; any attack spends their remaining movement, including an attack that kills nothing. A melee kill advances onto the defender's tile as part of that attack. Archers retain firing before movement and cannot fire after moving. Scouts do not attack. Surprise still consumes the attempted movement and remaining attacks, with its explanation visible until the round changes.

For new two-player single-player/training matches, the first player receives **2 gold on their first turn**, and the second receives **3**, including normal turn-start income. `MatchOpening` owns the reset recipe separately from income. Starting order, stable player identity/colour and the currently acting seat are distinct. Training randomizes the starter; the ordinary single-player setup still assigns the human Player 1/Blue and the first move, and tells them this in setup and the gameplay HUD.

The local frozen-policy playtest adds **You move first / AI moves first** beside its difficulty selector. The human remains Player 1/Blue regardless of who starts, and receives the appropriate 2/3 first-turn gold. The separate inference window shows their identity/order and hides enemy gold while training continues. This tests both starting roles against the same chosen opponent without changing the public game. Human recording subscribes at the opening regardless of the starter, captures only fair human actions, and records the actual first seat. Completed wins from either role are eligible for the existing bounded imitation path; abandoned inspection games remain ineligible.

Saved balances are not recalculated on load. PBp retains its established starting economy and 2–4-seat paths. The changed combat build is identified as 1.0.4, with an explicit import path for previously supported 1.0.3 snapshots. It continues reading protocols 4/5 and writes protocol 5/current app version; old clients reject new exports and need updating. The source snapshot and scaled health stay intact. Opening economy version 1 preserves the former 3/3 training opening; version 2 records 2/3. An old run changes to version 2 only through the explicit `--upgrade-opening-economy` operation, which preserves its original manifest. New runs use version 2.

The shared rules version is `blocknations-simulation-v4`; the observation/action schema remains 2, with 3,120 floats and 259 choices. Both public starting cities are known. Enemy gold, hidden units, private per-turn counters and persistent enemy identities remain outside the policy input.

## Preserved lineage and training recipe

The continued run is `mac-7x7-20261008T171514Z`, on standard 7×7 openings without a distance curriculum. Its pre-change step-15,120,003 checkpoint and ONNX pair are preserved under `frozen-evaluations/rules-v4-transfer`, with checkpoint SHA-256 `65320dcb27ea67d7d2a56f03933bca62cbb0a35b4203c9a291ceedab545cfc52`. Its original trainer recipe, opening manifest, ratings, progress and human-learning ledger remain archived in the owned run.

The actor and all 12 Adam optimizer states were continued. The first verification found Adam's step increasing from 351,905 to 353,729 and different actor weights in a newly saved checkpoint. Old human recordings are preserved; recordings made under incompatible rules are excluded from new-rule imitation rather than silently relabelled.

The unchanged network has two 128-unit layers and no recurrent memory. PPO still learns from captures (+1/−1); a 100-round cutoff remains an interruption with value bootstrapping. There are no recruitment quotas, named unit counters, exploration bonuses, or forced winning moves in the learner.

Documented generic adjustments for this continuation:

- Entropy coefficient 0.03, previously 0.01, to keep exploring useful alternatives.
- A 40-snapshot self-play window, previously 10, with snapshots spaced 25,000 learning steps apart, previously 5,000.
- Latest-opponent probability 0.25, previously 0.5; remaining self-play uses older snapshots.
- An optional, explicitly versioned `training-opponents.json` recipe: 20% of new match assignments use the existing fair tactical opponent, initially at work budget 128 and raised to 512 after the held-out checks below. The other 80% use learned self-play. The tactical challenger may use the whole roster; its type-specific restrictions are evaluation probes only.

Challenger assignment is deterministic from the run seed, worker, match/reset and current learning seat, and remains stable during that assignment. Advice replaces only the non-learning seat's actions. Each arena records the actual opponent identity; changed assignments are excluded from whole-match Elo as usual. The opponent module is disabled when a run has no recipe. Recipes must match the rules and retain at least half the assignments as learned self-play. Challenger decision counts are reported separately in training status.

These settings are an experiment, not a claim that PPO or its current opponent pool is optimal. Opponent snapshots and their random-generator positions are not yet restored faithfully across trainer restarts. That limitation remains part of the broader reproducibility plan.

Four independent C# arenas feed one Python learner. Training has no time limit and shares the existing **20 GB** storage root across all runs, with a final-checkpoint reserve and free-disk guard. The inspector stays closed while nobody watches. Replays/live views do not drive simulation stepping. Bounded retention means the allowance need not fill up; disk consumption is not a measure of learning progress.

## Evaluation and acceptance

`Tools/Training/evaluate_policy.py` freezes policy identity separately from the changing training process. Each suite covers four cells: candidate Blue/Red × first/second starter. Every cell gives each of four workers an equal quota. It records capture wins/losses, interruptions, starting-order outcomes, sample sizes, a Wilson interval, and recruited/winning unit types. A self-match's overall 50% candidate score follows from the paired design; **the first-player result is the balance measure**.

The initial seed is 10001, separately from training seed 42. Use additional held-out seeds before acceptance. Colour/starting cells are paired checks, and deterministic policies can produce repeated games on this fixed map; game count alone does not provide independent strategic coverage. Recruitment frequencies describe a policy, not proof that every recruited unit caused its wins.

Example frozen evaluation:

```sh
python Tools/Training/evaluate_policy.py \
  --worker Build/TrainingWorker/BlockNations.TrainingWorker \
  --checkpoint '<preserved numbered checkpoint.pt>' \
  --trainer-config '<matching trainer.yaml>' \
  --destination '<new evaluation directory>' \
  --games-per-cell 32 --seed 10001
```

Add `--reference-checkpoint` for a fixed learned opponent. Omit it for the fair tactical reference; its work is bounded by `--reference-work`. `--reference-recruit-type` is an evaluation-only restricted-roster probe, not a constraint on the candidate. `--tactical-candidate` compares tactical references without loading neural weights. `--difficulty Policy` samples the original learned action distribution after applying the authoritative legal-action mask; it is an evaluation mode, not another game difficulty. `--reference-difficulty` can differ from the candidate mode, so an identical frozen model can test difficulty presets against one another with both starting roles. Both modes are recorded in the receipt. Never feed evaluation results to PPO or present changing training Elo as fixed-reference strength.

Initial receipts under rules v4:

| Comparison | Capture wins | First-player capture wins | Interpretation |
| --- | ---: | ---: | --- |
| Transferred old checkpoint against itself | 64 total games | 18/64 | Existing habits do not transfer into balanced play automatically. |
| Step 15,334,995 against transferred checkpoint | 43/64 candidate wins | 11/64 | Some adaptation, with substantial starting-order bias. |
| Step 15,334,995 against itself, Hard preset | 64 total games | 0/64 | This policy has a poor first-player opening; overall 32/64 is not balance. |
| Step 15,334,995 against the fair Archer-only reference | 10/64 candidate wins | 30/64 | An alternative unit strategy wins from either starting role against this policy. This is not proof of equilibrium balance. |
| Step 16,089,952 against transferred checkpoint | 64/64 candidate wins | 32/64 | Improvement against the preserved baseline from both starting roles. |
| Step 16,089,952 against the fair tactical reference | 64/64 candidate wins | 32/64 | Improvement against a fixed independent controller; still not equilibrium balance. |
| Step 16,089,952 against the fair Archer-only reference | 40/64 candidate wins | 8/64 | Better performance, with a significant remaining role-dependent weakness. |

Held-out seed 20001 checks found step 16,089,952 winning 47/64 against the all-roster work-512 reference (15/32 when starting, 32/32 when second) and 37/64 against the Archer-only work-512 probe. Step 16,939,979 subsequently won 48/64 against the all-roster reference and 55/64 against the Archer probe (32/32 when starting, 23/32 when second). It still lost every first-player self-match. The work-512 all-roster controller therefore supplies more competitive training challenges without constraining the learner or prescribing unit-specific strategies. These paired fixed-map samples remain limited evidence, not an independent sample of human strategy.

### 2026-10-10 06:48 UTC progress check

The step-18,834,965 checkpoint/ONNX pair is preserved under `frozen-evaluations/rules-v4-progress-18834965`, with checkpoint SHA-256 `e950974ea47385534623d624d2cc8a9facb824d28e0d42e76220c044ad258820` and a matching trainer recipe/provenance manifest. Its actor and all 12 Adam states contain finite values; Adam's step has advanced to 437,958. The running learner reached step 19,059,941 during inspection, with four arenas, zero rejected actions, no recent trainer errors, and healthy numbered checkpoint/export pairs. The shared root used approximately 0.69/20 GB, with about 335 GB free; there is no duration limit and no active Unity viewer.

The following fixed tests used the same candidate and work-512 reference implementation, balanced over both colours and starting roles, on held-out seed 20001:

| Reference | Candidate capture wins | Candidate wins when first | Candidate wins when second |
| --- | ---: | ---: | ---: |
| All-unit tactical controller | 64/64 | 32/32 | 32/32 |
| Warrior-only tactical probe | 32/64 | 0/32 | 32/32 |
| Rider-only tactical probe | 27/64 | 0/32 | 27/32 |
| Archer-only tactical probe | 48/64 | 16/32 | 32/32 |
| Identical checkpoint, Hard sampling | 32/64 by paired construction | 0/32 | 32/32 |

Improvement from 48/64 to 64/64 against the all-unit reference does **not** establish general strength. Restricted tactical references expose counter-strategies that this controller's unrestricted recruitment does not select consistently. The learned candidate's winning armies against the all-unit reference included both Rider-only and Rider/Warrior mixes. Warrior-, Rider- and Archer-based reference strategies all still won against it. These are strategy probes against this candidate, not proof of equal unit balance or a solved game.

Additional self-tests separate the model's learned distribution from playtest sampling. The first player won 20/128 with Medium and 42/128 with Easy on seed 30001, versus 0/64 with Hard on seed 20001. On seed 40001 the unmodified learned distribution gave the first player 14/128 wins. In balanced direct comparisons on that seed, the unmodified distribution scored 58/128 against Hard and Medium scored 62/128 against Hard. Thus Hard's more restrictive sampling is not the sole cause of the poor first-player opening, and replacing it with the raw distribution is not a demonstrated strength improvement. Stochastic winning armies did sometimes recruit all three combat types; recruitment does not prove their causal contribution.

Recent changing-opponent training telemetry was much closer to even: the first player won 778/1,567 qualifying self-play captures. However, the learner also beat that rotating opponent pool in about three quarters of its matches from either role. This differs materially from the frozen same-model tests and must not be presented as equilibrium balance. Keep the current 2/3 opening, weights and training recipe running for a longer measured interval; repeat the preserved reference pack and both-role self-tests before accepting balance or changing the rules. If the fixed first-player weakness persists despite continued training, increase competitive opponent diversity and investigate the learner's opening/action representation rather than tuning gold to this policy's current habit.

The evaluator's new sampling checks passed 11 focused Python tests, including actual C# worker interruptions, balanced quotas, independent candidate/reference modes, and preservation of legal lower-probability choices. The candidate remains unaccepted; neither the embedded local release model nor the public Cloudflare game was replaced during this progress check.

### 2026-10-10 08:03 UTC regression check

The running learner reached step 22,449,991, with healthy checkpoint pairs, no rejected actions or failure, four C# arenas and no inspector. The shared root used approximately 0.72/20 GB. Its frozen checkpoint and matching export/configuration are preserved under `frozen-evaluations/rules-v4-progress-22449991`, with checkpoint SHA-256 `5a92ae42ded2a795bbdb0c2521be91d3ef7744abdb4f8ac8886e97b932b830ed`.

The identical seed-20001/work-512 reference pack shows a regression rather than monotonically improving strength:

| Reference | Candidate capture wins | Candidate wins when first | Candidate wins when second |
| --- | ---: | ---: | ---: |
| All-unit tactical controller | 32/64 | 32/32 | 0/32 |
| Warrior-only tactical probe | 32/64 | 0/32 | 32/32 |
| Rider-only tactical probe | 0/64 | 0/32 | 0/32 |
| Archer-only tactical probe | 64/64 | 32/32 | 32/32 |
| Preserved step-18,834,965 learned opponent | 0/64 | 0/32 | 0/32 |

The first player again lost all 64 same-model Hard self-matches. Repeating the all-unit, Rider-only and same-model comparisons with the unmodified learned distribution gave the same respective outcomes. Consequently, this regression is not explained by the Hard probability cutoff alone. The candidate mostly recruited Riders, sometimes Scouts or Archers, and no Warriors in these suites; its earlier Warrior-containing defence has disappeared from this sample. This is consistent with loss of previously useful behaviours, without yet identifying the exact optimization cause.

The earlier stronger checkpoint remains preserved and the local selected release remains unchanged. Neither training Elo nor recency should promote this regressed checkpoint. The next generic training adjustment should retain useful older learned opponents beyond the rolling self-play window, preserving their hashes, configurations and fair sampling modes across restarts. Test that league separately and keep it confined to the non-learning seat; do not add a Rider-specific rule, reward or forced defence to the learner. Re-evaluate against this unchanged reference pack before claiming improvement. The present rolling window covers about one million learning steps, so the strong step-18,834,965 opponent has aged out by this check.

The Easy/Medium self-match probes also favoured the second player. A fair tactical self-match at work 512 likewise did not resolve the bias. Do not change the approved 2/3 gold opening merely to force a weak policy's score to 50%. Train and test better opening choices first, then change unit/rule balance only with evidence across competent opponents and both starting positions.

Acceptance needs stronger performance against a preserved reference set and human playtests, successful strategies using different combat units and mixed armies, and no material first/second bias across competent, held-out opponents. Near 50/50 is a target with sample uncertainty, not an exact percentage to tune against one checkpoint. Scouts are a vision/utility unit; Scout-only winning armies are not a requirement. A viable specialist strategy need not recruit every type, and equal recruitment percentages are not the objective.

The current policy is **not accepted as strong or balanced**. More learning may take hours or days, and the required duration depends on whether the architecture learns defence and alternative openings. Check frozen candidates at two-hour intervals; prioritize a reproducible improvement over a rising self-play Elo. If extended training does not improve the fixed tests, investigate opponent diversity, observation/memory, network capacity and action representation before spending another night with the same recipe.

### 2026-10-10 09:01 UTC retained opponent league

Continued training is still producing inconsistent policies. Immediately before this adjustment, frozen step 24,324,993 won 64/64 against the unchanged all-unit work-512 reference, but only 10/64 against the Warrior probe, 16/64 against the Rider probe and 32/64 against the Archer probe. It won all 32 first-role games against step 18,834,965 and lost all 32 second-role games. Identical-model Hard self-tests now gave the first player 64/64 wins, reversing the earlier second-player sweep. These fixed-seed comparisons indicate unstable opening choices and opponent dependence; they do not establish a change in the underlying gold balance. The candidate and matching configuration/export remain preserved under `frozen-evaluations/rules-v4-pre-league-24324993`.

A version-2 per-run opponent recipe now retains useful learned references beyond the SDK's rolling snapshot window. Match assignments target 60% ordinary learned self-play and 40% fixed challengers. Within that challenger share, weights 1:3:1 select the fair work-512 tactical controller, frozen step 18,834,965 with Hard sampling, and frozen step 16,939,979 with the unmodified learned distribution. Thus their expected shares across all assignments are 8%, 24% and 8%. These are sampling probabilities, not exact quotas. The learner still selects every legal unit/action itself; no unit-specific rewards, recruitment quotas or defensive opening rules were added. PPO/network settings and the approved 2/3 first-turn economy remain unchanged.

Challengers replace only the non-learning seat's actions and use the same fair observations and legal masks. Frozen actors are loaded once, batched, and read-only. Each retained checkpoint/configuration is checked against its owned run, rules, board, opening, provenance manifest and SHA-256 digest. Explicit sampling settings reject silent preset drift. Per-arena seeds keep assignments and sampled challenger moves independent of batch ordering. Journals and telemetry record each actual opponent identity rather than rating the entire league as one entity. Identical unmodified actor weights retain their identity across checkpoint repackaging; a changed sampling preset has a separate identity.

The original recipe and all weights were preserved before activation. A provisional activation was cleanly saved, then resumed with the final identity contract from step 24,685,690, preserving all 12 Adam states at optimizer step 573,846. The final recipe uses identities `tactician-74d246ff24983fb04b1cf7b4`, `sampled-d585cf8859694001154a5922`, and `f615b1df125b78c5e996369e`. Provisional journals retain their recorded identities rather than being relabelled. Version-1 recipes remain supported; this is development training configuration, without changes to game saves or PBp formats.

The resumed four-arena C# workload is connected and producing actions from all three challengers, with zero rejected actions or reported failures. Initial throughput was approximately 1,425 learned decisions/second, with no Unity inspector open. Shared storage was approximately 0.79/20 GB. Duration remains unlimited and the supervisor retains the shared storage/free-space guards and normal checkpoint retention; storage growth is not a measure of learning quality.

Validation completed 96 Python tests with 94 passes and two opt-in skips. The separately enabled real-PPO/C# league test passed: training saved/resumed, its Adam step advanced from 48 to 96, and the retained opponent file stayed unchanged. Focused checks cover legal actions, both learner seats, weighted assignment, provenance/path/digest rejection, preset drift, canonical actor identity, actual C# journal identities and optimizer continuation. The other opt-in test is the unchanged private human demonstration pipeline, previously validated separately.

Persistent anchors address loss of previously useful opponents, but the stock ML-Agents rolling snapshot pool and global SDK random state are not fully restored across a restart. Do not claim exact continuation reproducibility. No post-adjustment improvement in playing strength has yet been established. At the next progress review, freeze another candidate and repeat the unchanged reference pack and both-role self-tests; broaden held-out seeds before accepting balance. The public build and selected local release model remain unchanged pending human review.

### 2026-10-10 09:26 UTC first retained-league evaluation

After approximately 25 minutes with the final league recipe, preserved checkpoint 25,899,973 has SHA-256 `8670aa3a27f7dcd82299eb16c633d19a2d49cb279eb2c80a60478a127fcdfa11`. Its matching ONNX, configuration, opponent recipe and provenance are retained under `frozen-evaluations/rules-v4-league-progress-25899973`. The goal is active; the obsolete usage cutoff no longer applies. Training continues without a duration limit, using four C# arenas with no viewer, about 1,520 decisions/second, zero rejected actions and approximately 0.80/20 GB shared storage.

The unchanged seed-20001, work-512, Hard reference pack produced:

| Reference | Candidate wins / scheduled games | Candidate wins when first | Candidate wins when second | Interruptions |
| --- | ---: | ---: | ---: | ---: |
| All-unit tactical controller | 64/64 | 32/32 | 32/32 | 0 |
| Warrior-only tactical probe | 32/64 | 0/32 | 32/32 | 0 |
| Rider-only tactical probe | 32/64 | 0/32 | 32/32 | 0 |
| Archer-only tactical probe | 48/64 | 16/32 | 32/32 | 16 |
| Identical checkpoint, Hard sampling | 32/64 by paired construction | 0/32 | 32/32 | 0 |
| Preserved step-18,834,965 learned opponent | 51/64 | 32/32 | 19/32 | 0 |

The pre-league candidate's corresponding Warrior, Rider and older-learned results were 10/64, 16/64 and 32/64. This new candidate improves those particular comparisons, but one before/after sample does not establish causality or general strength. The Archer comparison has 16 interruptions, not 16 losses or proven draws. The identical-model Hard first-player win rate returned to zero; the role weakness is unresolved.

Additional seeds 30001 and 40001 each used 128 games per comparison. Hard same-model tests again gave the first player 0/128 on each seed. Unmodified-distribution same-model tests gave first-player results 8/128 and 4/128 (combined 12/256); by paired construction these correspond to the designated candidate winning 4/64 and 2/64 when starting. Against the retained Hard step-18,834,965 opponent, the unmodified candidate scored 88/128 and 91/128, with candidate-first results 61/64 on both seeds and candidate-second results 27/64 and 30/64. Policy sampling therefore remains substantially role-dependent. These tests keep the board/opening fixed; changing RNG seeds varies stochastic choices, not independent map layouts or human strategies.

The candidate's evaluated recruitment remains predominantly Riders, with occasional Warriors and Scouts. No Archers were recruited in this sample. That is not yet the requested robust set of viable learned strategies; reference strategies using other units remain competitive. Continue the current league for a longer measured interval before stacking another training change. Keep these 1,152 evaluation matches as a fixed comparison receipt, repeat with a later frozen candidate, and reject promotion until both-role and human tests improve. The changing-pool training Elo remains telemetry, without an absolute strength interpretation or a reliable plateau guarantee.

## Export and local testing

`checkpoint-contract.json` records the rules/opening and the last checkpoint step before a transition. A numbered checkpoint must be newer than that boundary before its export claims current compatibility. An explicitly selected frozen candidate must also match its provenance manifest and content hash. Reading a latest pair retries selection if training retention retires it during the read. A running arena under new rules cannot relabel old weights as a new-rule release.

The local embedded experimental model is now the frozen step-16,089,952 v4 export, so the newly compiled single-player game does not try to load a v3-only model under changed combat rules. Easy, Medium and Hard remain experimental sampling presets until measured separation is established. Their names do not certify difficulty. The model is still 1,803,075 ONNX bytes; training/optimizer data and authentication are not included.

Ordinary self-play and automated balanced evaluations are authorized. Ask the owner before a watched comparison tournament. Keep the public Cloudflare build unchanged until the owner reviews the local learned-AI candidate.

## Validation

- 126 focused graphical Unity tests passed for the shared combat rules, scene/direct parity, first-turn economy, player labels, hidden enemy gold and human playtest flows.
- 85 Python tests completed with 84 passes and one opt-in human-pipeline check initially skipped. The separately run opt-in test passed with a synthetic second-player win produced by actual Unity input: two imitation updates, 128 examples, and checkpointed Adam steps continuing from 50 to 98. The installed self-contained worker also passed its 13 integration checks. Python checks cover explicit opening upgrades, preserved manifests/weights, rules transitions, export provenance, actual ONNX probability/mask parity, worker ownership, challenger/learner separation and balanced interruption reporting.
- The updated native Mac player and self-contained ARM64 C# worker built successfully. The selected export passed 24 ONNX conversion fixtures and eight actual Unity CPU fixtures (maximum probability error 7.5e-10). Model compatibility is verified separately from playing strength. The native AI-first playtest connected, reached the human turn with 3 gold, and returned without stopping the four-arena learner; recording passed separately through real Unity command hooks and the Python pipeline. The recorder-corrected player is also installed at the Editor’s default `Build/LocalTrainingV2.app` path; updating this optional player does not restart the running C# learner.

Local receipts, test XML, probability fixtures and screenshots live in ignored `Logs/Validation/BalanceV4/` and the isolated validation project's `Screenshots/`. Private training/human match files remain outside Git. Physical phone performance and new-rule public/browser acceptance remain separate work.
