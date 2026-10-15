"""Frozen student rollouts for new expert-labelled training experience."""
import hashlib
import math
from pathlib import Path

import numpy as np
import torch

from artifacts import checkpoint
from initialization import read, validate, OWNER as PREPARED
from policy import probabilities


class FrozenStudent:
    def __init__(self, root, manifest, *, rules, board_size):
        root, manifest = (Path(path).absolute() for path in (root, manifest))
        if manifest.resolve() != manifest or not manifest.is_relative_to(root/'benchmarks'):
            raise ValueError('Use an owned prepared student, never an evaluation journal.')
        manifest_sha = hashlib.sha256(manifest.read_bytes()).hexdigest()
        record = read(manifest)
        expected = dict(rulesVersion=rules, boardSize=board_size,
                        width=record.get('width'), architecture=record.get('architecture'))
        if (type(expected['width']) is not int or expected['width'] not in (16, 32, 64) or
                not isinstance(expected['architecture'], str)):
            raise ValueError('Use an explicit compatible prepared student width.')
        prepared = record.get('owner') == PREPARED
        if prepared:
            validate(record, expected)
            path = Path(record['checkpoint']).absolute()
            if path.resolve() != path or path.parent != manifest.parent:
                raise ValueError('Student weights must stay beside their prepared manifest.')
            expected_sha = record.get('checkpointSha256')
        else:
            wanted = dict(owner='BlockNations.CapabilityPrototype.v1', state='finished', partition='training',
                          observationVersion=4, actionVersion=2, openingEconomyVersion=2, **expected)
            if any(record.get(key) != value for key, value in wanted.items()):
                raise ValueError('Use prepared weights or an explicitly completed compatible student run.')
            path = manifest.parent/'checkpoints/capability/BlockNationsSeatV4/checkpoint.pt'
            if path.resolve() != path:
                raise ValueError('Completed student weights must remain in their owned run.')
            expected_sha = record.get('checkpoint', {}).get('checkpointSha256')
        self.actor, health = checkpoint(path, expected['width'], prepared=prepared,
                                        architecture=expected['architecture'])
        if (health['checkpointSha256'] != expected_sha or not prepared and health != record['checkpoint'] or
                hashlib.sha256(manifest.read_bytes()).hexdigest() != manifest_sha):
            raise ValueError('Frozen rollout student changed during loading.')
        self.actor.eval(); self.actor.requires_grad_(False)
        self.identity = dict(architecture=expected['architecture'], width=expected['width'],
            manifest=str(manifest), manifestSha256=manifest_sha, step=health['step'],
            checkpointSha256=health['checkpointSha256'],
            kind='prepared-frozen-student' if prepared else 'completed-frozen-student')

    def probabilities(self, observations, allowed):
        with torch.no_grad():
            return probabilities(self.actor, torch.as_tensor(observations),
                                 torch.as_tensor(allowed, dtype=torch.float32)).numpy()


def behaviour_distribution(teacher, student, teacher_fraction):
    """Mix actions only. The expert labels always remain the original teacher."""
    if (type(teacher_fraction) not in (int, float) or not math.isfinite(teacher_fraction) or
            not 0 <= teacher_fraction <= 1 or teacher.shape != student.shape or
            teacher.ndim != 2 or teacher.shape[1] != 259 or
            not np.isfinite(teacher).all() or not np.isfinite(student).all() or
            (teacher < 0).any() or (student < 0).any() or
            not np.allclose(teacher.sum(axis=1), 1) or not np.allclose(student.sum(axis=1), 1)):
        raise ValueError('Use normalized frozen policies and a bounded explicit mixture.')
    return (teacher_fraction*teacher+(1-teacher_fraction)*student).astype(np.float32)
