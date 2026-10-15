"""Copy an audited retained league into a prototype without importing journals."""
import copy
import hashlib
import json
from pathlib import Path
import shutil

from frozen_policy import FrozenPolicy
from model_contract import spec
from supervisor import ROOT_MARKER, atomic_json, size
from training_opponents import load_recipe, owned_frozen_path, recipe_id

OWNER = 'BlockNations.CapabilityLeagueInstall.v1'


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path):
    path = Path(path)
    if path.resolve() != path or not path.is_file() or path.stat().st_size > 128_000:
        raise ValueError('League metadata must be bounded, ordinary owned files.')
    return json.loads(path.read_text())


def check_actors(run, recipe):
    for entry in recipe['opponents']:
        if entry['kind'] != 'frozen-policy':
            continue
        config = json.loads(owned_frozen_path(run, entry['trainerConfig']).read_text())
        FrozenPolicy(owned_frozen_path(run, entry['checkpoint']),
                     config['behaviors']['BlockNationsSeatV2']['network_settings'], spec(2),
                     entry['checkpointSha256'], entry['actorId'])


def identity(run, contract):
    run = Path(run)
    receipt = run/'retained-opponents.json'
    if not receipt.exists():
        if (run/'training-opponents.json').exists():
            raise ValueError('A prototype league requires its immutable install receipt.')
        return None
    saved = read(receipt)
    files = saved.get('files')
    if (saved.get('owner') != OWNER or saved.get('contract') != contract or
            not isinstance(files, dict) or not 3 <= len(files) <= 27):
        raise ValueError('Retained league belongs to another prototype contract.')
    for relative, sha in files.items():
        path = run/relative
        if (Path(relative).is_absolute() or path.resolve() != path or
                not path.is_relative_to(run) or not path.is_file() or
                path.stat().st_size > 100_000_000 or digest(path) != sha):
            raise ValueError('An installed retained opponent or recipe changed.')
    recipe = load_recipe(run, contract['rulesVersion'])
    if recipe is None or recipe['version'] != 2 or recipe_id(recipe) != saved.get('leagueId'):
        raise ValueError('Installed league definition no longer matches its identity.')
    check_actors(run, recipe)
    return dict(leagueId=saved['leagueId'], installationSha256=digest(receipt),
                sourceRecipeSha256=saved['sourceRecipeSha256'], probability=recipe['probability'])


def install(root, source, run, contract):
    root, source, run = (Path(p).absolute() for p in (root, source, run))
    if (root.resolve() != root or read(root/ROOT_MARKER).get('owner') != 'BlockNations.LocalTraining.v1' or
            source.resolve() != source or run.resolve() != run or source == run or
            not any(source.is_relative_to(root/area) and source != root/area for area in ('runs', 'benchmarks')) or
            not run.is_relative_to(root/'benchmarks') or run == root/'benchmarks'):
        raise ValueError('Copy a retained training league within the owned training root.')
    metadata = read(source/'run.json')
    wanted = dict(owner='BlockNations.LocalTraining.v1', boardSize=contract['boardSize'],
                  rulesVersion=contract['rulesVersion'], openingEconomyVersion=2)
    if any(metadata.get(key) != value for key, value in wanted.items()):
        raise ValueError('League source belongs to different rules, board or opening.')
    original = (source/'training-opponents.json').read_bytes()
    recipe = copy.deepcopy(load_recipe(source, contract['rulesVersion']))
    if recipe is None or recipe['version'] != 2:
        raise ValueError('Use a bounded version-two retained-opponent league.')
    check_actors(source, recipe)
    if any((run/name).exists() for name in ('run.json', 'training-opponents.json', 'retained-opponents.json')):
        raise ValueError('Never overwrite an existing prototype league.')
    inputs = set()
    for entry in recipe['opponents']:
        if entry['kind'] == 'frozen-policy':
            checkpoint = owned_frozen_path(source, entry['checkpoint'])
            inputs.update((checkpoint, owned_frozen_path(source, entry['trainerConfig']), checkpoint.parent/'manifest.json'))
    estimate = sum(path.stat().st_size for path in inputs)+1_000_000
    if size(root)+estimate > 19_488_000_000 or shutil.disk_usage(root).free < 20_512_000_000+estimate:
        raise RuntimeError('Preserve shared storage and the league-copy/final-save reserve.')
    copied = set()
    for entry in recipe['opponents']:
        if entry['kind'] != 'frozen-policy':
            continue
        checkpoint = owned_frozen_path(source, entry['checkpoint'])
        config = owned_frozen_path(source, entry['trainerConfig'])
        if checkpoint.parent != config.parent:
            raise ValueError('Retained weights and configuration need one frozen folder.')
        target = run/checkpoint.parent.relative_to(source)
        if target.resolve() != target:
            raise ValueError('Retained destinations cannot contain linked directories.')
        target.mkdir(parents=True, exist_ok=True)
        for path in (checkpoint, config, checkpoint.parent/'manifest.json'):
            if path.resolve() != path or not path.is_file() or path.stat().st_size > 100_000_000:
                raise ValueError('Copy only bounded ordinary frozen policy files.')
            destination = target/path.name
            if not destination.exists():
                destination.write_bytes(path.read_bytes())
            copied.add(destination.relative_to(run))
        manifest = read(target/'manifest.json')
        manifest.update(checkpoint=str(target/checkpoint.name), trainerConfig=str(target/config.name))
        atomic_json(target/'manifest.json', manifest)
        entry['checkpoint'] = str((target/checkpoint.name).relative_to(run))
        entry['trainerConfig'] = str((target/config.name).relative_to(run))
    atomic_json(run/'run.json', dict(wanted, schema=4, runId=run.name,
        architecture=contract.get('architecture'), behavior='BlockNationsSeatV4',
        observationSize=4712, actionCount=259, experiment='capability-retained-opponents'))
    atomic_json(run/'training-opponents.json', recipe)
    (run/'retained-opponents-source.json').write_bytes(original)
    copied.update(map(Path, ('run.json', 'training-opponents.json', 'retained-opponents-source.json')))
    if (source/'training-opponents.json').read_bytes() != original:
        raise ValueError('Source league changed during preparation.')
    atomic_json(run/'retained-opponents.json', dict(owner=OWNER, contract=contract,
        sourceRun=str(source), sourceRecipeSha256=hashlib.sha256(original).hexdigest(),
        leagueId=recipe_id(recipe), files={str(path):digest(run/path) for path in sorted(copied)}))
    return identity(run, contract)
