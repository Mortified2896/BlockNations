"""A bounded, role-balanced metagame with one sampling mode throughout.

Fresh diagnostic results never enter PPO, the adaptive training league, or release
selection. A finite payoff table describes these opponents, not human Elo or the
exploitability of the whole game. Colours/roles share deterministic openings.
"""
import argparse
import copy
import json
from pathlib import Path
import shutil
import time

from evaluate_suite import frozen_actor, worker_digest
from information_study_runtime import evaluate_or_resume
from supervisor import ROOT_MARKER, atomic_json, size
from training_contract import CORE_THREE_RULES


def reference(manifest):
    _, identity = frozen_actor(manifest, CORE_THREE_RULES)
    return dict(kind='frozen', manifest=str(manifest), checkpointSha256=identity['sha256'],
                configSha256=identity['configSha256'])


def run(root, manifests, worker, destination, *, seeds=(140201, 140202), games=8, mode='Hard'):
    root, worker, destination = (Path(p).absolute() for p in (root, worker, destination))
    manifests = [Path(p).absolute() for p in manifests]
    if (root.resolve() != root or json.loads((root/ROOT_MARKER).read_text()).get('owner') != 'BlockNations.LocalTraining.v1'
            or destination.resolve() != destination or not destination.is_relative_to(root/'benchmarks')
            or destination.exists() or not 2 <= len(manifests) <= 5 or len(set(manifests)) != len(manifests)
            or any(p.resolve() != p or not p.is_relative_to(root/'runs') for p in manifests)
            or mode not in ('Hard', 'Policy') or games not in (4, 8, 16, 32)
            or not 1 <= len(seeds) <= 3 or len(set(seeds)) != len(seeds)
            or any(type(s) is not int or not 0 <= s < 2**31 for s in seeds)):
        raise ValueError('Use owned immutable models, one sampling mode and a new bounded diagnostic folder.')
    references = [reference(p) for p in manifests]
    if len({r['checkpointSha256'] for r in references}) != len(references):
        raise ValueError('Metagame models must have distinct frozen weights.')
    next_check = 0
    def guard():
        nonlocal next_check
        if time.monotonic() < next_check:
            return
        next_check = time.monotonic()+30
        if size(root) > 19_488_000_000 or shutil.disk_usage(root).free < 20_512_000_000:
            raise RuntimeError('Preserve the shared final-save reserve.')
    guard()
    destination.mkdir(parents=True)
    receipt = dict(owner='BlockNations.PolicyMetagame.v1', state='running', mode=mode, seeds=list(seeds),
                   gamesPerCell=games, models=references,
                   workerSha256=worker_digest(worker), results=[], partition='evaluation-only',
                   evidence='empirical balanced roles and colours; deterministic openings are not IID samples')
    atomic_json(destination/'metagame.json', receipt)
    try:
        for i, manifest in enumerate(manifests):
            for seed in seeds:
                cases = [dict(name='model-'+str(j), seed=seed, gamesPerCell=games, difficulty=mode,
                              referenceDifficulty=mode, reference=copy.deepcopy(ref))
                         for j, ref in enumerate(references) if j != i]
                for recruit in ('', 'warrior', 'rider', 'archer'):
                    cases.append(dict(name='tactical-'+(recruit or 'all'), seed=seed, gamesPerCell=games,
                        difficulty=mode, referenceDifficulty=mode,
                        reference=dict(kind='tactical', workBudget=512, recruitType=recruit)))
                suite = dict(version=1, rulesVersion=CORE_THREE_RULES, boardSize=7, openingEconomyVersion=2,
                             workerSha256=receipt['workerSha256'], cases=cases)
                path = destination/f'model-{i}-seed-{seed}.json'
                atomic_json(path, suite)
                pack = evaluate_or_resume(manifest, path, worker, path.with_suffix(''), guard)
                receipt['results'].append(dict(model=i, seed=seed, suite=str(path.with_suffix('')/'suite.json'),
                                              suiteId=pack['suiteId'], results=pack['results']))
                atomic_json(destination/'metagame.json', receipt)
        receipt['state'] = 'completed'
    except BaseException as error:
        receipt.update(state='failed', error=str(error))
        raise
    finally:
        atomic_json(destination/'metagame.json', receipt)
    return receipt


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for field in ('root', 'worker', 'destination'):
        parser.add_argument('--'+field, type=Path, required=True)
    parser.add_argument('--manifest', type=Path, action='append', required=True)
    parser.add_argument('--mode', choices=('Hard', 'Policy'), default='Hard')
    parser.add_argument('--seed', type=int, action='append')
    parser.add_argument('--games-per-cell', type=int, default=8)
    args = parser.parse_args()
    from mlagents.torch_utils import torch
    torch.set_num_threads(1)
    result = run(args.root, args.manifest, args.worker, args.destination, mode=args.mode,
                 games=args.games_per_cell, seeds=tuple(args.seed or (140201, 140202)))
    print(json.dumps(dict(state=result['state'], packs=len(result['results']), mode=result['mode'])))
