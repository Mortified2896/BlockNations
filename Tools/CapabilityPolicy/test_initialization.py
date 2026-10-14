import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

import numpy as np
import torch
from mlagents.trainers.settings import NetworkSettings, RunOptions
from mlagents.trainers.policy.torch_policy import TorchPolicy
from mlagents.trainers.ppo.optimizer_torch import TorchPPOOptimizer

sys.path.insert(0,str(Path(__file__).parents[1]/'Training'))
from model_contract import spec
from training_contract import CORE_THREE_RULES
from policy import CapabilityActor, ARCHITECTURE
from train import recipe
from trainer import register
from artifacts import checkpoint
import initialization
import corpus
from distill import divergence

torch.set_num_threads(1)


def arrays():
    count=128
    values=np.zeros((count,4712),np.float32)
    values[:,3119]=2; values[:,3258]=3; values[:,-1]=4
    values[:,3257]=np.arange(count)%2; values[:,3112]=np.arange(count)%2
    allowed=np.zeros((count,259),np.bool_); allowed[:,0]=allowed[:,-1]=True
    target=np.zeros((count,259),np.float32); target[:,0]=.75; target[:,-1]=.25
    group=np.repeat(np.arange(8,dtype=np.uint64),16)
    return dict(observations=values,allowed=allowed,teacherProbabilities=target,
                teacherValues=np.ones(count,np.float32)*.1,validation=group<2,group=group)


def prepared(folder):
    folder.mkdir(parents=True)
    register(); contract=spec(4)
    policy=TorchPolicy(209,contract,NetworkSettings(hidden_units=32,num_layers=2),CapabilityActor,{})
    settings=RunOptions.from_dict(recipe(8192,32)).behaviors['BlockNationsSeatV4']
    optimizer=TorchPPOOptimizer(policy,settings)
    modules=dict(policy.get_modules(),**optimizer.get_modules())
    path=folder/'checkpoint.pt'
    torch.save({name:module.state_dict() for name,module in modules.items()},path)
    manifest=dict(owner=initialization.OWNER,state='prepared',kind='teacher-distillation',partition='training',
                  architecture=ARCHITECTURE,observationVersion=4,actionVersion=2,openingEconomyVersion=2,
                  rulesVersion=CORE_THREE_RULES,boardSize=7,width=32,checkpoint=str(path),
                  checkpointSha256=hashlib.sha256(path.read_bytes()).hexdigest())
    (folder/'manifest.json').write_text(json.dumps(manifest))
    return folder/'manifest.json',path


class PreparedInitializationTests(unittest.TestCase):
    def test_prepared_weights_have_zero_ppo_steps_and_no_distillation_moments(self):
        with tempfile.TemporaryDirectory() as temp:
            _,path=prepared(Path(temp).resolve()/'input')
            _,health=checkpoint(path,32,prepared=True)
            self.assertEqual(health['step'],0); self.assertEqual(health['optimizerSteps'],{})
            with self.assertRaises(ValueError): checkpoint(path,32)
            state=torch.load(path,map_location='cpu'); state['global_step']['_GlobalSteps__global_step'].fill_(1)
            torch.save(state,path)
            with self.assertRaises(ValueError): checkpoint(path,32,prepared=True)

    def test_install_binds_copied_inputs_and_survives_source_removal(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve(); (root/'.blocknations-training-root.json').write_text(json.dumps(dict(owner='BlockNations.LocalTraining.v1')))
            manifest,path=prepared(root/'benchmarks/source')
            run=root/'benchmarks/new'; run.mkdir()
            expected=dict(width=32,boardSize=7,rulesVersion=CORE_THREE_RULES)
            identity=initialization.install(root,manifest,run,expected)
            self.assertEqual(initialization.identity(run,expected),identity)
            path.unlink(); manifest.unlink()
            self.assertEqual(initialization.identity(run,expected),identity)
            changed=json.loads((run/'initialization/source-manifest.json').read_text())
            changed['width']=64
            (run/'initialization/source-manifest.json').write_text(json.dumps(changed))
            with self.assertRaises(ValueError): initialization.identity(run,expected)

    def test_other_rules_or_architecture_are_not_silently_relabelled(self):
        document=dict(owner=initialization.OWNER,state='prepared',kind='teacher-distillation',partition='training',
                      architecture=ARCHITECTURE,observationVersion=4,actionVersion=2,openingEconomyVersion=2,
                      rulesVersion=CORE_THREE_RULES,boardSize=7,width=32)
        for changed in [dict(document,rulesVersion='blocknations-simulation-v4'),dict(document,architecture='legacy-mlp')]:
            with self.assertRaises(ValueError):
                initialization.validate(changed,dict(width=32,boardSize=7,rulesVersion=CORE_THREE_RULES))


class CorpusTests(unittest.TestCase):
    def test_whole_match_split_and_legal_labels(self):
        data=arrays(); summary=corpus.validate(data)
        self.assertEqual(summary['trainingGroups'],6); self.assertEqual(summary['validationGroups'],2)
        first=corpus.group(100,2,10,0)
        self.assertEqual(first,corpus.group(100,2,10,0))
        self.assertNotEqual(first[0],corpus.group(100,2,11,0)[0])
        target=torch.as_tensor(data['teacherProbabilities'])
        torch.testing.assert_close(divergence(target,target),torch.zeros(128),rtol=0,atol=0)

    def test_split_leaks_hidden_current_enemies_and_masked_labels_fail(self):
        data=arrays(); data['validation'][0]=False
        with self.assertRaises(ValueError): corpus.validate(data)
        data=arrays(); data['observations'][0,6]=-1
        with self.assertRaises(ValueError): corpus.validate(data)
        data=arrays(); data['teacherProbabilities'][0,1]=.1
        with self.assertRaises(ValueError): corpus.validate(data)

    def test_owned_training_load_rejects_evaluation_partition_and_tampering(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve(); folder=root/'benchmarks/corpus'; folder.mkdir(parents=True)
            data=arrays(); path=folder/'corpus.npz'; np.savez_compressed(path,**data)
            record=dict(owner=corpus.OWNER,state='completed',partition='training',observationVersion=4,
                        diagnosticFullVisionSeats=0,humanIngestion='none',corpus=str(path),
                        corpusSha256=hashlib.sha256(path.read_bytes()).hexdigest(),**corpus.validate(data))
            manifest=folder/'manifest.json'; manifest.write_text(json.dumps(record))
            loaded,_=corpus.load(root,manifest)
            np.testing.assert_array_equal(loaded['observations'],data['observations'])
            manifest.write_text(json.dumps(dict(record,partition='evaluation-only')))
            with self.assertRaises(ValueError): corpus.load(root,manifest)
            manifest.write_text(json.dumps(record)); path.write_bytes(path.read_bytes()+b'changed')
            with self.assertRaises(ValueError): corpus.load(root,manifest)


if __name__=='__main__': unittest.main()
