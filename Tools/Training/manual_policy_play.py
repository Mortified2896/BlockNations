"""Play a frozen policy through the actual worker using only fair seat observations.

Console coordinates are the policy's canonical 11x11 canvas. The seven-tile board
occupies coordinates 2..8. Hidden state, enemy wallets and unseen actions are never
displayed. Full vectors and AI choices are saved for post-match analysis only.
"""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import uuid

import numpy as np
from mlagents.torch_utils import torch
from mlagents_envs.base_env import ActionTuple
from dotnet_environment import DotNetEnvironment
from evaluate_suite import frozen_actor
from frozen_policy import evaluation_weights
from supervisor import ROOT_MARKER, size
from training_contract import CORE_THREE_RULES

OWNER='BlockNations.ManualPolicyDiagnostic.v1'


def coord(index):return f'({index%11},{index//11})'


def describe(index):
    if isinstance(index, (bool, np.bool_)) or not isinstance(index, (int, np.integer)) or not 0 <= index < 259:
        raise ValueError('Choose an encoded legal-action index.')
    if index==258:return 'end turn'
    if index>=242:
        slot=index-242
        return 'recruit '+(('archer','rider','scout','warrior')[slot] if slot<4 else 'slot '+str(slot))
    return ('attack ' if index>=121 else 'select/move ')+coord(index%121)


def fair_view(observation, available):
    if observation.shape[0]<3120 or not np.isfinite(observation).all():raise ValueError('Invalid observation.')
    tiles=observation[:2904].reshape(121,24)
    globals=observation[3112:3120]
    selected=np.flatnonzero(tiles[:,20])
    text=[f'Round {round(float(globals[2])*100)}; your gold {round(float(globals[1])*20)}; '+
          ('choose source' if globals[0]==0 else 'choose action for '+coord(int(selected[0])))]
    for y in range(8,1,-1):
        row=[]
        for x in range(2,9):
            t=tiles[y*11+x];symbol='.' if t[2] else '?'
            if t[3]:symbol='C' if t[3]>0 else 'c'  # Public starting cities are known.
            if t[6]:
                symbol='A' if t[11]*11>1.5 else 'R' if t[13]*11>1.5 else 'W'
                if t[6]<0:symbol=symbol.lower()
                symbol+=format(float(t[7]), 'g')
            row.append(symbol.rjust(3))
        text.append(str(y)+' '+' '.join(row))
    text.append('   '+' '.join(str(x).rjust(3) for x in range(2,9)))
    text.append('Legal: '+', '.join(f'{int(i)}={describe(int(i))}' for i in available))
    return '\n'.join(text)


def play(root,manifest,worker,destination,seat,first,seed,difficulty):
    root,manifest,worker,destination=(Path(p).absolute() for p in (root,manifest,worker,destination))
    if (root.resolve()!=root or json.loads((root/ROOT_MARKER).read_text()).get('owner')!='BlockNations.LocalTraining.v1' or
        manifest.resolve()!=manifest or not manifest.is_relative_to(root/'runs') or destination.resolve()!=destination or
        not destination.is_relative_to(root/'benchmarks') or destination.exists()):
        raise ValueError('Choose a new owned diagnostic folder and an immutable owned model.')
    if size(root)>20_000_000_000-512_000_000:raise ValueError('Preserve the shared final-save reserve.')
    actor,identity=frozen_actor(manifest,CORE_THREE_RULES)
    destination.mkdir(parents=True)
    configuration=dict(owner=OWNER,version=1,model=str(manifest),identity=identity,seat=seat,firstSeat=first,seed=seed,
        difficulty=difficulty,boardSize=7,vision='normal-fog',controller='manual-console-fair-observation',
        manifestSha256=hashlib.sha256(manifest.read_bytes()).hexdigest())
    (destination/'session.json').write_text(json.dumps(configuration,indent=2)+'\n')
    env=DotNetEnvironment(worker,destination,1,seed,7,False,2,uuid.uuid4().hex,first_seat=first,
        rules_version=CORE_THREE_RULES,observation_version=actor.observation_version)
    random=np.random.default_rng(seed)
    last_round=1;turns=0;human_choices=0
    journal=(destination/'decisions.jsonl').open('w')
    try:
        env.reset()
        while True:
            _,terminal=env.get_steps(env.names[seat])
            if len(terminal):
                result=dict(configuration,state='completed',winner=None if terminal.interrupted[0] else
                    seat if terminal.reward[0]>0 else 1-seat,interrupted=bool(terminal.interrupted[0]),
                    rounds=last_round,humanChoices=human_choices,allChoices=turns,rejections=sum(s['rejections'] for s in env.stats))
                (destination/'result.json').write_text(json.dumps(result,indent=2)+'\n')
                print('RESULT '+json.dumps({k:result[k] for k in ('winner','interrupted','rounds','humanChoices','allChoices','rejections')}),flush=True)
                return result
            for owner,name in enumerate(env.names):
                decisions,_=env.get_steps(name)
                if not len(decisions):continue
                vector=decisions.obs[0][0];mask=decisions.action_mask[0][0];available=np.flatnonzero(~mask)
                last_round=max(last_round,round(float(vector[3114])*100))
                if owner==seat:
                    print(fair_view(vector,available),flush=True)
                    while True:
                        try:choice=int(input('ACTION> '))
                        except ValueError:print('Enter one legal encoded action.',flush=True);continue
                        if choice in available:break
                        print('That action is masked; choose from the current list.',flush=True)
                    probabilities=None;human_choices+=1
                else:
                    probabilities=evaluation_weights(actor.probabilities(decisions.obs[0],decisions.action_mask[0]),difficulty)[0]
                    choice=int(random.choice(len(probabilities),p=probabilities))
                journal.write(json.dumps(dict(controller='manual' if owner==seat else 'frozen',seat=owner,
                    vector=base64.b64encode(vector.astype('<f4').tobytes()).decode(),available=available.tolist(),
                    choice=choice,probabilities=probabilities.tolist() if probabilities is not None else None))+'\n')
                journal.flush();turns+=1
                env.set_actions(name,ActionTuple(discrete=np.array([[choice]],dtype=np.int32)))
            env.step()
    finally:
        journal.close();env.close()


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for key in ('root','manifest','worker','destination'):parser.add_argument('--'+key,type=Path,required=True)
    parser.add_argument('--seat',type=int,choices=(0,1),default=0)
    parser.add_argument('--first-seat',type=int,choices=(0,1),default=0)
    parser.add_argument('--seed',type=int,default=140101)
    parser.add_argument('--difficulty',choices=('Policy','Easy','Medium','Hard'),default='Hard')
    args=parser.parse_args();torch.set_num_threads(1)
    play(args.root,args.manifest,args.worker,args.destination,args.seat,args.first_seat,args.seed,args.difficulty)
