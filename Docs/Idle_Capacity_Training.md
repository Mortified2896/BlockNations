# Training that yields to other work

The Mac's background learner and automatic benchmarks use low-priority, paced
compute while interactive applications, Codex tools and Omnigent jobs retain priority.
This is an operating policy, not a change to the learning recipe or game rules.

## Host policy

The optional local file
`~/Library/Application Support/BlockNations/Training/resource-policy.json` enables
the shared guard. An absent file or `enabled: false` retains the previous behavior.
The installed background-progress settings are:

```json
{
  "owner": "BlockNations.HostResources.v1",
  "enabled": true,
  "mode": "background",
  "resumeCpuPercent": 40,
  "pauseCpuPercent": 65,
  "resumeLoadPerCore": 0.65,
  "pauseLoadPerCore": 0.9,
  "idleSeconds": 120,
  "maxSwapMBps": 1,
  "activeFraction": 0.25,
  "sampleSeconds": 10
}
```

In `background` mode, CPU load, warning-level memory pressure and swap activity
are reported as `resources.warnings`. They do not stop or indefinitely postpone
the paced learner. A fresh pair of host samples permits resume without waiting
for the entire Mac to become idle. Critical memory pressure, thermal pressure
above nominal, unknown readings, power limits and storage/save protections still
block training. Background scheduling and pacing below remain active.

The earlier idle-only installation could starve training for hours on AC when
ordinary host load or warning-level memory pressure never cleared. On October 17
the installed policy was switched to `background` to restore progress under
those conditions. This changes execution scheduling, not weights, optimizer,
opponents or game rules. The main run is continuous and has no completion target;
being connected to power does not define a training finish point.

For strict idle-only behavior, set `mode` to `idle`. Older policy files without
`mode` retain this behavior. Start/resume then requires two minutes of continuously sampled headroom: CPU below
40%, one-minute runnable load below 0.65 per core, normal memory pressure,
less than 1 MB/s of combined swap I/O, and nominal thermal pressure. On this
eight-core Mac the start load threshold is 5.2. CPU usage comes from differences
between Mach host counters, not lifetime process averages. Swap I/O comes from
differences between live `vm_stat` counters; old swap occupancy alone does not
prevent future training.

In `idle` mode, running work yields at 65% CPU, runnable load 0.9 per core (7.2 on this Mac),
memory warning/critical pressure, at least 1 MB/s of swap I/O, or any thermal
pressure above nominal. Unknown readings defer work and restart the healthy
interval. These are pressure signals, not measured CPU temperatures. User
activity that leaves compute available does not by itself prohibit training.

The existing power guard still requires AC and 80% battery to resume, and stops
at 75% or on battery. The controller polls every 15 seconds. The supervisor and
selector sample resources at most every ten seconds while active. A pressure
stop uses the existing graceful checkpoint/optimizer save and verifies successful
shutdown before any restart. Control is cooperative: a slow in-flight simulator
call and the existing save deadline can delay shutdown. No worker is suspended
mid-call, no action is retried, and a failed save still requires review.

## Conservative compute scheduling

Only background compute children lower themselves to Unix nice 19 and passive
disk I/O; their simulator descendants inherit these settings. Control services
and explicit private human games retain their ordinary scheduling. The launchd
jobs remain Standard because Darwin-background throttling previously caused a
bounded simulation worker to time out.

Managed learner and benchmark environments pace complete simulation cycles at
25% active wall time, leaving roughly three idle intervals per work interval.
The measured work interval includes policy inference and PPO work between
simulator requests. Each voluntary sleep is bounded at two seconds so control
signals remain responsive, so a long individual RPC can exceed the nominal
fraction. This is a conservative pacing target, not a hard CPU quota or a
promise of zero effect on other applications. Torch/BLAS compute uses one CPU
thread; arena count, model, rewards and saved optimizer remain the recorded ones.

The normal power controller, training selector, supervisor, automatic benchmark
scheduler and newly launched controlled-study path share the resource policy.
An automatic benchmark follows the same selected mode; a pressure cancellation preserves
partial evidence and keeps that candidate eligible for a later retry. Its wall
time limit scales with the pacing fraction. Historical studies keep their exact
source/worker pins and receipts; do not repoint an old unfinished study to changed
code. Ad hoc executables launched outside these managed paths need their own
resource settings.

## Inspection and changes

`power-controller-status.json`, a run's `supervisor-status.json`,
`training-selector-state.json` and `automatic-evaluation-status.json` include a
`resources` object with the latest counters, CPU/swap rates, healthy interval,
and reason for waiting. Background-mode advisory pressure is listed separately
under `warnings`. A resource pause is distinct from an unexpected exit or
failed save. Changing `resource-policy.json` updates controller decisions on the
next probe; pacing/thread settings are applied when a compute process next starts.

Set `enabled` to `false` to disable this policy. Existing battery, disk, storage,
recipe-review and failed-save protections remain independent. Reversing a running
compute process's lower priority requires a clean saved restart.

## Validation

Focused tests cover background progress through two simulated hours of ordinary
host pressure, retained battery/critical-memory/thermal/unknown-reading stops,
mode changes, sustained idle eligibility, CPU hysteresis, memory/thermal/
swap pressure, stale and unavailable readings, bounded pacing, successful saved
stops before resume, unexpected-exit protection, and deferred/retried benchmarks.
A real macOS child/grandchild check verifies inherited nice 19 and passive I/O.
Simulator communication tests verify pacing happens before sending a request and
does not consume the existing response deadline or retry an unknown action.
