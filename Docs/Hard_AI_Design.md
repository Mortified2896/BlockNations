# Experimental Hard Tactician

Implemented 2026-10-07 for development playtesting. Normal/Default and Rider Focus retain their existing behavior. Hard is available in the regular VsAI setup, development AI-vs-AI selection, and the tournament participant pool. The owner watched the version-2 comparison and subsequently requested direct playtesting and more generic improvements. Version 3 is experimental; it has not had a watched tournament. Ask the owner before starting any further tournament. The separate [model opponent playtest](Model_AI_Playtest.md) uses the same fair observation and execution adapter.

## Decision architecture

`Assets/Scripts/AI/Policy/` is an engine-independent assembly. `AIObservation` contains ordinary data, an explicit acting seat, explicit hostile seats, and a root legal-action mask. `IAIActionPolicy` produces an incremental `IAIDecision`. The Unity adapter in `HardAIRuntime` captures observations, schedules work, and revalidates actions through `LegalActionService` immediately before execution.

Movement range/commitment, attack availability, damage, and distance primitives are shared with the existing runtime through `AIActionRules`. Runtime turn lifecycle, authoritative action effects, supported save fields, and PBp protocol formats remain owned by their existing systems. The legacy `AIRecruitVariant` enum is extended without changing existing numeric values; its name remains a compatibility container, not the policy architecture.

`currentTurnSeatIndex` is the runtime turn authority in both VsAI and PBp. VsAI handoffs and simulation use the same seat progression and seat-based turn initialization; `isPlayerTurn` is updated as a compatibility bridge. Invalid queried seats are rejected rather than normalized into another participant. Existing VsAI saves still import their legacy turn boolean at the load boundary; PBp keeps its supported protocol migration paths. These changes do not enable extra VsAI seats or teams.

Hard uses selective beam search over coordinated move/attack/recruit sequences. Version 3 separates generic position features from the weighted evaluator through `IAIPositionEvaluator`; the default linear weights are hand-authored, not learned. Features use unit statistics and action capabilities, rather than opponent/unit-name matchup rules. Terrain-distance fields guide hostile-city progress, contact, and exploration; bounded support avoids the earlier army-camping incentive. After generating own continuations, a small reply search tests observed hostile units with reset turn resources, including ranged attacks followed by movement and coordinated attacks on guarded cities. It does not invent hidden units, enemy gold, or enemy recruitment. Reply work is charged to the same fixed work allocation. These are selective observed replies, not a complete opposing turn tree. The adapter reassesses after every executed action. Capturing a hostile city ends current VsAI immediately, so an observed winning capture terminates own search early.

## Information rules

- Own units/cities/resources are available. Enemy units enter the observation only when currently visible to the acting seat.
- Undiscovered cities never enter the observation. Discovered cities can remain as last-observed objectives, with a current/stale flag. Hidden ownership changes do not update that memory.
- Exploration is recorded per seat without changing the human viewer's fog. Viewer UI flags and legacy ownership booleans are not policy inputs.
- Hostility is a separate observation boundary, populated with the current free-for-all relationships. Future team rules must change that boundary and authoritative legality together.
- Tactical simulation can predict newly explored coverage, but cannot discover unknown actual units/cities during search. Every execution is followed by a fresh observation.
- City memory is currently session-local and resets on loading a save. Existing per-seat explored tiles remain preserved. Persisting richer historical observations requires a separate versioned design.

The existing opponents still read enemy city positions outside their visibility. The owner explicitly chose to preserve them as comparison controls and audit their fairness separately.

## Workload and responsiveness

Version `hard-tactician-v3` uses a beam width of 24, depth limit of 8 actions, up to 2,048 candidate transitions per decision, and a shared turn search allocation of 8,192 transitions. Every legal root action is considered unless a winning capture terminates the search first or the work budget is exhausted. Later nodes keep at most 12 ordered successors, with move alternatives bounded per actor to leave room for coordinated actions. Ordering uses cheap observed features rather than evaluating additional unmetered child states. Once the allocation is exhausted, remaining actions use a deterministic one-ply pass over the current legal mask. Terminal captures and exhausted frontiers can finish earlier. Up to eight retained own candidates receive at most 32 observed-reply transitions each (reply beam width 2, depth 4, at most 4 ordered successors). The reserved reply allocation is capped at 256 transitions and one quarter of the available decision budget; it is included in `WorkCompleted`. Search scores use the worst found observed reply instead of adding the earlier rough threat estimate twice.

The scheduler yields after approximately 4 ms of work, checked between candidate evaluations. This is a scheduling target, not a guaranteed frame-time ceiling: observation capture and an individual evaluation are still atomic. There is no wall-clock search cutoff. A slower device performs the same analysis over more frames. Deterministic candidate ordering, integer evaluation, and tie-breaking make frame chunking independent of the chosen result.

The phone target is approximately three seconds for a representative hard turn on an older mainstream iPhone/Safari and a midrange Android/Chrome. These budgets are provisional: neither physical-phone timing nor a Unity Web build is verified. Do not claim that Editor timings establish browser/device performance. Benchmark before choosing final budgets, using the same policy version, position, and work settings on both devices.

Loading another game invalidates incremental work before it can execute or advance the replacement game's turn. Development pause applies during search and before execution.

## Inspection and validation

Open **Window → Block Nations → Hard AI Inspector** in Unity. It shows the top eight retained first-action candidates, their best found continuations, feature/evaluation components, worst found observed reply, search and reply work, elapsed time, completed depth, and stop reason. Candidates are provisional during search. Scores are heuristic values, not win probabilities. Only eight candidates are retained; this is not a record of every visited state.

Enable **Pause before executing**, then use **Execute one action** to inspect decisions. Selecting a candidate draws numbered move/attack lines in the Scene view. This first inspector is an Editor tool; a development-build/browser overlay is a follow-up. Closing the inspector releases its pause.

The review helper enables background execution for its current Play Mode session and restores the previous value on leaving Play Mode. This avoids silently freezing the simulation when Unity loses focus; it does not edit project settings. **Tools → Block Nations → AI Review Speed** changes a running review between Normal, Fast, Very Fast, and Ultra Fast. These controls change turn/restart pauses and development audio/snapshot behavior, not Hard's fixed search allocation. A turn pause already in progress finishes before the new speed applies.

Run **Tools → Block Nations → Validate Hard AI** for EditMode or PlayMode regression checks in the open Editor. These commands never run tournaments. Results are written to ignored `Logs/Validation/HardAI/`. Coverage includes both acting seats, coordinated rider captures, archer/rider action semantics, root-mask enforcement, frame-chunk determinism, hidden-information isolation, stale city memory, hidden path blockers, cancellation of an obsolete paused decision, and canonical seat progression independent of a stale legacy boolean.

Initial Editor validation on 2026-10-07 passed 24 EditMode and 12 PlayMode cases. The latter includes supported protocol 4/5 turn-owner conversion. The tournament menu was inspected and captured in a wide laptop Game window: portrait phone styling is excluded for landscape sizes, the three selected opponents are listed directly, and the full picker remains available. These checks do not establish playing strength or physical-device performance.

## Owner-watched comparisons

Ask the owner before starting. In MainMenu Play Mode, **Tools → Block Nations → Prepare Hard AI Tournament Review** stages these settings without saving preferences or starting a match, then maximizes the Game view for laptop inspection. The selected lineup appears above a collapsible picker for the full 17-variant pool. Proposed first review:

1. Small board: **11×11**.
2. Round-robin with three participants: Baseline with Offense/Exchange/Defense, Rider Focus with Offense, and Hard Tactician. These match the existing normal VsAI presets.
3. One game per pairing, seat swapping enabled: **six matches** total.
4. Ultra Fast, continuous looping disabled, inspector available. The match fuse is **100 complete rounds**: both seats get their round-100 turn, and a game still running afterward is recorded as an abort at round 100. Aborts count as attempts but contribute no score and no draw; rankings and score percentages use completed results only. Inspect settings before confirming start. Switch to a slower live review speed or pause for inspection when needed.

Existing pool indices 0–15 retain their meaning. Hard is appended at index 16. The three-participant mask is `66176` (indices 7, 9, 16). Do not automatically replace an existing saved participant selection.

Record outcomes by seat, decision timing, avoidable tactical losses, and stalls. Six matches are an inspection/smoke comparison, not a statistical proof of general strength. Repeating identical deterministic starts is not independent evidence. Broader acceptance needs varied tactical scenarios and the owner's human playtest, after the watched review.

### First watched review: invalid configuration

The owner-authorized six-game review on 2026-10-07 completed at Normal speed but did not test Hard. The scene-restart helper selected the initial Normal-versus-Rider pairing on every restart, while the scheduler advanced the displayed labels. Each raw match row's actual configuration remained `Default` with all local features versus `RiderFocus` with offense, and no Hard search was recorded. The displayed 2–2 record for every participant is invalid as evidence about policy strength. Preserve these raw development logs as diagnostic evidence; do not use this review's standings for acceptance.

`AIVsAIMatchHandoff` now separates a new run's initial pairing from an active run's upcoming pairing. It carries models, feature flags, profiles, and seat swaps through the scene transition. Before starting an AI batch match, the runtime checks these typed settings against the schedule; a mismatch becomes an aborted match rather than a mislabeled win/loss. Head-to-head profiles follow their swapped policies too. Ranked HUD previews omit opponents that have not played. Regression checks exercise all six scheduled handoffs without playing tournament games and verify that TurnManager actually dispatches Hard search for either seat. The corrected review was then explicitly authorized and started; its failed result is recorded below.

Validation after the handoff correction passed 28 EditMode and 14 PlayMode cases on 2026-10-07. These are regression checks, not tournament results or playing-strength evidence.

### Corrected watched review: version 1 failed

The second owner-authorized run used the actual scheduled policies. Normal won both games against Rider Focus (rounds 21 and 26) and both games against Hard version 1 (rounds 36 and 16). The first Rider-versus-Hard game aborted at the old 200-round fuse; the old code advanced the counter before stopping, so its raw row says 201. The final Hard-versus-Rider game was stopped during round 166 after the owner reported stalled armies. This is a partial five-result review, not a completed six-game result. Hard version 1 failed acceptance; do not enable its human selector based on this review.

The fair observation from the stalled game is preserved as `Assets/Editor/Tests/Fixtures/HardAI_CrowdedHome.json`. It contains 28 own units, 80 legal moves, no currently observed enemies, and 12 unexplored tiles. Version 1 preferred ending the turn because pairwise army cohesion outweighed exploration; a regression test reproduced that exact failure before the fix. Its broad successor enumeration also used the 2,048-transition decision allocation at only two completed depths. Version 2 chooses an advancing legal move and reaches seven completed depths with the same allocation on this observation. These are position-specific checks, not evidence that version 2 beats the controls.

The old tournament calculation also awarded a half-point for an abort, yielding the misleading 17% score with no wins/draws in the screenshot. Tournament standings, seat rates, pairing summaries, and ranked previews now exclude aborts from their score denominators while retaining the abort counts. True draws still score half a point. Raw historical CSV rows are preserved.

The revised watched tournament was explicitly authorized and completed; its result is recorded below. At that review the human-play selector was still gated. It was subsequently exposed for the owner's requested playtests. Physical-phone/browser timing and broader playing strength remain unverified.

Validation after the version-2 correction passed 41 EditMode and 15 PlayMode cases on 2026-10-07. New checks cover the captured crowded position, terrain-connected exploration, an explored map without city memory, deeper coordinated captures in a larger army, bounded enemy attack accounting, combined rider threats to a guarded capital, round-100 seat fairness, and abort-versus-draw scoring. The isolated PlayMode crowded-position check executes one own turn through the actual adapter and verifies that several units move and new tiles are explored. No opposing policy or tournament runs in these checks.


### Version-2 watched review: three wins, one unresolved game

The owner explicitly approved the revised six-match tournament on 2026-10-07. It ran in the fullscreen laptop Game view at Ultra Fast and completed in 59.73 seconds of Editor runtime. All six raw rows have an 11×11 board; all four Hard appearances identify `hard-tactician-v2` with a turn search allocation of 8,192. No additional tournament was started.

| Runtime seat A | Runtime seat B | Outcome | Round |
| --- | --- | --- | --- |
| Normal (all local features) | Rider Focus (offense) | Normal wins | 66 |
| Rider Focus (offense) | Normal (all local features) | Normal wins | 26 |
| Normal (all local features) | Hard v2 | Hard wins | 22 |
| Hard v2 | Normal (all local features) | Hard wins | 27 |
| Rider Focus (offense) | Hard v2 | Aborted at the round cap | 100 |
| Hard v2 | Rider Focus (offense) | Hard wins | 19 |

Hard finished with **3 wins, 0 losses, 0 draws, and 1 abort**. Normal finished 2–2, and Rider Focus had 0 wins, 3 losses, and 1 abort. The displayed Hard score is 100% over its three scored games; it is not four wins and does not establish an overall win probability. The aborted Rider/Hard game had 3 Rider units and 4 Hard units remaining; the cap stopped it exactly at round 100. Its cause needs position-level investigation rather than being counted as a draw or a success.

This is encouraging evidence compared with version 1, which lost both Normal pairings, but one tournament does not establish broad strength or smart play against humans. The owner subsequently requested direct playtesting and generic improvements. The human selector is now available as an experimental option. Further tournaments require explicit authorization. Phone/browser timing is still unmeasured.

## Version-3 generic evaluation and decision records

The generic reply scenarios first reproduced three failures under version 2. The current Editor regression run passes all 60 EditMode cases, including existing handoff/UI checks, external-action transport behavior, model setup separation, generic ranged/custom-stat replies, injected evaluators, deterministic chunking, and decision-record replay. This establishes the tested invariants, not a win rate or human strength. All 15 ordinary PlayMode cases also pass; the opt-in live model check is skipped by the regular regression command and passed separately. No version-3 tournament has run.

The Inspector can explicitly enable bounded local decision recording: at most 512 samples or 16 MiB per recording session under `Application.persistentDataPath/DevMatchResults/AIExperience/`. It defaults off. Each sample records versioned fair observations, the legal mask, selected action, candidate features, evaluator/policy versions, execution result, and the next observation when applicable. Deterministic Hard decisions can be replayed with matching policy/evaluator/schema versions. Model responses are recorded as external choices and cannot claim deterministic reinference. These samples are neither gameplay saves nor complete reinforcement-learning trajectories/rewards.

## Future learning and hosting

The observation/action/policy/evaluator interfaces and optional versioned decision samples are the integration seam for learned evaluators, imitation policies, or optional LLM opponents. Models must choose from the root action mask; the runtime remains authoritative. A remote policy must also handle stale state, errors, and latency. The current local model playtest cancels obsolete requests and ends the AI turn on an error; it does not silently switch to Hard. A public release needs an explicit retry/fallback policy. Secrets must stay on a backend, never in a Web build.

This is not yet a complete reinforcement-learning environment. Remaining work includes a shared complete match simulator (turns, income, endgame, observations), reset/step APIs, richer information memory, versioned trajectories/rewards, and transition-parity tests. Train/evaluate offline; deploy a small local policy only after measuring size and inference cost on target browsers.

Cloudflare should serve the eventual Unity Web assets while this AI runs locally. Building, checking asset sizes/headers, testing Safari/Chrome, and publishing that build are separate release work after AI review. The existing Node PBp relay has not been migrated to Workers by this change.
