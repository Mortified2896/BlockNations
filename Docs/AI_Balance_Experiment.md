# 7×7 balance and learned-AI continuation

Approved by the owner and implemented on 2026-10-10. This experiment changes the shared rules and new single-player opening, continues the existing learned weights, and measures whether stronger policies can use several viable strategies. It does not authorize publishing the learned-AI candidate to the public site.

## Rules and opening

The baseline definitions remain in `UnitRegistry`; Unity gameplay, the C# training arenas and tactical references use those definitions. Explicit experimental match profiles derive their overrides from that same catalog, without mutating the baseline registry.

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

After approximately 25 minutes with the final league recipe, preserved checkpoint 25,899,973 has SHA-256 `8670aa3a27f7dcd82299eb16c633d19a2d49cb279eb2c80a60478a127fcdfa11`. Its matching ONNX, configuration, opponent recipe and provenance are retained under `frozen-evaluations/rules-v4-league-progress-25899973`. Training continues without a duration limit, using four C# arenas with no viewer, about 1,520 decisions/second, zero rejected actions and approximately 0.80/20 GB shared storage.

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

### 2026-10-10 09:40 UTC exploration diagnosis

A diagnostic pass of 128 unmodified-policy same-model games used the preserved step-25,899,973 candidate. Probabilities were measured before playtest sampling, from the same fair observations and legal masks used by the policy. First-round recruit decisions with 2 or 3 own gold gave Rider probability approximately 1.0, Warrior probability approximately 7e-11 and Archer probability approximately 1e-11. Repeated end-turn/recruit opportunities are counted as decisions, not independent matches. The opening distribution is therefore effectively closed to those alternatives in ordinary stochastic sampling, despite their legality. No human demonstrations have been consumed under the current rules: the learner's demonstration status reports zero eligible winning games and zero imitation updates. The current opening collapse is not explained by imitation of the owner's Rider games.

Across non-forced decisions in this diagnostic, average entropy divided by log(number of legal actions) was approximately 0.087 for source choices and 0.073 for target choices. Forced choices were reported separately. Average entropy across all rollout decisions can obscure the much sharper recruit distribution. TensorBoard confirms constant learning rate 0.0003 and low aggregate policy entropy around 0.05–0.08 in recent sampled summaries. These summaries diagnose exploration; they do not establish strength or tactical understanding.

Keep the present run as a baseline until the next measured comparison. If the recruit distribution remains effectively deterministic and fixed-reference improvement stalls, evaluate a bounded, training-only exploration repair using all legal alternatives, without prescribing unit choices or changing the game's rewards. Such a change must preserve the current actor/optimizer and be tested for finite gradients, legal masks, learner ownership and clean checkpoint continuation. Merely increasing the usual entropy coefficient may be ineffective for alternatives already near zero probability; compare the actual choice distribution rather than assuming a larger coefficient solved the problem. A new network/action schema remains a separately planned architectural step.

### 2026-10-10 09:58 UTC isolated exploration repair test

`Tools/Training/exploration_pressure.py` implements an optional auxiliary actor loss, disabled when the owned run has no `exploration-pressure.json`. It uses only the learning seat's fair observations and authoritative legal masks already in PPO's update buffer. Captured batches exclude padding, are bounded to at most 128 observations, and own their data before PPO clears the buffer. The current feed-forward schema is required; recurrent policies are rejected pending a memory-aware adapter.

For a state with multiple legal choices and normalized entropy below the configured floor, the loss is `coefficient * KL(uniform-over-legal-actions || policy)`. Masked log-softmax gives finite recovery gradients even for probabilities near zero. Illegal actions receive no target probability or gradient, forced choices receive no pressure, and already-diverse states are skipped. This is an explicit generic exploration regularizer, without unit-specific targets, additional information or changed game rewards. It is not a requirement that the final opponent choose every action equally. The adapter takes at most one extra clipped actor update after the normal PPO epochs, reusing the existing Adam optimizer before optional human imitation; the exported inference architecture remains unchanged.

The bounded development recipe uses version 1, the matching rules/board, coefficient at most 0.02, normalized-entropy floor at most 0.25, and batch size 16–128. The isolated test used coefficient 0.005, floor 0.1 and batch 128. Activation is explicit at trainer startup, not a live file mutation. The production 7×7 run has no such recipe and was neither restarted nor changed during this test.

On an offline copy of frozen step 25,899,973, 200 pure auxiliary updates from a fresh experimental Adam optimizer changed first-round probabilities to approximately 92.5% Rider, 6.2% Warrior and 1.3% Archer. The original checkpoint hash remained unchanged. This was an actor-only gradient experiment, not continued PPO training, a resumable learner checkpoint or an accepted release.

That offline copy was rejected: its observed self-games repeatedly reached the 100-round interruption limit. The owned comparison process was stopped early after round-limit-heavy completed cells; there is no complete comparison pack or valid global win rate for the repaired copy. Purely softening the whole network can disrupt tactical behaviour. Do not warm-start the real learner from this actor-only artifact or interpret restored probabilities as stronger play.

The real-PPO test instead initializes an isolated learner, preserves its checkpointed optimizer, deliberately saturates its actor logits, and resumes with the optional pressure recipe. It verifies finite weights, all 12 Adam states, actual auxiliary actor steps distinct from critic steps, and zero rejected C# actions. Adam's maximum step advanced from 48 to 97 in the saturated continuation. The full Python suite completed 107 tests with 104 passes and three opt-in skips before the additional padding/recurrent test; focused release validation includes that new test and the separately enabled saturated continuation.

Keep the main retained-league run as the baseline through the next strength review. If the opening collapse and fixed-test weaknesses persist, run a controlled continued-PPO trial with this small auxiliary loss and compare the same frozen suite before adoption. The negative offline result makes preservation of tactical strength an explicit gate. Raising diversity, averaging training Elo, or approaching 50/50 through weaker moves is not sufficient acceptance.

### 2026-10-10 10:01 UTC hour checkpoint and bounded continued-PPO comparison

After approximately one hour with the retained league, frozen step 27,389,984 has checkpoint SHA-256 `e80d85f711cdd418b4e9a5cb10a91ca9534c46ad8ddc9f2a097276a37a55f6c6`. Its weights/export/configuration/recipe are preserved under `frozen-evaluations/rules-v4-league-hour-27389984`. The identical pinned 1,152-game benchmark gave 64/64 against the all-unit tactical controller, 32/64 against the Warrior probe, 16/64 against the Rider probe, 32/64 against the Archer probe and 64/64 against the preserved step-18,834,965 opponent. There were no interruptions. All same-model Hard and unmodified-policy comparisons now gave the first player a 100% capture win rate, reversing the previous checkpoint's second-player sweep. This is persistent strategic instability, not a demonstrated gold-balance solution.

The unmodified candidate also beat the retained Hard step-18,834,965 opponent 128/128 on each of seeds 30001 and 40001. Against the more recent step-25,899,973 unmodified opponent on seed 50001, it won 120/128: 58/64 when starting and 62/64 when second. Useful older-model strength has improved while particular tactical counters remain unresolved. The benchmark still shows mostly Riders, with Warriors/Scouts in some games and an occasional Archer. Do not promote solely for beating old opponents that the current league repeatedly trains against.

Two bounded continuation trials started from exactly the same full hour checkpoint, preserving its optimizer. Each added approximately 16,384 learner steps with one C# arena, the same seed and retained references. They ran sequentially for about 49 and 46 seconds, then stopped; no extra experimental learner remains running. The control used ordinary PPO; the trial enabled coefficient 0.005, entropy floor 0.1 and a 128-observation auxiliary batch. The main four-arena learner remained running without changes. Both trials had zero rejected actions and finite policy weights with all 12 Adam states.

Repeating the same 1,152-game benchmark for each trial did not support adoption of the auxiliary recipe. Both scored 64/64 against the all-unit controller, 32/64 against Warrior, 32/64 against Archer, and 64/64 against the older learned reference. The control scored 32/64 against the Rider probe; the pressure trial scored 16/64. Every same-model comparison still gave the first player all capture wins. Against the older Hard reference on the two additional seeds, the control scored 128/128 and 126/128; pressure scored 127/128 on each seed. There were no interruptions in either completed pack.

This short comparison is a sanity/risk check, not a long-term training experiment or enough evidence for a causal general-strength claim. Actual auxiliary execution is verified by actor optimizer step deltas exceeding critic deltas and recorded exploration telemetry. The pressure recipe remains disabled in production. Preserve both trial checkpoints and receipts for later experiments, retain the ordinary learner as the baseline, and evaluate a later checkpoint before either another recipe change or release selection. The three full packs plus recent-opponent comparison comprise 3,584 completed evaluation matches, separate from training Elo and the previously aborted offline study.

An information-contract audit also found that `MatchState.FirstSeat` is public match state but has no explicit field in `AIObservation` or schema-v2 tensors. The model can infer opening order indirectly from own gold, but the feed-forward policy receives no permanent explicit starting-role signal. Treat this as a representation follow-up, not a proven cause of the observed bias. A future versioned observation contract should include public initiative/starting-role information, preserve existing v2 actors and human recordings, and verify migrated initial policy/value outputs before training. Do not silently repurpose an existing tensor channel or relabel old checkpoints.

### Repeating the fixed benchmark

`Tools/Training/evaluate_suite.py` runs a saved benchmark definition against one frozen candidate manifest. Each case specifies a unique name, seed, balanced games per cell, candidate/reference sampling modes, and either a tactical reference, a frozen learned reference, or the candidate itself. Recruitment restrictions apply only to tactical references. Learned references must include their pinned checkpoint/configuration digests. The definition also pins the C# worker bundle, including its managed rules assembly and runtime/dependency configuration, not merely its native apphost.

The generated suite identity includes reference definitions, evaluator source hashes and ML-Agents/Torch/NumPy versions. The candidate identity is recorded separately, so the same benchmark can compare successive candidates. Complete per-match records remain in each case's `evaluation.json`; `suite.json` records running/completed/failed status and per-case results. Existing destinations are rejected, and partial failures cannot masquerade as completed packs. Colour and first/second roles remain balanced within every case. Seeds change stochastic choices rather than map geometry; results must not imply independent human opponents.

Example invocation, with private paths resolved to the selected frozen run:

```sh
python Tools/Training/evaluate_suite.py \
  --candidate-manifest /path/to/frozen-candidate/manifest.json \
  --suite Logs/Validation/BalanceV4/rules-v4-fixed-suite.json \
  --worker Build/TrainingWorker/BlockNations.TrainingWorker \
  --destination Logs/Validation/BalanceV4/fixed-suite-CANDIDATE_STEP
```

The saved v4 suite reconstructs the 12 comparisons above (1,152 games) and provides one repeatable command for later progress reviews. It is local validation data with machine-specific private file paths, not a public game asset or committed training record. This tool does not stop/restart the learner, launch a viewer, select a release or deploy anything. It validates the benchmark before starting matches and rejects changed reference content or a changed worker. Nine focused suite/evaluator tests passed, including an actual C# worker run. The complete 12-case pack is also checked against the previously recorded candidate results. Focused validation includes actual C# matches, balanced records, changed managed rules with an unchanged apphost, reference/configuration changes, invalid quotas, and explicit partial failures.

## Export and local testing

`checkpoint-contract.json` records the rules/opening and the last checkpoint step before a transition. A numbered checkpoint must be newer than that boundary before its export claims current compatibility. An explicitly selected frozen candidate must also match its provenance manifest and content hash. Reading a latest pair retries selection if training retention retires it during the read. A running arena under new rules cannot relabel old weights as a new-rule release.

The local embedded experimental model is now the frozen step-16,089,952 v4 export, so the newly compiled single-player game does not try to load a v3-only model under changed combat rules. Easy, Medium and Hard remain experimental sampling presets until measured separation is established. Their names do not certify difficulty. The model is still 1,803,075 ONNX bytes; training/optimizer data and authentication are not included.

Ordinary self-play and automated balanced evaluations are authorized. Ask the owner before a watched comparison tournament. Keep the public Cloudflare build unchanged until the owner reviews the local learned-AI candidate.

## Validation

- 126 focused graphical Unity tests passed for the shared combat rules, scene/direct parity, first-turn economy, player labels, hidden enemy gold and human playtest flows.
- 85 Python tests completed with 84 passes and one opt-in human-pipeline check initially skipped. The separately run opt-in test passed with a synthetic second-player win produced by actual Unity input: two imitation updates, 128 examples, and checkpointed Adam steps continuing from 50 to 98. The installed self-contained worker also passed its 13 integration checks. Python checks cover explicit opening upgrades, preserved manifests/weights, rules transitions, export provenance, actual ONNX probability/mask parity, worker ownership, challenger/learner separation and balanced interruption reporting.
- The updated native Mac player and self-contained ARM64 C# worker built successfully. The selected export passed 24 ONNX conversion fixtures and eight actual Unity CPU fixtures (maximum probability error 7.5e-10). Model compatibility is verified separately from playing strength. The native AI-first playtest connected, reached the human turn with 3 gold, and returned without stopping the four-arena learner; recording passed separately through real Unity command hooks and the Python pipeline. The recorder-corrected player is also installed at the Editor’s default `Build/LocalTrainingV2.app` path; updating this optional player does not restart the running C# learner.

Local receipts, test XML, probability fixtures and screenshots live in ignored `Logs/Validation/BalanceV4/` and the isolated validation project's `Screenshots/`. Private training/human match files remain outside Git. Physical phone performance and new-rule public/browser acceptance remain separate work.

### Controlled roster-reference league pilot

The fixed-reference replays show losses to restricted Warrior, Rider and Archer controllers, despite wins against the unrestricted tactical controller. Some losses include unnecessary movement away from the learner's city. This motivates testing opponent coverage before changing unit statistics again or prescribing a defensive rule.

A tactical league entry may now optionally specify `recruitType`, using an actual roster identifier. The restriction applies only to that fixed opponent's recruitment advice. The learning seat retains its entire legal roster, ordinary rewards, fair observation and action mask. Unknown roster identifiers fail closed. Restricted opponents have distinct rating identities; an absent or empty restriction preserves the previous unrestricted identity and recipe compatibility. The capability supports future roster identifiers without a fixed list of unit names in the trainer.

The pilot compares two separate resumed-PPO runs from frozen step 27,389,984, preserving the same actor, critic and optimizer. Each adds 65,536 learner steps, with one C# arena, seed 42 and identical PPO settings. Control keeps the existing 60% learned self-play / 40% retained-reference recipe. The trial keeps 60% learned self-play, with expected overall shares of 5% each for unrestricted, Warrior, Rider and Archer tactical references, 15% for the retained Hard step-18,834,965 policy and 5% for the retained unmodified step-16,939,979 policy. These are sampled expected proportions, not outcome quotas or learner unit preferences. Both resumed runs rebuild the stock transient self-play pool; the main four-arena run is untouched.

The fixed benchmark includes these tactical styles, so improvement against them is training-reference performance. Same-model role results and later held-out learned policies are separate gates for generalization. Do not promote a recipe solely because it scores better on training opponents. A short pilot can reject an unsafe change or justify a longer trial; it cannot certify strong human play or long-term unit balance.

The pilot completed successfully: control step 27,455,539 and roster-reference step 27,455,522 each advanced all 12 Adam state counters by 1,488, with finite policy weights and zero rejected actions. All six reference identities appeared in the roster pilot. The full Python suite ran 110 tests: 106 passed and four opt-in integration tests were skipped. The final focused identity/ownership checks also passed; the actual resumed C# pilot provides separate runtime coverage of restricted references.

On the identical 1,152-game pack, control / roster-reference wins were 64/64 / 64/64 against the unrestricted controller, 32/64 / 64/64 against Warrior, 20/64 / 32/64 against Rider, 32/64 / 64/64 against Archer, and 64/64 / 64/64 against the older Hard learned reference. Control had 16 Archer-case interruptions; the roster candidate had none. Same-model Hard remained a first-player sweep in both. Unmodified-policy self-games gave first-player rates of 100%/100% for the control's two additional seeds and 96.875%/100% for the roster candidate. These role results still fail the balance goal.

Against held-out unmodified step 25,899,973 on seed 50001, control won 115/128 (63/64 first, 52/64 second); roster-reference won 118/128 (56/64 first, 62/64 second), with no interruptions. The three-win overall difference is not a demonstrated general-strength gain. The useful evidence is improved tactical counter coverage with no obvious loss on this held-out comparison. Recruitment remains predominantly Rider, so this does not establish viable learned mixed-unit strategies.

Retain both pilot checkpoints and ignored receipts under `Logs/Validation/BalanceV4/roster-league-study/`. Extend the roster-reference recipe as a recoverable main-run experiment while retaining the previous recipe and transfer checkpoint. Resume the main run's own latest weights and optimizer, not the short pilot's weights. Keep auxiliary exploration disabled, use four C# arenas, unlimited time and the shared 20 GB cap. Later fixed and held-out comparisons must still establish durable strength and role balance before release selection. No game rules, policy input schema, public deployment or private human recordings change in this extension.

The main extension was activated at approximately 2026-10-10 10:32 UTC after a clean supervisor stop (exit 0). Main-run step 28,592,305, its matching export/configuration and previous recipe are preserved under `frozen-evaluations/rules-v4-roster-transfer-28592305`, checkpoint SHA-256 `9582eef435cf8af5eed9cf3568a2f2e1dcaade48cb1672b898a76e7e9526b790`. The saved checkpoint matched the SDK resume checkpoint's policy and global step, with all 12 optimizer states finite. The resumed learner uses that main checkpoint, not either pilot checkpoint. Initial runtime checks confirm four arenas, all six challenger identities, zero rejected actions, no viewer, unlimited time and the shared 20 GB budget. Treat this activation as the start of a new measured training-recipe period; the stock transient Ghost opponent pool is rebuilt on resume rather than falsely described as fully restored. Recheck fixed/held-out strength and first/second results after a meaningful continuation interval.

### 2026-10-10 fixed tactical strategy matrix

While the roster extension learns, a separate 256-game probe compared four fair tactical controllers against one another, with work budget 512, seed 60001 and 16 games per cell balanced over both colours and starting roles. The controllers use the same legal rules and fair seat projections; row/column roster restrictions apply only to their own recruitment. This is a probe of those particular controllers, not optimized play or evidence of human-level balance. With fixed starting geometry and deterministic tactical choices, repeated games are not independent strategy samples.

Entries below are row-controller capture wins out of 16 scheduled games; `I` marks interrupted games within that total.

| Row / reference | All units | Warrior | Rider | Archer |
| --- | ---: | ---: | ---: | ---: |
| All units | 8/16 | 4/16 | 0/16 | 4/16, 8 I |
| Warrior | 12/16 | 8/16 | 8/16 | 16/16 |
| Rider | 16/16 | 8/16 | 8/16 | 0/16, 8 I |
| Archer | 4/16, 8 I | 0/16 | 8/16, 8 I | 8/16 |

Every same-controller diagonal gave the second player all capture wins, including unrestricted, Warrior, Rider and Archer. This contrasts with the recent frozen learned policy's first-player sweep. Different controller habits can therefore reverse the observed role bias; do not tune opening gold to one current policy's result or call its self-match score an equilibrium measurement. Keep the approved 2/3 opening and test stronger evolving play from both roles.

The matrix shows distinct tactical counters and 32 total interruptions, without establishing balanced optimal strategies. Scout support and mixed-army synergy are not isolated by these single-type probes. Interruption-heavy pairings are not counted as completed draws or successful defensive balance. The bounded matrix changes no model, training recipe, rules or public build. Retained receipts and recent traces are in ignored `Logs/Validation/BalanceV4/tactical-strategy-matrix/`.

### Checkpoint-triggered comparisons

`Tools/Training/evaluate_milestone.py` waits for one specified learner-step threshold on an owned 7×7 run, freezes a completed immutable checkpoint/export/configuration, validates the frozen actor, and runs a saved benchmark. It does not stop/restart training, launch a viewer, select a playable model or deploy. The freeze records the actual selected step and training-recipe/configuration digests; a dedicated milestone directory cannot be overwritten. Retention races during pair selection/copy retry without retaining partial artifacts. Missing first exports can remain pending without weakening existing playtest compatibility errors.

The waiting job verifies a live trainer PID rather than trusting its status file, and fails if the measured trainer stops/changes, its configuration/recipe changes, the suite changes, evaluation source changes, or its bounded wait expires. The C# worker must match the saved suite both before waiting and before evaluation. A separate `.job.json` records waiting/completed/failed state; waiting metadata alone is not process-health evidence. An evaluation failure leaves the real training process alone. The existing evaluation receipts retain complete match data and explicit interruptions.

Validation: the full Python suite passed 116 of 120 tests with four opt-in skips, followed by 11 focused milestone checks including evaluator-source drift. A separately invoked end-to-end job froze an actual live-run checkpoint and completed 16 real C# games. This small smoke result verifies the tool, not a new strength claim.

The next roster-extension review is queued at learner step 29,592,305, one million learner steps after the preserved main transfer. Its saved 13-case pack retains the previous 12 benchmark cases and adds the 128-game seed-50001 unmodified comparison against frozen step 25,899,973, for 1,280 scheduled games. The added case changes the overall suite identity; compare common cases explicitly with earlier receipts rather than claiming the whole pack is unchanged. This reference is excluded from the current retained league, but shares the policy's training ancestry and is not an independent human-strategy test. Results still require inspection before any recipe promotion or release selection.


### Roster extension: first million-step review

The queued evaluation completed on 2026-10-10 at approximately 11:06 UTC: 1,280 scheduled games, all completed by capture, no interruptions. Its frozen main checkpoint is step 29,599,951, SHA-256 `520b8b53129c6f415e01a6c69ba2f66834e6fea4831308bf345ca2e57cc9afb7`, preserved under `frozen-evaluations/milestone-29592305`. Suite identity is `7134388d4009991cdf616d8c9d1556f4302e483b3ae93eecaa3979b4259b9533`; the local receipt is `Logs/Validation/BalanceV4/roster-million-step/suite.json`.

| Fixed comparison | Candidate wins / scheduled games |
| --- | --- |
| Unrestricted tactical reference | 64/64 |
| Warrior-only tactical reference | 48/64 |
| Rider-only tactical reference | 16/64 |
| Archer-only tactical reference | 64/64 |
| Frozen 18,834,965, Hard against Hard | 64/64 |
| Frozen 18,834,965, raw policy against Hard, two extra seeds | 255/256 |
| Held-out frozen 25,899,973, raw against raw | 44/128 |

The held-out comparison deteriorated from the earlier 27,389,984 checkpoint's 120/128 and the short roster pilot's 118/128. At this milestone the candidate won 14/64 when starting and 30/64 when second. All five same-model cases, including raw policy at two extra seeds, gave the second player every capture win. This reverses the previous first-player sweep; it does not establish balance. The same-model aggregate candidate 50% remains a consequence of paired seats.

The learned policy now recruits substantial Warriors alongside Riders (for example 96 of each against the unrestricted reference), with occasional Scouts and no Archer recruitment in this pack. This is evidence of changed behavior, not proof that all unit strategies are viable or that strength improved generally. Preserve both old and new frozen versions; do not promote this milestone as a stronger Hard opponent or tune opening gold to its role reversal.

Keep the approved training recipe unchanged for another measured interval, rather than reacting to one oscillating snapshot. Queue the same 13-case benchmark at step 31,592,305, three million steps after the main transfer, to test whether the regression and role reversal persist. Training continues without a viewer, under the shared 20 GB cap; public learned-AI selection remains pending human review.


### Three-million-step review and retained opponent history

At approximately 12:02 UTC the identical 13-case pack completed another 1,280 captures with no interruptions, using step 31,604,983, checkpoint SHA-256 `b28b607b6c16dd33dda15ccad250265be97d88e9502548087f2ee3195c157742`. Its receipt is `Logs/Validation/BalanceV4/roster-three-million-step/suite.json`; suite identity remains unchanged. Candidate wins were unrestricted 64/64, Warrior 32/64, Rider 64/64, Archer 64/64, old 18,834,965 Hard 64/64 and raw-versus-old-Hard 256/256 across extra seeds. Against held-out 25,899,973 raw it won 67/128: 64/64 when starting, only 3/64 when second. All five same-model cases still gave the second player every capture win. Rider and Scout combinations appeared in same-model games, with Warriors elsewhere; no Archers were recruited in this pack.

A separate balanced 128-game raw-policy comparison, seed 70001, between steps 31,604,983 and 29,599,951 gave every win to the first player: the newer model won 64/64 when starting and 0/64 when second. Receipt: `Logs/Validation/BalanceV4/roster-three-vs-one-million/evaluation.json`. Both versions lose every first-player same-model game, so aggregate 50% in this cross-version comparison is also not equilibrium balance. The evidence suggests exploitable, changing opening responses rather than monotonic improvement; the restricted Rider improvement is not a general strength claim.

To reduce forgetting of recent learned responses, the retained league now includes both frozen milestone policies with raw sampling, weight 1 each. The old 18,834,965 Hard reference's weight was reduced from 3 to 1. Total challenger probability remains 40%, total weights remain 8, and learned self-play remains 60%. Each of the eight retained challengers therefore receives an expected 5% of new matches. The held-out 25,899,973 benchmark policy remains excluded from training. All learner actions and unit choices remain available; no type-specific reward or prescribed opening is introduced.

The supervisor saved cleanly (exit 0), preserving its own current step 31,730,113 under `frozen-evaluations/milestone-31730113` together with the previous recipe. The immutable checkpoint matched the SDK resume policy/global step and had 12 finite Adam states. Resume at approximately 12:04 UTC restored that checkpoint and optimizer, rather than copying either opponent's weights into the learner. Runtime verified all eight challenger identities, four arenas, zero rejections/errors, no viewer, unlimited time and approximately 1.22 GB shared storage. The transient Ghost snapshot pool is rebuilt on resume. The same benchmark is queued at step 34,730,113 to assess this change over a measured interval. Do not promote a model or modify opening gold based on these oscillations.


### Archer diagnostic with more search work

A separate probe at approximately 12:28 UTC increased only the fair reference controller's work from 512 to 2,048, using seed 80001, 16 games per cell and all four seat/starting-role cells. Frozen learned step 31,604,983, sampled with its raw policy, still beat the Archer-only controller in 64/64 captures from both starting roles. It recruited no Archers. More search work alone therefore did not expose a useful ranged challenge for this particular learned version.

Against a Rider-only tactical controller at the same work budget, the Archer-only controller won all 32 matches when second. Its 32 first-player matches reached the 100-round limit, with no captures or losses. Those interruptions remain interruptions, not draws; the 32/32 capture win rate must not be presented as 100% over 64 completed games. This limited test shows one ranged controller can defend and win in a specific matchup/role, not general Archer viability or a solved learning strategy. The fixed geometry and deterministic controller limit independence of repeated samples.

Local evidence is `Logs/Validation/BalanceV4/archer-more-compute/request.json`, `results.json` and each comparison's full `evaluation.json`. The request pins the frozen model and worker identity. No learner restrictions, rewards, game rules, weights, training recipe or benchmark definition changed. Continue the retained-history interval; if Archer non-use persists, distinguish poor action exploration/positioning from unit balance before changing stats.

### Controlled exploration comparison, 2026-10-10 12:51 UTC

Three private continuations started from the same immutable step-31,730,113 checkpoint, SHA-256 `8baf28dbb187300d1dd7cccd74a4fb111fbe5ce80b68e56751ee2135ddd301a7`, preserving the policy and all 12 Adam states. Each targeted 131,072 additional learner steps with seed 42, one arena, the same eight retained challengers, and the unchanged rules, rewards and legal-action contract. The arms used ordinary entropy coefficient 0.03 (control), coefficient 0.1, or coefficient 0.03 with the existing optional bounded exploration pressure (coefficient 0.02, normalized entropy floor 0.25, batch 128). The pressure arm adds a uniform-legal-action actor loss on saturated distributions; it does not replace actions or assign unit rewards.

All three trainers exited cleanly with finite policy/optimizer states and zero rejected actions. Control and higher entropy advanced every Adam state by 3,024 updates; pressure advanced actor states by 3,087 and critic states by 3,024, confirming 63 auxiliary actor updates. Their final steps were 31,861,201, 31,861,210 and 31,861,212, respectively. Each completed the identical 1,280-game benchmark, suite `7134388d4009991cdf616d8c9d1556f4302e483b3ae93eecaa3979b4259b9533`, without interruptions.

| Measure | Control | Higher entropy | Exploration pressure |
| --- | ---: | ---: | ---: |
| Wins against Warrior reference /64 | 44 | 32 | 49 |
| Wins against Rider reference /64 | 48 | 64 | 32 |
| Wins against held-out step 25,899,973 /128 | 68 | 71 | 29 |
| First-player wins in raw same-policy tests /256 | 0 | 30 | 10 |
| Archer recruits in those raw same-policy tests | 0 | 31 | 2 |
| Archer recruits by winning sides in those tests | 0 | 15 | 1 |

All arms beat the unrestricted, Archer-only and old step-18,834,965 Hard references in 64/64 each. Higher entropy also retained 256/256 wins in the two raw-policy comparisons with that old Hard reference; pressure fell to 229/256. The higher-entropy arm showed mixed armies containing Archers, but recruitment is not evidence that an Archer caused a win. A separate inspection of its bounded retained replays found 59 distinct raw-self-play traces, 13 Archer attacks dealing 13 displayed health points, eight kills and seven attacks by eventual winning sides. These are actual actions from spectator replay state, not extra information supplied to the policy, and the retained traces are not the complete evaluation sample.

Fair city-selected opening probes confirmed all four recruits were legal. The parent, control and higher-entropy policies still chose Rider with practically unit probability; Archer probabilities were approximately `1.8e-15`, `6.9e-14` and `2.1e-13`. Pressure raised that probability to `1.2e-6`, still negligible. The second-player probe followed a hypothetical first-player pass and must not be presented as a played opening result. This distinguishes later mixed-unit exploration from recovery of alternative opening choices.

Private receipts and immutable branch weights are in `Logs/Validation/BalanceV4/entropy-study`, `pressure-study` and `exploration-continuation-review`; the latter includes benchmark results, opening probabilities and `recent-unit-use.json`. Reject the pressure arm as a strength improvement at this budget. Higher entropy merits a longer matched trial because it recovered some ranged play, but the Warrior regression and remaining starting-role bias prevent promotion or a claim of balanced unit viability. The main retained-history interval and public release remain unchanged while its queued benchmark completes. Fixed geometry, repeated deterministic lines and a single training seed limit the statistical independence of these comparisons.

### Generic roster augmentation correctness check, 2026-10-10 13:15 UTC

The new opt-in `roster_augmented_training.py` entry point presents public recruit capabilities in a shuffled order and restores action indices at the C# boundary. It does not change stats, rewards, fair information, available recruits or the shipped input/output shapes. This is a generalization experiment: the existing dense network can otherwise learn a preferred fixed output slot instead of comparing capabilities. It remains disabled in the main learner and the matched entropy trials.

Eight focused tests passed, including every four-slot permutation and real-worker recruitment of each represented unit. A separate preserved-weight smoke started from step 31,730,113, targeted 4,096 additional learner steps and exited cleanly after approximately 20 seconds. All 12 Adam states advanced by 48 updates and remained finite. Its single arena executed 4,622 actions, completed 145 captures with no interruptions, exercised all eight retained challenger identities, and reported zero rejected actions or failures. The adapter composes with the underlying environment; fixed challengers keep their original unpermuted capability/action tensors. These results validate the trainer/action boundary, not strength, balance or transfer to new units. Private receipts are in `Logs/Validation/BalanceV4/roster-augmentation-smoke` and the owned `study-roster-augmentation-smoke-31730113` run. Longer matched exploration trials continue separately, with one additional arena at a time and the existing shared storage guard.

A read-only capability sensitivity probe also tested the parent and all three short exploration arms on the same fair city-selected first-turn observation, with each of the 24 public roster permutations. Every policy kept choosing presented recruit slot 1 as its most probable opening choice in all 24 layouts. That slot is Rider in the usual ordering, but it represents each physical type equally often in this probe; decoded average recruitment probabilities were approximately 25% per type. This is an opening-specific dependence on the slot, not balanced recruitment in actual games or evidence that an Archer opening wins. The probe supports testing roster augmentation instead of assuming the supplied capability table is already being used in an order-independent way. Observation and model hashes and all rows are preserved in `Logs/Validation/BalanceV4/roster-capability-sensitivity/results.json`.

### Retained-history milestone and matched roster trial, 2026-10-10 13:53 UTC

The main retained-history interval completed its queued 1,280-game pack, with all games ending by capture and no interruptions. The frozen candidate is step 34,744,985, checkpoint SHA-256 `fc7790bfcc91eb325aa341d25ec822a2906587b0d87e17082240c023a844154b`, preserved under `frozen-evaluations/milestone-34730113`. Its suite identity remains `7134388d4009991cdf616d8c9d1556f4302e483b3ae93eecaa3979b4259b9533`. It beat the unrestricted, Rider-only, Archer-only and old 18,834,965 Hard references in 64/64 each. Against the Warrior-only reference it won 37/64, split 5/32 when starting and 32/32 when second. Against held-out 25,899,973 raw it won 66/128, split 63/64 first and 3/64 second. All Hard same-model cases still gave the second player every win. Raw same-model first-player wins were 10/128 and 18/128 on the two extra seeds. This is modest behavioral movement, not a demonstrated resolution of role bias or broad improvement over the previous milestone. Four Archers were recruited across those 256 raw same-model games; only one belonged to an eventual winning side.

The separate roster-permutation trial continued the identical step-31,730,113 parent, eight-opponent recipe, seed, one-arena setup and higher entropy coefficient (`beta=0.1`) for the same requested 131,072 additional learner steps as the unaugmented short entropy arm. It finished at step 31,861,248 in approximately 518 seconds, with all 12 finite Adam states advanced by 3,024 updates and zero rejected actions. Its immutable checkpoint SHA-256 is `06866dea263c2f4d0580b7440037c5ecf2f112d22f197dbd9ed36f21a86705d4`. The identical reference pack completed 1,280 captures with no interruptions.

| Comparison | Unaugmented higher entropy | Roster permutation plus higher entropy |
| --- | --- | --- |
| Unrestricted tactical reference | 64/64 | 64/64 |
| Warrior-only tactical reference | 32/64 | 32/64 |
| Rider-only tactical reference | 64/64 | 32/64 |
| Archer-only tactical reference | 64/64 | 64/64 |
| Old 18,834,965, Hard against Hard | 64/64 | 0/64 |
| Raw against old Hard, two extra seeds | 256/256 | 83/256 |
| Held-out 25,899,973, raw against raw | 71/128 | 92/128 |
| Raw same-model first-player capture wins | 30/256 | 170/256 |
| Archers recruited in those raw same-model games | 31 | 121 |
| Archers on eventual winning sides in those games | 15 | 64 |

The augmented arm produced more mixed armies and better results against one held-out version, but regressed sharply against other preserved responses. Recruitment and self-match role percentages do not establish Archer utility, balance or general strength. A follow-up read-only test on the same frozen fair opening still found presented slot 1 preferred in all 24 roster layouts, for both the augmented arm and the new main milestone. In the usual ordering the augmented opening still gave Rider probability approximately `0.9999999971`. Thus the short trial has not repaired the opening's dependence on a fixed slot, even though later actions changed. Keep augmentation experimental; do not replace the main learner or playable opponent with it on the basis of variety or a nearer self-match role split.

Private receipts are in `Logs/Validation/BalanceV4/retained-history-three-million-step`, `roster-entropy-study` and `roster-entropy-review`, including `capability-sensitivity.json`. Longer matched control, entropy and pressure arms each target one million additional steps from the same parent; their results must be reviewed before promoting a training adjustment. Fixed board geometry, one training seed and repeated deterministic lines remain limits on generalization and sample independence.

At the user's request, a separate local 7×7 human match opened on the redesigned v4 units using frozen step 34,744,985 with Hard sampling. The human is Blue and starts with 2 gold; the second player's first turn receives 3. The frozen checkpoint remains stable during play, fair player visibility is retained, and completed compatible human wins are recorded in the owning run for bounded imitation. The main four-arena learner continues independently, with no training inspector, no duration limit, zero rejections or failure, and approximately 1.88/20 GB shared storage at this inspection. Human playtesting does not authorize public learned-AI deployment.

## Checkpoint and roster review, 2026-10-11

The rules-v4 main learner remains the reproducible four-unit baseline while the matched exploration trials finish. A new frozen checkpoint at step **43,454,988** was evaluated on the unchanged 1,280-game reference suite, with both starting roles in every case. Its SHA-256 is `cbad12dce7e451d9b6856259a59e24cbd741c7248f692bc4d79cee312d627aa7`; receipts are in `Logs/Validation/BalanceV4/latest-43300000/`. All 1,280 games ended by capture, with no interruptions.

| Reference case | Latest candidate wins | Earlier step 34,744,985 |
| --- | ---: | ---: |
| Unrestricted fair tactician | 64/64 | 64/64 |
| Warrior-only fair tactician | 53/64 | 37/64 |
| Rider-only fair tactician | 48/64 | 64/64 |
| Archer-only fair tactician | 64/64 | 64/64 |
| Frozen step 18,834,965, Hard sampling | 64/64 | 64/64 |
| Held-out step 25,899,973, raw policy | 62/128 | 66/128 |

The latest raw-policy self-matches on seeds 30001/40001 produced **248 first-player wins out of 256**. Candidate recruitment in those same cases was 667 Riders, 189 Scouts, 31 Warriors and **zero Archers**. The earlier checkpoint's first-player share was 28/256. This is evidence of a changed, highly role-biased learned strategy; it does not establish an intrinsic universal first-player advantage or improved balanced strength. Candidate-versus-itself overall win totals are 50% by construction because roles are swapped. Do not use that aggregate as evidence that first/second-player balance is solved. Do not promote the latest checkpoint solely because it is newer or beats one reference more often.

The original higher-entropy matched trial hit its **80-minute time guard** before completing the one-million-step target. Its final partial checkpoint at step 32,572,892 and the original failure receipts are preserved. There was no checkpoint export failure or storage exhaustion. Recovery restarts the incomplete entropy and exploration-pressure arms from the identical frozen parent at 31,730,113, seed 42, one arena and unchanged settings; it raises only the owned job's time guard to three hours per arm. The completed control is retained. Separate recovery/review receipts are in `Logs/Validation/BalanceV4/exploration-million-recovery/` and `exploration-million-recovery-review/`. Review uses the same pinned rules-v4 worker and fixed references. A longer guard does not change the one-million-step comparison budget.

The approved roster alternatives now have explicit C#/Unity rule profiles:

- `blocknations-simulation-v4`: unchanged four-unit baseline and Archer vision 1.
- `blocknations-simulation-v5-vision2`: four units and Archer vision 2, isolating the vision change.
- `blocknations-simulation-v5-core3`: Warrior/Rider/Archer recruitment and Archer vision 2.

The schema-v2 catalog keeps `[archer, rider, scout, warrior]`, including Scout's disabled slot. Purchase masks, authoritative command validation and tactical lookahead respect public availability; Warrior keeps slot 3. The existing 3,120-float/259-action layout remains compatible with transferring weights. The recruit-presence channel and active-count global describe availability under the new profiles. Record parent training rules separately from the rules under which transferred weights are subsequently trained/evaluated. Saved Scout definitions and existing units remain supported; ordinary gameplay/PBp stays on the baseline profile. Native training/playtests select profiles explicitly before match setup. No public release changes are part of this experiment.

The new profiles are prepared separately; the live learner and already running exploration comparisons still use the pinned v4 worker. Finish those comparisons and their reviews before selecting the next training recipe. Then create a distinct run/recipe and worker identity for a matched vision-only/core-three comparison, preserving baseline weights and model-slot provenance. The Python supervisor, frozen-reference transfer manifests and evaluator gates must receive explicit profile support before activating that training branch. Do not bypass their current v4 compatibility checks or overwrite the running worker.

Validation: 93 focused Unity checks passed, followed by the final rule-profile/scene-parity suite after the last lifecycle adjustment. The separate native worker completed 64 random-action games per profile with zero rejections; these are legality checks, not strength measurements. An exact baseline comparison against the unchanged running binary passed 2,048 batches across four arenas, including 11 completed matches and terminal rewards. Python's existing transport accepted the new worker's default v4 profile. Receipts remain in `Logs/Validation/RuleProfiles-20261011/`. The shared training directory used approximately 2.26/20 GB at this check, and the main learner remained unpaused with four arenas, no failures and zero rejections. The new worker is built in a separate output directory; it has not replaced the running worker or the native human playtest app.

## Completed exploration comparison and matched rule trials, 2026-10-11

All three one-million-step exploration arms and their 1,280-game reference suites have completed. Recovery restarted the incomplete arms from the same parent rather than comparing a partial continuation with a full one. The parent was step 31,730,113, seed 42, one arena, and the same eight retained/tactical challengers. Each resumed optimizer advanced and produced finite weights, with zero rejected actions. These suites retained the same worker, cases and sampling definitions; receipt suite ID is `7134388d4009991cdf616d8c9d1556f4302e483b3ae93eecaa3979b4259b9533`.

| Measurement | Control beta 0.03 | Higher entropy beta 0.1 | Bounded exploration pressure |
| --- | ---: | ---: | ---: |
| Unrestricted tactician wins | 64/64 | 64/64 | 64/64 |
| Warrior-only tactician wins | 46/64 | 48/64 | 35/64 |
| Rider-only tactician wins | 48/64 | 64/64 | 64/64 |
| Archer-only tactician wins | 64/64 | 64/64 | 64/64 |
| Old step 18,834,965, Hard against Hard | 40/64 | 64/64 | 64/64 |
| Held-out step 25,899,973, raw policies | 63/128 | 126/128 | 66/128 |
| Raw self-match first-player capture wins, extra two seeds | 62/256 | 64/256 | 136/256 |
| Hard self-match first-player capture wins, all three seeds | 6/320 | 26/320 | 282/320 |
| Archers recruited in the 256 extra raw self-matches | 1 | 6 | 0 |

The higher-entropy arm is a promising parent for the next controlled experiment because it improved the fixed reference results without a recruitment quota. It is not a proven balanced or varied opponent: its raw self-match first-player share is 25%, and its opening Rider probability is still approximately 0.999856. The pressure arm's raw self-match role split is nearer 50%, but it regressed against the Warrior reference and did not gain against the held-out model; Hard sampling has a very different role bias. Do not promote it on the basis of that one percentage. These are one-seed continuations with repeated fixed geometry, not independent proof of human strength or equilibrium game balance. Archer purchases on eventual winning sides also do not establish that those purchases caused the wins.

The Python boundary now explicitly accepts the three prepared profiles. The supervisor preserves a resumed run's recorded rules and rejects an implicit rule change. Transferred reference weights keep their original `rulesVersion`, hashes and configurations; the recipe/evaluator requires a separate source/target transfer contract with schema, shapes and the unchanged catalog. A new run records its parent and target rules separately. A checkpoint cannot claim new-rules training before its step exceeds the parent boundary. Human demonstrations are filtered by the active rules; existing private recordings remain in their owning run. Browser export remains gated to the reviewed baseline contract.

`Tools/Training/rule_profile_study.py` runs a matched baseline, vision-only and three-unit continuation from one preserved parent. Each uses one arena, the same learned-step target, seed, optimizer and training recipe. Fixed benchmarks run before and after each continuation, under that arm's rules, with both colors and starting roles. The before measurement is labeled as transferred weights; it never claims they were trained under the new rules. Benchmarks have distinct rules/worker/source identities, so do not append them to the old v4 Elo series. The main four-arena v4 learner remains unchanged alongside these bounded trials.

The selected common parent is the completed higher-entropy checkpoint at step **32,730,121**, SHA-256 `4e135da8db12b4cb49960da9e4719c72891299f3c88d18902fc7fa0a521989c5`. All optimization settings, including beta 0.1, are preserved in each arm; the rule profile is the experimental difference. The full trial target is one million additional learned steps per arm, with a three-hour time guard per arm. The study and main learner share the marked training root and 20 GB artifact budget; the study reserves 512 MB for checkpoint saving, checks the free-disk reserve, owns its child process and bounded log, and keeps the Unity viewer closed. No current human opponent or public deployment is automatically replaced. Inspect receipts under `Logs/Validation/RuleProfileStudy-20261011/` before choosing a model or rule change for a human playtest.

Validation before activation: the Python suite ran 140 checks (137 passed, three optional integrations skipped), and the final milestone guard checks passed 11/11. Baseline-worker/human-ingestion checks passed 16/16 against the original v4 binary. Three real resume/train/save smoke arms each added at least 4,096 learned steps, advanced every retained Adam counter by 48, changed six policy tensors, and saved finite PT/ONNX pairs with zero rejected actions. Their fixed before/after smoke benchmarks completed 192 games in total; those short checks establish a working training/evaluation path, not improved strength. The parent checkpoint hash remained unchanged. Receipts are in `Logs/Validation/RuleProfileStudy-20261011/smoke/` and `validation.json`. Shared storage was approximately 2.72/20 GB and the main run had passed 52 million saved steps with four arenas, no failure and no rejected actions. The installed human/inspection app remains the v4 build until a separate matching native player is rebuilt.
