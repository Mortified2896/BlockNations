# Experimental Hard Tactician

Implemented 2026-10-07 for development review. Normal/Default and Rider Focus retain their existing behavior. Hard is available in development AI-vs-AI selection and the tournament participant pool. Its human-play selector remains hidden until the owner watches and reviews the comparison tournament (`HardAIRuntime.PlayerPlaytestApproved`). No tournament is authorized to start automatically.

## Decision architecture

`Assets/Scripts/AI/Policy/` is an engine-independent assembly. `AIObservation` contains ordinary data, an explicit acting seat, explicit hostile seats, and a root legal-action mask. `IAIActionPolicy` produces an incremental `IAIDecision`. The Unity adapter in `HardAIRuntime` captures observations, schedules work, and revalidates actions through `LegalActionService` immediately before execution.

Movement range/commitment, attack availability, damage, and distance primitives are shared with the existing runtime through `AIActionRules`. Runtime turn lifecycle, authoritative action effects, supported save fields, and PBp protocol formats remain owned by their existing systems. The legacy `AIRecruitVariant` enum is extended without changing existing numeric values; its name remains a compatibility container, not the policy architecture.

`currentTurnSeatIndex` is the runtime turn authority in both VsAI and PBp. VsAI handoffs and simulation use the same seat progression and seat-based turn initialization; `isPlayerTurn` is updated as a compatibility bridge. Invalid queried seats are rejected rather than normalized into another participant. Existing VsAI saves still import their legacy turn boolean at the load boundary; PBp keeps its supported protocol migration paths. These changes do not enable extra VsAI seats or teams.

Hard uses selective beam search over coordinated move/attack/recruit sequences, scored for captures, material, visible next-turn threats, positioning, exploration, and economy. It reassesses after every executed action. This version predicts threats from observed enemies; it does not yet search a complete opposing turn tree. Capturing a hostile city ends current VsAI immediately, so an observed winning capture terminates search early.

## Information rules

- Own units/cities/resources are available. Enemy units enter the observation only when currently visible to the acting seat.
- Undiscovered cities never enter the observation. Discovered cities can remain as last-observed objectives, with a current/stale flag. Hidden ownership changes do not update that memory.
- Exploration is recorded per seat without changing the human viewer's fog. Viewer UI flags and legacy ownership booleans are not policy inputs.
- Hostility is a separate observation boundary, populated with the current free-for-all relationships. Future team rules must change that boundary and authoritative legality together.
- Tactical simulation can predict newly explored coverage, but cannot discover unknown actual units/cities during search. Every execution is followed by a fresh observation.
- City memory is currently session-local and resets on loading a save. Existing per-seat explored tiles remain preserved. Persisting richer historical observations requires a separate versioned design.

The existing opponents still read enemy city positions outside their visibility. The owner explicitly chose to preserve them as comparison controls and audit their fairness separately.

## Workload and responsiveness

Version `hard-tactician-v1` uses a beam width of 24, depth limit of 8 actions, up to 2,048 candidate transitions per decision, and a shared turn search allocation of 8,192 transitions. Once the allocation is exhausted, remaining actions use a deterministic one-ply pass over the current legal mask. Terminal captures and exhausted frontiers can finish earlier.

The scheduler yields after approximately 4 ms of work, checked between candidate evaluations. This is a scheduling target, not a guaranteed frame-time ceiling: observation capture and an individual evaluation are still atomic. There is no wall-clock search cutoff. A slower device performs the same analysis over more frames. Deterministic candidate ordering, integer evaluation, and tie-breaking make frame chunking independent of the chosen result.

The phone target is approximately three seconds for a representative hard turn on an older mainstream iPhone/Safari and a midrange Android/Chrome. These budgets are provisional: neither physical-phone timing nor a Unity Web build is verified. Do not claim that Editor timings establish browser/device performance. Benchmark before choosing final budgets, using the same policy version, position, and work settings on both devices.

Loading another game invalidates incremental work before it can execute or advance the replacement game's turn. Development pause applies during search and before execution.

## Inspection and validation

Open **Window → Block Nations → Hard AI Inspector** in Unity. It shows the top eight retained first-action candidates, their best found continuations, score components, search work, elapsed time, completed depth, and stop reason. Candidates are provisional during search. Scores are heuristic values, not win probabilities. Only eight candidates are retained; this is not a record of every visited state.

Enable **Pause before executing**, then use **Execute one action** to inspect decisions. Selecting a candidate draws numbered move/attack lines in the Scene view. This first inspector is an Editor tool; a development-build/browser overlay is a follow-up. Closing the inspector releases its pause.

Run **Tools → Block Nations → Validate Hard AI** for EditMode or PlayMode regression checks in the open Editor. These commands never run tournaments. Results are written to ignored `Logs/Validation/HardAI/`. Coverage includes both acting seats, coordinated rider captures, archer/rider action semantics, root-mask enforcement, frame-chunk determinism, hidden-information isolation, stale city memory, hidden path blockers, cancellation of an obsolete paused decision, and canonical seat progression independent of a stale legacy boolean.

Initial Editor validation on 2026-10-07 passed 24 EditMode and 12 PlayMode cases. The latter includes supported protocol 4/5 turn-owner conversion. The tournament menu was inspected and captured in a wide laptop Game window: portrait phone styling is excluded for landscape sizes, the three selected opponents are listed directly, and the full picker remains available. These checks do not establish playing strength or physical-device performance.

## Owner-watched comparison gate

Ask the owner before starting. In MainMenu Play Mode, **Tools → Block Nations → Prepare Hard AI Tournament Review** stages these settings without saving preferences or starting a match, then maximizes the Game view for laptop inspection. The selected lineup appears above a collapsible picker for the full 17-variant pool. Proposed first review:

1. Small board: **11×11**.
2. Round-robin with three participants: Baseline with Offense/Exchange/Defense, Rider Focus with Offense, and Hard Tactician. These match the existing normal VsAI presets.
3. One game per pairing, seat swapping enabled: **six matches** total.
4. Normal speed, continuous looping disabled, inspector available. Inspect settings before confirming start.

Existing pool indices 0–15 retain their meaning. Hard is appended at index 16. The three-participant mask is `66176` (indices 7, 9, 16). Do not automatically replace an existing saved participant selection.

Record outcomes by seat, decision timing, avoidable tactical losses, and stalls. Six matches are an inspection/smoke comparison, not a statistical proof of general strength. Repeating identical deterministic starts is not independent evidence. Broader acceptance needs varied tactical scenarios and the owner's human playtest, after the watched review.

## Future learning and hosting

The observation/action/policy interfaces are the integration seam for learned evaluators, imitation policies, or optional LLM guidance. Models must choose from the root action mask; the runtime remains authoritative. A remote policy must also handle stale state, errors, latency, and local fallback. Secrets must stay on a backend, never in a Web build.

This is not yet a complete reinforcement-learning environment. Remaining work includes a shared complete match simulator (turns, income, endgame, observations), reset/step APIs, richer information memory, versioned trajectories/rewards, and transition-parity tests. Train/evaluate offline; deploy a small local policy only after measuring size and inference cost on target browsers.

Cloudflare should serve the eventual Unity Web assets while this AI runs locally. Building, checking asset sizes/headers, testing Safari/Chrome, and publishing that build are separate release work after AI review. The existing Node PBp relay has not been migrated to Workers by this change.
