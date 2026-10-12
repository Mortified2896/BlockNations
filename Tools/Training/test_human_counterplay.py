import unittest
import tempfile
from pathlib import Path
import numpy as np
from human_counterplay import fit, prepare


class HumanCounterplayTests(unittest.TestCase):
    def test_unowned_root_and_escaping_name_fail_before_preparation(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            with self.assertRaisesRegex(ValueError, 'owned training root'):
                prepare(root, root/'runs/source', root/'runs/source/manifest.json', root/'worker', '../escape')
            self.assertFalse((root/'runs').exists())

    def test_fit_teaches_a_masked_move_without_touching_parent_or_illegal_choices(self):
        from mlagents.torch_utils import torch
        from mlagents.trainers.policy.torch_policy import TorchPolicy
        from mlagents.trainers.settings import NetworkSettings
        from mlagents.trainers.torch_entities.networks import SimpleActor
        from dotnet_environment import SPEC
        torch.set_num_threads(1)
        policy = TorchPolicy(42, SPEC, NetworkSettings(hidden_units=16, num_layers=1), SimpleActor,
                             dict(conditional_sigma=False, tanh_squash=False))
        parent = {k:v.clone() for k,v in policy.actor.state_dict().items()}
        obs = np.zeros((4,3120), np.float32);obs[:,-1]=2
        masks = np.zeros((4,259),np.float32);masks[:,[1,2,258]]=1
        examples = [('fixture', obs, masks, np.ones(4,dtype=np.int64))]
        result = fit(policy, examples, batches=32)
        self.assertLess(result['finalLoss'], result['initialLoss'])
        self.assertTrue(any(not torch.equal(parent[k],v) for k,v in policy.actor.state_dict().items()))
        self.assertTrue(all(torch.isfinite(v).all() for v in policy.actor.state_dict().values()))
        with self.assertRaises(ValueError):fit(policy, [], batches=32)
        with self.assertRaises(ValueError):fit(policy, examples, batches=1025)
