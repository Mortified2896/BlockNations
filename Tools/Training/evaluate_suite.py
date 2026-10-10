"""Repeat a fixed, provenance-checked benchmark against a frozen candidate.

Development tooling only. Each case keeps balanced colours and starting roles;
the learned policy is never restricted by tactical reference recruitment probes.
"""
import argparse
import hashlib
import json
from importlib.metadata import version
from pathlib import Path

from evaluate_policy import FrozenActor, TacticalActor, evaluate
from frozen_policy import SAMPLING_MODES
from mlagents.torch_utils import torch
from training_contract import RULES_VERSION, OPENING_ECONOMY_VERSION


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def worker_digest(worker):
    worker = Path(worker)
    base = worker.name[:-4] if worker.suffix == '.dll' else worker.name
    files = {worker}
    for suffix in ('.dll', '.deps.json', '.runtimeconfig.json'):
        companion = worker.parent/(base+suffix)
        if companion.is_file():
            files.add(companion)
    # A native .NET apphost alone does not identify its managed game rules.
    values = {path.name: digest(path) for path in sorted(files)}
    return hashlib.sha256(json.dumps(values, sort_keys=True).encode()).hexdigest()


def frozen_actor(manifest_path):
    manifest_path = Path(manifest_path)
    manifest = json.loads(manifest_path.read_text())
    if (manifest.get('rulesVersion') != RULES_VERSION or manifest.get('boardSize') != 7 or
            manifest.get('openingEconomyVersion') != OPENING_ECONOMY_VERSION):
        raise ValueError('Benchmark requires a compatible frozen 7x7 provenance manifest.')
    checkpoint = Path(manifest['checkpoint'])
    config = Path(manifest['trainerConfig'])
    if checkpoint.parent.resolve() != manifest_path.parent.resolve() or config.parent.resolve() != checkpoint.parent.resolve():
        raise ValueError('Frozen weights and configuration must belong to the manifest directory.')
    config_contents = config.read_bytes()
    network = json.loads(config_contents)['behaviors']['BlockNationsSeatV2']['network_settings']
    actor = FrozenActor(checkpoint, network)
    if actor.sha256 != manifest['sha256']:
        raise ValueError('Frozen benchmark weights changed after selection.')
    # The immutable benchmark receipt includes configuration as well as weights.
    return actor, dict(sha256=actor.sha256, configSha256=hashlib.sha256(config_contents).hexdigest(), actorId=actor.actor_id)


def prepare(candidate_manifest, suite_path):
    suite = json.loads(Path(suite_path).read_text())
    if (suite.get('version') != 1 or suite.get('rulesVersion') != RULES_VERSION or
            suite.get('boardSize') != 7 or suite.get('openingEconomyVersion') != OPENING_ECONOMY_VERSION or
            not isinstance(suite.get('workerSha256'), str) or len(suite['workerSha256']) != 64 or
            any(c not in '0123456789abcdef' for c in suite['workerSha256']) or
            not isinstance(suite.get('cases'), list) or not 1 <= len(suite['cases']) <= 32):
        raise ValueError('Choose a bounded, versioned benchmark suite for the current contract.')
    candidate, identity = frozen_actor(candidate_manifest)
    prepared, definitions, names = [], [], set()
    for case in suite['cases']:
        name = case.get('name')
        games, seed = case.get('gamesPerCell'), case.get('seed')
        mode, reference_mode = case.get('difficulty'), case.get('referenceDifficulty')
        if (not isinstance(name, str) or not name or any(c not in 'abcdefghijklmnopqrstuvwxyz0123456789-_' for c in name) or
                name in names or type(games) is not int or not 4 <= games <= 256 or games % 4 or
                type(seed) is not int or not 0 <= seed < 2**31 or
                mode not in SAMPLING_MODES or reference_mode not in SAMPLING_MODES):
            raise ValueError('Cases require unique safe names, bounded balanced games, seeds and explicit sampling modes.')
        names.add(name)
        reference = case.get('reference', {})
        kind = reference.get('kind')
        if kind == 'self':
            actor, ref_identity = candidate, dict(kind='self')
        elif kind == 'frozen':
            actor, ref_identity = frozen_actor(reference['manifest'])
            if (reference.get('checkpointSha256') != ref_identity['sha256'] or
                    reference.get('configSha256') != ref_identity['configSha256']):
                raise ValueError('Fixed reference differs from the benchmark definition.')
            ref_identity['kind'] = kind
        elif kind == 'tactical':
            work, recruit = reference.get('workBudget'), reference.get('recruitType', '')
            if (type(work) is not int or not 1 <= work <= 2048 or not isinstance(recruit, str) or
                    recruit != recruit.lower()):
                raise ValueError('Invalid tactical reference definition.')
            actor = TacticalActor(work, recruit)
            ref_identity = dict(kind=kind, implementation=actor.name, workBudget=work, recruitType=recruit)
        else:
            raise ValueError('Unknown benchmark reference kind.')
        definition = dict(name=name, gamesPerCell=games, seed=seed, difficulty=mode,
                          referenceDifficulty=reference_mode, reference=ref_identity)
        definitions.append(definition)
        prepared.append((definition, actor))
    canonical = dict(version=1, rulesVersion=RULES_VERSION, boardSize=7,
                     openingEconomyVersion=OPENING_ECONOMY_VERSION, cases=definitions,
                     workerSha256=suite['workerSha256'],
                     packages={name: version(name) for name in ('mlagents', 'torch', 'numpy')},
                     evaluatorSources={name: digest(Path(__file__).with_name(name))
                                       for name in ('evaluate_suite.py', 'evaluate_policy.py', 'frozen_policy.py', 'playtest_sampling.py')})
    suite_id = hashlib.sha256(json.dumps(canonical, sort_keys=True).encode()).hexdigest()
    return candidate, identity, prepared, dict(canonical, suiteId=suite_id)


def run(candidate_manifest, suite_path, worker, destination):
    candidate, identity, cases, definition = prepare(candidate_manifest, suite_path)
    destination = Path(destination)
    worker_identity = worker_digest(worker)
    if worker_identity != definition['workerSha256']:
        raise ValueError('Worker differs from the fixed benchmark definition.')
    # Never merge results from a different candidate or a partial previous run.
    destination.mkdir(parents=True, exist_ok=False)
    receipt = dict(definition, candidate=identity, workerSha256=worker_identity, state='running', results=[])
    def save():
        temporary = destination/'suite.json.tmp'
        temporary.write_text(json.dumps(receipt, indent=2)+'\n')
        temporary.replace(destination/'suite.json')
    save()
    try:
        for case, reference in cases:
            result = evaluate(worker, destination/case['name'], candidate, reference,
                              games_per_cell=case['gamesPerCell'], seed=case['seed'],
                              difficulty=case['difficulty'], reference_difficulty=case['referenceDifficulty'])
            result.pop('records')  # Complete match records remain in each case's evaluation.json.
            receipt['results'].append(dict(case=case['name'], **result))
            save()
        receipt['state'] = 'completed'
    except Exception as error:
        receipt.update(state='failed', error=str(error))
        raise
    finally:
        save()
    return receipt


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--candidate-manifest', type=Path, required=True)
    parser.add_argument('--suite', type=Path, required=True)
    parser.add_argument('--worker', type=Path, required=True)
    parser.add_argument('--destination', type=Path, required=True)
    args = parser.parse_args()
    torch.set_num_threads(2)
    result = run(args.candidate_manifest, args.suite, args.worker, args.destination)
    print(json.dumps(dict(suiteId=result['suiteId'], state=result['state'], cases=len(result['results']))))
