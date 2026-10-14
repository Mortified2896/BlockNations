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
