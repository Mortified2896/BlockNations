"""Install private native controls with separately owned fog/full-vision resumptions."""
import argparse
import json
import os
from pathlib import Path
import plistlib
import subprocess
import sys

from information_study import prepare_arm
from model_catalog import OWNER, read
from playtest import atomic_json
from training_opponents import validate_recipe
from training_contract import run_rules_version


def install(root, player, worker):
    root,player,worker=(Path(p).absolute() for p in (root,player,worker))
    if not (player/'Contents/Info.plist').is_file() or not worker.is_file():
        raise ValueError('Build the updated native player and diagnostic worker before installation.')
    inputs=read(root/'benchmarks/fog-selfplay-inputs-20261014/inputs.json')
    parent,source=Path(inputs['parent']),Path(inputs['recipeRun'])
    runs=[]
    for suffix,mask in (('fog',0),('fullvision',3)):
        name='selfplay-'+suffix+'-20261014';run=root/'runs'/name
        if not run.exists():
            run,_=prepare_arm(root,name,parent,source,worker,1000000,81,2,fog_diagnostic=True,full_vision_seats=mask)
            marker=read(run/'run.json');marker.update(selectorManaged=True,selfPlayOnly=True)
            atomic_json(run/'run.json',marker)
        marker=read(run/'run.json')
        if not marker.get('selectorManaged') or marker.get('diagnosticFullVisionSeats')!=mask:
            raise ValueError('A different run occupies the selector ID; it was preserved.')
        recipe=validate_recipe(run,read(run/'training-opponents.json'),run_rules_version(run))
        if recipe['probability']!=0:raise ValueError('Selector continuations require pure self-play.')
        runs.append(dict(id=name,command=[sys.executable,str(Path(__file__).with_name('supervisor.py')), '--root',str(root),
            '--run-id',name,'--resume','--hours','6','--budget-gb','20','--board-size','7','--backend','dotnet',
            '--env',str(player),'--worker',str(worker),'--parallel-games','8','--torch-threads','1','--no-auto-viewer']))
    main=read(root/'power-controller.json')
    if not main.get('command'):raise ValueError('The original main training launcher is missing.')
    # The main recipe/controller is not changed by installation. A deliberate
    # Start/resume selection transfers scheduling to the chooser and preserves it.
    runs.insert(0,dict(id=main['runId'],command=main['command']))
    host=root/'runs/training-selector-session';host.mkdir(exist_ok=True)
    marker=read(host/'run.json')
    if marker and marker.get('experiment')!='training-selector-session':raise ValueError('Session directory is unrelated.')
    atomic_json(host/'run.json',dict(owner='BlockNations.LocalTraining.v1',experiment='training-selector-session',schema=2,
        observationSize=3120,actionCount=259,boardSize=7,seed=81,rulesVersion=run_rules_version(source)))
    atomic_json(host/'supervisor-status.json',dict(state='playtesting',checkpointCount=0,trainerPid=0))
    config=root/'training-selector-config.json'
    atomic_json(config,dict(owner=OWNER,version=1,runs=runs,sessionRun=str(host),viewerRun=main['runId'],player=str(player),worker=str(worker)))
    launch=Path.home()/'Library/LaunchAgents/com.blocknations.training-selector.plist'
    descriptor=dict(Label='com.blocknations.training-selector',RunAtLoad=True,KeepAlive=True,ProcessType='Standard',
        ProgramArguments=[sys.executable,str(Path(__file__).with_name('training_selector.py')),'--root',str(root),'--config',str(config)],
        StandardOutPath=str(root/'training-selector.log'),StandardErrorPath=str(root/'training-selector.log'))
    launch.write_bytes(plistlib.dumps(descriptor))
    domain='gui/'+str(os.getuid())
    subprocess.run(['launchctl','bootout',domain+'/'+descriptor['Label']],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    subprocess.run(['launchctl','bootstrap',domain,str(launch)],check=True)
    print(json.dumps(dict(config=str(config),runs=[r['id'] for r in runs],player=str(player),trainingStarted=False),indent=2))


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    for field in ('root','player','worker'):parser.add_argument('--'+field,type=Path,required=True)
    args=parser.parse_args();install(args.root,args.player,args.worker)
