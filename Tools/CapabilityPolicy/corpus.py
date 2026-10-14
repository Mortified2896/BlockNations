"""Bounded fair training corpus; held-out matches stay out of distillation updates."""
import hashlib
import json
from pathlib import Path
import zipfile

import numpy as np

from policy import OBSERVATIONS, ACTIONS, ROLE_OFFSET

OWNER = 'BlockNations.CapabilityTrainingCorpus.v1'
FIELDS = ('observations', 'allowed', 'teacherProbabilities', 'teacherValues', 'validation', 'group')


def group(seed, worker, match, reset):
    digest = hashlib.sha256(f'{seed}:{worker}:{match}:{reset}'.encode()).digest()
    return int.from_bytes(digest[:8], 'big'), digest[8] % 5 == 0


def validate(arrays):
    if set(arrays) != set(FIELDS):
        raise ValueError('Unexpected corpus fields.')
    values, allowed, target, value, held, groups = (arrays[name] for name in FIELDS)
    count = len(values)
    if (not 128 <= count <= 16384 or values.shape != (count, OBSERVATIONS) or values.dtype != np.float32 or
            allowed.shape != (count, ACTIONS) or allowed.dtype != np.bool_ or
            target.shape != allowed.shape or target.dtype != np.float32 or
            value.shape != (count,) or value.dtype != np.float32 or
            held.shape != (count,) or held.dtype != np.bool_ or
            groups.shape != (count,) or groups.dtype != np.uint64 or
            not all(np.isfinite(array).all() for array in (values, target, value)) or
            not allowed[:, -1].all() or not (target >= 0).all() or
            not np.all(target[~allowed] == 0) or not np.allclose(target.sum(axis=1), 1, rtol=1e-5, atol=1e-6) or
            not np.all(values[:, 3119] == 2) or not np.all(values[:, 3258] == 3) or not np.all(values[:, -1] == 4) or
            not np.isin(values[:, ROLE_OFFSET], (0, 1)).all()):
        raise ValueError('Corpus observations, labels, legal masks or contracts are invalid.')
    tiles = values[:, :2904].reshape(count, 121, 24)
    if np.any((tiles[:, :, 6] < 0) & (tiles[:, :, 2] == 0)):
        raise ValueError('Current hidden enemy units cannot enter the fair corpus.')
    train_groups, held_groups = np.unique(groups[~held]), np.unique(groups[held])
    multi=allowed.sum(axis=1)>1
    if (min(len(train_groups), len(held_groups)) < 2 or np.intersect1d(train_groups, held_groups).size or
            min(np.count_nonzero(held), np.count_nonzero(~held)) < 32 or
            min(np.count_nonzero(multi&held),np.count_nonzero(multi&~held))<32):
        raise ValueError('Use a distinct multi-match validation partition, never a row-wise split.')
    return dict(decisions=count, trainingDecisions=int((~held).sum()), validationDecisions=int(held.sum()),
                trainingGroups=len(train_groups), validationGroups=len(held_groups),
                multiChoiceDecisions=int((allowed.sum(axis=1) > 1).sum()))


def load(root, manifest):
    root, manifest = (Path(path).absolute() for path in (root, manifest))
    if (manifest.resolve() != manifest or not manifest.is_relative_to(root/'benchmarks') or
            manifest.stat().st_size > 128_000):
        raise ValueError('Use a bounded owned training-corpus manifest.')
    record = json.loads(manifest.read_text())
    if (record.get('owner') != OWNER or record.get('state') != 'completed' or
            record.get('partition') != 'training' or record.get('observationVersion') != 4 or
            record.get('diagnosticFullVisionSeats') != 0 or record.get('humanIngestion') != 'none'):
        raise ValueError('Held-out benchmark/replay data cannot initialize the learner.')
    path = Path(record['corpus']).absolute()
    if path.resolve() != path or path.parent != manifest.parent or path.stat().st_size > 400_000_000:
        raise ValueError('Corpus must remain bounded beside its own manifest.')
    if hashlib.sha256(path.read_bytes()).hexdigest() != record.get('corpusSha256'):
        raise ValueError('Training corpus changed after collection.')
    with zipfile.ZipFile(path) as archive:
        if (len(archive.infolist()) != len(FIELDS) or set(archive.namelist()) != {name+'.npy' for name in FIELDS} or
                sum(item.file_size for item in archive.infolist()) > 400_000_000):
            raise ValueError('Corpus expanded data exceeds its bounded tensor contract.')
    with np.load(path, allow_pickle=False) as document:
        arrays = {name: document[name] for name in FIELDS}
    measured = validate(arrays)
    if any(record.get(key) != value for key, value in measured.items()):
        raise ValueError('Corpus counts disagree with their complete tensor data.')
    return arrays, record
