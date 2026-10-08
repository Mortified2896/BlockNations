# General AI training direction

Recorded on 2026-10-09 from the owner's discussion of the long-term game direction. This is a reference plan, not authorization to implement a broad core-system refactor. The current self-play run and its weights remain a baseline; this plan does not prescribe a new seed, board size, or reset.

The owner subsequently approved phases 1–2 as an overnight goal. Their implementation, rule corrections, validation and remaining boundaries are recorded in [Shared C# Simulation](Shared_Simulation_Implementation.md). The later policy, reproducibility and evaluation phases below remain follow-ups.

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

Keep useful older opponents in a bounded pool to test whether new learning forgets previous challenges. Select a best tested checkpoint rather than assuming the newest is strongest. Behavioral summaries/replays help diagnose failures but are not mandatory explanations in the player's UI. Preserve the existing requirement to ask before a watched comparison tournament; automatic training matches need no per-match approval.

## Proposed implementation sequence

1. Define the simulation contract and incremental extraction/parity checks; profile current throughput to establish a baseline.
2. Move rule authority into that shared simulation in bounded steps, retaining existing gameplay/save/PBp compatibility and the working training adapter.
3. Introduce a separately versioned, more adaptable policy representation; compare it against the preserved MVP model.
4. Expand training distributions/opponent management and cross-machine reproducibility; use evaluation to choose changes and models.
5. Validate/export a compact local opponent for phone/browser playtesting before claiming shipping readiness.

Choose exact files and acceptance checks when each phase is requested. A full rules rewrite, new network, and new curriculum are not one combined first patch.

## References

- [Current ML training MVP](ML_Training_MVP.md): implemented controls, schemas, storage, replay, and validation limits.
- [Development follow-ups](Development_Followups.md): other core/PBp work and boundaries.
- [Compatibility policy](PBp-Compatibility.md): supported persistent/protocol migration paths.
- [Unity self-play configuration guidance](https://github.com/Unity-Technologies/ml-agents/blob/release_22_docs/docs/Training-Configuration-File.md#self-play): opponent diversity/stability and conservative reward design.
