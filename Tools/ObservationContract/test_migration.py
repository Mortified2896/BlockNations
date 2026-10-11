import base64
import copy
import json
from pathlib import Path
import tempfile
import unittest

import numpy as np
import torch

from migrate_checkpoint import migrate, prepare, validate_network, INPUT_WEIGHT, LEGACY_OBSERVATIONS, OBSERVATIONS, RULES
from verification import fixtures, models, probabilities

NETWORK = dict(normalize=False, hidden_units=128, num_layers=2)


class MigrationChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from mlagents_envs.base_env import BehaviorSpec, ObservationSpec, DimensionProperty, ObservationType, ActionSpec
        from mlagents.trainers.settings import NetworkSettings
        from mlagents.trainers.torch_entities.networks import SimpleActor, ValueNetwork
        torch.manual_seed(42)
        spec = BehaviorSpec([ObservationSpec((LEGACY_OBSERVATIONS,), (DimensionProperty.NONE,), ObservationType.DEFAULT, 'VectorSensor')],
                            ActionSpec.create_discrete((259,)))
        actor = SimpleActor(spec.observation_specs, NetworkSettings(**NETWORK), spec.action_spec)
        critic = ValueNetwork(['extrinsic'], spec.observation_specs, NetworkSettings(**NETWORK))
        optimizer = torch.optim.Adam(list(actor.parameters()) + list(critic.parameters()), lr=0.000075)
        values = torch.randn(4, LEGACY_OBSERVATIONS)
        loss = -probabilities(actor, values, torch.ones(4, 259))[:, 258].log().mean() + critic([values])[0]['extrinsic'].square().mean()
        loss.backward(); optimizer.step()
        cls.parent = dict(Policy=actor.state_dict(), global_step={'_GlobalSteps__global_step': torch.tensor([45000], dtype=torch.int64)},
                          **{'Optimizer:critic': critic.state_dict(), 'Optimizer:value_optimizer': optimizer.state_dict()})

    def test_preserves_parent_tensors_and_optimizer_state(self):
        child = migrate(self.parent, NETWORK)
        for kind in ('Policy', 'Optimizer:critic'):
            for key, before in self.parent[kind].items():
                after = child[kind][key]
                if key == INPUT_WEIGHT:
                    self.assertEqual(tuple(after.shape), (128, OBSERVATIONS))
                    torch.testing.assert_close(after[:, :LEGACY_OBSERVATIONS], before, rtol=0, atol=0)
                    self.assertEqual(after[:, LEGACY_OBSERVATIONS:].count_nonzero().item(), 0)
                else:
                    torch.testing.assert_close(after, before, rtol=0, atol=0)
        self.assertEqual(child['global_step']['_GlobalSteps__global_step'].item(), 45000)
        self.assertEqual(child['Optimizer:value_optimizer']['param_groups'], self.parent['Optimizer:value_optimizer']['param_groups'])
        for key, before in self.parent['Optimizer:value_optimizer']['state'].items():
            after = child['Optimizer:value_optimizer']['state'][key]
            torch.testing.assert_close(before['step'], after['step'], rtol=0, atol=0)
            for moment in ('exp_avg', 'exp_avg_sq'):
                if key in (6, 12):
                    torch.testing.assert_close(before[moment], after[moment][:, :LEGACY_OBSERVATIONS], rtol=0, atol=0)
                    self.assertEqual(after[moment][:, LEGACY_OBSERVATIONS:].count_nonzero().item(), 0)
                else:
                    torch.testing.assert_close(before[moment], after[moment], rtol=0, atol=0)
        child['Policy'][INPUT_WEIGHT][0, 0] += 1
        self.assertNotEqual(child['Policy'][INPUT_WEIGHT][0, 0], self.parent['Policy'][INPUT_WEIGHT][0, 0])

    def test_resumed_adam_can_learn_new_features(self):
        child = migrate(self.parent, NETWORK)
        actor, critic = models(NETWORK, OBSERVATIONS, child)
        optimizer = torch.optim.Adam(list(actor.parameters()) + list(critic.parameters()))
        optimizer.load_state_dict(child['Optimizer:value_optimizer'])
        values = torch.cat((torch.randn(4, LEGACY_OBSERVATIONS), torch.ones(4, OBSERVATIONS-LEGACY_OBSERVATIONS)), dim=1)
        loss = -probabilities(actor, values, torch.ones(4, 259))[:, 258].log().mean() + critic([values])[0]['extrinsic'].square().mean()
        loss.backward(); optimizer.step()
        self.assertGreater(actor.state_dict()[INPUT_WEIGHT][:, LEGACY_OBSERVATIONS:].count_nonzero().item(), 0)
        self.assertGreater(critic.state_dict()[INPUT_WEIGHT][:, LEGACY_OBSERVATIONS:].count_nonzero().item(), 0)
        self.assertTrue(all(state['step'].item() == 2 for state in optimizer.state_dict()['state'].values()))
        self.assertEqual(self.parent['Optimizer:value_optimizer']['state'][6]['step'].item(), 1)

    def test_rejects_unsupported_network_contracts(self):
        for change in (dict(normalize=True), dict(memory={}), dict(hidden_units=256), dict(num_layers=3)):
            with self.subTest(change=change), self.assertRaises(ValueError):
                validate_network(dict(NETWORK, **change))

    def test_rejects_nonfinite_actor_critic_and_moments(self):
        for kind in ('Policy', 'Optimizer:critic', 'Optimizer:value_optimizer'):
            child = copy.deepcopy(self.parent)
            value = child[kind][INPUT_WEIGHT] if kind != 'Optimizer:value_optimizer' else child[kind]['state'][6]['exp_avg']
            value[0, 0] = float('nan')
            with self.subTest(kind=kind), self.assertRaises(ValueError):
                migrate(child, NETWORK)

    def test_rejects_optimizer_shape_association_and_invalid_moments(self):
        for change in ('association', 'missing', 'shape', 'negative', 'counter'):
            child = copy.deepcopy(self.parent)
            optimizer = child['Optimizer:value_optimizer']
            if change == 'association': optimizer['param_groups'][0]['params'][6:8] = [7, 6]
            if change == 'missing': del optimizer['state'][12]
            if change == 'shape': optimizer['state'][6]['exp_avg'] = torch.zeros(128, OBSERVATIONS)
            if change == 'negative': optimizer['state'][6]['exp_avg_sq'][0, 0] = -1
            if change == 'counter': optimizer['state'][6]['step'].fill_(0.5)
            with self.subTest(change=change), self.assertRaises(ValueError): migrate(child, NETWORK)

    def test_rejects_wrong_action_or_step_metadata(self):
        for change in ('actions', 'memory', 'step'):
            child = copy.deepcopy(self.parent)
            if change == 'actions': child['Policy']['discrete_act_size_vector'].fill_(258)
            if change == 'memory': child['Policy']['memory_size_vector'].fill_(64)
            if change == 'step': child['global_step']['_GlobalSteps__global_step'].fill_(-1)
            with self.subTest(change=change), self.assertRaises(ValueError): migrate(child, NETWORK)

    def test_refuses_overwrite_before_reading_parent(self):
        with tempfile.TemporaryDirectory() as folder, self.assertRaises(ValueError):
            prepare(Path(folder)/'missing.json', Path(folder), Path(folder)/'fixtures.json')

    def test_stale_parent_hash_never_creates_output(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            checkpoint = root/'BlockNationsSeatV2-45000.pt'; checkpoint.write_bytes(b'changed')
            config = root/'trainer.json'; config.write_text('{}')
            manifest = root/'manifest.json'
            manifest.write_text(json.dumps(dict(boardSize=7, rulesVersion=RULES, openingEconomyVersion=2,
                step=45000, checkpoint=str(checkpoint), trainerConfig=str(config), sha256='0'*64, trainerConfigSha256='0'*64)))
            with self.assertRaises(ValueError): prepare(manifest, root/'candidate', root/'fixtures.json')
            self.assertFalse((root/'candidate').exists())


class FixtureChecks(unittest.TestCase):
    def example(self):
        legacy = np.zeros(LEGACY_OBSERVATIONS, dtype='<f4'); legacy[-1] = 2
        expanded = np.concatenate((legacy, np.zeros(OBSERVATIONS-LEGACY_OBSERVATIONS, dtype='<f4'))); expanded[-1] = 3
        record = dict(boardSize=7, seat=0, firstSeat=1, source=-1, available=[258],
            legacy=base64.b64encode(legacy.tobytes()).decode(), expanded=base64.b64encode(expanded.tobytes()).decode())
        return dict(version=1, observationSchema=3, actionSchema=2, legacyObservationSize=LEGACY_OBSERVATIONS,
                    observationSize=OBSERVATIONS, actionCount=259, encoding='float32-le', rulesVersion=RULES, fixtures=[record])

    def check(self, value):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)/'fixtures.json'; path.write_text(json.dumps(value))
            return fixtures(path)

    def test_compatible_prefix(self):
        old, new, mask, _ = self.check(self.example())
        np.testing.assert_array_equal(old, new[:, :LEGACY_OBSERVATIONS])
        self.assertEqual(np.count_nonzero(mask), 1)

    def test_rejects_mismatched_contract_role_mask_and_prefix(self):
        for change in ('version', 'role', 'mask', 'prefix'):
            value = self.example()
            if change == 'version': value['observationSchema'] = 2
            if change == 'role': value['fixtures'][0]['firstSeat'] = 0
            if change == 'mask': value['fixtures'][0]['available'] = [0]
            if change == 'prefix': value['fixtures'][0]['legacy'] = base64.b64encode(np.ones(LEGACY_OBSERVATIONS, dtype='<f4').tobytes()).decode()
            with self.subTest(change=change), self.assertRaises(ValueError): self.check(value)


if __name__ == '__main__':
    unittest.main()
