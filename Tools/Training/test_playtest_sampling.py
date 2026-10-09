import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch

import numpy as np
from playtest_sampling import sampling_weights, install
from playtest_controller import freeze, main
from supervisor import prepare_root, trainer_config


class DifficultySamplingTests(unittest.TestCase):
    def test_profiles_preserve_masks_and_differ_in_variation(self):
        p = np.zeros((2,259)); p[:,[1,4,10]]=[.6,.3,.1]
        weights = {d:sampling_weights(p,d) for d in ['Easy','Medium','Hard']}
        for w in weights.values():
            np.testing.assert_allclose(w.sum(axis=1),1)
            self.assertTrue((w[p==0]==0).all())
        self.assertLess(weights['Easy'][0,1],weights['Medium'][0,1])
        self.assertEqual(weights['Hard'][0,1],1)
        self.assertGreater(weights['Easy'][0,10],weights['Medium'][0,10])

    def test_near_equal_hard_choices_still_vary(self):
        p=np.zeros((1,259)); p[0,[2,3]]=[.51,.49]
        w=sampling_weights(p,'Hard')
        self.assertGreater(w[0,3],.4)

    def test_invalid_or_empty_probabilities_are_rejected(self):
        for value in [np.zeros((1,259)),np.ones((1,12)),np.full((1,259),np.nan),np.full((1,259),-1)]:
            with self.assertRaises(ValueError):sampling_weights(value,'Medium')
        with self.assertRaises(ValueError):sampling_weights(np.ones((1,259)),'Maximum')

    def test_actual_sdk_sampler_keeps_illegal_actions_out_without_changing_logits(self):
        from mlagents.torch_utils import torch
        from mlagents.trainers.torch_entities.distributions import CategoricalDistInstance
        original=CategoricalDistInstance.sample
        try:
            torch.manual_seed(42)
            logits=torch.full((20,259),-1e10); logits[:,[1,4]] = 0
            distribution=CategoricalDistInstance(logits)
            before=distribution.probs.clone()
            for difficulty in ['Easy','Medium','Hard']:
                install(difficulty)
                actions=distribution.sample().numpy().flatten()
                self.assertTrue(set(actions).issubset({1,4}))
                self.assertEqual(set(actions),{1,4})
                torch.testing.assert_close(distribution.probs,before)
        finally:CategoricalDistInstance.sample=original


class OfflineControllerTests(unittest.TestCase):
    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory()
        self.root=Path(self.temporary.name)/'training'; prepare_root(self.root)
        self.run=self.root/'runs'/'saved'; self.run.mkdir()
        (self.run/'run.json').write_text(json.dumps(dict(owner='BlockNations.LocalTraining.v1',schema=2,boardSize=7,
                  observationSize=3120,actionCount=259,seed=42)))
        (self.run/'trainer.yaml').write_text(json.dumps(trainer_config(100000,5000)))
        directory=self.run/'checkpoints'/self.run.name/'BlockNationsSeatV2'; directory.mkdir(parents=True)
        self.source=directory/'BlockNationsSeatV2-123.pt'; self.source.write_bytes(b'frozen weights')
        export=self.source.with_suffix('.onnx');export.write_bytes(b'export');os.utime(export,(1,1))
        (self.run/'supervisor-status.json').write_text(json.dumps(dict(state='stopped',checkpointCount=1)))
        self.app=Path(self.temporary.name)/'Training.app'; (self.app/'Contents').mkdir(parents=True)
        (self.app/'Contents/Info.plist').write_bytes(b'fixture')

    def tearDown(self):self.temporary.cleanup()

    def test_frozen_comparison_copy_survives_rolling_source_removal(self):
        frozen=freeze(self.run)
        self.source.unlink()
        self.assertEqual(frozen.read_bytes(),b'frozen weights')
        manifest=json.loads((frozen.parent/'manifest.json').read_text())
        self.assertEqual(manifest['difficulties'],['Easy','Medium','Hard'])
        self.assertEqual(manifest['boardSize'],7)

    def test_linked_frozen_target_is_rejected_even_when_destination_is_missing(self):
        directory=self.run/'frozen-playtests'/self.source.stem; directory.mkdir(parents=True)
        target=directory/self.source.name
        target.symlink_to(self.root/'not-a-checkpoint')
        with self.assertRaises(ValueError):freeze(self.run)
        self.assertTrue(target.is_symlink())

    def test_closed_offline_viewer_starts_no_learner_and_releases_lock(self):
        config=(self.run/'trainer.yaml').read_bytes()
        viewer=Mock(); viewer.error='';viewer.process.poll.return_value=0
        argv=['playtest_controller.py','--run',str(self.run),'--env',str(self.app)]
        with patch('playtest_controller.signal.signal'),patch('playtest_controller.argparse._sys.argv',argv), \
                patch('playtest_controller.ViewerSession',return_value=viewer),patch('playtest.subprocess.Popen') as launch:
            main()
        launch.assert_not_called()
        viewer.close.assert_called_once()
        self.assertEqual((self.run/'trainer.yaml').read_bytes(),config)
        self.assertEqual(self.source.read_bytes(),b'frozen weights')
        self.assertFalse((self.root/'supervisor.lock').exists())
        status=json.loads((self.run/'supervisor-status.json').read_text())
        self.assertEqual(status['state'],'stopped');self.assertEqual(status['stopReason'],'playtest_closed')
        self.assertEqual(status['trainerPid'],0)

    def test_live_supervisor_lock_is_never_replaced(self):
        lock=self.root/'supervisor.lock'; lock.write_text(str(os.getpid()))
        argv=['playtest_controller.py','--run',str(self.run),'--env',str(self.app)]
        with patch('playtest_controller.argparse._sys.argv',argv),self.assertRaises(ValueError):main()
        self.assertEqual(lock.read_text(),str(os.getpid()))


if __name__=='__main__':unittest.main()
