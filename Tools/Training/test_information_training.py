import json
import os
from pathlib import Path
import tempfile
import unittest
import uuid

import numpy as np
from mlagents_envs.base_env import ActionTuple
from dotnet_environment import DotNetEnvironment
from model_contract import SIZES, spec, names, prefix
from weakness_sampling import weights, apply

WORKER=Path(os.environ.get('BLOCKNATIONS_TEST_WORKER','/not-built'))


class InformationContracts(unittest.TestCase):
    def test_prefix_projection_refuses_invented_observations(self):
        x=np.arange(3259,dtype=np.float32)[None,:]
        np.testing.assert_array_equal(prefix(x,3,2),x[:,:3120])
        with self.assertRaises(ValueError):prefix(x,2,3)
        with self.assertRaises(ValueError):prefix(x,4,2)

    def test_weakness_weights_preserve_coverage_and_cap(self):
        w=weights([100,0,0,0,0,0,0,0],[100]*8)
        self.assertAlmostEqual(sum(w),1);self.assertLessEqual(max(w),.25+1e-12)
        self.assertGreaterEqual(min(w),.5/8);self.assertGreater(w[0],w[1])
        with self.assertRaises(ValueError):weights([1]*8,[0]*8)

    @unittest.skipUnless(WORKER.is_file(),'Build actual shared worker first.')
    def test_actual_worker_versions_legal_actions_and_metrics(self):
        for version in (2,3,4):
            with tempfile.TemporaryDirectory() as directory:
                env=DotNetEnvironment(WORKER,directory,4,42,7,False,2,uuid.uuid4().hex,observation_version=version)
                try:
                    env.reset()
                    for _ in range(80):
                        for seat,name in enumerate(env.names):
                            decisions,_=env.get_steps(name)
                            self.assertEqual(decisions.obs[0].shape[1],SIZES[version])
                            if not len(decisions):continue
                            choices=np.array([[np.flatnonzero(~row)[0]] for row in decisions.action_mask[0]],dtype=np.int32).reshape(-1,1)
                            env.set_actions(name,ActionTuple(discrete=choices))
                        env.step()
                    self.assertTrue(all(s['schema']==version for s in env.stats))
                    env._publish_status(force=True)
                    diagnostic=json.loads((Path(directory)/'decision-diagnostics.json').read_text())
                    self.assertEqual(diagnostic['observationVersion'],version)
                    self.assertGreater(sum(w['rootSelections'] for w in diagnostic['workers']),0)
                finally:env.close()

    @unittest.skipUnless(WORKER.is_file(),'Build actual shared worker first.')
    def test_episode_starts_cannot_enter_balanced_evaluation(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(ValueError):
                DotNetEnvironment(WORKER,directory,4,42,7,False,2,uuid.uuid4().hex,first_seat=0,episode_starts=Path(directory)/'episode-starts.json')
