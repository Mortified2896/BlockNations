"""Retained learned opponents: provenance, fair masks, ownership and resume."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import uuid

import numpy as np
from mlagents.torch_utils import torch
from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import SimpleActor
from mlagents_envs.base_env import DecisionSteps, ActionTuple

from dotnet_environment import SPEC, NAMES, DotNetEnvironment
from frozen_policy import FrozenPolicy
from match_rating import policy_id
from supervisor import trainer_config
from training_contract import RULES_VERSION, OPENING_ECONOMY_VERSION
from training_opponents import TrainingOpponents, load_recipe, entry_id, challenger_ids

WORKER = Path(os.environ.get('BLOCKNATIONS_TEST_WORKER',Path(__file__).resolve().parents[2]/'Build/TrainingWorker/BlockNations.TrainingWorker.dll'))


class RetainedOpponentTests(unittest.TestCase):
    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory(prefix='bn-retained-opponents-')
        self.run=Path(self.temporary.name)/'league';self.run.mkdir()
        (self.run/'run.json').write_text(json.dumps(dict(owner='BlockNations.LocalTraining.v1',schema=2,
            boardSize=7,openingEconomyVersion=OPENING_ECONOMY_VERSION)))
        folder=self.run/'frozen-evaluations'/'fixture';folder.mkdir(parents=True)
        self.checkpoint=folder/'BlockNationsSeatV2-100.pt'
        self.network=dict(hidden_units=8,num_layers=1,normalize=False)
        actor=SimpleActor(SPEC.observation_specs,NetworkSettings(**self.network),SPEC.action_spec)
        torch.save({'Policy':actor.state_dict()},self.checkpoint)
        self.config=folder/'trainer.yaml'
        self.config.write_text(json.dumps({'behaviors':{'BlockNationsSeatV2':{'network_settings':self.network}}}))
        self.entry=dict(kind='frozen-policy',weight=1,sampling='Hard',samplingSettings=[.25,.7],actorId=policy_id(actor.state_dict()),
            checkpoint=str(self.checkpoint.relative_to(self.run)),
            trainerConfig=str(self.config.relative_to(self.run)),
            checkpointSha256=hashlib.sha256(self.checkpoint.read_bytes()).hexdigest(),
            configSha256=hashlib.sha256(self.config.read_bytes()).hexdigest())
        (folder/'manifest.json').write_text(json.dumps(dict(sha256=self.entry['checkpointSha256'],
            rulesVersion=RULES_VERSION,boardSize=7,openingEconomyVersion=OPENING_ECONOMY_VERSION)))
        self.recipe=dict(version=2,rulesVersion=RULES_VERSION,boardSize=7,
            openingEconomyVersion=OPENING_ECONOMY_VERSION,probability=.5,opponents=[self.entry])
        self.write_recipe()

    def tearDown(self):self.temporary.cleanup()

    def write_recipe(self,recipe=None):
        (self.run/'training-opponents.json').write_text(json.dumps(recipe or self.recipe))

    def test_retained_checkpoint_and_sampling_identity_survive_recreating_pool(self):
        pool=TrainingOpponents(self.run,42);restored=TrainingOpponents(self.run,42)
        self.assertEqual(pool.identifier,restored.identifier)
        self.assertEqual(challenger_ids(pool.recipe),[entry_id(self.entry)])
        stats=[dict(worker=i,match=i+1,trainerResets=0) for i in range(4)]
        policy=dict(learningSeat=0,learner='learner',opponent='rolling')
        self.assertEqual(pool.selected(stats,policy),restored.selected(stats,policy))
        changed=dict(self.entry,sampling='Policy',samplingSettings=None)
        self.assertNotEqual(entry_id(changed),entry_id(self.entry))
        self.assertEqual(entry_id(changed),self.entry['actorId'])

    def test_changed_files_wrong_provenance_and_unbounded_recipes_are_rejected(self):
        for field,value in [('probability',.51),('boardSize',6),('openingEconomyVersion',1),('opponents',[]),
                            ('opponents',[dict(self.entry,sampling='Maximum')]),
                            ('opponents',[dict(self.entry,samplingSettings=[.5,.7])]),
                            ('opponents',[dict(self.entry,weight=float('nan'))]),
                            ('opponents',[self.entry,self.entry])]:
            self.write_recipe(dict(self.recipe,**{field:value}))
            with self.assertRaises(ValueError):load_recipe(self.run)
        self.write_recipe();self.checkpoint.write_bytes(b'changed checkpoint')
        with self.assertRaises(ValueError):load_recipe(self.run)

    def test_retained_file_cannot_escape_owned_directory(self):
        self.recipe['opponents']=[dict(self.entry,checkpoint='../outside.pt')]
        self.write_recipe()
        with self.assertRaises(ValueError):load_recipe(self.run)
        self.recipe['opponents']=[dict(self.entry,trainerConfig=str(self.config))]
        self.write_recipe()
        with self.assertRaises(ValueError):load_recipe(self.run)

    def test_each_learning_seat_is_untouched_and_only_legal_opponent_actions_are_replaced(self):
        for learning_seat in (0,1):
            policy=dict(learningSeat=learning_seat,learner='learner',opponent='rolling')
            stats=[dict(worker=i,match=1,trainerResets=0) for i in range(4)]
            seed=next(seed for seed in range(100) if TrainingOpponents(self.run,seed).selected(stats,policy))
            pool=TrainingOpponents(self.run,seed)
            opponent_seat=1-learning_seat
            ids=np.arange(opponent_seat,8,2,dtype=np.int32)
            mask=np.ones((4,259),dtype=np.bool_);mask[:,258]=False
            steps=DecisionSteps([np.zeros((4,3120),dtype=np.float32)],np.zeros(4),ids,[mask],
                                np.zeros(4,dtype=np.int32),np.zeros(4,dtype=np.float32))
            class Environment:
                workers=4
                behavior_specs={name:SPEC for name in NAMES}
                def get_steps(self,name):
                    assert name==NAMES[opponent_seat]
                    return steps,None
            env=Environment();env.stats=stats
            original={agent:17 for agent in range(8)}
            replaced,assignments=pool.prepare(env,original,policy)
            selected=pool.selected(stats,policy)
            self.assertEqual(original,{agent:17 for agent in range(8)})
            for worker in range(4):
                self.assertEqual(replaced[worker*2+learning_seat],17)
                self.assertEqual(replaced[worker*2+opponent_seat],258 if worker in selected else 17)
                self.assertEqual(assignments[worker]['opponent'],entry_id(self.entry) if worker in selected else 'rolling')
            self.assertEqual(pool.decisions_by_policy[entry_id(self.entry)],len(selected))
            self.assertTrue(all(not p.requires_grad for actor in pool.actors.values() for p in actor.actor.parameters()))

    def test_changed_checkpoint_is_rejected_before_inference(self):
        with self.assertRaises(ValueError):
            FrozenPolicy(self.checkpoint,self.network,SPEC,'0'*64)
        with self.assertRaises(ValueError):
            FrozenPolicy(self.checkpoint,self.network,SPEC,self.entry['checkpointSha256'],'0'*24)

    def test_repackaged_checkpoint_preserves_actor_identity(self):
        actor=FrozenPolicy(self.checkpoint,self.network,SPEC)
        checkpoint=self.checkpoint.parent/'BlockNationsSeatV2-101.pt'
        torch.save({'Policy':actor.actor.state_dict(),'extra_optimizer_metadata':42},checkpoint)
        repackaged=FrozenPolicy(checkpoint,self.network,SPEC)
        self.assertNotEqual(repackaged.sha256,actor.sha256)
        self.assertEqual(repackaged.actor_id,actor.actor_id)
        self.assertEqual(entry_id(dict(self.entry,sampling='Policy',samplingSettings=None)),actor.actor_id)

    def test_changed_config_or_wrong_rules_manifest_is_rejected(self):
        original=self.config.read_bytes()
        self.config.write_bytes(original+b' ')
        with self.assertRaises(ValueError):load_recipe(self.run)
        self.config.write_bytes(original)
        manifest=self.checkpoint.parent/'manifest.json'
        data=json.loads(manifest.read_text());data['rulesVersion']='old-rules'
        manifest.write_text(json.dumps(data))
        with self.assertRaises(ValueError):load_recipe(self.run)

    def test_weighted_league_keeps_distinct_ids_and_reproducible_assignments(self):
        tactical=dict(kind='fair-tactician',workBudget=512,weight=1)
        self.recipe['opponents']=[tactical,dict(self.entry,weight=3)]
        self.write_recipe()
        pool=TrainingOpponents(self.run,42);restored=TrainingOpponents(self.run,42)
        policy=dict(learningSeat=0,learner='learner',opponent='rolling')
        counts=[0,0]
        for match in range(1000):
            stats=[dict(worker=0,match=match,trainerResets=0)]
            self.assertEqual(pool.selected(stats,policy),restored.selected(stats,policy))
            index=pool.assignments[0][1]
            self.assertEqual(index,restored.assignments[0][1])
            if index>=0:counts[index]+=1
        self.assertTrue(440 < sum(counts) < 560)
        self.assertTrue(2 < counts[1]/counts[0] < 4)
        self.assertEqual(len(set(challenger_ids(pool.recipe))),2)

    @unittest.skipUnless(WORKER.is_file(),'Build the C# worker first.')
    def test_real_worker_records_retained_identity_without_rejected_actions(self):
        policy=dict(learningSeat=0,learner='learner',opponent='rolling')
        env=DotNetEnvironment(WORKER,self.run,4,42,7,False,2,uuid.uuid4().hex,lambda:policy)
        try:
            env.reset()
            for _ in range(20):
                for name in NAMES:
                    decisions,_=env.get_steps(name)
                    env.set_actions(name,ActionTuple(discrete=np.full((len(decisions),1),258,dtype=np.int32)))
                env.step()
            self.assertGreater(env.opponents.decisions,0)
            self.assertEqual(sum(s['rejections'] for s in env.stats),0)
            events=[json.loads(line) for line in (self.run/'match-events.jsonl').read_text().splitlines()]
            self.assertTrue(any((e.get('context') or {}).get('opponent')==entry_id(self.entry) for e in events))
        finally:env.close()

    @unittest.skipUnless(WORKER.is_file() and os.environ.get('BLOCKNATIONS_TEST_LEAGUE_PPO'),
                         'Opt-in actual PPO checkpoint/resume integration.')
    def test_actual_ppo_continues_optimizer_with_retained_league_after_resume(self):
        config=self.run/'trainer.yaml';config.write_text(json.dumps(trainer_config(4096,2048)))
        env=dict(os.environ,BLOCKNATIONS_RATING_RUN=str(self.run),BLOCKNATIONS_RATING_BOARD='7',
            BLOCKNATIONS_RATING_SESSION=uuid.uuid4().hex,BLOCKNATIONS_SIMULATION_WORKER=str(WORKER),
            BLOCKNATIONS_PARALLEL_GAMES='4',BLOCKNATIONS_SIMULATION_SEED='42',
            BLOCKNATIONS_SIMULATION_CURRICULUM='false',BLOCKNATIONS_SIMULATION_DISTANCE='2',
            BLOCKNATIONS_OPENING_ECONOMY_VERSION=str(OPENING_ECONOMY_VERSION),OMP_NUM_THREADS='2',MKL_NUM_THREADS='2')
        command=[sys.executable,str(Path(__file__).with_name('rated_training.py')),str(config),'--run-id',self.run.name,
                 '--results-dir',str(self.run/'checkpoints'),'--seed','42','--torch-device','cpu']
        def train(resume=False):
            log=self.run/('resume.log' if resume else 'initial.log')
            with log.open('w') as output:
                result=subprocess.run(command+(['--resume'] if resume else []),env=env,stdout=output,stderr=subprocess.STDOUT,timeout=90)
            self.assertEqual(result.returncode,0,log.read_text()[-6000:])
        train()
        checkpoint=self.run/'checkpoints'/self.run.name/'BlockNationsSeatV2'/'checkpoint.pt'
        original=torch.load(checkpoint,map_location='cpu')
        plan=json.loads(config.read_text());plan['behaviors']['BlockNationsSeatV2']['max_steps']=8192
        config.write_text(json.dumps(plan));env['BLOCKNATIONS_RATING_SESSION']=uuid.uuid4().hex
        train(resume=True)
        continued=torch.load(checkpoint,map_location='cpu')
        old=max(float(v['step']) for v in original['Optimizer:value_optimizer']['state'].values())
        new=max(float(v['step']) for v in continued['Optimizer:value_optimizer']['state'].values())
        self.assertGreater(new,old)
        status=json.loads((self.run/'arena-status.json').read_text())
        self.assertGreater(status['challengerDecisionsByPolicy'].get(entry_id(self.entry),0),0)
        self.assertEqual(status['rejections'],0)
        self.assertEqual(hashlib.sha256(self.checkpoint.read_bytes()).hexdigest(),self.entry['checkpointSha256'])
        print(f'Retained league PPO/resume: Adam {old} -> {new}; frozen identity {entry_id(self.entry)}')


if __name__=='__main__':unittest.main()
