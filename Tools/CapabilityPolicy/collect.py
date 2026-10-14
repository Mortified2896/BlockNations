"""Generate new teacher/self-play experience; never import diagnostic journals."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import shutil
import sys
import time
import uuid
from importlib.metadata import version

import numpy as np
import torch

sys.path.insert(0, str(Path(__file__).parents[1]/'Training'))
from evaluate_suite import frozen_actor, worker_digest
from study_report import manifest as checked_manifest
from model_contract import spec, behavior
from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import ValueNetwork
from dotnet_environment import DotNetEnvironment
from mlagents_envs.base_env import ActionTuple
from supervisor import ROOT_MARKER, atomic_json, size
from information_study_runtime import PowerGuard
from train import exclusive
from corpus import OWNER, group, validate
from playtest_sampling import PRESETS, sampling_weights


def teacher_distribution(probabilities, allowed, mode):
    """Label and roll out the same explicitly chosen deployment policy."""
    values = np.asarray(probabilities)
    mask = np.asarray(allowed)
    if (mode not in ('Policy', *PRESETS) or values.ndim != 2 or values.shape[1] != 259 or
            mask.shape != values.shape or mask.dtype != np.bool_ or
            not np.isfinite(values).all() or (values < 0).any() or
            np.any(values[~mask] != 0) or np.any(values.sum(axis=1) <= 0)):
        raise ValueError('Teacher labels require finite legal probabilities and an explicit sampler.')
    target = values / values.sum(axis=1, keepdims=True)
    return (target if mode == 'Policy' else sampling_weights(target, mode)).astype(np.float32)


def collect(root, teacher, worker, destination, *, decisions=8192, seed=21501501,
            teacher_mode='Policy', action_noise=.2):
    root, teacher, worker, destination = (Path(path).absolute() for path in (root, teacher, worker, destination))
    estimate = decisions*(4712*4+259*5+32)
    if (root.resolve() != root or json.loads((root/ROOT_MARKER).read_text()).get('owner') != 'BlockNations.LocalTraining.v1' or
            destination.resolve() != destination or not destination.is_relative_to(root/'benchmarks') or
            destination == root/'benchmarks' or decisions not in (4096, 8192, 16384) or not 0 <= seed < 2**31 or
            teacher_mode not in ('Policy', *PRESETS) or type(action_noise) not in (int, float) or
            not math.isfinite(action_noise) or not 0 <= action_noise <= .2):
        raise ValueError('Use a new bounded owned corpus and an explicit recorded seed.')
    model = checked_manifest(root, teacher)
    if version('mlagents') != '1.1.0':
        raise RuntimeError('Teacher collection requires audited ML-Agents 1.1.0.')
    actor, identity = frozen_actor(teacher, model['rulesVersion'])
    plan = json.loads(Path(model['trainerConfig']).read_text())['behaviors'][behavior(actor.observation_version)]
    if plan['hyperparameters'].get('shared_critic'):
        raise ValueError('This teacher adapter requires its recorded separate extrinsic critic.')
    critic = ValueNetwork(['extrinsic'], spec(actor.observation_version).observation_specs, NetworkSettings(**plan['network_settings']))
    saved = torch.load(model['checkpoint'], map_location='cpu')
    critic.load_state_dict(saved['Optimizer:critic'], strict=True); critic.eval()
    if any(not torch.isfinite(tensor).all() for tensor in critic.state_dict().values()):
        raise ValueError('Teacher value network has non-finite weights.')
    sources=list(Path(__file__).parent.glob('*.py'))+list((Path(__file__).parents[1]/'Training').glob('*.py'))
    pins={str(path):hashlib.sha256(path.read_bytes()).hexdigest() for path in sources}
    power = PowerGuard(True); next_storage = 0; deadline=time.monotonic()+600
    def guard():
        nonlocal next_storage
        power.check()
        if time.monotonic()>deadline or (destination/'stop.request').exists():
            raise RuntimeError('Bounded collection stop/time limit reached.')
        if time.monotonic() >= next_storage:
            next_storage = time.monotonic()+15
            if size(root)+estimate > 19_488_000_000 or shutil.disk_usage(root).free < 20_512_000_000+estimate:
                raise RuntimeError('Preserve shared storage and the corpus/final-save reserve.')
            if any(hashlib.sha256(Path(path).read_bytes()).hexdigest()!=sha for path,sha in pins.items()):
                raise RuntimeError('Pinned collection source changed; preserve its experiment identity.')
    guard()
    destination.mkdir(parents=True, exist_ok=False)
    record = dict(owner=OWNER, state='collecting', partition='training', humanIngestion='none',
                  observationVersion=4, actionVersion=2, diagnosticFullVisionSeats=0,
                  rulesVersion=model['rulesVersion'], boardSize=7, openingEconomyVersion=2,
                  teacherManifest=str(teacher), teacherManifestSha256=hashlib.sha256(teacher.read_bytes()).hexdigest(),
                  teacher=identity, teacherObservationVersion=actor.observation_version,
                  workerSha256=worker_digest(worker), seed=seed, requestedDecisions=decisions,
                  teacherSampling=dict(mode=teacher_mode,
                      settings=list(PRESETS[teacher_mode]) if teacher_mode in PRESETS else None),
                  valueLabels='fair critic of the unmodified teacher; approximate under changed sampling',
                  actionNoise=dict(kind='uniform-over-all-legal-actions', probability=action_noise), startedUnix=time.time())
    record.update(sources=pins,packages={name:version(name) for name in ('mlagents','torch','numpy')})
    arrays = dict(observations=np.empty((decisions, 4712), np.float32), allowed=np.empty((decisions, 259), np.bool_),
                  teacherProbabilities=np.empty((decisions, 259), np.float32), teacherValues=np.empty(decisions, np.float32),
                  validation=np.empty(decisions, np.bool_), group=np.empty(decisions, np.uint64))
    rollout = destination/'rollout'; rollout.mkdir()
    env = DotNetEnvironment(worker, rollout, 4, seed, 7, False, 2, uuid.uuid4().hex,
                           rules_version=model['rulesVersion'], observation_version=4)
    random = np.random.default_rng(seed); count = 0
    atomic_json(destination/'manifest.json', record)
    try:
        env.reset()
        while count < decisions:
            guard()
            for name in env.names:
                current, _ = env.get_steps(name)
                if not len(current): continue
                allowed = ~current.action_mask[0]
                p = actor.probabilities(current.obs[0], ~allowed)
                p = teacher_distribution(p, allowed, teacher_mode)
                with torch.no_grad():
                    labels = critic([torch.as_tensor(current.obs[0][:, :actor.observation_size])])[0]['extrinsic'].numpy()
                choices = []
                stats = {stat['worker']: stat for stat in env.stats}
                for i, agent in enumerate(current.agent_id):
                    if count < decisions:
                        stat = stats[int(agent)//2]
                        key, held = group(seed, stat['worker'], stat['match'], stat['trainerResets'])
                        arrays['observations'][count] = current.obs[0][i]
                        arrays['allowed'][count] = allowed[i]
                        arrays['teacherProbabilities'][count] = p[i]
                        arrays['teacherValues'][count] = labels[i]
                        arrays['validation'][count] = held; arrays['group'][count] = key
                        count += 1
                    choice = random.choice(np.flatnonzero(allowed[i])) if random.random() < action_noise else random.choice(259, p=p[i])
                    choices.append([choice])
                env.set_actions(name, ActionTuple(discrete=np.array(choices, dtype=np.int32)))
            env.step()
        if any(stat['rejections'] or stat.get('failure') for stat in env.stats):
            raise ValueError('Fair corpus generation rejected an action or failed.')
        record.update(validate(arrays))
        path = destination/'corpus.npz'
        with path.open('xb') as stream: np.savez_compressed(stream, **arrays)
        record.update(state='completed', corpus=str(path), corpusSha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                      rejections=0, collectionGames=sum(stat['games'] for stat in env.stats),
                      collectionInterruptions=sum(stat['interruptions'] for stat in env.stats))
    except BaseException as error:
        record.update(state='failed', error=type(error).__name__+': '+str(error), collectedDecisions=count)
        raise
    finally:
        env.close(); record['finishedUnix'] = time.time(); atomic_json(destination/'manifest.json', record)
    return record


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('root', 'teacher', 'worker', 'destination'): parser.add_argument('--'+name, type=Path, required=True)
    parser.add_argument('--decisions', type=int, choices=(4096, 8192, 16384), default=8192)
    parser.add_argument('--seed', type=int, default=21501501)
    parser.add_argument('--teacher-mode', choices=('Policy', *PRESETS), default='Policy',
                        help='The same explicit sampler supplies both labels and rollout actions.')
    parser.add_argument('--action-noise', type=float, default=.2)
    args = parser.parse_args(); torch.set_num_threads(1)
    with exclusive(args.root.absolute()):
        result = collect(args.root, args.teacher, args.worker, args.destination, decisions=args.decisions, seed=args.seed,
                         teacher_mode=args.teacher_mode, action_noise=args.action_noise)
    print(json.dumps({key: result[key] for key in ['state', 'decisions', 'trainingGroups', 'validationGroups', 'rejections']}))
