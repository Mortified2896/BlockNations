"""Sequential matched continuations for public information, history and curricula.

One treatment per study. No release selection, live-run mutation, viewer, human
ingestion or installed-SDK edits. All runs share the marked root's storage guard.
"""
import argparse
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import threading
import time

from bounded_log import BoundedLog
from evaluate_milestone import freeze
from evaluate_suite import frozen_actor, worker_digest, run as evaluate
from model_contract import SIZES, behavior, checkpoint_schema, spec, validate_schema
from rule_profile_study import arm_files, sha
from supervisor import ROOT_MARKER, atomic_json, interrupt_trainer, size
from training_contract import run_rules_version
from weakness_sampling import apply as apply_weakness
from information_study_runtime import PowerGuard, PowerPause, save_environment, resume_environment, evaluate_or_resume


def migration(checkpoint, network, source, target):
    module_spec=importlib.util.spec_from_file_location('observation_migration',Path(__file__).parents[1]/'ObservationContract/migrate_checkpoint.py')
    module=importlib.util.module_from_spec(module_spec); module_spec.loader.exec_module(module)
    return module.migrate(checkpoint,network,source_size=SIZES[source],target_size=SIZES[target])


def prepare_arm(root, name, parent_path, recipe_run, worker, steps, seed, schema, *, episode_starts=None, weakness=None, fog_diagnostic=False, full_vision_seats=0):
    if full_vision_seats not in (0, 3) or full_vision_seats and not fog_diagnostic:
        raise ValueError('Fog training compares standard vision with symmetric full vision in diagnostic runs.')
    from mlagents.torch_utils import torch
    parent=json.loads(Path(parent_path).read_text()); original=checkpoint_schema(parent['checkpoint'])
    validate_schema(schema)
    if schema < original or schema > original+1:
        raise ValueError('Compare a parent contract with itself or one next prefix extension.')
    run, environment, provenance=arm_files(root,name,parent_path,recipe_run,run_rules_version(recipe_run),worker,steps,seed,parallel_games=8)
    options=json.loads((run/'trainer.yaml').read_text()); network=options['behaviors'][behavior(original)]['network_settings']
    saved=run/'checkpoints'/name/behavior(original)/'checkpoint.pt'
    old=torch.load(saved,map_location='cpu'); new=old
    for previous in range(original,schema): new=migration(new,network,previous,previous+1)
    if schema != original:
        folder=saved.parent.with_name(behavior(schema)); saved.parent.rename(folder)
        saved=folder/'checkpoint.pt'; torch.save(new,saved)
        options['behaviors'][behavior(schema)]=options['behaviors'].pop(behavior(original))
        atomic_json(run/'trainer.yaml',options)
    contract=json.loads((run/'run.json').read_text())
    contract.update(schema=schema,behavior=behavior(schema),observationSize=SIZES[schema],actionSchema=2,
                    experiment='matched-information-study',humanIngestion='disabled-for-both-arms')
    contract['weightTransfer']['observationMigration']=dict(source=original,target=schema,parentSha256=parent['sha256'],optimizer='preserved')
    treatment_record=dict(observationVersion=schema,actionVersion=2,humanIngestion='disabled',workers=8,torchThreads=1)
    if episode_starts:treatment_record['episodeStartsSha256']=sha(Path(episode_starts).read_bytes())
    if weakness:treatment_record['weaknessDiagnosticSha256']=sha(Path(weakness).read_bytes())
    contract['informationTreatment']=treatment_record
    if fog_diagnostic:
        contract.update(diagnosticOnly=True, diagnosticFullVisionSeats=full_vision_seats)
        treatment_record['diagnosticFullVisionSeats']=full_vision_seats
    atomic_json(run/'run.json',contract)
    environment.update(BLOCKNATIONS_OBSERVATION_VERSION=str(schema),BLOCKNATIONS_HUMAN_IMITATION='disabled',BLOCKNATIONS_TORCH_THREADS='1')
    environment['BLOCKNATIONS_DIAGNOSTIC_FULL_VISION_SEATS']=str(full_vision_seats)
    execution=json.loads((run/'execution-settings.json').read_text());execution.update(modelSchema=schema,torchThreads=1)
    atomic_json(run/'execution-settings.json',execution)
    if episode_starts is not None:
        contents=Path(episode_starts).read_bytes()
        if len(contents)>4*1024*1024: raise ValueError('Episode-start corpus exceeds the bounded worker contract.')
        document=json.loads(contents)
        if document.get('sourcePartition')!='training': raise ValueError('Evaluation positions cannot enter episode training.')
        (run/'episode-starts.json').write_bytes(contents)
        environment['BLOCKNATIONS_EPISODE_STARTS']=str(run/'episode-starts.json')
    if weakness is not None:
        recipe=json.loads((run/'training-opponents.json').read_text())
        diagnostic=json.loads(Path(weakness).read_text())
        if diagnostic.get('candidateSha256') != parent['sha256']:raise ValueError('Weakness diagnostic belongs to another frozen parent.')
        atomic_json(run/'training-opponents.json',apply_weakness(recipe,diagnostic))
    # Strictly construct both actor and critic and compare the unchanged initial
    # policy/value function, not merely tensor shapes or final Adam counters.
    from mlagents.trainers.settings import NetworkSettings
    from mlagents.trainers.torch_entities.networks import SimpleActor, ValueNetwork
    from frozen_policy import FrozenPolicy
    import numpy as np
    from mlagents.trainers.policy.torch_policy import TorchPolicy
    initial=FrozenPolicy(parent['checkpoint'],network,spec(original),parent['sha256'])
    policy=TorchPolicy(seed,spec(schema),NetworkSettings(**network),SimpleActor,dict(conditional_sigma=False,tanh_squash=False))
    policy.actor.load_state_dict(new['Policy'],strict=True)
    random=np.random.default_rng(seed)
    values=random.normal(0,.2,(8,SIZES[schema])).astype(np.float32); mask=random.random((8,259))<.5;mask[:,-1]=False
    with torch.no_grad():
        encoded,_=policy.actor.network_body([torch.as_tensor(values)])
        actual=policy.actor.action_model._get_dists(encoded,torch.as_tensor(~mask,dtype=torch.float32)).discrete[0].probs.numpy()
    np.testing.assert_allclose(np.where(mask,0,actual),initial.probabilities(values,mask),rtol=1e-4,atol=1e-6)
    before_critic=ValueNetwork(['extrinsic'],spec(original).observation_specs,NetworkSettings(**network))
    after_critic=ValueNetwork(['extrinsic'],spec(schema).observation_specs,NetworkSettings(**network))
    before_critic.load_state_dict(old['Optimizer:critic'],strict=True);after_critic.load_state_dict(new['Optimizer:critic'],strict=True)
    with torch.no_grad():
        before_value=before_critic([torch.as_tensor(values[:,:SIZES[original]])])[0]['extrinsic'].numpy()
        after_value=after_critic([torch.as_tensor(values)])[0]['extrinsic'].numpy()
    np.testing.assert_allclose(after_value,before_value,rtol=1e-4,atol=1e-5)
    if any(int(new['Optimizer:value_optimizer']['state'][i]['step'])!=int(old['Optimizer:value_optimizer']['state'][i]['step']) for i in range(6,18)):
        raise ValueError('Observation migration changed Adam counters.')
    atomic_json(run/'initialization.json',dict(version=1,sourceSchema=original,targetSchema=schema,actorParitySamples=8,criticParitySamples=8,
                optimizerCountersPreserved=True,parentSha256=parent['sha256']))
    return run,environment


def run_study(root,parent,recipe_run,suite,worker,destination,name,*,treatment='observation',steps=250000,seeds=(61,62,63),hours=3,episode_starts=None,weakness=None,power_controlled=False,resume=False,confirmation=False):
    from mlagents.torch_utils import torch
    torch.set_num_threads(1)
    root,parent,recipe_run,suite,worker,destination=map(lambda p:Path(p).absolute(),(root,parent,recipe_run,suite,worker,destination))
    if (root.is_symlink() or root.resolve()!=root or json.loads((root/ROOT_MARKER).read_text()).get('owner')!='BlockNations.LocalTraining.v1' or
            recipe_run.parent!=root/'runs' or recipe_run.resolve()!=recipe_run or parent.resolve()!=parent or destination.resolve()!=destination or
            not parent.is_relative_to(root/'runs') or not destination.is_relative_to(root/'benchmarks')):
        raise ValueError('Study inputs and a new destination must belong to the marked shared root.')
    if (not re.fullmatch(r'[a-z0-9][a-z0-9-]{0,32}',name) or type(steps)is not int or not 2048<=steps<=1000000 or
            not 1<=len(seeds)<=3 or len(set(seeds))!=len(seeds) or any(type(s)is not int or not 0<=s<2**31 for s in seeds) or not 0<hours<=6):
        raise ValueError('Use bounded distinct seeds, steps, names and per-arm time.')
    if treatment not in ('observation','history','episode-starts','weakness','fog') or (episode_starts is not None)!=(treatment=='episode-starts') or (weakness is not None)!=(treatment=='weakness'):
        raise ValueError('Choose exactly one recorded treatment per experiment.')
    source_schema=checkpoint_schema(json.loads(parent.read_text())['checkpoint'])
    if treatment=='history': control_schema,target_schema=3,4
    elif source_schema != 2 and treatment=='observation': raise ValueError('Public-role experiment needs its preserved v2 parent.')
    elif treatment=='observation':control_schema,target_schema=2,3
    else:control_schema=target_schema=source_schema
    if control_schema != source_schema: raise ValueError('History compares from an explicitly selected frozen v3 parent.')
    frozen_actor(parent,run_rules_version(recipe_run))
    definition=json.loads(suite.read_text());definition['workerSha256']=worker_digest(worker)
    if treatment=='fog':definition.update(diagnosticOnly=True,diagnosticFullVision='fog')
    if not resume:destination.mkdir(parents=True,exist_ok=False)
    suite_copy=destination/'fixed-suite.json'
    if not resume:atomic_json(suite_copy,definition)
    full_vision_suite=destination/'full-vision-suite.json'
    if treatment=='fog' and not resume:atomic_json(full_vision_suite,dict(definition,diagnosticFullVision='both'))
    repeat_suite=destination/'confirmation-suite.json'
    if confirmation and not resume:
        from tournament_selection import confirmation_suite
        atomic_json(repeat_suite,confirmation_suite(definition))
    source_files=list(Path(__file__).parent.glob('*.py'))+[Path(__file__).parents[1]/'ObservationContract/migrate_checkpoint.py',parent,suite,recipe_run/'trainer.yaml',recipe_run/'training-opponents.json']
    if episode_starts:source_files.append(Path(episode_starts))
    if weakness:source_files.append(Path(weakness))
    pins={str(p):sha(p.read_bytes()) for p in source_files}
    receipt=dict(version=1,owner='BlockNations.InformationStudy.v1',state='running',name=name,hoursPerArm=hours,treatment=treatment,seeds=list(seeds),stepsPerArm=steps,powerControlled=power_controlled,confirmation=confirmation,
                 controlSchema=control_schema,treatmentSchema=target_schema,pins=pins,workerDigest=worker_digest(worker),results=[],startedUnix=time.time(),pid=os.getpid())
    if resume:
        previous_receipt=json.loads((destination/'study.json').read_text())
        for key in ('owner','name','hoursPerArm','treatment','seeds','stepsPerArm','powerControlled','confirmation','controlSchema','treatmentSchema','pins','workerDigest'):
            if previous_receipt.get(key)!=receipt[key]:raise ValueError('Resume requires the exact original study inputs and limits.')
        if previous_receipt['state'] not in ('paused_power','running'):raise ValueError('Only a power-paused or interrupted study may resume.')
        receipt=previous_receipt;receipt.update(state='running',pid=os.getpid(),resumedUnix=time.time())
        receipt.pop('finishedUnix',None)
    lock=root/'information-study.lock'
    if resume and lock.exists():
        try:os.kill(int(lock.read_text()),0)
        except ProcessLookupError:lock.unlink()
    descriptor=os.open(lock,os.O_CREAT|os.O_EXCL|os.O_WRONLY,0o600)
    os.write(descriptor,str(os.getpid()).encode());os.close(descriptor)
    stopping=False; process=None; capture=None; assertion=None; started=None; current_key=None
    power=PowerGuard(power_controlled);next_full_guard=0
    def stop(*_):
        nonlocal stopping
        stopping=True
    previous={sig:signal.signal(sig,stop) for sig in (signal.SIGINT,signal.SIGTERM)}
    def guard():
        nonlocal next_full_guard
        power.check()
        if stopping or (destination/'stop.request').exists():raise RuntimeError('Study stopped by request; artifacts preserved.')
        if time.monotonic()<next_full_guard:return
        next_full_guard=time.monotonic()+30
        if size(root)>19_488_000_000 or shutil.disk_usage(root).free<20_512_000_000:raise RuntimeError('Shared storage/free-disk reserve reached.')
        if any(sha(Path(p).read_bytes())!=h for p,h in pins.items()) or worker_digest(worker)!=receipt['workerDigest']:raise RuntimeError('Pinned study source or worker changed.')
    try:
        if sys.platform=='darwin': assertion=subprocess.Popen(['/usr/bin/caffeinate','-i','-s','-w',str(os.getpid())],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        atomic_json(destination/'study.json',receipt)
        if treatment=='fog':
            nofog=evaluate_or_resume(parent,full_vision_suite,worker,destination/'baseline-full-vision',guard)
            rows=[r for c,r in zip(nofog['cases'],nofog['results']) if c['reference']['kind']!='self']
            receipt['baselineFullVision']=dict(suiteId=nofog['suiteId'],externalWins=sum(r['wins'] for r in rows),externalGames=sum(r['games'] for r in rows),interruptions=sum(r['interruptions'] for r in rows))
            atomic_json(destination/'study.json',receipt)
        guard()
        baseline=evaluate_or_resume(parent,suite_copy,worker,destination/'baseline',guard)
        baseline_external=[r for c,r in zip(baseline['cases'],baseline['results']) if c['reference']['kind']!='self']
        receipt['baseline']=dict(suiteId=baseline['suiteId'],externalWins=sum(r['wins'] for r in baseline_external),
            externalGames=sum(r['games'] for r in baseline_external),interruptions=sum(r['interruptions'] for r in baseline_external))
        if confirmation:
            repeat=evaluate_or_resume(parent,repeat_suite,worker,destination/'baseline-confirmation',guard)
            rows=[r for c,r in zip(repeat['cases'],repeat['results']) if c['reference']['kind']!='self']
            receipt['baselineConfirmation']=dict(suiteId=repeat['suiteId'],externalWins=sum(r['wins'] for r in rows),externalGames=sum(r['games'] for r in rows),interruptions=sum(r['interruptions'] for r in rows))
        atomic_json(destination/'study.json',receipt)
        for seed in seeds:
            # Reverse order on alternating seeds to reduce simple time-order effects.
            arms=[('control',control_schema),('treatment',target_schema)]
            if seeds.index(seed)%2:arms.reverse()
            for arm,schema in arms:
                if any(r['seed']==seed and r['arm']==arm for r in receipt['results']):continue
                guard()
                run=root/'runs'/('info-'+name+'-'+str(seed)+'-'+arm)
                current_key=str(seed)+'-'+arm
                if not run.exists():
                    run,environment=prepare_arm(root,run.name,parent,recipe_run,worker,steps,seed,schema,
                        episode_starts=episode_starts if arm=='treatment' else None,weakness=weakness if arm=='treatment' else None,
                        fog_diagnostic=treatment=='fog',full_vision_seats=3 if treatment=='fog' and arm=='treatment' else 0)
                    save_environment(run,environment)
                    receipt.setdefault('armInputs',{})[current_key]={name:sha((run/name).read_bytes()) for name in
                        ('run.json','trainer.yaml','training-opponents.json','study-environment.json')}
                    atomic_json(destination/'study.json',receipt)
                else:
                    if not resume:raise ValueError('Existing trial was not explicitly resumed.')
                    if any(sha((run/name).read_bytes())!=digest for name,digest in receipt['armInputs'][current_key].items()):
                        raise ValueError('Owned trial inputs changed before resume.')
                    environment=resume_environment(run)
                folder=destination/current_key;folder.mkdir(exist_ok=resume)
                started=time.monotonic();log=BoundedLog(folder/'trial.log')
                prior_elapsed=receipt.get('armElapsedSeconds',{}).get(current_key,0)
                process=subprocess.Popen([sys.executable,str(Path(__file__).with_name('rated_training.py')),str(run/'trainer.yaml'),
                    '--run-id',run.name,'--results-dir',str(run/'checkpoints'),'--seed',str(seed),'--torch-device','cpu','--timeout-wait','300','--resume'],
                    cwd=run,env=environment,stdin=subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
                def drain(child=process,writer=log):
                    try:
                        while chunk:=child.stdout.read1(4096):writer.append(chunk)
                    finally:child.stdout.close();writer.close()
                capture=threading.Thread(target=drain,daemon=True);capture.start()
                receipt.update(currentSeed=seed,currentArm=arm,currentRun=str(run),trainerPid=process.pid)
                atomic_json(destination/'study.json',receipt)
                last_guard=0
                while process.poll() is None:
                    guard()
                    if stopping or (destination/'stop.request').exists() or prior_elapsed+time.monotonic()-started>hours*3600:raise RuntimeError('Owned trial stopped by request/time guard.')
                    time.sleep(2)
                capture.join(timeout=5)
                if process.returncode!=0:raise RuntimeError('Information trainer failed; its bounded log and weights are preserved.')
                final=max((run/'checkpoints'/run.name/behavior(schema)).glob(behavior(schema)+'-*.pt'),key=lambda p:int(p.stem.rsplit('-',1)[1]))
                state=torch.load(final,map_location='cpu');original=torch.load(Path(json.loads(parent.read_text())['checkpoint']),map_location='cpu')
                for module in (state['Policy'],state['Optimizer:critic'],*state['Optimizer:value_optimizer']['state'].values()):
                    if any(torch.is_tensor(v) and not torch.isfinite(v).all() for v in module.values()):raise ValueError('Non-finite actor, critic or optimizer state.')
                deltas={str(i):int(state['Optimizer:value_optimizer']['state'][i]['step'])-int(original['Optimizer:value_optimizer']['state'][i]['step']) for i in range(6,18)}
                if min(deltas.values())<=0:raise ValueError('Preserved optimizer did not advance.')
                if int(final.stem.rsplit('-',1)[1])<json.loads(parent.read_text())['step']+steps:raise ValueError('Matched learning-step target was not reached.')
                arena=json.loads((run/'arena-status.json').read_text())
                if arena.get('rejections')!=0 or arena.get('failure'):raise ValueError('Trial legality/health failed.')
                frozen=run/'frozen-evaluations'/('milestone-'+str(int(final.stem.rsplit('-',1)[1])))/'manifest.json'
                if not frozen.exists():frozen=freeze(run,final,int(final.stem.rsplit('-',1)[1]),(run/'trainer.yaml').read_bytes(),sha((run/'training-opponents.json').read_bytes()))
                guard();pack=evaluate_or_resume(frozen,suite_copy,worker,folder/'fixed-results',guard)
                external=[r for c,r in zip(pack['cases'],pack['results']) if c['reference']['kind']!='self']
                repeated={}
                if treatment=='fog':
                    nofog=evaluate_or_resume(frozen,full_vision_suite,worker,folder/'full-vision-results',guard)
                    rows=[r for c,r in zip(nofog['cases'],nofog['results']) if c['reference']['kind']!='self']
                    repeated.update(fullVisionSuiteId=nofog['suiteId'],fullVisionWins=sum(r['wins'] for r in rows),fullVisionGames=sum(r['games'] for r in rows),fullVisionInterruptions=sum(r['interruptions'] for r in rows))
                if confirmation:
                    repeat=evaluate_or_resume(frozen,repeat_suite,worker,folder/'confirmation-results',guard)
                    rows=[r for c,r in zip(repeat['cases'],repeat['results']) if c['reference']['kind']!='self']
                    repeated=dict(confirmationSuiteId=repeat['suiteId'],confirmationWins=sum(r['wins'] for r in rows),confirmationGames=sum(r['games'] for r in rows),confirmationInterruptions=sum(r['interruptions'] for r in rows))
                from decision_audit import summarize
                options=next(iter(json.loads((run/'trainer.yaml').read_text())['behaviors'].values()))
                audit=summarize(json.loads((run/'decision-diagnostics.json').read_text()),options['reward_signals']['extrinsic']['gamma'],options['time_horizon'])
                atomic_json(folder/'decision-audit.json',audit)
                receipt['results'].append(dict(seed=seed,arm=arm,schema=schema,manifest=str(frozen),suiteId=pack['suiteId'],
                    externalWins=sum(r['wins'] for r in external),externalGames=sum(r['games'] for r in external),
                    interruptions=sum(r['interruptions'] for r in external),optimizerStepDeltas=deltas,rejections=arena['rejections'],
                    elapsedSeconds=prior_elapsed+time.monotonic()-started,actualStep=int(final.stem.rsplit('-',1)[1]),**repeated))
                atomic_json(destination/'study.json',receipt)
                started=None;current_key=None
        receipt['state']='completed'
        receipt['comparison']={arm:dict(wins=sum(r['externalWins'] for r in receipt['results'] if r['arm']==arm),
            games=sum(r['externalGames'] for r in receipt['results'] if r['arm']==arm),
            interruptions=sum(r['interruptions'] for r in receipt['results'] if r['arm']==arm)) for arm in ('control','treatment')}
        if confirmation:
            receipt['confirmationComparison']={arm:dict(wins=sum(r['confirmationWins'] for r in receipt['results'] if r['arm']==arm),games=sum(r['confirmationGames'] for r in receipt['results'] if r['arm']==arm),interruptions=sum(r['confirmationInterruptions'] for r in receipt['results'] if r['arm']==arm)) for arm in ('control','treatment')}
        if treatment=='fog':
            receipt['fullVisionComparison']={arm:dict(wins=sum(r['fullVisionWins'] for r in receipt['results'] if r['arm']==arm),games=sum(r['fullVisionGames'] for r in receipt['results'] if r['arm']==arm),interruptions=sum(r['fullVisionInterruptions'] for r in receipt['results'] if r['arm']==arm)) for arm in ('control','treatment')}
        receipt['decision']='Review paired role/opponent results and human play before a longer continuation; no automatic promotion.'
    except PowerPause as error:
        receipt.update(state='paused_power',powerPauseReason=str(error))
        events=receipt.setdefault('powerPauses',[])
        events.append(dict(unix=time.time(),arm=current_key,reason=str(error)));del events[:-100]
    except BaseException as error:
        receipt.update(state='failed',error=str(error));raise
    finally:
        if process is not None and process.poll() is None:interrupt_trainer(process,timeout=45)
        if capture is not None:capture.join(timeout=5)
        if started is not None and current_key is not None:
            elapsed=receipt.setdefault('armElapsedSeconds',{})
            elapsed[current_key]=elapsed.get(current_key,0)+time.monotonic()-started
        receipt.update(finishedUnix=time.time(),trainerPid=0);atomic_json(destination/'study.json',receipt)
        if lock.exists() and lock.read_text()==str(os.getpid()):lock.unlink()
        if assertion is not None:assertion.terminate();assertion.wait(timeout=5)
        for sig,handler in previous.items():signal.signal(sig,handler)
    return receipt


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for field in ('root','parent','recipe-run','suite','worker','destination'):parser.add_argument('--'+field,type=Path,required=True)
    parser.add_argument('--name',required=True);parser.add_argument('--steps',type=int,default=250000)
    parser.add_argument('--seeds',type=int,nargs='+',default=[61,62,63]);parser.add_argument('--hours-per-arm',type=float,default=3)
    parser.add_argument('--treatment',choices=('observation','history','episode-starts','weakness','fog'),default='observation')
    parser.add_argument('--episode-starts',type=Path);parser.add_argument('--weakness-diagnostic',type=Path)
    parser.add_argument('--power-controlled',action='store_true');parser.add_argument('--resume',action='store_true')
    parser.add_argument('--confirmation',action='store_true')
    args=parser.parse_args()
    result=run_study(args.root,args.parent,args.recipe_run,args.suite,args.worker,args.destination,args.name,
        steps=args.steps,seeds=tuple(args.seeds),hours=args.hours_per_arm,treatment=args.treatment,episode_starts=args.episode_starts,weakness=args.weakness_diagnostic,
        power_controlled=args.power_controlled,resume=args.resume,confirmation=args.confirmation)
    print(json.dumps(dict(state=result['state'],arms=len(result['results']))))
