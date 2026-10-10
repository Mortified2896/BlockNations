import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
import uuid

import numpy as np
from mlagents.torch_utils import torch
from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import SimpleActor
from mlagents.trainers.buffer import BufferKey
from mlagents.trainers.trajectory import ObsUtil

from dotnet_environment import SPEC
from exploration_pressure import ExplorationPressure, masked_uniform_kl
from supervisor import trainer_config
from training_contract import RULES_VERSION, OBSERVATIONS, ACTIONS

WORKER = Path(os.environ.get('BLOCKNATIONS_TEST_WORKER', 'missing-worker'))


class ExplorationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.run = Path(self.temp.name)/'experiment'; self.run.mkdir()
        (self.run/'run.json').write_text(json.dumps(dict(owner='BlockNations.LocalTraining.v1', schema=2, boardSize=7)))
        self.recipe = dict(version=1, rulesVersion=RULES_VERSION, boardSize=7,
                           coefficient=.005, entropyFloor=.1, batchSize=128)

    def tearDown(self):
        self.temp.cleanup()

    def enable(self):
        (self.run/'exploration-pressure.json').write_text(json.dumps(self.recipe))
        return ExplorationPressure(self.run)

    def test_missing_recipe_is_disabled_and_wrong_board_or_unbounded_coefficient_is_rejected(self):
        pressure = ExplorationPressure(self.run)
        self.assertIsNone(pressure.prepare(None, None, 0))
        self.assertIsNone(pressure.update(None, None, None))
        for field, value in [('boardSize', 11), ('coefficient', .2), ('entropyFloor', float('nan')), ('batchSize', 4096)]:
            previous = self.recipe[field]; self.recipe[field] = value
            with self.assertRaises(ValueError): self.enable()
            self.recipe[field] = previous

    def test_saturated_legal_choices_receive_finite_recovery_gradient_but_illegal_choices_do_not(self):
        logits = torch.tensor([[25., 0., 0., 100.]], requires_grad=True)
        mask = torch.tensor([[1., 1., 1., 0.]])
        loss, eligible, entropy = masked_uniform_kl(logits, mask, .1)
        self.assertTrue(eligible.item()); self.assertLess(entropy.item(), .0001)
        loss.backward()
        self.assertTrue(torch.isfinite(logits.grad).all())
        self.assertGreater(logits.grad[0,0].item(), .6)
        self.assertLess(logits.grad[0,1].item(), -.3)
        self.assertEqual(logits.grad[0,3].item(), 0.)

    def test_forced_and_already_exploring_choices_have_no_pressure(self):
        logits = torch.tensor([[50., 0., 0.], [0., 0., 0.]], requires_grad=True)
        loss, eligible, _ = masked_uniform_kl(logits, torch.tensor([[1.,0.,0.], [1.,1.,1.]]), .1)
        self.assertFalse(eligible.any()); self.assertEqual(loss.item(), 0.)
        loss.backward(); self.assertTrue((logits.grad == 0).all())
        with self.assertRaises(ValueError): masked_uniform_kl(logits, torch.zeros_like(logits), .1)

    def test_actual_actor_recovery_uses_existing_optimizer_without_touching_illegal_bias(self):
        actor = SimpleActor(SPEC.observation_specs, NetworkSettings(hidden_units=8, num_layers=1, normalize=False), SPEC.action_spec)
        with torch.no_grad():
            for p in actor.parameters(): p.zero_()
            bias = actor.action_model._discrete_distribution.branches[0].bias
            bias[1] = 25
        optimizer = torch.optim.Adam(actor.parameters(), lr=.001)
        observation = np.zeros((16, OBSERVATIONS), dtype=np.float32)
        mask = np.zeros((16, ACTIONS), dtype=np.float32); mask[:, :3] = 1
        policy = SimpleNamespace(actor=actor)
        original = bias.detach().clone()
        pressure = self.enable()
        statistics = pressure.update(policy, optimizer, (observation, mask))
        self.assertEqual(statistics['Policy/Exploration Eligible Fraction'], 1.)
        self.assertLess(bias[1].item(), original[1].item())
        self.assertGreater(bias[0].item(), original[0].item())
        self.assertTrue(torch.equal(bias[3:], original[3:]))
        self.assertGreater(len(optimizer.state), 0)

    def test_capture_ignores_padding_is_bounded_and_rejects_recurrent_policy(self):
        pressure = self.enable()
        observations = np.zeros((200, OBSERVATIONS), dtype=np.float32)
        observations[:,0] = np.arange(200)
        buffer = {ObsUtil.get_name_at(0): observations,
                  BufferKey.ACTION_MASK: np.ones((200, ACTIONS), dtype=np.float32),
                  BufferKey.MASKS: np.array([True]*150+[False]*50)}
        policy = SimpleNamespace(use_recurrent=False, sequence_length=1, behavior_spec=SPEC)
        first = pressure.prepare(policy, buffer, 100)
        second = pressure.prepare(policy, buffer, 100)
        self.assertEqual(first[0].shape, (128, OBSERVATIONS))
        self.assertTrue((first[0][:,0] < 150).all())
        np.testing.assert_array_equal(first[0], second[0])
        observations[:] = -1
        self.assertTrue((first[0][:,0] >= 0).all(), 'Captured batches own their data after PPO clears its buffer.')
        policy.use_recurrent = True
        with self.assertRaises(ValueError): pressure.prepare(policy, buffer, 100)

    @unittest.skipUnless(WORKER.is_file() and os.environ.get('BLOCKNATIONS_TEST_EXPLORATION_PPO'),
                         'Opt-in real PPO optimizer/checkpoint continuation.')
    def test_real_ppo_pressure_continues_checkpointed_optimizer_and_legal_self_play(self):
        self.enable()
        config = self.run/'trainer.yaml'; config.write_text(json.dumps(trainer_config(4096, 2048)))
        env = dict(os.environ, BLOCKNATIONS_RATING_RUN=str(self.run), BLOCKNATIONS_RATING_BOARD='7',
            BLOCKNATIONS_RATING_SESSION=uuid.uuid4().hex, BLOCKNATIONS_SIMULATION_WORKER=str(WORKER),
            BLOCKNATIONS_PARALLEL_GAMES='4', BLOCKNATIONS_SIMULATION_SEED='42',
            BLOCKNATIONS_SIMULATION_CURRICULUM='false', BLOCKNATIONS_SIMULATION_DISTANCE='2',
            BLOCKNATIONS_OPENING_ECONOMY_VERSION='2', OMP_NUM_THREADS='2', MKL_NUM_THREADS='2')
        command = [sys.executable, str(Path(__file__).with_name('rated_training.py')), str(config),
                   '--run-id', self.run.name, '--results-dir', str(self.run/'checkpoints'), '--seed', '42', '--torch-device', 'cpu']
        checkpoint = self.run/'checkpoints'/self.run.name/'BlockNationsSeatV2/checkpoint.pt'
        steps = []
        for resume in (False, True):
            if resume:
                saturated = torch.load(checkpoint, map_location='cpu')
                # Exercise recovery rather than merely enabling a no-op recipe
                # on a newly initialized, already-diverse random policy.
                saturated['Policy']['action_model._discrete_distribution.branches.0.weight'].zero_()
                bias = saturated['Policy']['action_model._discrete_distribution.branches.0.bias']
                bias.zero_(); bias[243] = 25
                torch.save(saturated, checkpoint)
                plan = json.loads(config.read_text()); plan['behaviors']['BlockNationsSeatV2']['max_steps'] = 8192
                config.write_text(json.dumps(plan)); env['BLOCKNATIONS_RATING_SESSION'] = uuid.uuid4().hex
            with (self.run/('resume.log' if resume else 'initial.log')).open('w') as log:
                result = subprocess.run(command+(['--resume'] if resume else []), env=env, stdout=log,
                                        stderr=subprocess.STDOUT, timeout=90)
            self.assertEqual(result.returncode, 0, 'Read the isolated integration log for details.')
            state = torch.load(checkpoint, map_location='cpu')
            optimizer = state['Optimizer:value_optimizer']['state']
            self.assertEqual(len(optimizer), 12)
            self.assertTrue(all(torch.isfinite(v).all() for v in state['Policy'].values()))
            steps.append(max(float(v['step']) for v in optimizer.values()))
        self.assertGreater(steps[1], steps[0])
        # Auxiliary actor steps advance actor moments, leaving the critic at the
        # ordinary PPO update count. This proves pressure actually executed.
        self.assertGreater(max(float(v['step']) for v in optimizer.values()),
                           min(float(v['step']) for v in optimizer.values()))
        status = json.loads((self.run/'arena-status.json').read_text())
        self.assertEqual(status['rejections'], 0)
        print('Exploration-enabled real PPO Adam continuation:', steps)


if __name__ == '__main__': unittest.main()
