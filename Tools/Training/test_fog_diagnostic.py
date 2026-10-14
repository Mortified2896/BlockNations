import json
import os
from pathlib import Path
import tempfile
import unittest
import uuid

import numpy as np
from mlagents_envs.base_env import ActionTuple

from dotnet_environment import DotNetEnvironment
from evaluate_suite import frozen_actor
from evaluate_policy import MatchedArenaRandom
from fog_diagnostic import paired_outcomes
from information_study_runtime import save_environment, resume_environment
from training_contract import CORE_THREE_RULES

WORKER=Path(os.environ.get('BLOCKNATIONS_TEST_WORKER','/not-built'))


class FogDiagnosticTests(unittest.TestCase):
    def test_other_arenas_cannot_consume_a_matched_games_sampling_stream(self):
        isolated=np.random.default_rng([73,1,2,0])
        batch=MatchedArenaRandom([np.random.default_rng([73,1,0,0]),np.random.default_rng([73,1,2,0])])
        batch.choice(259)
        self.assertEqual(batch.choice(259),isolated.choice(259))
    def test_pairing_preserves_losses_and_turn_limits(self):
        def pack(winner, interrupted=False):
            return dict(cases=[dict(name='fixed')],results=[dict(seed=73,records=[
                dict(firstSeat=0,candidateSeat=1,worker=0,game=0,winner=winner,interrupted=interrupted)])])
        transitions=paired_outcomes(pack(0),pack(1))
        self.assertEqual(transitions['loss-to-win'],1);self.assertEqual(sum(transitions.values()),1)
        self.assertEqual(paired_outcomes(pack(0,True),pack(1))['limit-to-win'],1)
        altered=pack(1);altered['results'][0]['seed']=74
        with self.assertRaises(ValueError):paired_outcomes(pack(0),altered)

    def test_diagnostic_frozen_manifest_is_rejected_before_normal_inference(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'manifest.json';path.write_text(json.dumps(dict(diagnosticOnly=True)))
            with self.assertRaisesRegex(ValueError,'Diagnostic'):frozen_actor(path)

    def test_power_resume_retains_full_vision_assignment(self):
        with tempfile.TemporaryDirectory() as directory:
            run=Path(directory)
            save_environment(run,dict(BLOCKNATIONS_RATING_RUN=str(run),BLOCKNATIONS_HUMAN_IMITATION='disabled',
                BLOCKNATIONS_DIAGNOSTIC_FULL_VISION_SEATS='3'))
            self.assertEqual(resume_environment(run)['BLOCKNATIONS_DIAGNOSTIC_FULL_VISION_SEATS'],'3')

    @unittest.skipUnless(WORKER.is_file(),'Build diagnostic worker first.')
    def test_actual_worker_reveals_only_explicit_seats_and_executes_masked_actions(self):
        for mask in (0,1,2,3):
            for seat in (0,1):
                with tempfile.TemporaryDirectory() as directory:
                    env=DotNetEnvironment(WORKER,directory,1,73,7,False,2,uuid.uuid4().hex,
                        first_seat=seat,rules_version=CORE_THREE_RULES,diagnostic_full_vision_seats=mask)
                    try:
                        env.reset()
                        decisions,_=env.get_steps(env.names[seat])
                        tiles=decisions.obs[0][0,:121*24].reshape(121,24)
                        count=np.count_nonzero(tiles[:,2])
                        self.assertEqual(count,49 if mask&(1<<seat) else 9)
                        for _ in range(60):
                            for name in env.names:
                                decisions,_=env.get_steps(name)
                                if len(decisions):
                                    choices=np.array([[np.flatnonzero(~row)[0]] for row in decisions.action_mask[0]],dtype=np.int32)
                                    env.set_actions(name,ActionTuple(discrete=choices))
                            env.step()
                        self.assertFalse(env.failure)
                    finally:env.close()

    def test_invalid_or_curriculum_full_vision_is_rejected(self):
        for mask in (-1,4,True):
            with self.assertRaises(ValueError):
                DotNetEnvironment(WORKER,'.',1,73,7,False,2,uuid.uuid4().hex,diagnostic_full_vision_seats=mask)
        with self.assertRaises(ValueError):
            DotNetEnvironment(WORKER,'.',1,73,11,True,2,uuid.uuid4().hex,diagnostic_full_vision_seats=3)
