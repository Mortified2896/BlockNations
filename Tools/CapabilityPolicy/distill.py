"""Fit a capability actor/value head to a frozen teacher on new fair experience.

Internal validation is by whole match, not acceptance data. Prepared PPO weights
start at step zero with fresh Adam; distillation moments are never relabelled as
reinforcement-learning history or copied into the new PPO optimizer.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import shutil
import sys
import time

import numpy as np
import torch
from onnx.reference import ReferenceEvaluator
from mlagents.trainers.settings import NetworkSettings, RunOptions
from mlagents.trainers.policy.torch_policy import TorchPolicy
from mlagents.trainers.ppo.optimizer_torch import TorchPPOOptimizer

sys.path.insert(0, str(Path(__file__).parents[1]/'Training'))
from model_contract import spec
from supervisor import ROOT_MARKER, atomic_json, size
from information_study_runtime import PowerGuard
from policy import CapabilityActor, probabilities, export, ARCHITECTURE
from trainer import register
from train import exclusive, recipe
from artifacts import checkpoint
from corpus import load
from initialization import OWNER
from architectures import actor_class, ARCHITECTURES


def divergence(predicted, target):
    return (target*(target.clamp(min=1e-9).log()-predicted.clamp(min=1e-9).log())).sum(dim=1)


def metrics(actor, arrays, indices):
    losses=[]; errors=[]; correct=[]
    with torch.no_grad():
        for offset in range(0, len(indices), 128):
            selected=indices[offset:offset+128]
            values=torch.as_tensor(arrays['observations'][selected])
            allowed=torch.as_tensor(arrays['allowed'][selected], dtype=torch.float32)
            target=torch.as_tensor(arrays['teacherProbabilities'][selected])
            p=probabilities(actor, values, allowed)
            v=actor.critic_pass([values])[0]['extrinsic']
            losses.extend(divergence(p, target).numpy())
            errors.extend((v-torch.as_tensor(arrays['teacherValues'][selected])).square().numpy())
            correct.extend((p.argmax(dim=1)==target.argmax(dim=1)).numpy())
    losses=np.asarray(losses); errors=np.asarray(errors); correct=np.asarray(correct)
    multi=arrays['allowed'][indices].sum(axis=1)>1
    root=arrays['observations'][indices,3112]==0
    result=dict(valueMSE=float(errors.mean()))
    for name, mask in [('multiChoice',multi),('rootMultiChoice',multi&root),('conditionalMultiChoice',multi&~root),('forced',~multi)]:
        result[name]=dict(count=int(mask.sum()), kl=float(losses[mask].mean()) if mask.any() else None,
                          teacherTopAgreement=float(correct[mask].mean()) if mask.any() else None)
    return result


def prepare(root, corpus_manifest, destination, *, width=32, seed=209, updates=2048, architecture=ARCHITECTURE):
    root, corpus_manifest, destination=(Path(path).absolute() for path in (root, corpus_manifest, destination))
    if (root.resolve()!=root or json.loads((root/ROOT_MARKER).read_text()).get('owner')!='BlockNations.LocalTraining.v1' or
            destination.resolve()!=destination or not destination.is_relative_to(root/'benchmarks') or destination==root/'benchmarks' or
            width not in (16,32,64) or not 0<=seed<2**31 or not 128<=updates<=16384):
        raise ValueError('Use a new owned bounded distillation trial.')
    power=PowerGuard(True); power.check()
    if size(root)>19_400_000_000 or shutil.disk_usage(root).free<20_600_000_000:
        raise RuntimeError('Preserve shared storage and the final-save reserve.')
    arrays, data=load(root,corpus_manifest)
    train=np.flatnonzero(~arrays['validation']); validation=np.flatnonzero(arrays['validation'])
    register(); torch.manual_seed(seed)
    contract=spec(4)
    factory=actor_class(architecture)
    actor=factory(contract.observation_specs, NetworkSettings(hidden_units=width,num_layers=2),contract.action_spec)
    optimizer=torch.optim.Adam(actor.parameters(),lr=.001)
    random=np.random.default_rng(seed)
    sources=list(Path(__file__).parent.glob('*.py'))+list((Path(__file__).parents[1]/'Training').glob('*.py'))
    pins={str(path):hashlib.sha256(path.read_bytes()).hexdigest() for path in sources}
    destination.mkdir(parents=True,exist_ok=False)
    initial=metrics(actor,arrays,validation)
    best_score=initial['multiChoice']['kl']+.1*initial['valueMSE']; best_state=copy.deepcopy(actor.state_dict())
    best_metrics=initial; best_update=0; update=0; last_policy=None; last_torch_random=None
    record=dict(owner='BlockNations.CapabilityDistillation.v1',state='training',partition='training',architecture=architecture,
                corpusManifest=str(corpus_manifest),corpusManifestSha256=hashlib.sha256(corpus_manifest.read_bytes()).hexdigest(),
                corpusSha256=data['corpusSha256'],teacher=data['teacher'],rulesVersion=data['rulesVersion'],boardSize=data['boardSize'],
                width=width,seed=seed,updates=updates,initialValidation=initial,history=[],sources=pins,
                optimization=dict(learningRate=.001,batchSize=128,valueCoefficient=.1,gradientClip=1.,target='raw frozen policy probabilities and fair critic value'),startedUnix=time.time())
    atomic_json(destination/'distillation.json',record)
    deadline=time.monotonic()+1800; next_check=0
    def guard():
        nonlocal next_check
        power.check()
        if time.monotonic()>deadline or (destination/'stop.request').exists():
            raise RuntimeError('Bounded distillation stop/time limit reached.')
        if time.monotonic()>=next_check:
            next_check=time.monotonic()+15
            if size(root)>19_488_000_000 or shutil.disk_usage(root).free<20_512_000_000:
                raise RuntimeError('Shared storage reserve reached.')
            if any(hashlib.sha256(Path(path).read_bytes()).hexdigest()!=sha for path,sha in pins.items()):
                raise RuntimeError('Pinned distillation source changed; preserve its experiment identity.')
    try:
        for update in range(1,updates+1):
            guard()
            selected=random.choice(train,size=128,replace=True)
            values=torch.as_tensor(arrays['observations'][selected])
            allowed=torch.as_tensor(arrays['allowed'][selected],dtype=torch.float32)
            target=torch.as_tensor(arrays['teacherProbabilities'][selected])
            predicted=probabilities(actor,values,allowed)
            value=actor.critic_pass([values])[0]['extrinsic']
            loss=divergence(predicted,target).mean()+.1*(value-torch.as_tensor(arrays['teacherValues'][selected])).square().mean()
            if not torch.isfinite(loss): raise ValueError('Non-finite distillation loss.')
            optimizer.zero_grad(); loss.backward(); torch.nn.utils.clip_grad_norm_(actor.parameters(),1.); optimizer.step()
            if update%128==0 or update==updates:
                measured=metrics(actor,arrays,validation)
                score=measured['multiChoice']['kl']+.1*measured['valueMSE']
                if score<best_score:
                    best_score=score; best_state=copy.deepcopy(actor.state_dict()); best_metrics=measured; best_update=update
                record['history'].append(dict(update=update,validation=measured))
                atomic_json(destination/'distillation.json',record)
                print(json.dumps(dict(update=update,validationMultiChoiceKL=measured['multiChoice']['kl'],rootAgreement=measured['rootMultiChoice']['teacherTopAgreement'],conditionalAgreement=measured['conditionalMultiChoice']['teacherTopAgreement'])),flush=True)
        last_policy=copy.deepcopy(actor.state_dict()); last_torch_random=torch.get_rng_state()
        actor.load_state_dict(best_state,strict=True)
        if best_metrics['multiChoice']['kl']>=initial['multiChoice']['kl']*.5:
            raise ValueError('Teacher approximation did not halve held-out-match multi-choice KL; do not prepare an initializer.')
        folder=destination/'initialization'; folder.mkdir()
        # Reconstruct stock PPO modules with fresh optimizer state and step zero.
        policy=TorchPolicy(seed,contract,NetworkSettings(hidden_units=width,num_layers=2),factory,{})
        policy.actor.load_state_dict(best_state,strict=True)
        settings=RunOptions.from_dict(recipe(8192,width)).behaviors['BlockNationsSeatV4']
        ppo=TorchPPOOptimizer(policy,settings)
        modules=dict(policy.get_modules(),**ppo.get_modules())
        saved=folder/'checkpoint.pt'
        torch.save({name:module.state_dict() for name,module in modules.items()},saved)
        _, health=checkpoint(saved,width,prepared=True,architecture=architecture)
        inputs=torch.as_tensor(arrays['observations'][validation[:64]])
        masks=torch.as_tensor(arrays['allowed'][validation[:64]],dtype=torch.float32)
        export(actor,inputs[:1],masks[:1],folder/'PreparedPolicy.onnx')
        reference=ReferenceEvaluator(str(folder/'PreparedPolicy.onnx')); maximum=0.
        for count in (1,7,len(inputs)):
            with torch.no_grad(): expected=probabilities(actor,inputs[:count],masks[:count]).numpy()
            actual=reference.run(None,{'obs_0':inputs[:count].numpy(),'action_masks':masks[:count].numpy()})[0]
            np.testing.assert_allclose(actual,expected,rtol=1e-4,atol=1e-6)
            if not np.all(actual[masks[:count].numpy()==0]==0): raise ValueError('Export allowed a masked action.')
            maximum=max(maximum,float(np.max(np.abs(actual-expected))))
        initializer=dict(owner=OWNER,state='prepared',kind='teacher-distillation',partition='training',architecture=architecture,
                         observationVersion=4,actionVersion=2,openingEconomyVersion=2,rulesVersion=data['rulesVersion'],boardSize=data['boardSize'],
                         width=width,checkpoint=str(saved),checkpointSha256=health['checkpointSha256'],
                         corpusSha256=data['corpusSha256'],teacher=data['teacher'],sourceDistillation=str(destination/'distillation.json'),
                         bestUpdate=best_update,validation=best_metrics,ppoStep=0,ppoAdam='fresh-empty',probabilityExportMaximumError=maximum)
        atomic_json(folder/'manifest.json',initializer)
        record.update(state='prepared',bestUpdate=best_update,bestValidation=best_metrics,initializationManifest=str(folder/'manifest.json'))
    except BaseException as error:
        record.update(state='failed',error=type(error).__name__+': '+str(error))
        raise
    finally:
        torch.save(dict(owner='BlockNations.CapabilityDistillationProgress.v1',
                        Policy=last_policy if last_policy is not None else actor.state_dict(),
                        distillationOptimizer=optimizer.state_dict(),bestPolicy=best_state,bestUpdate=best_update,
                        update=update,randomState=random.bit_generator.state,
                        torchRandomState=last_torch_random if last_torch_random is not None else torch.get_rng_state(),
                        corpusSha256=data['corpusSha256'],sources=pins),destination/'progress.pt')
        record['finishedUnix']=time.time(); atomic_json(destination/'distillation.json',record)
    return record


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ('root','corpus','destination'): parser.add_argument('--'+name,type=Path,required=True)
    parser.add_argument('--width',type=int,choices=(16,32,64),default=32)
    parser.add_argument('--architecture',choices=tuple(ARCHITECTURES),default=ARCHITECTURE)
    parser.add_argument('--seed',type=int,default=209); parser.add_argument('--updates',type=int,default=2048)
    args=parser.parse_args(); torch.set_num_threads(1)
    with exclusive(args.root.absolute()):
        result=prepare(args.root,args.corpus,args.destination,width=args.width,seed=args.seed,updates=args.updates,architecture=args.architecture)
    print(json.dumps({key:result[key] for key in ['state','bestUpdate','bestValidation','initializationManifest']}))
