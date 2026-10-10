"""Milestone ownership, actual frozen weights, retention and waiting failures."""
import hashlib
import json
import os
from pathlib import Path
import tempfile
import time
import unittest
from unittest.mock import patch

from mlagents.torch_utils import torch
from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import SimpleActor
from dotnet_environment import SPEC
from evaluate_milestone import checked_run, freeze, milestone
from playtest import latest_checkpoint
from training_contract import OPENING_ECONOMY_VERSION


class MilestoneTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name).resolve()
        self.run = self.root/'owned';self.run.mkdir()
        self.directory = self.run/'checkpoints'/self.run.name/'BlockNationsSeatV2';self.directory.mkdir(parents=True)
        self.manifest = dict(owner='BlockNations.LocalTraining.v1',schema=2,boardSize=7,
            observationSize=3120,actionCount=259,openingEconomyVersion=OPENING_ECONOMY_VERSION)
        (self.run/'run.json').write_text(json.dumps(self.manifest))
        self.network = dict(hidden_units=8,num_layers=1,normalize=False)
        self.config = json.dumps({'behaviors':{'BlockNationsSeatV2':{'network_settings':self.network}}}).encode()
        (self.run/'trainer.yaml').write_bytes(self.config)
        (self.run/'supervisor-status.json').write_text(json.dumps(dict(state='running',trainerPid=os.getpid())))
        self.suite = self.root/'suite.json';self.suite.write_text(json.dumps(dict(workerSha256='worker')))
        self.destination = self.root/'results'
        self.worker_patch = patch('evaluate_milestone.worker_digest',return_value='worker');self.worker_patch.start()
        actor = SimpleActor(SPEC.observation_specs,NetworkSettings(**self.network),SPEC.action_spec)
        self.policy = {'Policy':actor.state_dict()}

    def tearDown(self):
        self.worker_patch.stop();self.temp.cleanup()

    def pair(self,step):
        source = self.directory/f'BlockNationsSeatV2-{step}.pt';torch.save(self.policy,source)
        source.with_suffix('.onnx').write_bytes(b'export fixture')
        os.utime(source.with_suffix('.onnx'),(time.time()-10,time.time()-10))
        return source

    def call(self,**options):
        return milestone(self.run,150,self.suite,'worker',self.destination,**options)

    def test_pending_checkpoint_is_optional_without_weakening_manifest_checks(self):
        self.assertIsNone(latest_checkpoint(self.run,allow_pending=True))
        with self.assertRaises(ValueError): latest_checkpoint(self.run)
        (self.run/'run.json').write_text(json.dumps(dict(self.manifest,schema=1)))
        with self.assertRaises(ValueError): latest_checkpoint(self.run,allow_pending=True)

    def test_actual_weights_frozen_once_and_original_remains_independent(self):
        source = self.pair(200)
        manifest = freeze(self.run,source,150,self.config,'recipe')
        data = json.loads(manifest.read_text());saved = Path(data['checkpoint'])
        self.assertEqual(data['sha256'],hashlib.sha256(source.read_bytes()).hexdigest())
        self.assertEqual(data['step'],200);self.assertTrue(data['actorId'])
        source.write_bytes(b'changed original')
        self.assertEqual(hashlib.sha256(saved.read_bytes()).hexdigest(),data['sha256'])
        with self.assertRaises(ValueError): freeze(self.run,source,150,self.config,'recipe')

    def test_retired_pair_does_not_leave_a_partial_freeze(self):
        source = self.pair(200);source.with_suffix('.onnx').unlink()
        with self.assertRaises(FileNotFoundError): freeze(self.run,source,150,self.config,'recipe')
        self.assertEqual(list((self.run/'frozen-evaluations').iterdir()),[])
        self.pair(200)
        self.assertTrue(freeze(self.run,source,150,self.config,'recipe').is_file())

    def test_invalid_weights_fail_and_partial_directory_is_removed(self):
        source = self.pair(200);source.write_bytes(b'invalid checkpoint')
        with self.assertRaises(Exception): freeze(self.run,source,150,self.config,'recipe')
        self.assertEqual(list((self.run/'frozen-evaluations').iterdir()),[])

    def test_waits_for_first_complete_pair_and_evaluates_exactly_once(self):
        with patch('evaluate_milestone.time.sleep',side_effect=lambda _:self.pair(200)), \
                patch('evaluate_milestone.evaluate',return_value={'state':'completed'}) as evaluate:
            self.assertEqual(self.call(),{'state':'completed'})
            evaluate.assert_called_once()
            self.assertEqual(json.loads(evaluate.call_args.args[0].read_text())['step'],200)

    def test_copy_retention_race_reselects_without_duplicate_evaluation(self):
        self.pair(200)
        actual_freeze = freeze
        calls = 0
        def retired(*args):
            nonlocal calls
            calls += 1
            if calls==1:raise FileNotFoundError('retired immutable pair')
            return actual_freeze(*args)
        with patch('evaluate_milestone.freeze',side_effect=retired), \
                patch('evaluate_milestone.evaluate',return_value={'state':'completed'}) as evaluate:
            self.call();self.assertEqual(calls,2);evaluate.assert_called_once()

    def test_configuration_change_fails_without_evaluation(self):
        for target in [self.run/'trainer.yaml',self.run/'training-opponents.json',self.suite]:
            original = target.read_bytes() if target.exists() else None
            with patch('evaluate_milestone.time.sleep',side_effect=lambda _,p=target:p.write_bytes(b'changed')), \
                    patch('evaluate_milestone.evaluate') as evaluate:
                with self.assertRaises(ValueError):self.call()
                evaluate.assert_not_called()
            if original is None:target.unlink()
            else:target.write_bytes(original)

    def test_status_file_is_not_proof_of_live_training(self):
        with patch('evaluate_milestone.os.kill',side_effect=ProcessLookupError), \
                patch('evaluate_milestone.evaluate') as evaluate:
            with self.assertRaises(ProcessLookupError):self.call()
            evaluate.assert_not_called()

    def test_loaded_evaluator_code_drift_is_not_mislabeled_as_original_benchmark(self):
        changed = False
        actual_read = Path.read_bytes
        def contents(path):
            if changed and path.name == 'evaluate_policy.py':
                return b'changed evaluator source'
            return actual_read(path)
        def wake(_):
            nonlocal changed
            changed = True
        with patch.object(Path,'read_bytes',contents), patch('evaluate_milestone.time.sleep',side_effect=wake), \
                patch('evaluate_milestone.evaluate') as evaluate:
            with self.assertRaises(ValueError):self.call()
            evaluate.assert_not_called()

    def test_stopped_or_replaced_trainer_and_wait_deadline_fail(self):
        with patch('evaluate_milestone.time.sleep',side_effect=lambda _:(self.run/'supervisor-status.json').write_text(
                json.dumps(dict(state='running',trainerPid=os.getpid()+1)))):
            with self.assertRaises(RuntimeError):self.call()
        (self.run/'supervisor-status.json').write_text(json.dumps(dict(state='stopped',trainerPid=os.getpid())))
        with self.assertRaises(RuntimeError):self.call()
        (self.run/'supervisor-status.json').write_text(json.dumps(dict(state='running',trainerPid=os.getpid())))
        with self.assertRaises(TimeoutError):self.call(wait_seconds=.00001)

    def test_incompatible_and_linked_run_or_occupied_destination_rejected(self):
        for key,value in [('boardSize',6),('owner','other'),('openingEconomyVersion',1)]:
            (self.run/'run.json').write_text(json.dumps(dict(self.manifest,**{key:value})))
            with self.assertRaises(ValueError):checked_run(self.run)
        (self.run/'run.json').write_text(json.dumps(self.manifest))
        linked=self.root/'link';linked.symlink_to(self.run)
        with self.assertRaises(ValueError):checked_run(linked)
        self.destination.mkdir()
        with self.assertRaises(ValueError):self.call()


if __name__=='__main__':unittest.main()
