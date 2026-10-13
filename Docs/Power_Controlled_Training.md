# Power-controlled local training

Installed on this Mac on 2026-10-13 for the existing V2 run
`mac-7x7-core3-gentle-20261011`. This does not select an overnight study actor,
change the training recipe, or change macOS charging settings.

The user LaunchAgent `com.blocknations.training-power` runs
`Tools/Training/power_controller.py` at login and every 15 seconds while logged
in. It reads `pmset -g batt`. AC attached counts as plugged in even when macOS
says "not charging", so an 80% charge ceiling works.

- Start/resume the configured run on AC power at **80% or above**.
- Keep running on AC between 76% and 79%.
- Request a graceful save/stop when unplugged or at **75% or below** on AC.
- Wait for successful supervisor shutdown before resuming at 80% on AC.

Detection takes up to approximately 15 seconds while the machine is awake,
followed by the existing supervisor's save/shutdown time. Resume uses the saved
weights and optimizer. The original shared storage/free-disk guards remain in
force. After reboot, login discards stale process identities and resumes the last
checkpoint when the power thresholds allow it. Recorded faults still need review.
This job does not wake a sleeping/closed laptop or run after logout.

The October 14 overnight restart exposed intermittent five-second `pmset`
timeouts even while fresh interactive probes reported AC at 80%. The controller
now retries one failed or malformed probe, with a five-second bound on each
attempt. A valid battery/low-charge reading is acted on immediately. Both failed
attempts still request the existing graceful stop and latch a fault; no cached
AC reading substitutes for unknown power. A transient failure can therefore add
up to five seconds to detection. The original successful save and fault receipts
were preserved before the reviewed restart.

Local configuration and state are outside Git:

```text
~/Library/Application Support/BlockNations/Training/power-controller.json
~/Library/Application Support/BlockNations/Training/power-controller-status.json
~/Library/LaunchAgents/com.blocknations.training-power.plist
```

The configuration pins the exact resume argument array, including the training
virtualenv Python launcher and its expected native process executable (macOS
framework launchers rewrite argv[0]), run id, root,
worker, eight arenas, one Torch thread, continuous duration and 20 GB budget.
Process identity is checked before signalling. Another supervisor is left alone.
Manual stops, unexpected exits, failed saves and unavailable power readings latch
a fault instead of causing repeated restarts. Review the supervisor status and
resolve the cause before removing `power-controller-status.json` to rearm.

To disable automatic management, set `enabled` to `false` in the configuration.
This leaves an already-running learner alone. To unload the login job:

```sh
launchctl bootout "gui/$(id -u)/com.blocknations.training-power"
```

The focused Python tests cover real `pmset` formats, threshold hysteresis,
process identity, reboot recovery, manual-stop/failure protection and duplicate prevention. A real
isolated subprocess test exercises SIGTERM, successful save acknowledgement and
resume with paths containing spaces. Installation acceptance confirmed launchd
registration and adoption of the existing learner on AC at 80%; physical cable
removal and actual battery depletion were not exercised during installation.
