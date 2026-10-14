import hashlib
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch

from evaluate_policy import FrozenActor
from model_catalog import OWNER, discover, resolve
from playtest import PlaytestSession, atomic_json
from supervisor import ROOT_MARKER, trainer_config
from training_contract import CORE_THREE_RULES
from training_selector import Selector


class SelectorTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name).resolve()
        atomic_json(self.root/ROOT_MARKER,dict(owner='BlockNations.LocalTraining.v1'))
        (self.root/'runs').mkdir();(self.root/'benchmarks').mkdir()
        self.fog=self.make_run('fog-run',0);self.full=self.make_run('full-run',3)
        self.session=self.make_run('session',0)
        marker=json.loads((self.session/'run.json').read_text());marker['experiment']='training-selector-session';atomic_json(self.session/'run.json',marker)
        self.config=dict(owner=OWNER,player='/Training.app',sessionRun=str(self.session),viewerRun=self.fog.name,runs=[
            dict(id=r.name,command=['python','supervisor.py','--root',str(self.root),'--run-id',r.name,'--resume']) for r in (self.fog,self.full)])
    def tearDown(self):self.temp.cleanup()
    def make_run(self,name,mask):
        run=self.root/'runs'/name;run.mkdir()
        atomic_json(run/'run.json',dict(owner='BlockNations.LocalTraining.v1',schema=2,boardSize=7,seed=42,
            rulesVersion=CORE_THREE_RULES,diagnosticOnly=True,diagnosticFullVisionSeats=mask,weightTransfer=dict(parentStep=100)))
        atomic_json(run/'trainer.yaml',trainer_config(200,50));return run
    def checkpoint(self,run,step):
        folder=run/'checkpoints'/run.name/'BlockNationsSeatV2';folder.mkdir(parents=True,exist_ok=True)
        path=folder/f'BlockNationsSeatV2-{step}.pt';path.write_bytes(('weights'+str(step)).encode())
        path.with_suffix('.onnx').write_bytes(b'complete');os.utime(path.with_suffix('.onnx'),(1,1));return path
    def test_initialization_is_not_relabelled_as_a_trained_full_vision_model(self):
        self.checkpoint(self.full,100);cat=discover(self.root)
        self.assertEqual(cat['models'],[])
        self.assertEqual(next(r for r in cat['runs'] if r['id']=='full-run')['trainingVision'],'FullVision')
        self.checkpoint(self.full,101);model=discover(self.root)['models'][0]
        self.assertEqual(model['trainingVision'],'FullVision');self.assertEqual(model['step'],101)
    def test_changed_checkpoint_stale_id_and_foreign_paths_are_rejected(self):
        path=self.checkpoint(self.fog,101);identifier=discover(self.root)['models'][0]['id']
        path.write_bytes(b'changed longer weights')
        with self.assertRaises(ValueError):resolve(self.root,identifier)
        with self.assertRaises(ValueError):resolve(self.root,'../foreign.pt')
        path.unlink();path.symlink_to(self.root/'foreign.pt');(self.root/'foreign.pt').write_bytes(b'foreign')
        self.assertEqual(discover(self.root)['models'],[])
    @patch('playtest.subprocess.Popen')
    def test_explicit_model_and_match_visibility_are_frozen_and_echo_checked(self,launch):
        path=self.checkpoint(self.full,101);model=discover(self.root)['models'][0]
        actor=Mock(sha256=model['checkpointSha256'])
        process=Mock();process.poll.return_value=None;launch.return_value=process
        session=PlaytestSession(self.session,Path('/Training.app'),training_active=False)
        with patch('evaluate_policy.FrozenActor',return_value=actor):
            session.start(dict(requestId='a'*32,modelId=model['id'],matchVision='Fog',difficulty='Hard',firstSeat=1),{})
        self.assertEqual(session.state['trainingVision'],'FullVision');self.assertEqual(session.state['matchVision'],'Fog')
        self.assertEqual((self.session/'playtest/frozen.pt').read_bytes(),path.read_bytes())
        args=launch.call_args.args[0]
        self.assertEqual(args[args.index('--training-match-vision')+1],'Fog')
        self.assertIn('trained FullVision',args[args.index('--training-policy-version')+1])
        self.assertNotIn('self_play',json.loads((self.session/'playtest/inference.yaml').read_text())['behaviors']['BlockNationsSeatV2'])
        atomic_json(self.session/'playtest/arena-status.json',dict(trainerConnected=True,matchVision='FullVision',round=1))
        session.poll({});self.assertEqual(session.state['state'],'error');self.assertTrue(path.exists())
    def test_training_choice_waits_for_power_and_preserves_previous_controller(self):
        atomic_json(self.root/'power-controller.json',dict(enabled=True,runId='fog-run'))
        selector=Selector(self.root,self.config)
        selector.request(dict(requestId='b'*32,op='start',runId='full-run'))
        with patch('training_selector.read_power',return_value=(True,18)),patch('training_selector.subprocess.Popen') as launch:
            selector.tick();launch.assert_not_called()
        self.assertEqual(selector.state['desiredRun'],'full-run')
        self.assertFalse(json.loads((self.root/'power-controller.json').read_text())['enabled'])
        self.assertTrue(json.loads((self.root/'training-selector-previous-power.json').read_text())['enabled'])
        with patch('training_selector.read_power',return_value=(True,80)),patch('training_selector.subprocess.Popen',return_value=Mock()) as launch:
            selector.tick();self.assertEqual(launch.call_args.args[0],self.config['runs'][1]['command'])
        if selector.training_log:selector.training_log.close()
    def test_switch_requests_a_saved_stop_and_preserves_unrelated_supervisors(self):
        selector=Selector(self.root,self.config)
        with patch.object(selector,'active',return_value='fog-run'):
            selector.request(dict(requestId='c'*32,op='start',runId='full-run'))
        self.assertTrue((self.fog/'stop.request').exists())
        with patch.object(selector,'active',return_value='unmanaged'):
            with self.assertRaises(ValueError):selector.request(dict(requestId='d'*32,op='start',runId='full-run'))

    def test_failed_save_blocks_switch_instead_of_starting_another_learner(self):
        selector=Selector(self.root,self.config)
        selector.state.update(desiredRun='full-run',stoppingRun='fog-run')
        atomic_json(self.fog/'supervisor-status.json',dict(state='failed',exitCode=1))
        with patch('training_selector.read_power',return_value=(True,80)),patch('training_selector.subprocess.Popen') as launch:
            selector.tick();launch.assert_not_called()
        self.assertEqual(selector.state['desiredRun'],'')
        self.assertIn('successful save',selector.state['error'])

    def test_restarted_selector_does_not_restart_a_finished_adopted_learner(self):
        selector=Selector(self.root,self.config)
        selector.state.update(desiredRun='full-run',observedRun='full-run')
        atomic_json(self.full/'supervisor-status.json',dict(state='stopped',exitCode=0))
        with patch('training_selector.read_power',return_value=(True,80)),patch('training_selector.subprocess.Popen') as launch:
            selector.tick();launch.assert_not_called()
        self.assertEqual(selector.state['desiredRun'],'')
