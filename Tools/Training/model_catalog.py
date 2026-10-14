"""Owned run/model discovery for explicit private training and playtest choices."""
import hashlib
import json
from pathlib import Path
import re
import time

from model_contract import behavior, SIZES
from training_contract import SUPPORTED_RULES

OWNER = 'BlockNations.TrainingSelector.v1'
_digests = {}


def read(path):
    try:
        if path.is_symlink() or path.stat().st_size > 256000: return {}
        return json.loads(path.read_text())
    except (OSError, ValueError): return {}


def digest(path):
    stat = path.stat(); key = (str(path), stat.st_size, stat.st_mtime_ns)
    if key not in _digests:
        if len(_digests) > 2048: _digests.clear()
        _digests[key] = hashlib.sha256(path.read_bytes()).hexdigest()
    return _digests[key]


def owned(root, path):
    path = Path(path).absolute()
    if path.resolve() != path or not path.is_relative_to(root/'runs'):
        raise ValueError('Choose an owned, unlinked training artifact.')
    return path


def vision(document):
    mask = document.get('diagnosticFullVisionSeats', 0)
    if type(mask) is not int or mask not in (0, 3):
        raise ValueError('Only symmetric fog or full vision can be selected.')
    return 'FullVision' if mask == 3 else 'Fog'


def discover(root):
    root = Path(root).absolute()
    from supervisor import ROOT_MARKER
    if root.resolve()!=root or read(root/ROOT_MARKER).get('owner') != 'BlockNations.LocalTraining.v1':
        raise ValueError('Choose the marked shared training root.')
    runs, models = [], []
    study_runs = {}
    from power_controller import command_for
    for path in (root/'benchmarks').glob('*/study.json'):
        study=read(path);current=study.get('currentRun');pid=study.get('trainerPid',0)
        if study.get('state')=='running' and current and type(pid) is int and pid>0:
            command=command_for(pid)
            if 'rated_training.py' in command and '--run-id '+Path(current).name+' ' in command+' ':
                study_runs[Path(current).name]=True
    for run in sorted((root/'runs').iterdir()):
        if not run.is_dir() or run.is_symlink(): continue
        document = read(run/'run.json'); schema = document.get('schema')
        if (document.get('owner') != 'BlockNations.LocalTraining.v1' or schema not in SIZES or
                document.get('boardSize') != 7 or document.get('rulesVersion') not in SUPPORTED_RULES or
                'recipe' in document.get('experiment', '') or document.get('experiment') == 'training-selector-session'):
            continue
        try:
            training_vision = vision(document)
            status = read(run/'supervisor-status.json'); arena = read(run/'arena-status.json')
            pid = status.get('supervisorPid', 0)
            command = command_for(pid) if type(pid) is int and pid > 0 else ''
            active = '--run-id '+run.name+' ' in command+' ' and '--root '+str(root) in command or run.name in study_runs
            state = 'running (study)' if run.name in study_runs else status.get('state', 'prepared') if active else 'stopped' if status else 'prepared'
            model_ids = []
            candidates = []
            champion = read(run/'playtest-champion.json')
            if champion.get('candidateManifest'):
                candidates.append((run/champion['candidateManifest'], 'Evaluated champion'))
            folder = run/'checkpoints'/run.name/behavior(schema)
            rolling = sorted((p for p in folder.glob(behavior(schema)+'-*.pt') if re.fullmatch(r'BlockNationsSeatV[234]-\d+\.pt',p.name)), key=lambda p:int(p.stem.rsplit('-',1)[1]), reverse=True)
            candidates.extend((p, 'Latest checkpoint' if i == 0 else 'Saved checkpoint') for i,p in enumerate(rolling[:3]))
            frozen = sorted((run/'frozen-evaluations').glob('*/manifest.json'), key=lambda p:p.stat().st_mtime, reverse=True)
            candidates.extend((p, 'Frozen checkpoint') for p in frozen[:5])
            seen = set()
            for path, selection in candidates:
                try:
                    metadata = read(owned(root,path)) if path.suffix == '.json' else document
                    checkpoint = owned(root,metadata['checkpoint']) if path.suffix == '.json' else owned(root,path)
                    config = owned(root,metadata['trainerConfig']) if path.suffix == '.json' else run/'trainer.yaml'
                    match = re.fullmatch(r'BlockNationsSeatV([234])-(\d+)\.pt',checkpoint.name)
                    if not match or checkpoint in seen or not checkpoint.is_file() or checkpoint.stat().st_size > 100000000: continue
                    model_schema, step = map(int,match.groups())
                    # A copied parent is initialization, not a model trained under a new recipe.
                    floor = document.get('weightTransfer',{}).get('parentStep',0)
                    if step <= floor: continue
                    if path.suffix != '.json':
                        export=checkpoint.with_suffix('.onnx')
                        if not export.is_file() or export.is_symlink() or time.time()-export.stat().st_mtime < 2: continue
                    sha = digest(checkpoint)
                    if path.suffix == '.json' and sha != metadata.get('sha256'): continue
                    plan=read(config)
                    if behavior(model_schema) not in plan.get('behaviors',{}): continue
                    trained=vision(metadata)
                    identifier=hashlib.sha256((str(checkpoint.relative_to(root))+sha+digest(config)).encode()).hexdigest()
                    model=dict(id=identifier,runId=run.name,name=checkpoint.stem,step=step,schema=model_schema,
                        trainingVision=trained,selection=selection,checkpointSha256=sha,configSha256=digest(config),
                        checkpoint=str(checkpoint),trainerConfig=str(config),boardSize=7,
                        rulesVersion=metadata.get('rulesVersion',document['rulesVersion']))
                    if model['rulesVersion'] not in SUPPORTED_RULES: continue
                    models.append(model);model_ids.append(identifier);seen.add(checkpoint)
                except (OSError,ValueError,KeyError): continue
            runs.append(dict(id=run.name,label=run.name,trainingVision=training_vision,state=state,active=active,
                paused=bool(arena.get('paused')) if active else False,decisions=arena.get('decisions',0),
                games=arena.get('games',0),modelCount=len(model_ids),latestStep=max((m['step'] for m in models if m['runId']==run.name),default=0)))
        except (OSError,ValueError): continue
    return dict(owner=OWNER,version=1,updatedUnix=time.time(),runs=runs,models=models)


def resolve(root, identifier):
    if not isinstance(identifier,str) or not re.fullmatch('[a-f0-9]{64}',identifier):
        raise ValueError('Choose a model from the current catalog.')
    selected=next((m for m in discover(root)['models'] if m['id']==identifier),None)
    if selected is None: raise ValueError('That checkpoint is unavailable or has changed. Refresh the model selection.')
    from evaluate_policy import FrozenActor
    actor=FrozenActor(Path(selected['checkpoint']),read(Path(selected['trainerConfig']))['behaviors'][behavior(selected['schema'])]['network_settings'])
    if actor.sha256 != selected['checkpointSha256']: raise ValueError('Selected model changed before loading.')
    return selected
