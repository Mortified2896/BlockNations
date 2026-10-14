import json
from pathlib import Path
import tempfile
import unittest
import subprocess
import time
import os
import plistlib
from unittest.mock import patch

from power_controller import action, identity, parse_power, read_power, tick, command_for, reconcile_boot, parse_battery_registry


class PowerControllerTests(unittest.TestCase):
    def test_thermal_save_wait_and_nominal_resume(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); run = root/'runs/example'; run.mkdir(parents=True)
            config = dict(root=folder, runId='example', enabled=True, thermalGuard=True,
                          command=['python', '/supervisor.py', '--root', folder, '--run-id', 'example', '--resume'])
            (root/'supervisor.lock').write_text('12')
            processes = {12: config['command']}; signals = []; launches = []
            state = tick(config, {}, True, 80, 100, processes.get, signals.append, launches.append, thermal=1)
            self.assertEqual(state['state'], 'running')
            state = tick(config, state, True, 80, 101, processes.get, signals.append, launches.append, thermal=2)
            self.assertEqual(signals, [12]); self.assertEqual(state['stopReason'], 'thermal_pressure')
            processes.clear(); (root/'supervisor.lock').unlink()
            (run/'supervisor-status.json').write_text(json.dumps(dict(state='stopped', exitCode=0)))
            state = tick(config, state, True, 80, 102, processes.get, signals.append, launches.append, thermal=1)
            self.assertEqual(state['state'], 'waiting_for_cooling'); self.assertFalse(launches)
            state = tick(config, state, True, 80, 103, processes.get, signals.append, lambda c: launches.append(c) or 13, thermal=0)
            self.assertEqual(state['state'], 'starting'); self.assertEqual(len(launches), 1)

    def test_controlled_study_gets_exclusive_cpu_then_main_resumes(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = str(Path(folder).resolve())
            root = Path(folder); run = root/'runs/example'; run.mkdir(parents=True)
            destination = root/'benchmarks/example'; destination.mkdir(parents=True)
            controller = destination/'controller.json'
            controller.write_text(json.dumps(dict(owner='BlockNations.PowerStudy.v1', enabled=True, destination=str(destination))))
            config = dict(root=folder, runId='example', enabled=True, deferToStudyController=str(controller),
                          command=['python', '/supervisor.py', '--root', folder, '--run-id', 'example', '--resume'])
            (root/'supervisor.lock').write_text('12')
            processes = {12: config['command']}; signals = []; launches = []
            state = tick(config, {}, True, 80, 100, processes.get, signals.append, launches.append)
            self.assertEqual(signals, [12]); self.assertEqual(state['stopReason'], 'study_priority')
            processes.clear(); (root/'supervisor.lock').unlink()
            (run/'supervisor-status.json').write_text(json.dumps(dict(state='stopped', exitCode=0)))
            for study_state in ('running', 'paused_power'):
                (destination/'study.json').write_text(json.dumps(dict(state=study_state)))
                state = tick(config, state, True, 80, 101, processes.get, signals.append, launches.append)
                self.assertEqual(state['state'], 'waiting_for_study'); self.assertFalse(launches)
            (destination/'study.json').write_text(json.dumps(dict(state='completed')))
            state = tick(config, state, True, 80, 102, processes.get, signals.append, lambda c: launches.append(c) or 13)
            self.assertEqual(state['state'], 'starting'); self.assertEqual(len(launches), 1)

    def test_transient_timeout_uses_fresh_retry_instead_of_stopping(self):
        timeout = subprocess.TimeoutExpired(['/usr/bin/pmset', '-g', 'batt'], 5)
        ac = "Now drawing from 'AC Power'\n -InternalBattery-0 80%; AC attached"
        with patch('power_controller.subprocess.check_output', side_effect=[timeout, ac]) as probe:
            self.assertEqual(read_power(), (True, 80))
        self.assertEqual(probe.call_count, 2)
        self.assertTrue(all(call.kwargs['timeout'] == 5 for call in probe.call_args_list))

    def test_fresh_battery_read_is_not_retried_or_replaced_with_ac(self):
        battery = "Now drawing from 'Battery Power'\n -InternalBattery-0 80%; discharging"
        with patch('power_controller.subprocess.check_output', return_value=battery) as probe:
            ac, percent = read_power()
        self.assertEqual(action(True, ac, percent), 'stop')
        self.assertEqual(probe.call_count, 1)

    def test_failed_retry_preserves_unknown_power_failure(self):
        timeout = subprocess.TimeoutExpired(['/usr/bin/pmset', '-g', 'batt'], 5)
        with patch('power_controller.subprocess.check_output', side_effect=[timeout, timeout, timeout]) as probe:
            with self.assertRaises(subprocess.TimeoutExpired):
                read_power()
        self.assertEqual(probe.call_count, 3)

    def test_malformed_retry_does_not_assume_plugged_in(self):
        with patch('power_controller.subprocess.check_output', side_effect=['battery unavailable', 'battery unavailable', b'invalid plist']) as probe:
            with self.assertRaises(ValueError):
                read_power()
        self.assertEqual(probe.call_count, 3)

    def test_live_registry_fallback_preserves_ac_battery_and_low_charge(self):
        timeout = subprocess.TimeoutExpired('pmset', 5)
        for connected, percent in ((True, 80), (False, 80), (True, 75)):
            output = plistlib.dumps([dict(ExternalConnected=connected, CurrentCapacity=percent, MaxCapacity=100)])
            with patch('power_controller.subprocess.check_output', side_effect=[timeout, timeout, output]) as probe:
                self.assertEqual(read_power(), (connected, percent))
            self.assertEqual(probe.call_args.args[0][0], '/usr/sbin/ioreg')
            self.assertEqual(action(True, connected, percent), 'keep' if connected and percent > 75 else 'stop')

    def test_registry_missing_ambiguous_or_invalid_properties_fail_closed(self):
        for batteries in ([], [{}, {}], {}, [dict(ExternalConnected=1, CurrentCapacity=80, MaxCapacity=100)],
                          [dict(ExternalConnected=True, CurrentCapacity=80, MaxCapacity=0)],
                          [dict(ExternalConnected=True, CurrentCapacity=101, MaxCapacity=100)]):
            with self.assertRaises(ValueError): parse_battery_registry(plistlib.dumps(batteries))

    def test_benchmark_review_blocks_power_restart(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder);run = root/'runs/example';run.mkdir(parents=True)
            (run/'training-review.json').write_text(json.dumps(dict(owner='BlockNations.TrainingReview.v1', runId='example', state='review_required')))
            config = dict(root=folder, runId='example', enabled=True,
                          command=['python', '/supervisor.py', '--root', folder, '--run-id', 'example', '--resume'])
            launches = []
            state = tick(config, {'lastPid': 12}, True, 80, 100, lambda pid: [], launch=launches.append)
            self.assertEqual(state['state'], 'benchmark_review_required')
            self.assertFalse(launches)

    def test_virtualenv_launch_keeps_native_process_identity(self):
        config = dict(command=['/venv/bin/python', '/supervisor.py', '--resume'], processExecutable='/native/Python')
        self.assertTrue(identity('/native/Python /supervisor.py --resume', config))
        self.assertFalse(identity('/native/Python /other.py --resume', config))

    def test_reboot_clears_stale_pids_but_preserves_recorded_fault(self):
        state = reconcile_boot(dict(bootIdentity='old', lastPid=12, stoppingPid=12), 'new')
        self.assertNotIn('lastPid', state)
        self.assertNotIn('stoppingPid', state)
        self.assertEqual(state['bootIdentity'], 'new')
        state = reconcile_boot(dict(bootIdentity='old', lastPid=12, fault='failed save'), 'new')
        self.assertEqual(state['fault'], 'failed save')

    def test_real_pmset_formats_and_unknown(self):
        self.assertEqual(parse_power("Now drawing from 'AC Power'\n -InternalBattery-0 80%; AC attached; not charging present: true"), (True, 80))
        self.assertEqual(parse_power("Now drawing from 'Battery Power'\n -InternalBattery-0 79%; discharging; 4:20 remaining"), (False, 79))
        with self.assertRaises(ValueError):
            parse_power('battery unavailable')

    def test_hysteresis_and_unplugged(self):
        for percentage in (76, 79):
            self.assertEqual(action(False, True, percentage), 'wait')
            self.assertEqual(action(True, True, percentage), 'keep')
        self.assertEqual(action(False, True, 80), 'start')
        self.assertEqual(action(True, True, 75), 'stop')
        self.assertEqual(action(True, False, 100), 'stop')
        self.assertEqual(action(False, False, 100), 'wait')

    def test_stop_save_wait_resume_and_no_duplicate_launch(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            run = root / 'runs' / 'example'
            run.mkdir(parents=True)
            config = dict(root=folder, runId='example', enabled=True,
                          command=['python', '/supervisor.py', '--root', folder, '--run-id', 'example', '--resume'])
            processes = {12: config['command']}
            (root / 'supervisor.lock').write_text('12')
            signals, launches = [], []
            def launch(cmd):
                launches.append(cmd)
                processes[13] = cmd
                return 13
            def poll(state, ac, percentage):
                return tick(config, state, ac, percentage, 100, processes.get, signals.append, launch)
            state = poll({}, True, 80)
            self.assertEqual(state['state'], 'running')
            self.assertFalse(launches)
            state = poll(state, False, 79)
            self.assertEqual(signals, [12])
            state = poll(state, True, 80)
            self.assertEqual(state['state'], 'saving')
            self.assertFalse(launches)
            processes.clear()
            (root / 'supervisor.lock').unlink()
            (run / 'supervisor-status.json').write_text(json.dumps(dict(state='stopped', exitCode=0)))
            state = poll(state, True, 79)
            self.assertEqual(state['state'], 'waiting_for_power')
            state = poll(state, True, 80)
            self.assertEqual(len(launches), 1)
            state = poll(state, True, 80)
            self.assertEqual(state['state'], 'running')
            self.assertEqual(len(launches), 1)
            state = poll(state, True, 75)
            self.assertEqual(signals, [12, 13])

    def test_manual_exit_and_foreign_pid_do_not_restart_or_signal(self):
        with tempfile.TemporaryDirectory() as folder:
            config = dict(root=folder, runId='example', enabled=True,
                          command=['python', '/supervisor.py', '--root', folder, '--run-id', 'example', '--resume'])
            signals, launches = [], []
            state = tick(config, {'lastPid': 12}, True, 80, 100, lambda pid: [], signals.append, launches.append)
            self.assertEqual(state['state'], 'fault')
            (Path(folder) / 'supervisor.lock').write_text('12')
            state = tick(config, {}, False, 75, 100, lambda pid: ['unrelated'], signals.append, launches.append)
            self.assertEqual(state['state'], 'another_supervisor_active')
            self.assertFalse(signals or launches)
            self.assertFalse(identity(config['command'][:-1], dict(config, command=['python', '/other.py'])))

    def test_real_process_graceful_save_and_resume_with_spaced_paths(self):
        with tempfile.TemporaryDirectory(prefix='power controller ') as folder:
            root = Path(folder)
            run = root / 'runs' / 'example'
            run.mkdir(parents=True)
            script = root / 'fake supervisor.py'
            script.write_text('''import signal, time, json, pathlib, os, sys
root = pathlib.Path(sys.argv[sys.argv.index('--root')+1])
def stop(*args):
    (root/'runs/example/supervisor-status.json').write_text(json.dumps({'state':'stopped','exitCode':0}))
    (root/'supervisor.lock').unlink()
    raise SystemExit(0)
signal.signal(signal.SIGTERM, stop)
(root/'supervisor.lock').write_text(str(os.getpid()))
while True: time.sleep(.05)
''')
            # macOS framework Python rewrites argv[0]; pin the native executable.
            executable = command_for(os.getpid()).split(' ', 1)[0]
            command = [executable, str(script), '--root', folder, '--run-id', 'example', '--resume']
            config = dict(root=folder, runId='example', enabled=True, command=command)
            children = []
            def launch(cmd):
                child = subprocess.Popen(cmd)
                children.append(child)
                return child.pid
            def wait_ready():
                deadline = time.monotonic()+5
                while not (root/'supervisor.lock').exists():
                    if time.monotonic()>deadline: self.fail('Fake supervisor did not start')
                    time.sleep(.02)
            try:
                state = tick(config, {}, True, 80, 100, launch=launch)
                wait_ready()
                state = tick(config, state, True, 80, 101, launch=launch)
                self.assertEqual(state['state'], 'running', (command, command_for(children[0].pid)))
                state = tick(config, state, False, 80, 102, launch=launch)
                self.assertEqual(state['state'], 'saving')
                children[0].wait(timeout=5)
                state = tick(config, state, True, 79, 103, launch=launch)
                self.assertEqual(state['state'], 'waiting_for_power')
                state = tick(config, state, True, 80, 104, launch=launch)
                wait_ready()
                self.assertEqual(len(children), 2)
                state = tick(config, state, True, 75, 105, launch=launch)
                children[1].wait(timeout=5)
                self.assertEqual(children[1].returncode, 0)
            finally:
                for child in children:
                    if child.poll() is None:
                        child.terminate()
                        child.wait(timeout=5)


if __name__ == '__main__':
    unittest.main()
