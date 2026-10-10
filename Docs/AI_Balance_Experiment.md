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

Add `--reference-checkpoint` for a fixed learned opponent. Omit it for the fair tactical reference; its work is bounded by `--reference-work`. `--reference-recruit-type` is an evaluation-only restricted-roster probe, not a constraint on the candidate. `--tactical-candidate` compares tactical references without loading neural weights. Never feed evaluation results to PPO or present changing training Elo as fixed-reference strength.

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

The Easy/Medium self-match probes also favoured the second player. A fair tactical self-match at work 512 likewise did not resolve the bias. Do not change the approved 2/3 gold opening merely to force a weak policy's score to 50%. Train and test better opening choices first, then change unit/rule balance only with evidence across competent opponents and both starting positions.

Acceptance needs stronger performance against a preserved reference set and human playtests, successful strategies using different combat units and mixed armies, and no material first/second bias across competent, held-out opponents. Near 50/50 is a target with sample uncertainty, not an exact percentage to tune against one checkpoint. Scouts are a vision/utility unit; Scout-only winning armies are not a requirement. A viable specialist strategy need not recruit every type, and equal recruitment percentages are not the objective.

The current policy is **not accepted as strong or balanced**. More learning may take hours or days, and the required duration depends on whether the architecture learns defence and alternative openings. Check frozen candidates at two-hour intervals; prioritize a reproducible improvement over a rising self-play Elo. If extended training does not improve the fixed tests, investigate opponent diversity, observation/memory, network capacity and action representation before spending another night with the same recipe.

## Export and local testing

`checkpoint-contract.json` records the rules/opening and the last checkpoint step before a transition. A numbered checkpoint must be newer than that boundary before its export claims current compatibility. An explicitly selected frozen candidate must also match its provenance manifest and content hash. Reading a latest pair retries selection if training retention retires it during the read. A running arena under new rules cannot relabel old weights as a new-rule release.

The local embedded experimental model is now the frozen step-16,089,952 v4 export, so the newly compiled single-player game does not try to load a v3-only model under changed combat rules. Easy, Medium and Hard remain experimental sampling presets until measured separation is established. Their names do not certify difficulty. The model is still 1,803,075 ONNX bytes; training/optimizer data and authentication are not included.

Ordinary self-play and automated balanced evaluations are authorized. Ask the owner before a watched comparison tournament. Keep the public Cloudflare build unchanged until the owner reviews the local learned-AI candidate.

## Validation

- 126 focused graphical Unity tests passed for the shared combat rules, scene/direct parity, first-turn economy, player labels, hidden enemy gold and human playtest flows.
- 85 Python tests completed with 84 passes and one opt-in human-pipeline check initially skipped. The separately run opt-in test passed with a synthetic second-player win produced by actual Unity input: two imitation updates, 128 examples, and checkpointed Adam steps continuing from 50 to 98. The installed self-contained worker also passed its 13 integration checks. Python checks cover explicit opening upgrades, preserved manifests/weights, rules transitions, export provenance, actual ONNX probability/mask parity, worker ownership, challenger/learner separation and balanced interruption reporting.
- The updated native Mac player and self-contained ARM64 C# worker built successfully. The selected export passed 24 ONNX conversion fixtures and eight actual Unity CPU fixtures (maximum probability error 7.5e-10). Model compatibility is verified separately from playing strength. The native AI-first playtest connected, reached the human turn with 3 gold, and returned without stopping the four-arena learner; recording passed separately through real Unity command hooks and the Python pipeline. The recorder-corrected player is also installed at the Editor’s default `Build/LocalTrainingV2.app` path; updating this optional player does not restart the running C# learner.

Local receipts, test XML, probability fixtures and screenshots live in ignored `Logs/Validation/BalanceV4/` and the isolated validation project's `Screenshots/`. Private training/human match files remain outside Git. Physical phone performance and new-rule public/browser acceptance remain separate work.
