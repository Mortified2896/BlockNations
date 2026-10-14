"""The extra capacity must preserve the parent and actually be trainable."""
import copy
import os
from pathlib import Path
import tempfile
import unittest
import uuid

import numpy as np
from mlagents.torch_utils import torch
from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import SimpleActor, ValueNetwork
from mlagents_envs.base_env import ActionTuple

from capacity_expansion import expand, INPUT, MIDDLE, ACTOR_HEAD, CRITIC_HEAD
from dotnet_environment import DotNetEnvironment
from model_contract import spec, SIZES
from training_contract import CORE_THREE_RULES

WORKER = Path(os.environ.get('BLOCKNATIONS_TEST_WORKER', '/not-built'))
NETWORK = dict(hidden_units=128, num_layers=2, normalize=False, memory=None)


def modules(network, schema):
    settings, contract = NetworkSettings(**network), spec(schema)
    return (SimpleActor(contract.observation_specs, settings, contract.action_spec),
            ValueNetwork(['extrinsic'], contract.observation_specs, settings))


def probabilities(actor, observations, masks):
    encoded, _ = actor.network_body([observations])
    return actor.action_model._get_dists(encoded, (~masks).float()).discrete[0].probs


class CapacityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        torch.set_num_threads(1)

    def checkpoint(self, schema=2):
        torch.manual_seed(83)
        actor, critic = modules(NETWORK, schema)
        optimizer = torch.optim.Adam(list(actor.parameters())+list(critic.parameters()), lr=7.5e-5)
        sum(p.square().sum() for p in [*actor.parameters(), *critic.parameters()] if p.requires_grad).backward()
        optimizer.step()
        return dict(Policy=copy.deepcopy(actor.state_dict()), **{'Optimizer:critic':copy.deepcopy(critic.state_dict()),
            'Optimizer:value_optimizer':copy.deepcopy(optimizer.state_dict()),
            'global_step':{'_GlobalSteps__global_step':torch.tensor([10000], dtype=torch.int64)}})

    def verify_predictions(self, parent, grown, network, observations, mask, schema=2):
        before, before_value = modules(NETWORK, schema)
        after, after_value = modules(network, schema)
        for actor, critic, state in ((before, before_value, parent), (after, after_value, grown)):
            actor.load_state_dict(state['Policy'], strict=True)
            critic.load_state_dict(state['Optimizer:critic'], strict=True)
        with torch.no_grad():
            torch.testing.assert_close(probabilities(before, observations, mask), probabilities(after, observations, mask), rtol=1e-5, atol=1e-6)
            torch.testing.assert_close(before_value([observations])[0]['extrinsic'], after_value([observations])[0]['extrinsic'], rtol=1e-5, atol=1e-6)

    def test_function_moments_counters_and_parent_preserved_for_supported_schemas(self):
        for schema in (2, 3, 4):
            parent = self.checkpoint(schema)
            originals = copy.deepcopy(parent)
            grown, network = expand(parent, NETWORK, schema, 84)
            self.assertEqual(NETWORK['hidden_units'], 128)
            self.assertEqual(network['hidden_units'], 256)
            self.verify_predictions(parent, grown, network, torch.randn(48, SIZES[schema]),
                                    torch.cat((torch.rand(48, 258) < .6, torch.zeros(48, 1, dtype=torch.bool)), 1), schema)
            for module in ('Policy', 'Optimizer:critic'):
                for key, value in parent[module].items():
                    self.assertTrue(torch.equal(value, originals[module][key]))
            self.assertTrue(torch.equal(grown['global_step']['_GlobalSteps__global_step'], originals['global_step']['_GlobalSteps__global_step']))
            for index, state in originals['Optimizer:value_optimizer']['state'].items():
                new = grown['Optimizer:value_optimizer']['state'][index]
                self.assertTrue(torch.equal(state['step'], new['step']))
                for key in ('exp_avg', 'exp_avg_sq'):
                    slices = tuple(slice(0, length) for length in state[key].shape)
                    self.assertTrue(torch.equal(new[key][slices], state[key]))
                    extra = new[key].clone(); extra[slices] = 0
                    self.assertEqual(torch.count_nonzero(extra).item(), 0)
                    self.assertTrue(torch.equal(parent['Optimizer:value_optimizer']['state'][index][key], state[key]))

    def test_new_features_receive_gradients_after_zero_heads_open_and_adam_resumes(self):
        parent = self.checkpoint()
        grown, network = expand(parent, NETWORK, 2, 84)
        actor, critic = modules(network, 2)
        actor.load_state_dict(grown['Policy']); critic.load_state_dict(grown['Optimizer:critic'])
        optimizer = torch.optim.Adam([*actor.parameters(), *critic.parameters()], lr=7.5e-5)
        optimizer.load_state_dict(grown['Optimizer:value_optimizer'])
        initial_actor = copy.deepcopy(actor.state_dict()); initial_critic = copy.deepcopy(critic.state_dict())
        x = torch.randn(32, SIZES[2])
        mask = torch.zeros(32, 259, dtype=torch.bool)
        for _ in range(2):
            optimizer.zero_grad()
            loss = -probabilities(actor, x, mask)[:, 243].clamp_min(1e-12).log().mean()+critic([x])[0]['extrinsic'].square().mean()
            loss.backward(); optimizer.step()
        for state, initial, head in ((actor.state_dict(), initial_actor, ACTOR_HEAD), (critic.state_dict(), initial_critic, CRITIC_HEAD)):
            self.assertGreater(torch.count_nonzero(state[head][:, 128:]).item(), 0)
            self.assertFalse(torch.equal(state[INPUT][128:], initial[INPUT][128:]))
            self.assertFalse(torch.equal(state[MIDDLE][128:], initial[MIDDLE][128:]))
        self.assertTrue(all(s['step'].item() == 3 for s in optimizer.state_dict()['state'].values()))

    def test_corrupt_or_unsupported_parents_fail_closed(self):
        parent = self.checkpoint()
        for options in (dict(hidden_units=256), dict(num_layers=3), dict(normalize=True), dict(memory={'memory_size':128})):
            with self.assertRaises(ValueError):expand(parent, dict(NETWORK, **options), 2, 84)
        def changed(function):
            state = copy.deepcopy(parent); function(state)
            with self.assertRaises(ValueError):expand(state, NETWORK, 2, 84)
        changed(lambda s:s['Policy'][INPUT].fill_(float('nan')))
        changed(lambda s:s['Policy']['memory_size_vector'].fill_(128))
        changed(lambda s:s['Optimizer:value_optimizer']['param_groups'][0]['params'].reverse())
        changed(lambda s:s['Optimizer:value_optimizer']['state'][6]['exp_avg_sq'].fill_(-1))
        changed(lambda s:s['Optimizer:value_optimizer']['state'][6]['step'].fill_(.5))
        changed(lambda s:s['Optimizer:value_optimizer']['param_groups'][0].update(lr=float('nan')))
        changed(lambda s:s['global_step']['_GlobalSteps__global_step'].fill_(-1))

    def test_actual_sdk_resume_restores_every_module_instead_of_silently_reinitializing(self):
        from mlagents.trainers.settings import TrainerSettings
        from mlagents.trainers.ppo.optimizer_torch import TorchPPOOptimizer, PPOSettings
        from mlagents.trainers.policy.torch_policy import TorchPolicy
        from mlagents.trainers.model_saver.torch_model_saver import TorchModelSaver
        grown, network = expand(self.checkpoint(), NETWORK, 2, 84)
        settings = TrainerSettings(network_settings=NetworkSettings(**network), hyperparameters=PPOSettings(), max_steps=20000)
        policy = TorchPolicy(84, spec(2), settings.network_settings, SimpleActor, dict(conditional_sigma=False, tanh_squash=False))
        optimizer = TorchPPOOptimizer(policy, settings)
        with tempfile.TemporaryDirectory() as directory:
            torch.save(grown, Path(directory)/'checkpoint.pt')
            saver = TorchModelSaver(settings, directory, load=True)
            saver.register(policy); saver.register(optimizer); saver.initialize_or_load()
            self.assertEqual(policy.get_current_step(), 10000)
            for key in ('Policy', 'Optimizer:critic'):
                actual = saver.modules[key].state_dict()
                for name, value in grown[key].items():self.assertTrue(torch.equal(value, actual[name]))
            actual = saver.modules['Optimizer:value_optimizer'].state_dict()
            for index, state in grown['Optimizer:value_optimizer']['state'].items():
                for name, value in state.items():self.assertTrue(torch.equal(value, actual['state'][index][name]))

    @unittest.skipUnless(WORKER.is_file(), 'Build the actual worker first.')
    def test_initial_policy_is_identical_on_actual_fair_decisions(self):
        parent = self.checkpoint(); grown, network = expand(parent, NETWORK, 2, 84)
        with tempfile.TemporaryDirectory() as directory:
            env = DotNetEnvironment(WORKER, directory, 4, 84, 7, False, 2, uuid.uuid4().hex, rules_version=CORE_THREE_RULES)
            try:
                env.reset(); count = 0
                for _ in range(64):
                    for name in env.names:
                        steps, _ = env.get_steps(name)
                        if not len(steps):continue
                        self.verify_predictions(parent, grown, network, torch.as_tensor(steps.obs[0]), torch.as_tensor(steps.action_mask[0]))
                        count += len(steps)
                        choices = np.array([[np.flatnonzero(~m)[0]] for m in steps.action_mask[0]], dtype=np.int32)
                        env.set_actions(name, ActionTuple(discrete=choices))
                    env.step()
                self.assertGreater(count, 128)
                self.assertEqual(sum(s['rejections'] for s in env.stats), 0)
            finally:env.close()


if __name__ == '__main__':
    unittest.main()
