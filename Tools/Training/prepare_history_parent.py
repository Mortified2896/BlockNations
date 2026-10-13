"""Make an owned, function-preserving v3 parent for a memory-only v3/v4 trial."""
import hashlib
import json
from pathlib import Path
import shutil

from information_study import prepare_arm
from model_contract import behavior
from supervisor import atomic_json
from training_contract import run_rules_version
from training_opponents import owned_frozen_path, validate_recipe


def prepare(root, parent, recipe_run, worker, name, *, snapshot_recipe=None):
    from mlagents.torch_utils import torch
    from evaluate_suite import frozen_actor
    root,parent,recipe_run,worker=map(Path,(root,parent,recipe_run,worker))
    original=json.loads(parent.read_text())
    before=hashlib.sha256(Path(original['checkpoint']).read_bytes()).hexdigest()
    if before!=original['sha256']:raise ValueError('Protected parent changed.')
    run,_=prepare_arm(root,name,parent,recipe_run,worker,250000,71,3)
    if snapshot_recipe is not None:
        for entry in snapshot_recipe['opponents']:
            if entry['kind']!='frozen-policy':continue
            source=owned_frozen_path(recipe_run,entry['checkpoint']);config=owned_frozen_path(recipe_run,entry['trainerConfig'])
            folder=run/Path(entry['checkpoint']).parent
            if not folder.exists():
                folder.mkdir(parents=True)
                for path in (source,config,source.parent/'manifest.json'):shutil.copyfile(path,folder/path.name)
                receipt=json.loads((folder/'manifest.json').read_text())
                receipt.update(checkpoint=str(folder/source.name),trainerConfig=str(folder/config.name))
                atomic_json(folder/'manifest.json',receipt)
        validate_recipe(run,snapshot_recipe,run_rules_version(run))
        atomic_json(run/'training-opponents.json',snapshot_recipe)
    state=torch.load(run/'checkpoints'/name/behavior(3)/'checkpoint.pt',map_location='cpu')
    step=int(state['global_step']['_GlobalSteps__global_step'].item())
    if step!=original['step']:raise ValueError('Prepared parent changed the learner step.')
    folder=run/'frozen-evaluations/history-parent';folder.mkdir()
    checkpoint=folder/(behavior(3)+'-'+str(step)+'.pt')
    shutil.copyfile(run/'checkpoints'/name/behavior(3)/'checkpoint.pt',checkpoint)
    config=folder/'trainer.yaml';shutil.copyfile(run/'trainer.yaml',config)
    manifest=dict(original,schema=3,observationVersion=3,observationSize=3259,checkpoint=str(checkpoint),trainerConfig=str(config),
        sha256=hashlib.sha256(checkpoint.read_bytes()).hexdigest(),trainerConfigSha256=hashlib.sha256(config.read_bytes()).hexdigest(),
        preparation='zero-column v2-to-v3 extension; no extra learning',protectedParentManifest=str(parent),protectedParentSha256=before)
    path=folder/'manifest.json';atomic_json(path,manifest)
    actor,identity=frozen_actor(path,run_rules_version(run));manifest['actorId']=identity['actorId'];atomic_json(path,manifest)
    assert hashlib.sha256(Path(original['checkpoint']).read_bytes()).hexdigest()==before
    return run,path
