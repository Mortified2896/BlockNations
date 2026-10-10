# General AI training direction

Recorded on 2026-10-09 from the owner's discussion of the long-term game direction. This is a reference plan, not authorization to implement a broad core-system refactor. The current self-play run and its weights remain a baseline; this plan does not prescribe a new seed, board size, or reset.

The owner subsequently approved phases 1–2 as an overnight goal. Their implementation, rule corrections, validation and remaining boundaries are recorded in [Shared C# Simulation](Shared_Simulation_Implementation.md). The later policy, reproducibility and evaluation phases below remain follow-ups.

On 2026-10-10 the owner approved the [7×7 balance and training experiment](AI_Balance_Experiment.md): whole-number unit stats, a 2/3 first-turn opening, continued weights, configurable fair training challengers and frozen balanced evaluations. This implements bounded parts of experiment/opponent management; memory, an adaptable new network/action representation and faithful RNG continuation remain follow-ups.

## Intended outcome

Build a reusable simulation and training framework that can grow with larger boards, new units and mechanics, and eventually additional seats/relationships. Ship a strong opponent that runs locally on target iPhone/Android devices, including the browser build hosted by Cloudflare. Training runs on development machines; the shipped game needs only the compatible model and local inference path.

The priority is the architecture for learning the game, rather than special rules against particular opponents or explanations of a current policy's strategy. Evaluation supports architectural decisions but is not the whole plan.

## 1. One authoritative simulation, independent of rendering

Establish a plain game-state interface for reset/configuration, observation by explicit seat, legal actions, action application, and outcomes. Unity renders and accepts player commands against that state; training drives the same rules without requiring sprites, transforms, or health canvases for every action.

Extract existing rules in bounded steps: movement/path blocking, combat, recruitment/economy, capture, and turn progression. Do not maintain a separate simplified training implementation. Compare state transitions against supported gameplay, including both seats, fog/hidden blockers, resources, capture, and turn starts, before moving authority.

Keep player identity, ownership, turn seat, viewer knowledge, team relationships, and transport independent. Preserve existing free-for-all rules until team/membership rules are decided. The current two-seat learned encoding is not a permanent game-capacity limit. Preserve supported saves and PBp migration paths; simulation extraction does not itself authorize format retirement.

## 2. Capability-based observations and adaptable decisions

The MVP already supplies unit/recruit statistics and authoritative action masks. Its fixed 11×11 canvas, 16 recruit slots, two-seat encoding, and two-stage source/action selection are versioned MVP constraints, not permanent interfaces.

Explore a compact spatial board encoder, shared processing of unit/recruit capabilities, and scoring of legal action candidates. A new unit should primarily be described by its cost, movement, combat, vision, and abilities, rather than by a hand-written named strategy. Start with a bounded masked representation if needed; variable candidate processing may require a custom trainer/export adapter and must be measured rather than assumed to be a stock configuration option.

Generalization is a goal, not a guarantee. New combinations can require further training; genuinely new mechanics require rules and observation/action features. Version model contracts separately from gameplay saves, retain compatible old inference adapters, and make incompatible exports fail clearly. Do not silently omit units/actions to fit a tensor capacity.

An opt-in development adapter now supports **recruit-slot permutation** for the existing feed-forward schema-v2 policy. `Tools/Training/roster_augmentation.py` shuffles the public capability blocks and their corresponding recruitment masks together, then maps the selected index back before sending it to the C# environment. Source selection, movement/attack targets, rewards, turn ownership and fog inputs retain their original meaning. The environment wrapper uses composition so its internal fixed opponents continue seeing the original roster order. Decisions and their mappings are cached until the next real step; terminal rewards and interruption flags are preserved. This makes memorizing a fixed recruit index less useful, without prescribing which unit to buy. It does not yet prove the trained network is order independent or that unseen units work well.

Activation uses the separate `roster_augmented_training.py` entry point with the ordinary trainer arguments/environment, and an explicit owned-run `roster-augmentation.json` recipe containing `version: 1`, the current `rulesVersion`, matching `boardSize`, `mode: "per-decision"` and an integer `seed`. The adapter rejects recurrent policies, incompatible contracts and padded recruit actions. The normal supervisor and existing runs remain on their normal entry point. Game saves, inference shapes and mobile model size are unchanged. Eight focused checks cover all 24 four-unit permutations, inverse action legality, unchanged non-roster inputs, per-agent mappings, stale-decision invalidation, terminal/group rewards, deterministic seeds and all four actual C# recruit commands. A preserved-weight 4,096-step PPO smoke also completed with all 12 optimizer states advancing, all eight retained challenger identities exercised, and zero rejected actions. Performance needs a separate matched trial before activation in the main learner; see the balance experiment log.

## 3. Fair information, fog, and useful memory

Represent public starting city locations, current sight, and remembered observations distinctly. Last-seen enemy information must record uncertainty/age and must come only from that observer's legitimate history. Hidden live enemy units, resources, and ownership changes are not policy inputs.

Compare explicit history with a small recurrent policy when the learning task calls for it. Keep spectator statistics and omniscient replay views outside policy observations. Scouting/defence should emerge from winning under these information rules; a scouting bonus is not an architectural requirement.

## 4. Reusable experiments and faithful continuation

A training recipe records board/opening distributions, roster and rules version, information/action schema, reward definition, opponent selection, seeds, and compute/storage limits. Train across relevant situations and expand difficulty based on evidence, while retaining some earlier situations to test forgetting.

A resumable checkpoint should preserve model/optimizer state, random-generator state, and the recipe. Record checkpoint lineage and compatibility so training can move between the Mac and RTX machine and can continue after compatible content changes. Today the saved seed is restored, but Unity recreates its random generator rather than restoring its position; faithful continuation is a follow-up.

Keep generated data bounded. Preserve resumable and selected best checkpoints, small metrics, and limited useful replay/action traces. Filling the shared 20 GB allowance is not a learning objective. Resume/model version metadata changes need an explicit compatibility plan before implementation.

## 5. Replaceable trainers and practical phone inference

Keep Unity ML-Agents/PPO as the first training adapter. The game should depend on the simulation/observation/action contract and an inference policy, not Python or a particular algorithm. Leave room for other RL trainers, imitation from authorized human games, and learned-policy/value-guided search.

Measure model size, RAM, move latency, responsiveness, and browser/native compatibility on actual target phones early. Phone/browser inference is still unverified in the MVP. Preserve comparable strength levels across devices with explicit model/work settings; elapsed time varies by hardware. The earlier approximately three-second benchmark-phone target is a calibration goal, not evidence of measured performance or a universal timeout.

## Supporting evaluation and model selection

Compare checkpoints against a stable frozen reference set with controlled seeds, balanced colours/positions and first-player assignments. Report sample sizes/uncertainty and distinguish turn-limit interruptions from genuine results. Changing the reference set, board, or rules creates a separately identified benchmark series. Self-play Elo against a changing opponent pool is training telemetry, not absolute strength or human Elo.

The development trainer now supports a provenance-checked retained opponent league, and `Tools/Training/evaluate_suite.py` repeats a pinned reference pack against successive frozen candidates. See [the balance experiment](AI_Balance_Experiment.md) for the active recipe, measured results and remaining acceptance gates.

Keep useful older opponents in a bounded pool to test whether new learning forgets previous challenges. Select a best tested checkpoint rather than assuming the newest is strongest. Behavioral summaries/replays help diagnose failures but are not mandatory explanations in the player's UI. Preserve the existing requirement to ask before a watched comparison tournament; automatic training matches need no per-match approval.

## Proposed implementation sequence

1. Define the simulation contract and incremental extraction/parity checks; profile current throughput to establish a baseline.
2. Move rule authority into that shared simulation in bounded steps, retaining existing gameplay/save/PBp compatibility and the working training adapter.
3. Introduce a separately versioned, more adaptable policy representation; compare it against the preserved MVP model.
4. Expand training distributions/opponent management and cross-machine reproducibility; use evaluation to choose changes and models.
5. Validate/export a compact local opponent for phone/browser playtesting before claiming shipping readiness.

Choose exact files and acceptance checks when each phase is requested. A full rules rewrite, new network, and new curriculum are not one combined first patch.

## Four-hour continuation and roster review, 2026-10-10

The owner requested four-hour progress checks and authorized sensible rules/roster adjustments. The existing progress automation now uses that interval; the main 7×7 learner continues between checks under the shared 20 GB budget. Finish the already running matched exploration experiments before changing their conditions. Keep the four-unit rules-v4 run and compatible weights as the comparison baseline.

Low recruitment does not establish that a unit is useless. The frozen main checkpoint at step 34,744,985 recruited 724 Riders, 539 Warriors, 211 Scouts and 4 Archers across the 256 raw-policy self-matches on seeds 30001/40001. These are candidate recruitment counts from one evaluated version, not current live frequencies, causal utility measurements or 256 independent strategies. Scout is used in that sample; Archer is particularly rare. Receipts remain in `Logs/Validation/BalanceV4/retained-history-three-million-step/suite.json`.

There are concrete role questions to test. Archer costs 2 gold, has range 2 but vision 1, requires a currently visible target and cannot attack after moving. Its long-range role therefore often depends on another friendly spotter. Scout costs 1, moves 1 and has vision 2 without an attack. On this small board with public city locations, the value of buying vision instead of combat is uncertain. The current feed-forward policy retains explored-tile flags but has no enemy-unit observation memory; this is a separate learning limitation, not proof of poor roster design.

Next steps after the matched exploration results:

1. Review strength, role bias and counter-strategy performance before promoting a training recipe. More elapsed time and more varied purchases are insufficient acceptance criteria.
2. Compare an explicit three-unit core (Warrior, Rider, Archer) with the preserved four-unit setup. Treat Scout as an experimental reconnaissance option, retaining its definition and supported saved units. Do not silently reindex an existing model's recruit meanings or remove registry/save support.
3. Test Archer vision 2 as a separate, minimal shared-rule change before combining multiple stat buffs. It would let an Archer see the distance at which it can shoot while retaining its movement/attack restriction. Version changed rules and benchmark series, preserve the parent weights, and keep Unity play and C# training on the same definitions.
4. Evaluate whether each role enables useful winning or defensive responses, using both starting positions, several fixed opponents and human playtests. Do not reward or mandate purchasing particular unit names. A smaller roster is a game-design experiment, not a substitute for an adaptable action representation, useful memory or sound exploration.

These alternatives are the next experimental plan; the current main run still uses all four units and unchanged rules-v4 definitions. Keep the learned browser release pending the owner's playtest review.

## References

- [Current ML training MVP](ML_Training_MVP.md): implemented controls, schemas, storage, replay, and validation limits.
- [Development follow-ups](Development_Followups.md): other core/PBp work and boundaries.
- [Compatibility policy](PBp-Compatibility.md): supported persistent/protocol migration paths.
- [Unity self-play configuration guidance](https://github.com/Unity-Technologies/ml-agents/blob/release_22_docs/docs/Training-Configuration-File.md#self-play): opponent diversity/stability and conservative reward design.
