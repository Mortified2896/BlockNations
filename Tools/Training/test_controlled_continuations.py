"""Unattended comparison ownership, immutable parents and stop guards."""
import io
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from mlagents.torch_utils import torch
from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import SimpleActor

import controlled_continuations as controller
from dotnet_environment import SPEC
from evaluate_suite import digest
from supervisor import ROOT_MARKER
from training_contract import CORE_THREE_RULES


class StubStudy:
    """Lifecycle double only; never creates or claims a trained checkpoint."""
    def __init__(self, command, running=False):
        self.pid, self.returncode, self.signals = 12345, None if running else 0, []
        self.stdout = io.BytesIO()
        if not running:
            target = Path(command[command.index('--destination')+1])
            target.mkdir(parents=True)
            (target/'study.json').write_text(json.dumps(dict(state='completed', trials=[dict(lifecycleFixture=True)])))

    def poll(self):
        return self.returncode

    def send_signal(self, event):
        self.signals.append(event)
        self.returncode = 0

    def wait(self, timeout):
        return self.returncode


class ControlledContinuationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        (self.root/ROOT_MARKER).write_text(json.dumps(dict(owner='BlockNations.LocalTraining.v1')))
        self.recipe = self.root/'runs'/'parent'
        folder = self.recipe/'frozen-evaluations'/'fixed'; folder.mkdir(parents=True)
        checkpoint = folder/'BlockNationsSeatV2-100.pt'
        actor = SimpleActor(SPEC.observation_specs, NetworkSettings(hidden_units=8, num_layers=1), SPEC.action_spec)
        torch.save({'Policy':actor.state_dict()}, checkpoint)
        config = folder/'trainer.yaml'
        config.write_text(json.dumps({'behaviors':{'BlockNationsSeatV2':{
            'network_settings':{'hidden_units':8, 'num_layers':1, 'normalize':False}}}}))
        self.parent = folder/'manifest.json'
        self.parent.write_text(json.dumps(dict(checkpoint=str(checkpoint), trainerConfig=str(config), step=100,
            rulesVersion=CORE_THREE_RULES, boardSize=7, openingEconomyVersion=2, sha256=digest(checkpoint))))
        (self.recipe/'run.json').write_text(json.dumps(dict(owner='BlockNations.LocalTraining.v1', schema=2)))
        (self.recipe/'training-opponents.json').write_text('{}')
        self.suite = self.root/'baseline-suite.json'; self.suite.write_text('{}')
        self.worker = self.root/'fixture-worker'; self.worker.write_bytes(b'worker lifecycle fixture')
        self.destination = self.root/'benchmarks'/'paired'
        self.inputs = controller.fingerprints([self.parent, checkpoint, config])

    def tearDown(self):
        self.temporary.cleanup()

    def run_queue(self, **options):
        return controller.run(self.root, self.parent, self.recipe, self.suite, self.worker, self.destination,
                              **dict(dict(seeds=[45, 46, 47], steps=4096), **options))

    def test_invalid_inputs_and_existing_study_cannot_fork_or_overwrite_artifacts(self):
        with patch.object(controller.subprocess, 'Popen') as spawn:
            for options in (dict(seeds=[]), dict(seeds=[45, 45]), dict(seeds=[True]), dict(seeds=[{}]),
                            dict(hours=float('nan')), dict(hours=True), dict(hours=0),
                            dict(steps=True), dict(coefficient=.03), dict(learning_rate=0)):
                with self.assertRaises(ValueError): self.run_queue(**options)
                self.assertFalse(self.destination.exists())
            lock = self.root/'rule-profile-study.lock'; lock.write_text('existing owner')
            with self.assertRaises(ValueError): self.run_queue()
            self.assertEqual(lock.read_text(), 'existing owner')
            lock.unlink()
            self.destination.mkdir(parents=True)
            saved = self.destination/'queue.json'; saved.write_text('preserved result')
            with self.assertRaises(ValueError): self.run_queue()
            self.assertEqual(saved.read_text(), 'preserved result')
            spawn.assert_not_called()

    def test_storage_and_free_disk_reserves_apply_before_start(self):
        with patch.object(controller.subprocess, 'Popen') as spawn:
            with patch.object(controller, 'size', return_value=controller.SHARED_BUDGET):
                with self.assertRaisesRegex(RuntimeError, 'reserve'): self.run_queue()
            with patch.object(controller.shutil, 'disk_usage', return_value=SimpleNamespace(free=20_000_000_000)):
                with self.assertRaisesRegex(RuntimeError, 'reserve'): self.run_queue()
            spawn.assert_not_called()
            self.assertFalse(self.destination.exists())
            self.assertFalse((self.root/'controlled-continuations.lock').exists())

    def test_each_seed_gets_both_arms_from_same_immutable_parent_with_explicit_pressure(self):
        commands = []
        def spawn(command, **_):
            commands.append(command)
            return StubStudy(command)
        with patch.object(controller.subprocess, 'Popen', side_effect=spawn):
            result = self.run_queue()
        self.assertEqual(result['state'], 'completed')
        self.assertEqual([(x['seed'], x['arm']) for x in result['completed']],
                         [(seed, arm) for seed in [45, 46, 47] for arm in ['control', 'exploration']])
        for command, outcome in zip(commands, result['completed']):
            self.assertEqual(command[command.index('--parent')+1], str(self.parent))
            self.assertEqual(command[command.index('--seed')+1], str(outcome['seed']))
            self.assertEqual('--exploration-coefficient' in command, outcome['arm'] == 'exploration')
        self.assertEqual(controller.fingerprints(map(Path, self.inputs)), self.inputs)
        self.assertEqual(result['planSha256'], digest(self.destination/'plan.json'))
        self.assertFalse((self.root/'controlled-continuations.lock').exists())

    def test_input_drift_interrupts_owned_study_and_preserves_parent_and_failure_receipt(self):
        child = None
        def spawn(command, **_):
            nonlocal child
            child = StubStudy(command, running=True)
            self.suite.write_text('{"changed": true}')
            return child
        with patch.object(controller.subprocess, 'Popen', side_effect=spawn):
            with self.assertRaisesRegex(RuntimeError, 'inputs changed'): self.run_queue()
        self.assertEqual(child.signals, [controller.signal.SIGINT])
        result = json.loads((self.destination/'queue.json').read_text())
        self.assertEqual(result['state'], 'failed')
        self.assertEqual(result['completed'], [])
        self.assertEqual(controller.fingerprints(map(Path, self.inputs)), self.inputs)
        self.assertFalse((self.root/'controlled-continuations.lock').exists())

    def test_storage_limit_during_study_saves_and_stops_instead_of_starting_next_arm(self):
        child = None
        def spawn(command, **_):
            nonlocal child
            child = StubStudy(command, running=True)
            return child
        # First check is preflight, second is immediately before the first arm,
        # and the third discovers the shared reserve while that arm is running.
        with patch.object(controller, 'size', side_effect=[0, 0, controller.SHARED_BUDGET]), \
                patch.object(controller.subprocess, 'Popen', side_effect=spawn) as launch:
            with self.assertRaisesRegex(RuntimeError, 'reserve'): self.run_queue()
        launch.assert_called_once()
        self.assertEqual(child.signals, [controller.signal.SIGINT])
        self.assertEqual(json.loads((self.destination/'queue.json').read_text())['state'], 'failed')
        self.assertEqual(controller.fingerprints(map(Path, self.inputs)), self.inputs)


if __name__ == '__main__':
    unittest.main()
