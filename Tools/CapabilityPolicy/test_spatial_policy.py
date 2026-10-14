from pathlib import Path
import os
import sys
import tempfile
import unittest
from unittest.mock import patch
import uuid

sys.path.insert(0,str(Path(__file__).parents[1]/'Training'))

import numpy as np
import torch
from mlagents.trainers.settings import NetworkSettings, RunOptions
from mlagents.trainers.ppo.optimizer_torch import TorchPPOOptimizer

from architectures import actor_class, DEFAULT, SPATIAL
from policy import probabilities, export
from model_contract import spec
from spatial_policy import SpatialCapabilityActor
from test_policy import samples
from trainer import CapabilityPPOTrainer, register
from train import recipe
from artifacts import checkpoint

torch.set_num_threads(1)


def actor():
    torch.manual_seed(209)
    contract=spec(4)
    return actor_class(SPATIAL)(contract.observation_specs,NetworkSettings(hidden_units=32,num_layers=2),contract.action_spec)


class SpatialPolicyTests(unittest.TestCase):
    def test_roster_permutations_preserve_other_actions_and_value(self):
        model=actor(); values,allowed=samples()
        with torch.no_grad():
            before=probabilities(model,values,allowed)
            before_value=model.critic_pass([values])[0]['extrinsic']
            for seed in range(8):
                perm=torch.randperm(16,generator=torch.Generator().manual_seed(seed))
                changed=values.clone(); mask=allowed.clone()
                changed[:,2904:3112]=values[:,2904:3112].reshape(-1,16,13)[:,perm].reshape(-1,208)
                changed[:,3241:3257]=values[:,3241:3257][:,perm]
                mask[:,242:258]=allowed[:,242:258][:,perm]
                actual=probabilities(model,changed,mask)
                torch.testing.assert_close(actual[:,:242],before[:,:242])
                torch.testing.assert_close(actual[:,242:258],before[:,242:258][:,perm])
                torch.testing.assert_close(actual[:,-1],before[:,-1])
                torch.testing.assert_close(model.critic_pass([changed])[0]['extrinsic'],before_value)
                self.assertTrue(torch.all(actual[mask==0]==0))

    def test_global_context_retains_layout_when_pooled_features_match(self):
        model=actor()
        squares=torch.rand((2,121,32),generator=torch.Generator().manual_seed(991))
        changed=squares.flip(1);present=torch.ones((2,121,1))
        torch.testing.assert_close(squares.mean(dim=1),changed.mean(dim=1))
        self.assertFalse(torch.allclose(model.network_body.board_context(squares,present),model.network_body.board_context(changed,present)))

    def test_probability_export_dynamic_batches_match(self):
        from onnx.reference import ReferenceEvaluator
        model=actor();values,allowed=samples()
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'spatial.onnx';export(model,values[:1],allowed[:1],path)
            evaluator=ReferenceEvaluator(str(path))
            for count in [1,7]:
                with torch.no_grad():expected=probabilities(model,values[:count],allowed[:count]).numpy()
                actual=evaluator.run(None,{'obs_0':values[:count].numpy(),'action_masks':allowed[:count].numpy()})[0]
                np.testing.assert_allclose(actual,expected,rtol=1e-4,atol=1e-6)

    def test_stock_ppo_update_save_and_architecture_checks(self):
        from mlagents.trainers.buffer import AgentBuffer,BufferKey,RewardSignalUtil
        from mlagents.trainers.trajectory import ObsUtil
        from mlagents.trainers.model_saver.torch_model_saver import TorchModelSaver
        register();settings=RunOptions.from_dict(recipe(8192,32)).behaviors['BlockNationsSeatV4']
        custom=object.__new__(CapabilityPPOTrainer);custom.seed=209;custom.shared_critic=True;custom.trainer_settings=settings
        with patch.dict(os.environ,{'BLOCKNATIONS_CAPABILITY_ARCHITECTURE':SPATIAL}):
            policy=custom.create_policy(None,spec(4))
        self.assertIsInstance(policy.actor,SpatialCapabilityActor)
        optimizer=TorchPPOOptimizer(policy,settings)
        values,allowed=samples(32)
        with torch.no_grad():
            p=probabilities(policy.actor,values,allowed)
            old=policy.actor.critic_pass([values])[0]['extrinsic'].numpy()
        batch=AgentBuffer()
        for key,value in {ObsUtil.get_name_at(0):values.numpy(),BufferKey.ACTION_MASK:allowed.numpy(),
            BufferKey.DISCRETE_ACTION:np.full((32,1),258,np.int64),BufferKey.DISCRETE_LOG_PROBS:p[:,-1:].log().numpy(),
            BufferKey.MASKS:np.ones(32,np.bool_),BufferKey.ADVANTAGES:np.ones(32),
            RewardSignalUtil.value_estimates_key('extrinsic'):old,RewardSignalUtil.returns_key('extrinsic'):old+1}.items():batch[key].set(value)
        self.assertTrue(all(np.isfinite(value) for value in optimizer.update(batch,num_sequences=32).values()))
        policy.set_step(4096)
        with tempfile.TemporaryDirectory() as folder:
            saver=TorchModelSaver(settings,folder);saver.register(policy);saver.register(optimizer)
            saver.save_checkpoint('BlockNationsSeatV4',4096)
            restored,health=checkpoint(Path(folder)/'checkpoint.pt',32,architecture=SPATIAL)
            self.assertTrue(all(value==1 for value in health['optimizerSteps'].values()))
            torch.testing.assert_close(probabilities(restored,values,allowed),probabilities(policy.actor,values,allowed),rtol=0,atol=0)
            with self.assertRaises(RuntimeError):checkpoint(Path(folder)/'checkpoint.pt',32,architecture=DEFAULT)
        with self.assertRaises(ValueError):actor_class('guess-from-tensors')

    @unittest.skipUnless(os.environ.get('BLOCKNATIONS_CAPABILITY_TEST_WORKER'),'Set the standalone worker for real integration checks.')
    def test_actual_csharp_actions_are_legal(self):
        from dotnet_environment import DotNetEnvironment
        from mlagents_envs.base_env import ActionTuple
        from training_contract import CORE_THREE_RULES
        model=actor();random=np.random.default_rng(2215);counts=[0,0]
        with tempfile.TemporaryDirectory() as folder:
            env=DotNetEnvironment(os.environ['BLOCKNATIONS_CAPABILITY_TEST_WORKER'],folder,2,2215,7,False,2,uuid.uuid4().hex,
                                 rules_version=CORE_THREE_RULES,observation_version=4)
            try:
                env.reset()
                for _ in range(64):
                    for seat,name in enumerate(env.names):
                        current,_=env.get_steps(name)
                        if not len(current):continue
                        allowed=torch.as_tensor(~current.action_mask[0],dtype=torch.float32)
                        with torch.no_grad():p=probabilities(model,torch.as_tensor(current.obs[0]),allowed).numpy()
                        actions=np.array([[random.choice(259,p=row)] for row in p],np.int32)
                        self.assertTrue(all(allowed[i,choice]==1 for i,choice in enumerate(actions[:,0])))
                        counts[seat]+=len(actions);env.set_actions(name,ActionTuple(discrete=actions))
                    env.step()
                self.assertEqual(sum(s['rejections'] for s in env.stats),0);self.assertTrue(min(counts)>0)
            finally:env.close()


if __name__=='__main__':unittest.main()
