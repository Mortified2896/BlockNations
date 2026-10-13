import json
from pathlib import Path
import tempfile
import unittest
import subprocess
import time
import os

from power_controller import action, identity, parse_power, tick, command_for, reconcile_boot


class PowerControllerTests(unittest.TestCase):
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
