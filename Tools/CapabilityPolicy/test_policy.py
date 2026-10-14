import sys
from pathlib import Path
import tempfile
import unittest
import os
import uuid

import numpy as np
import torch
from mlagents.trainers.settings import NetworkSettings

sys.path.insert(0, str(Path(__file__).parents[1]/'Training'))
from model_contract import spec
from policy import (CapabilityActor, ProbabilityPolicy, probabilities, export, ACTIONS,
                    OBSERVATIONS, RECRUIT_OFFSET, GLOBALS_OFFSET,
                    RECRUIT_CAPABILITY_OFFSET, ROLE_OFFSET)

torch.set_num_threads(1)


def actor():
    contract = spec(4)
    torch.manual_seed(207)
    return CapabilityActor(contract.observation_specs, NetworkSettings(hidden_units=32, num_layers=2), contract.action_spec)


def samples(batch=7):
    generator = torch.Generator().manual_seed(517)
    values = torch.rand((batch, OBSERVATIONS), generator=generator)
    tiles = values[:, :RECRUIT_OFFSET].reshape(batch, 121, 24)
    tiles[:, :, 0] = 1
    tiles[:, :, 20] = 0; tiles[:, 60, 20] = 1
    recruits = values[:, RECRUIT_OFFSET:GLOBALS_OFFSET].reshape(batch, 16, 13)
    recruits[:, :, 0] = 1
    values[:, 3119] = 2; values[:, 3258] = 3; values[:, -1] = 4
    allowed = (torch.rand((batch, ACTIONS), generator=generator) > .4).float()
    allowed[:, -1] = 1
    return values, allowed


class CapabilityPolicyTests(unittest.TestCase):
    def test_recruit_permutations_preserve_all_other_actions_and_value(self):
        model = actor(); values, allowed = samples()
        with torch.no_grad():
            original = probabilities(model, values, allowed)
            original_value = model.critic_pass([values])[0]['extrinsic']
            for seed in range(24):
                perm = torch.randperm(16, generator=torch.Generator().manual_seed(seed))
                changed = values.clone(); changed_mask = allowed.clone()
                recruits = values[:, RECRUIT_OFFSET:GLOBALS_OFFSET].reshape(-1, 16, 13)
                changed[:, RECRUIT_OFFSET:GLOBALS_OFFSET] = recruits[:, perm].reshape(-1, 208)
                changed[:, RECRUIT_CAPABILITY_OFFSET:ROLE_OFFSET] = values[:, RECRUIT_CAPABILITY_OFFSET:ROLE_OFFSET][:, perm]
                changed_mask[:, 242:258] = allowed[:, 242:258][:, perm]
                actual = probabilities(model, changed, changed_mask)
                torch.testing.assert_close(actual[:, :242], original[:, :242])
                torch.testing.assert_close(actual[:, 242:258], original[:, 242:258][:, perm])
                torch.testing.assert_close(actual[:, -1], original[:, -1])
                torch.testing.assert_close(model.critic_pass([changed])[0]['extrinsic'], original_value)

    def test_identical_capabilities_share_a_score_in_unfamiliar_slots(self):
        model = actor(); values, allowed = samples()
        fields = values[:, RECRUIT_OFFSET:GLOBALS_OFFSET].reshape(-1, 16, 13)
        fields[:, 15] = fields[:, 4]
        values[:, RECRUIT_CAPABILITY_OFFSET+15] = values[:, RECRUIT_CAPABILITY_OFFSET+4]
        allowed[:, 242+4] = allowed[:, 242+15] = 1
        result = probabilities(model, values, allowed)
        torch.testing.assert_close(result[:, 242+4], result[:, 242+15], rtol=0, atol=0)

    def test_masked_actions_are_zero_and_legal_probabilities_normalize(self):
        values, allowed = samples(); result = probabilities(actor(), values, allowed)
        self.assertTrue(torch.isfinite(result).all())
        self.assertTrue(torch.all(result[allowed == 0] == 0))
        torch.testing.assert_close(result.sum(dim=1), torch.ones(len(values)))
        allowed.zero_(); allowed[:, -1] = 1
        only = probabilities(actor(), values, allowed)
        torch.testing.assert_close(only[:, -1], torch.ones(len(values)), rtol=0, atol=0)
        self.assertTrue(torch.all(only[:, :-1] == 0))

    def test_wrong_contract_or_unusable_masks_fail(self):
        model = actor(); values, allowed = samples()
        for bad_values, bad_mask in [(values[:, :3120], allowed), (values, allowed[:, :258]),
                                     (values, allowed*float('nan')), (values, torch.zeros_like(allowed))]:
            with self.assertRaises(ValueError): probabilities(model, bad_values, bad_mask)
        changed = values.clone(); changed[:, -1] = 3
        with self.assertRaises(ValueError): probabilities(model, changed, allowed)
        for settings in [NetworkSettings(normalize=True), NetworkSettings(hidden_units=256)]:
            with self.assertRaises(ValueError):
                CapabilityActor(spec(4).observation_specs, settings, spec(4).action_spec)
        with self.assertRaises(ValueError):
            CapabilityActor(spec(2).observation_specs, NetworkSettings(hidden_units=32), spec(2).action_spec)

    def test_shared_parameters_and_value_learn(self):
        model = actor(); values, allowed = samples(16)
        allowed[:] = 1
        optimizer = torch.optim.Adam(model.parameters(), lr=.003)
        def loss():
            p = probabilities(model, values, allowed)
            v = model.critic_pass([values])[0]['extrinsic']
            return -p[:, 257].log().mean()+(v-1).square().mean()
        initial = float(loss())
        for _ in range(12):
            optimizer.zero_grad(); error = loss(); error.backward(); optimizer.step()
        self.assertLess(float(loss()), initial*.8)
        for layer in [model.network_body.spatial[0], model.network_body.recruit[0], model.network_body.recruit_score[0]]:
            self.assertGreater(float(layer.weight.grad.abs().sum()), 0)

    def test_actual_mlagents_policy_sampling_and_stats(self):
        from mlagents.trainers.policy.torch_policy import TorchPolicy
        from mlagents_envs.base_env import DecisionSteps
        values, allowed = samples()
        model = TorchPolicy(81, spec(4), NetworkSettings(hidden_units=32), CapabilityActor, {})
        steps = DecisionSteps([values.numpy()], np.zeros(len(values)), np.arange(len(values)),
                              [(allowed == 0).numpy()], np.zeros(len(values), dtype=np.int32), np.zeros(len(values)))
        action = model.get_action(steps, worker_id=0)
        chosen = action.env_action.discrete[:, 0]
        self.assertTrue(all(allowed[i, choice] == 1 for i, choice in enumerate(chosen)))
        from mlagents.trainers.torch_entities.agent_action import AgentAction
        sampled = AgentAction(None, [torch.as_tensor(chosen)])
        stats = model.actor.get_stats([values], sampled, allowed)
        self.assertTrue(torch.isfinite(stats['entropy']).all())

    def test_padded_square_fields_do_not_supply_information(self):
        model = actor(); values, allowed = samples()
        tiles = values[:, :RECRUIT_OFFSET].reshape(-1, 121, 24)
        tiles[:, :22, 0] = 0
        changed = values.clone()
        changed_tiles = changed[:, :RECRUIT_OFFSET].reshape(-1, 121, 24)
        changed_tiles[:, :22, 1:] = 100
        changed[:, 3120:3142] = 100
        changed[:, 3259:3259+22*12] = 100
        torch.testing.assert_close(probabilities(model, values, allowed), probabilities(model, changed, allowed), rtol=0, atol=0)

    def test_probability_export_dynamic_batches_match_torch(self):
        from onnx.reference import ReferenceEvaluator
        model = actor(); values, allowed = samples()
        with tempfile.TemporaryDirectory() as folder:
            destination = Path(folder)/'prototype.onnx'
            export(model, values[:1], allowed[:1], destination)
            reference = ReferenceEvaluator(str(destination))
            for count in (1, 7):
                actual = reference.run(None, {'obs_0': values[:count].numpy(), 'action_masks': allowed[:count].numpy()})[0]
                expected = probabilities(model, values[:count], allowed[:count]).detach().numpy()
                np.testing.assert_allclose(actual, expected, rtol=1e-4, atol=1e-6)
                self.assertTrue(np.all(actual[allowed[:count].numpy() == 0] == 0))
            with self.assertRaises(ValueError): export(model, values, allowed, destination)

    @unittest.skipUnless(os.environ.get('BLOCKNATIONS_CAPABILITY_TEST_WORKER'), 'Set the actual standalone worker for integration checks.')
    def test_real_csharp_decisions_stay_fair_and_legal(self):
        from dotnet_environment import DotNetEnvironment
        from mlagents_envs.base_env import ActionTuple
        from training_contract import CORE_THREE_RULES, RULES_VERSION
        model = actor(); random = np.random.default_rng(50120)
        for rules, board in [(CORE_THREE_RULES, 7), (RULES_VERSION, 5)]:
            with self.subTest(rules=rules, board=board), tempfile.TemporaryDirectory() as folder:
                counts = [0, 0]
                env = DotNetEnvironment(os.environ['BLOCKNATIONS_CAPABILITY_TEST_WORKER'], folder, 2, 50120,
                                       board, False, 2, uuid.uuid4().hex, rules_version=rules, observation_version=4)
                try:
                    env.reset()
                    for _ in range(96):
                        for seat, name in enumerate(env.names):
                            decisions, _ = env.get_steps(name)
                            if not len(decisions): continue
                            values = torch.as_tensor(decisions.obs[0])
                            allowed = torch.as_tensor(~decisions.action_mask[0], dtype=torch.float32)
                            with torch.no_grad(): distribution = probabilities(model, values, allowed).numpy()
                            choices = np.array([[random.choice(ACTIONS, p=row)] for row in distribution], dtype=np.int32)
                            self.assertTrue(all(allowed[i, choice] == 1 for i, choice in enumerate(choices[:, 0])))
                            counts[seat] += len(decisions)
                            env.set_actions(name, ActionTuple(discrete=choices))
                        env.step()
                    self.assertEqual(sum(stat['rejections'] for stat in env.stats), 0)
                    self.assertTrue(all(count > 0 for count in counts))
                finally:
                    env.close()


class TrainingOwnershipTests(unittest.TestCase):
    def test_enabled_ordinary_autostarter_must_be_scheduled_separately(self):
        import json
        from train import exclusive
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); config = root/'power-controller.json'
            config.write_text(json.dumps(dict(enabled=True)))
            with self.assertRaises(RuntimeError):
                with exclusive(root): self.fail('Do not race a separate auto-start controller.')
            self.assertFalse((root/'information-study.lock').exists())
            self.assertFalse((root/'supervisor.lock').exists())

    def test_distinct_trainer_recipe_builds_stock_ppo_with_shared_critic(self):
        from mlagents import plugins
        from mlagents.trainers.settings import RunOptions
        from mlagents.trainers.ppo.optimizer_torch import TorchPPOOptimizer
        from trainer import register, CapabilityPPOTrainer, TRAINER
        from train import recipe
        previous = plugins.all_trainer_types.get('ppo')
        register()
        settings = RunOptions.from_dict(recipe(8192, 32)).behaviors['BlockNationsSeatV4']
        self.assertEqual(settings.trainer_type, TRAINER)
        self.assertIs(plugins.all_trainer_types.get('ppo'), previous)
        custom = object.__new__(CapabilityPPOTrainer)
        custom.seed = 207; custom.shared_critic = True; custom.trainer_settings = settings
        policy = custom.create_policy(None, spec(4))
        optimizer = TorchPPOOptimizer(policy, settings)
        self.assertIs(optimizer.critic, policy.actor)
        self.assertIsInstance(policy.actor, CapabilityActor)
        from mlagents.trainers.buffer import AgentBuffer, BufferKey, RewardSignalUtil
        from mlagents.trainers.trajectory import ObsUtil
        from mlagents.trainers.model_saver.torch_model_saver import TorchModelSaver
        from artifacts import checkpoint
        values, allowed = samples(32)
        with torch.no_grad():
            before = probabilities(policy.actor, values, allowed)
            old_values = policy.actor.critic_pass([values])[0]['extrinsic'].numpy()
        batch = AgentBuffer()
        for key, value in {
            ObsUtil.get_name_at(0): values.numpy(), BufferKey.ACTION_MASK: allowed.numpy(),
            BufferKey.DISCRETE_ACTION: np.full((32, 1), 258, dtype=np.int64),
            BufferKey.DISCRETE_LOG_PROBS: before[:, -1:].log().numpy(),
            BufferKey.MASKS: np.ones(32, dtype=bool), BufferKey.ADVANTAGES: np.ones(32),
            RewardSignalUtil.value_estimates_key('extrinsic'): old_values,
            RewardSignalUtil.returns_key('extrinsic'): old_values+1,
        }.items(): batch[key].set(value)
        stats = optimizer.update(batch, num_sequences=32)
        self.assertTrue(all(np.isfinite(value) for value in stats.values()))
        policy.set_step(4096)
        with tempfile.TemporaryDirectory() as folder:
            saver = TorchModelSaver(settings, folder)
            saver.register(policy); saver.register(optimizer)
            saver.save_checkpoint('BlockNationsSeatV4', 4096)
            frozen, verified = checkpoint(Path(folder)/'checkpoint.pt', 32)
            self.assertTrue(all(value == 1 for value in verified['optimizerSteps'].values()))
            resumed = custom.create_policy(None, spec(4)); resumed_optimizer = TorchPPOOptimizer(resumed, settings)
            loader = TorchModelSaver(settings, folder, load=True)
            loader.register(resumed); loader.register(resumed_optimizer); loader.initialize_or_load()
            self.assertEqual(resumed.get_current_step(), 4096)
            torch.testing.assert_close(probabilities(frozen, values, allowed), probabilities(resumed.actor, values, allowed), rtol=0, atol=0)
            second = resumed_optimizer.update(batch, num_sequences=32)
            self.assertTrue(all(np.isfinite(value) for value in second.values()))
            self.assertTrue(all(int(state['step']) == 2 for state in resumed_optimizer.optimizer.state.values()))

    def test_live_learner_is_not_replaced(self):
        from train import exclusive
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            lock = root/'supervisor.lock'; lock.write_text(str(os.getpid()))
            with self.assertRaises(RuntimeError):
                with exclusive(root): self.fail('A live learner must retain ownership.')
            self.assertEqual(lock.read_text(), str(os.getpid()))
            self.assertFalse((root/'information-study.lock').exists())

    def test_owned_locks_release_on_failure(self):
        from train import exclusive
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            with self.assertRaises(ValueError):
                with exclusive(root):
                    self.assertEqual((root/'supervisor.lock').read_text(), str(os.getpid()))
                    raise ValueError('Preserve failed trial evidence.')
            self.assertEqual(list(root.iterdir()), [])

    def test_linked_locks_are_rejected(self):
        from train import exclusive
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); other = root/'other'; other.write_text(str(os.getpid()))
            (root/'information-study.lock').symlink_to(other)
            with self.assertRaises(ValueError):
                with exclusive(root): self.fail('Linked locks must fail.')
            self.assertEqual(other.read_text(), str(os.getpid()))


class CheckpointTests(unittest.TestCase):
    def fixture(self, path):
        model = actor(); values, allowed = samples()
        optimizer = torch.optim.Adam(model.parameters(), lr=.0003)
        for _ in range(2):
            optimizer.zero_grad()
            loss = -probabilities(model, values, allowed)[:, -1].log().mean()+model.critic_pass([values])[0]['extrinsic'].square().mean()
            loss.backward(); optimizer.step()
        state = dict(Policy=model.state_dict(), **{'Optimizer:critic': model.state_dict(),
                     'Optimizer:value_optimizer': optimizer.state_dict(),
                     'global_step': {'_GlobalSteps__global_step': torch.tensor([4096])}})
        torch.save(state, path)
        return state

    def test_checkpoint_strictly_retains_actor_value_and_adam_state(self):
        from artifacts import checkpoint
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)/'saved.pt'; self.fixture(path)
            _, receipt = checkpoint(path, 32)
            self.assertEqual(receipt['step'], 4096)
            self.assertTrue(receipt['finiteTensors'] > 0)
            self.assertTrue(all(value == 2 for value in receipt['optimizerSteps'].values()))
            with self.assertRaises(RuntimeError): checkpoint(path, 64)

    def test_nonfinite_or_wrong_adam_shape_is_rejected(self):
        from artifacts import checkpoint
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)/'saved.pt'; state = self.fixture(path)
            first = next(iter(state['Optimizer:value_optimizer']['state'].values()))
            first['exp_avg'] = torch.zeros(1)
            torch.save(state, path)
            with self.assertRaises(ValueError): checkpoint(path, 32)
            state = self.fixture(path)
            first = next(iter(state['Optimizer:value_optimizer']['state'].values()))
            first['exp_avg'].fill_(float('nan'))
            torch.save(state, path)
            with self.assertRaises(ValueError): checkpoint(path, 32)


if __name__ == '__main__':
    unittest.main()
