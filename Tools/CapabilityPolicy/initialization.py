"""Explicit new-run weight initialization, separate from positive-step resume."""
import hashlib
import json
from pathlib import Path
import shutil

from artifacts import checkpoint
from policy import ARCHITECTURE

OWNER = 'BlockNations.CapabilityInitialization.v1'
INSTALLED = 'BlockNations.CapabilityInitializationInstall.v1'


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path):
    path = Path(path).absolute()
    if path.resolve() != path or not path.is_file() or path.stat().st_size > 128_000:
        raise ValueError('Use a bounded unlinked initialization manifest.')
    return json.loads(path.read_text())


def validate(document, expected):
    wanted = dict(owner=OWNER, state='prepared', partition='training',
                  architecture=ARCHITECTURE, observationVersion=4, actionVersion=2,
                  openingEconomyVersion=2, kind='teacher-distillation', **expected)
    if any(document.get(key) != value for key, value in wanted.items()):
        raise ValueError('Prepared initialization belongs to a different architecture, rules or recipe.')


def identity(run, expected):
    run = Path(run)
    path = run/'initialization.json'
    if not path.exists():
        if (run/'initialization').exists():
            raise ValueError('Incomplete initialization must be preserved for review.')
        return None
    record = read(path)
    document = read(run/'initialization/source-manifest.json')
    validate(document, expected)
    prepared = run/'initialization/prepared.pt'
    _, health = checkpoint(prepared, expected['width'], prepared=True)
    if (record.get('owner') != INSTALLED or record.get('sourceManifestSha256') != digest(run/'initialization/source-manifest.json') or
            record.get('checkpointSha256') != health['checkpointSha256'] or
            document.get('checkpointSha256') != health['checkpointSha256'] or
            record.get('sourceCheckpoint') != document.get('checkpoint')):
        raise ValueError('Installed initialization content changed.')
    return dict(kind=document['kind'], sourceManifestSha256=record['sourceManifestSha256'],
                checkpointSha256=health['checkpointSha256'], installationSha256=digest(path))


def install(root, manifest, run, expected):
    root, manifest, run = (Path(path).absolute() for path in (root, manifest, run))
    if (root.resolve()!=root or json.loads((root/'.blocknations-training-root.json').read_text()).get('owner')!='BlockNations.LocalTraining.v1' or
            manifest.resolve() != manifest or not manifest.is_relative_to(root/'benchmarks') or
            run.resolve() != run or not run.is_relative_to(root/'benchmarks')):
        raise ValueError('Initialization and the new trial must belong to the shared benchmark root.')
    document = read(manifest)
    validate(document, expected)
    source = Path(document['checkpoint']).absolute()
    if source.resolve() != source or source.parent != manifest.parent:
        raise ValueError('Prepared weights must belong to their initialization manifest directory.')
    _, health = checkpoint(source, expected['width'], prepared=True)
    if health['checkpointSha256'] != document.get('checkpointSha256'):
        raise ValueError('Prepared initialization checkpoint changed.')
    folder = run/'initialization'; folder.mkdir(exist_ok=False)
    shutil.copy2(manifest, folder/'source-manifest.json')
    shutil.copy2(source, folder/'prepared.pt')
    target = run/'checkpoints/capability/BlockNationsSeatV4'
    target.mkdir(parents=True, exist_ok=False)
    shutil.copy2(source, target/'checkpoint.pt')
    record = dict(owner=INSTALLED, sourceManifest=str(manifest), sourceCheckpoint=str(source),
                  sourceManifestSha256=digest(manifest), checkpointSha256=health['checkpointSha256'])
    (run/'initialization.json').write_text(json.dumps(record, indent=2)+'\n')
    return identity(run, expected)
