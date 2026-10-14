# Quiet overnight training and reusable experiments

Prepared on 2026-10-15 after the owner requested continued AI improvement,
overnight learning, keyboard darkness and no game audio. Strength against humans
remains an open goal; a completed training session is not evidence of that goal.

## Quiet execution

Overnight learning and evaluation use the standalone C# worker and one PyTorch
CPU thread. They do not launch Unity, a native game, the optional inspector, or a
human match. The supervisor and study use only `caffeinate -i -s -w`; they do not
assert display/user activity. Keep the Mac plugged in, logged in and ventilated
with its lid open. A sleep assertion does not make closed-lid training reliable.

Development players now mute `AudioListener` before scene startup when invoked
with `--training-viewer true`, `--training-status`, or `--training-silent true`.
This includes human snapshot players. Viewer and snapshot launchers explicitly
request silence and windowed presentation. Silence stays in the player across
audio-device changes. Ordinary product launches retain their audio settings.
No scene, prefab, UI layout or shipped policy needs to change for this safeguard.

A later live check found the Mac's output had become unmuted at 50%, with
AirPods Pro 3 selected, although no game/editor was running. A one-time mute
therefore was not a durable overnight backstop. `overnight_quiet.py` now checks
the current output every 15 seconds through CoreAudio, reapplies mute if needed,
and verifies the current route's mute readback. It retries once if the route
changes during the operation; unavailable or unacknowledged settings produce a
fault receipt rather than a claim of silence. This polling backstop is bounded
by the interval; the headless learner and pre-scene player mute remain the
primary protection against game music.

The same guard reads back zero built-in keyboard brightness/backlight and its
30-second idle timeout. Its owned configuration expires at 09:00 Hong Kong time
on October 15, 2026. After expiry it stops applying settings and does not restore
audible output automatically. The current Mac's private keyboard API was checked
for the required selectors before use; missing APIs fail explicitly. Config and
status are `overnight-quiet.json` and `overnight-quiet-status.json` below the
shared training root, and the login job is `com.blocknations.overnight-quiet`.

For this night the existing native inspector was closed, macOS output was muted,
and the built-in keyboard brightness was set to zero. Its idle timeout was
changed from 300 to 30 seconds, with idle dimming reported as enabled. These were
verified through system readback; the previous keyboard settings are preserved
outside Git. The native System Settings automation was unavailable, so no UI
verification or AirPods reconnection test is claimed. Do not automatically
restore audible output or reopen a player while the owner is asleep.

## Power, heat and workload scheduling

The normal power controller can opt into `thermalGuard: true`. Managed training
starts only at nominal macOS thermal pressure (0), continues through fair
pressure (1), and gracefully saves/stops at serious or critical pressure (2/3).
It waits for nominal pressure before resuming. These are Foundation's pressure
states, not measured component temperatures or a promise that the case is cool.
Unknown readings retain the existing stop/fault behaviour.

Power-controlled information studies use the same fresh `pmset` retry and live
I/O registry fallback as the main controller, plus the thermal guard. They save
and enter `paused_power` when power or thermal conditions require it. The receipt
records the reason; the study controller resumes the same inputs after AC,
80% battery and nominal pressure return. AC/80% start and AC/75% stop thresholds,
the shared 20 GB allowance and final-save/free-disk reserves remain in force.
Checks run between bounded simulator calls; a slow in-flight reference call can
delay the next guard check. Unknown-outcome actions are never retried.

The main controller's optional `deferToStudyController` names an owned study
controller below the shared benchmark root. An enabled, unfinished study gets
priority: the main learner saves, waits, and resumes its saved weights/optimizer
after the study completes or fails. The study's `exclusiveTraining: true` waits
for the main supervisor lock to become inactive before launching. This prevents
the two learners from competing for CPU. Use Standard launchd scheduling for
the owned study so macOS Background throttling does not impede RPC cleanup.

The previous capacity study failed while a worker exceeded its five-second
shutdown deadline. Cleanup now requests close, then terminates, then kills only
its own child if it still cannot exit. A delayed shutdown after complete games
does not become an interrupted-game result. Actual RPC failures still propagate.
Preserve the failed receipt; a changed source/worker starts a new experiment
identity rather than editing immutable historical pins.

## Overnight capacity comparison

The new `capacity-night-20261015` study continues the protected 163,463,523
parent with its optimizer state. It uses the strengthened retained pool and
fixed Hard population from the October 14 investigation:

- Existing 128x2 network versus function-preserving expansion to 256x2.
- Seeds 84, 85 and 86; alternating arm order; one million added steps per arm.
- Eight simulation arenas, one Torch thread, unchanged observations, legal
  actions, fog, rules, rewards and PPO recipe.
- Identical initial actor/critic function, retained Adam counters/moments, and
  zero moments for added parameters; added features must receive gradients.
- Frozen primary and confirmation evaluations, both seats and starting roles.
  Raw match records and immutable input/worker hashes remain inspectable.
- No release promotion by the experiment. Judge gains by paired seed/opponent/
  role results, interruptions and elapsed time; greater capacity costs compute.

The shared root stores the study under `benchmarks/capacity-night-20261015/` and
its launch configuration/status under the sibling controller directory. The
morning review must distinguish completed arms from unfinished work and retain
the strongest previously evaluated compact actor until the new evidence warrants
a separate private-playtest selection.

## Long-term direction

The reusable foundation remains the shared authoritative simulator, explicit
seat ownership, legitimate per-seat observations, versioned model/rules/roster
contracts, protected opponent population and reproducible controlled studies.
New rules and genuinely new abilities require shared rule implementations and
corresponding observable/action capabilities. They do not silently fit an old
checkpoint merely because its tensors can be loaded.

The current feed-forward policy still has a fixed canvas, recruit capacity and
action representation. A larger MLP can test a capacity bottleneck, but cannot
solve missing information or guarantee generalization to new units. The next
representation step in [the training direction](AI_Training_Direction.md) is a
shared spatial/capability encoder with legal-action candidate scoring, tested
on held-out rosters and rule profiles. Fair history or recurrence needs a matched
study and a compatible local inference adapter. The earlier slot-permutation
trial lost strength and must not be treated as a successful generalization result.

Keep these changes isolated and measured. Promotion requires replicated fixed
population gains and retained strong-opponent performance under the actual Hard
sampler, followed by human counterplay in both starting roles. Training Elo,
purchase variety, higher step counts and throughput do not substitute for that.

## Installation and validation receipt

The current-source self-contained C# worker built successfully. The focused
Python run passed 100 tests, including actual V2/V3/V4 worker observations/legal
actions/diagnostics, network/critic/optimizer expansion parity, real SDK resume,
power/thermal guards, exclusive scheduling, cleanup escalation and promotion
regressions. The first attempt used an older installed worker whose diagnostics
lacked root-selection counters; the source-built worker passed that contract.

Unity's nine new audio checks passed in an isolated EditMode run. The wider run
passed 47 of 48 existing/new checks; its remaining human-menu scene check and an
isolated retry were interrupted by Unity Assistant's unrelated Relay WebSocket
connection error. No gameplay assertion failed, but that wider suite is not
reported as passed. The native player build exited successfully, and an actual
windowed viewer emitted the pre-scene mute marker before it was closed. The
code-only builder did not rewrite a scene; incidental importer/package/setting
normalization was restored. Private receipts are under
`Logs/Validation/QuietOvernight-20261015/`.

The main learner acknowledged a successful save/stop (`exitCode: 0`) before the
exclusive study started. Its resume command now uses the current-source worker
and verified silent player; learner weights, optimizer and recipe were preserved.
The independent selector's player configuration and Desktop inspector launcher
also point to the silent build. Local setup/configuration backups are preserved
under `benchmarks/quiet-night-20261015-setup/` in the shared training root.

Both frozen parent packs completed with zero interruptions: primary 438/512,
confirmation 428/512. Against the protected 162,299,959 actor the parent won only
1/64 in each pack. These are pre-training baselines, not overnight gains or human
win rates. The first control arm then advanced with zero rejected actions. The
morning review is scheduled in the current chat for 09:00 Hong Kong time on
October 15; routine hourly checks stay quiet and the heartbeat pauses after that
report. Remaining arms and their results must be read from the live receipts.

## First completed control and counterplay check

Seed 84's 128x2 control completed its million-step continuation at step
164,463,559. The read-only reporter verified its exact frozen actor, all raw
games and both balanced evaluation packs. Its actor, critic and optimizer
contained 55 finite checkpoint tensors, all twelve retained optimizer counters
advanced by 23,136, and the training receipt recorded zero rejected actions.

| Fixed Hard pack | Frozen parent | Seed 84 control | Change |
| --- | --- | --- | --- |
| Primary | 438/512 | 491/512 | +10.35 percentage points |
| Confirmation | 428/512 | 479/512 | +9.96 percentage points |

The control won 51/64 primary and 39/64 confirmation matches against the
protected 162,299,959 archive. Its first/second-role results were 23/32 and
28/32 on primary, and 19/32 and 20/32 on confirmation. The parent won only 1/64
against that actor on either pack. The Warrior-only reference still exposed a
first-role weakness: 24/32 wins on both packs, compared with 32/32 when second.
These are completed results for one continuation, not a capacity-treatment
result or proof that the improvement repeats across training seeds. Repeated
deterministic openings are not independent maps.

A separate evaluation-only replay checked the previously recorded city-defence
failure with the parent, protected archive and completed control. All 129
recorded actions remained legal, normal-fog vectors exactly reproduced the
original journal, and the diagnostic scored 22 final-round positions across
normal fog and the full-visibility counterfactual.

| Actor | Root End Turn probability under fog, raw Policy | Root End Turn probability with full visibility, raw Policy |
| --- | --- | --- |
| Parent 163,463,523 | 16.18% | 99.94% |
| Protected archive 162,299,959 | 7.26% | 99.99% |
| Seed 84 control 164,463,559 | 18.65% | 99.85% |

At the two relevant fog decisions, all three Hard actors still selected the
city defender and moved it from (3,3) to (3,4), reproducing the exposed-city
mistake in that reachable prefix. With full visibility they instead ended the
turn at the root. This is a matched decision diagnostic, not a new full-game
win-rate test. Stronger aggregate benchmark results have not resolved this
specific counterplay weakness.

Private diagnostic receipts are in
`benchmarks/capacity-night-evidence-20261015/city-defense-control84/` below the
shared training root. Keep that journal and its positions outside training.
Finish the capacity pairs before changing their conditions; a subsequent
information/uncertainty experiment should use separate training situations and
the strengthened retained pool, with fair observable history and its own
replicated controls. Do not infer that a larger network or last-seen history
alone solves threats from genuinely unobserved units. No champion or public
release was changed by these checks.

The follow-up [recorded decision diagnostic](Training_Replay_Diagnostics.md)
now supports V2/V3/V4 actors and read-only full-view projections on the exact
normal-fog state. It revalidated the same city case with the previous paired
V3/V4 pilot actors as well. The current fair-history snapshot contained only
two visible enemy records, with no stale sighting for the additional supporting
ranged threat. Both pilot actors still made the Hard defence error. That is a
representation/decision diagnostic, not a full-game or human-strength gain.
The retained runtime, source hashes and five-model receipt remain in
`benchmarks/capacity-night-evidence-20261015/` for the morning review; the
capacity study's original source and worker pins are unchanged.

## First completed capacity pair

The first seed-84 pair has now completed and its raw-match report verifies both
actors on both packs with zero interruptions and rejected training actions.

| Seed 84 arm | Primary | Confirmation | Recorded elapsed time |
| --- | --- | --- | --- |
| Compact 128x2 control | 491/512 | 479/512 | 45.3 minutes |
| Expanded 256x2 treatment | 484/512 | 482/512 | 56.7 minutes |

The expanded actor lost 1.37 percentage points on primary and gained 0.59 on
confirmation relative to control, taking about 25% longer. Against the protected
archive its first-role wins fell from 23/32 to 12/32 on primary and from 19/32
to 11/32 on confirmation; its second-role results were 25/32 and 26/32. Thus a
small confirmation mean gain conceals a weaker archive matchup when first.
One pair does not establish a capacity benefit or justify replacing the compact
opponent. Seeds 85/86 continue with their original alternating arm order.

A separate read-only replay of the two completed actors also preserved all 129
original legal choices and the outcome. Both still selected and moved the city
defender under Hard at decisions 118/119. At the root, expanded raw End Turn
probability was 6.21%, versus control's 18.65%; this is a decision preference,
not a full-game win rate. Its receipt is
`benchmarks/capacity-night-evidence-20261015/city-defense-capacity84/`.
The study, protected champion and public release retain their original roles.

## Fresh fair console counterplay

The completed compact seed-84 actor also played two fresh normal-fog console
diagnostics on seeds 150501/150502. The agent-operated opponent received only
its legitimate observation, public rules and legal-action list; no full-view
projection or opponent wallet was consulted during either match.

| Console opponent's role | Outcome against compact control | Rounds | Total recorded choices |
| --- | --- | --- | --- |
| First | Console opponent won | 6 | 58 |
| Second | Console opponent won | 7 | 83 |

Both games used two Archers and a Rider, coordinating ranged damage, movement
after shooting and melee cleanup. The second game also faced a learned Archer
and a final Warrior defender. Both completed with zero rejections and zero
interruptions. Separate simulator replays reproduced every recorded vector,
legal-action list, choice and final outcome. These are two agent-operated
counterexamples, not physical human tests or a calibrated human win rate.
They show that the compact actor's high fixed-panel score has not resolved
ordinary coordinated counterplay in either starting role.

The sources and verified replays are under
`benchmarks/capacity-night-evidence-20261015/fair-control84-{first,second}/`
and the corresponding `-verified/` folders. Keep them evaluation-only. The
next learning change needs stronger experience with varied, coordinated
opponents and uncertainty; adopting the larger network or adding last-seen
features alone is not supported by these observations. Finish the recorded
capacity pairs before changing their conditions.

## Independent direct comparison for private review

The compact seed-84 candidate separately faced the preserved 162,299,959
champion on evaluation seeds 704501 and 1704504, using 128 games per mode/pack
with balanced colours and starting roles. The raw-match validator rechecked
all 512 outcomes; none were interrupted.

| Sampling | Primary candidate wins | Confirmation candidate wins |
| --- | --- | --- |
| Hard versus Hard | 96/128 | 84/128 |
| Raw Policy versus Raw Policy | 68/128 | 73/128 |

Hard first/second-role results were 49/64 and 47/64 on primary, then 38/64 and
46/64 on confirmation. This passed the existing Hard direct-incumbent criterion.
Raw Policy first-role results were below half on both packs, so retain the
mode distinction. These empirical gates do not create independent maps or
establish human strength. The direct review did not perform the separate
population-selection step or change a champion/release. The frozen candidate
is already discoverable in the private model selector.

Evidence is under
`benchmarks/capacity-night-evidence-20261015/control84-direct-archive/`.
The two completed capacity pairs also disagree about the expansion: seed 84
lost primary mean strength while seed 85 gained 13/512 on both packs relative
to its own control. Finish seed 86 and inspect role retention before adopting
an architectural or recipe change.
