"""Matched rule-profile continuations with preserved weights and fixed evaluation.

One bounded trial at a time, alongside the unchanged baseline supervisor. Uses
the same marked root and shared budget, never launches a viewer or promotes a
release. Source rules remain explicit when frozen actors transfer to new rules.
"""
import argparse
import copy
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import threading
import time
from types import SimpleNamespace
import uuid

from bounded_log import BoundedLog
from decoupled_runtime import configure
from evaluate_milestone import freeze
from evaluate_suite import frozen_actor, run as evaluate, worker_digest
from exploration_pressure import validate_recipe as validate_exploration
from mlagents.torch_utils import torch
from supervisor import ROOT_MARKER, atomic_json, interrupt_trainer, read_json, size
from training_contract import RULES_VERSION, SUPPORTED_RULES, OPENING_ECONOMY_VERSION, transfer_contract, checkpoint_step, run_rules_version
from training_opponents import load_recipe, owned_frozen_path, validate_recipe
from model_contract import checkpoint_schema, behavior as model_behavior, SIZES


def sha(data):
    return hashlib.sha256(data).hexdigest()


def experiment_options(parallel_games, learning_rate):
    if type(parallel_games) is not int or parallel_games not in (1, 2, 4, 8, 16):
        raise ValueError('Choose a supported training arena count: 1, 2, 4, 8 or 16.')
    if learning_rate is not None and (type(learning_rate) not in (int, float) or
            not math.isfinite(learning_rate) or not 0 < learning_rate <= .01):
        raise ValueError('Choose a finite learning-rate override between 0 and 0.01.')


def arm_files(root, name, parent_path, recipe_run, rules, worker, steps, seed, *, roster_augmentation=False,
              parallel_games=1, learning_rate=None, exploration_pressure=None):
    experiment_options(parallel_games, learning_rate)
    if type(roster_augmentation) is not bool or roster_augmentation and exploration_pressure is not None:
        raise ValueError('Choose an explicit, separate exploration or roster-augmentation experiment.')
    pressure = validate_exploration(exploration_pressure, rules, 7) if exploration_pressure is not None else None
    recipe_version = 'rule-profile-study-v3-exploration' if pressure else 'rule-profile-study-v2'
    parent = read_json(parent_path)
    _, identity = frozen_actor(parent_path, parent['rulesVersion'])  # Strict shapes, finite actor and checkpoint hash.
    if parent.get('step') is None or parent.get('step') < 1:
        raise ValueError('The parent needs a recorded immutable training step.')
    recipe = copy.deepcopy(load_recipe(recipe_run, run_rules_version(recipe_run)))
    if recipe is None or recipe['version'] != 2:
        raise ValueError('Choose a preserved, bounded retained-opponent league.')
    run = root/'runs'/name
    run.mkdir(exist_ok=False)
    schema = checkpoint_schema(parent['checkpoint'])
    behavior_name = model_behavior(schema)
    if schema != 2 and parent['rulesVersion'] != rules:
        raise ValueError('New observation contracts require a separately audited rule-profile transfer.')
    saved = run/'checkpoints'/name/behavior_name
    saved.mkdir(parents=True)
    source = Path(parent['checkpoint'])
    if checkpoint_step(source) != parent['step']:
        raise ValueError('Parent step differs from the immutable checkpoint identity.')
    if parent.get('trainerConfigSha256', identity['configSha256']) != identity['configSha256']:
        raise ValueError('The preserved parent configuration differs from its manifest.')
    contents = source.read_bytes()
    if sha(contents) != identity['sha256']:
        raise ValueError('The preserved parent changed during study preparation.')
    (saved/'checkpoint.pt').write_bytes(contents)
    plan = json.loads(Path(parent['trainerConfig']).read_text())
    behavior = plan['behaviors'][behavior_name]
    behavior.update(max_steps=parent['step']+steps, checkpoint_interval=min(50000, steps), keep_checkpoints=3)
    # Preserve optimization settings unless a bounded override is explicitly
    # part of this fresh study. The copied optimizer state remains intact.
    previous_rate = behavior['hyperparameters'].get('learning_rate')
    if learning_rate is not None:
        behavior['hyperparameters']['learning_rate'] = float(learning_rate)
    atomic_json(run/'trainer.yaml', plan)
    provenance = dict(parentManifest=str(parent_path), parentStep=parent['step'],
                      checkpointSha256=identity['sha256'], configSha256=identity['configSha256'],
                      trainedRulesVersion=parent['rulesVersion'], targetRulesVersion=rules,
                      optimizer='preserved', contract=transfer_contract(parent['rulesVersion'], rules))
    provenance['contract'].update(modelSchema=schema, observationSize=SIZES[schema])
    if learning_rate is not None or parallel_games != 1:
        provenance['trainingOptions'] = dict(parallelGames=parallel_games, learningRateOverride=learning_rate,
                                            parentLearningRate=previous_rate, recipeVersion=recipe_version)
    if pressure:
        provenance['explorationPressure'] = pressure
    if roster_augmentation:
        transform = dict(version=1, rulesVersion=rules, boardSize=7, mode='per-decision', seed=seed)
        provenance['publicRosterTransform'] = transform
    atomic_json(run/'run.json', dict(owner='BlockNations.LocalTraining.v1', schema=schema, runId=name,
        boardSize=7, behavior=behavior_name, opening='standard', curriculum=False, seed=seed,
        rulesVersion=rules, openingEconomyVersion=OPENING_ECONOMY_VERSION, observationSize=SIZES[schema], actionCount=259,
        initialWeights='preserved-parent-checkpoint', weightTransfer=provenance,
        experiment='matched-exploration-pressure' if pressure else 'matched-roster-augmentation' if roster_augmentation else 'matched-rule-profile',
        recipeVersion=recipe_version, parallelGames=parallel_games,
        createdUtc=time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())))
    if roster_augmentation:
        from roster_augmentation import load_recipe as check_augmentation
        atomic_json(run/'roster-augmentation.json', transform)
        check_augmentation(run)
    if pressure:
        atomic_json(run/'exploration-pressure.json', pressure)
    recipe['rulesVersion'] = rules
    for entry in recipe['opponents']:
        if entry['kind'] != 'frozen-policy':
            continue
        checkpoint = owned_frozen_path(recipe_run, entry['checkpoint'])
        config = owned_frozen_path(recipe_run, entry['trainerConfig'])
        if config.parent != checkpoint.parent:
            raise ValueError('Retained weights and configuration need a single owned frozen folder.')
        relative = Path(entry['checkpoint']).parent
        destination = run/relative
        destination.mkdir(parents=True)
        # Copy only audited reference files. Private human recordings stay in their owning run.
        for original in (checkpoint, config, checkpoint.parent/'manifest.json'):
            if original.is_symlink():
                raise ValueError('Frozen reference files must not be linked.')
            shutil.copyfile(original, destination/original.name)
        frozen = read_json(destination/'manifest.json')
        frozen.update(checkpoint=str(destination/checkpoint.name), trainerConfig=str(destination/config.name))
        atomic_json(destination/'manifest.json', frozen)  # Keep original trained rules and hash.
        if frozen['rulesVersion'] != rules:
            entry['transferRules'] = transfer_contract(frozen['rulesVersion'], rules)
        else:
            entry.pop('transferRules', None)
    validate_recipe(run, recipe, rules)
    atomic_json(run/'training-opponents.json', recipe)
    environment = dict(os.environ, BLOCKNATIONS_RATING_RUN=str(run), BLOCKNATIONS_RATING_BOARD='7',
        BLOCKNATIONS_RATING_SESSION=uuid.uuid4().hex, OMP_NUM_THREADS='1', MKL_NUM_THREADS='1', OPENBLAS_NUM_THREADS='1',
        PYTHONUNBUFFERED='1')
    configure(SimpleNamespace(backend='dotnet', worker=worker, parallel_games=parallel_games, seed=seed, board_size=7,
              curriculum=False, curriculum_distance=2, opening_economy_version=2, rules_version=rules), run, environment)
    return run, environment, provenance


def profile_suite(source, rules, worker, destination):
    suite = copy.deepcopy(read_json(source))
    if suite.get('rulesVersion') != RULES_VERSION:
        raise ValueError('Start from the preserved baseline suite.')
    suite.update(rulesVersion=rules, workerSha256=worker_digest(worker))
    for case in suite['cases']:
        reference = case['reference']
        if reference['kind'] == 'frozen':
            original_rules = read_json(Path(reference['manifest']))['rulesVersion']
            if original_rules != rules:
                reference['transferRules'] = transfer_contract(original_rules, rules)
    atomic_json(destination, suite)
    return destination


def study(root, parent, recipe_run, suite, worker, destination, name, steps, seed, hours, budget_gb,
          *, profiles=None, roster_augmentation=False, parallel_games=1, learning_rate=None, exploration_pressure=None):
    experiment_options(parallel_games, learning_rate)
    if profiles is not None and not isinstance(profiles, (list, tuple)):
        raise ValueError('Choose an explicit bounded list of supported rule profiles.')
    profiles = tuple(profiles) if profiles is not None else SUPPORTED_RULES
    if (not 1 <= len(profiles) <= len(SUPPORTED_RULES) or
            any(not isinstance(profile, str) or profile not in SUPPORTED_RULES for profile in profiles) or
            len(set(profiles)) != len(profiles) or type(roster_augmentation) is not bool):
        raise ValueError('Choose unique supported rule profiles and an explicit augmentation flag.')
    if roster_augmentation and exploration_pressure is not None:
        raise ValueError('Test exploration and roster augmentation in separate controlled experiments.')
    if exploration_pressure is not None:
        for rules in profiles:
            validate_exploration(exploration_pressure, rules, 7)
    if (read_json(root/ROOT_MARKER).get('owner') != 'BlockNations.LocalTraining.v1' or root.is_symlink() or
            root.resolve() != root.absolute() or recipe_run.parent != root/'runs'):
        raise ValueError('Use the real marked shared training root and an owned recipe run.')
    if (not re.fullmatch(r'[a-z0-9][a-z0-9-]{0,45}', name) or not 2048 <= steps <= 10_000_000 or
            not 0 < hours <= 12 or not 1 <= budget_gb <= 20 or not 0 <= seed < 2**31):
        raise ValueError('Choose bounded, reproducible study parameters.')
    destination.mkdir(parents=True, exist_ok=False)
    lock = root/'rule-profile-study.lock'
    descriptor = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    with os.fdopen(descriptor, 'w') as output:
        output.write(str(os.getpid()))
    sources = {path:sha(path.read_bytes()) for path in Path(__file__).parent.glob('*.py')}
    sources.update({parent:sha(parent.read_bytes()), suite:sha(suite.read_bytes()),
                    recipe_run/'training-opponents.json':sha((recipe_run/'training-opponents.json').read_bytes())})
    receipt = dict(version=1, state='running', pid=os.getpid(), addedSteps=steps, seed=seed, workerCount=parallel_games,
        recipeVersion='rule-profile-study-v3-exploration' if exploration_pressure is not None else 'rule-profile-study-v2',
        learningRateOverride=learning_rate, explorationPressure=exploration_pressure,
        parentManifest=str(parent), profiles=list(profiles), rosterAugmentation=roster_augmentation,
        workerSha256=worker_digest(worker),
        pinnedSources={str(path):value for path,value in sources.items()}, sharedBudgetBytes=int(budget_gb*1e9),
        startedUnix=time.time(), trials=[])
    process, capture, current_run, assertion, stopping = None, None, None, None, False
    def stop(*_):
        nonlocal stopping
        stopping = True
    for event in (signal.SIGINT, signal.SIGTERM):
        signal.signal(event, stop)
    def guard():
        if stopping or (destination/'stop.request').exists():
            raise RuntimeError('Study stopped by request; preserved checkpoints remain resumable.')
        if size(root) > budget_gb*1e9-512_000_000 or shutil.disk_usage(root).free < 20_512_000_000:
            raise RuntimeError('Study stopped at the shared storage or free-disk reserve.')
        if any(sha(path.read_bytes()) != value for path,value in sources.items()) or worker_digest(worker) != receipt['workerSha256']:
            raise RuntimeError('Study source/worker changed; preserve this comparison and start a new one.')
    try:
        if sys.platform == 'darwin':
            assertion = subprocess.Popen(['/usr/bin/caffeinate', '-i', '-s', '-w', str(os.getpid())],
                                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        for rules in profiles:
            guard()
            mode = {SUPPORTED_RULES[0]:'baseline', SUPPORTED_RULES[1]:'vision2', SUPPORTED_RULES[2]:'core3'}[rules]
            run, environment, provenance = arm_files(root, 'study-'+name+'-'+mode, parent, recipe_run, rules, worker, steps, seed,
                                                    roster_augmentation=roster_augmentation,
                                                    parallel_games=parallel_games, learning_rate=learning_rate,
                                                    exploration_pressure=exploration_pressure)
            current_run = run
            if exploration_pressure is not None:
                path = run/'exploration-pressure.json'
                sources[path] = sha(path.read_bytes())
                receipt['pinnedSources'][str(path)] = sources[path]
                atomic_json(destination/'study.json', receipt)
            target_suite = profile_suite(suite, rules, worker, destination/(mode+'-suite.json'))
            source_rules = provenance['trainedRulesVersion']
            transfer = transfer_contract(source_rules, rules) if rules != source_rules else None
            initial = evaluate(parent, target_suite, worker, destination/(mode+'-before'), candidate_transfer=transfer)
            guard()
            started = time.monotonic()
            entrypoint = 'roster_augmented_training.py' if roster_augmentation else 'rated_training.py'
            command = [sys.executable, str(Path(__file__).with_name(entrypoint)), str(run/'trainer.yaml'),
                '--run-id', run.name, '--results-dir', str(run/'checkpoints'), '--seed', str(seed),
                '--torch-device', 'cpu', '--timeout-wait', '300', '--resume']
            log = BoundedLog(run/'trial.log')
            process = subprocess.Popen(command, env=environment, cwd=run, stdin=subprocess.DEVNULL,
                stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            def drain(child=process, writer=log):
                try:
                    while chunk := child.stdout.read1(4096):
                        writer.append(chunk)
                finally:
                    child.stdout.close(); writer.close()
            capture = threading.Thread(target=drain, daemon=True); capture.start()
            receipt.update(currentProfile=rules, trainerPid=process.pid, currentRun=str(run))
            atomic_json(destination/'study.json', receipt)
            while process.poll() is None:
                guard()
                if (run/'stop.request').exists() or time.monotonic()-started > hours*3600:
                    raise RuntimeError('Owned trial stopped at its time guard or stop request.')
                atomic_json(run/'study-status.json', dict(state='running', trainerPid=process.pid,
                    rulesVersion=rules, elapsedSeconds=time.monotonic()-started, sharedBudgetBytes=receipt['sharedBudgetBytes']))
                time.sleep(5)
            capture.join(timeout=5)
            if process.returncode != 0:
                raise RuntimeError('Profile trainer failed; inspect its bounded trial log.')
            models = run/'checkpoints'/run.name/'BlockNationsSeatV2'
            final = max(models.glob('BlockNationsSeatV2-*.pt'), key=lambda path:int(path.stem.rsplit('-',1)[1]))
            old = torch.load(Path(read_json(parent)['checkpoint']), map_location='cpu')
            new = torch.load(final, map_location='cpu')
            if int(final.stem.rsplit('-',1)[1]) < provenance['parentStep']+steps:
                raise RuntimeError('The trial did not reach its matched learned-step budget.')
            optimizer = new['Optimizer:value_optimizer']['state']
            for state in (new['Policy'], *optimizer.values()):
                if any(torch.is_tensor(value) and not torch.isfinite(value).all() for value in state.values()):
                    raise ValueError('The checkpoint contains non-finite weights or optimizer state.')
            deltas = {str(key):int(value['step'])-int(old['Optimizer:value_optimizer']['state'][key]['step']) for key,value in optimizer.items()}
            if not deltas or min(deltas.values()) <= 0:
                raise RuntimeError('The transferred optimizer did not advance.')
            arena = read_json(run/'arena-status.json')
            if arena.get('rejections') != 0 or arena.get('failure') or arena.get('simulationVersion') != rules:
                raise RuntimeError('Trial rules/legality/health failed.')
            frozen = freeze(run, final, provenance['parentStep']+steps, (run/'trainer.yaml').read_bytes(),
                            sha((run/'training-opponents.json').read_bytes()))
            guard()
            after = evaluate(frozen, target_suite, worker, destination/(mode+'-after'))
            outcome = dict(profile=rules, rosterAugmentation=roster_augmentation, explorationPressure=exploration_pressure,
                run=str(run), manifest=str(frozen), optimizerStepDelta=deltas,
                seconds=time.monotonic()-started, decisions=arena['decisions'], rejections=arena['rejections'],
                suiteId=after['suiteId'], beforeCandidate=initial['candidate'], afterCandidate=after['candidate'])
            receipt['trials'].append(outcome)
            atomic_json(run/'study-status.json', dict(state='completed', **outcome))
            atomic_json(destination/'study.json', receipt)
        receipt.update(state='completed', trainerPid=0)
    except BaseException as error:
        receipt.update(state='failed', error=str(error))
        raise
    finally:
        if process is not None and process.poll() is None:
            interrupt_trainer(process, timeout=45)
        if capture is not None:
            capture.join(timeout=5)
        if receipt['state'] == 'failed' and current_run is not None:
            atomic_json(current_run/'study-status.json', dict(state='failed', error=receipt['error'],
                trainerExitCode=process.returncode if process is not None else None,
                checkpointCount=len(list(current_run.rglob('*.pt'))), exportCount=len(list(current_run.rglob('*.onnx')))))
        receipt['finishedUnix'] = time.time()
        atomic_json(destination/'study.json', receipt)
        if lock.exists() and lock.read_text() == str(os.getpid()):
            lock.unlink()
        if assertion is not None:
            assertion.terminate(); assertion.wait(timeout=5)
    return receipt


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for field in ('root', 'parent', 'recipe-run', 'suite', 'worker', 'destination'):
        parser.add_argument('--'+field, type=Path, required=True)
    parser.add_argument('--name', required=True)
    parser.add_argument('--steps', type=int, default=1_000_000)
    parser.add_argument('--seed', type=int, default=42)
    parser.add_argument('--hours-per-arm', type=float, default=3)
    parser.add_argument('--budget-gb', type=float, default=20)
    parser.add_argument('--profile', choices=SUPPORTED_RULES, action='append', help='Limit this owned study to selected profiles.')
    parser.add_argument('--roster-augmentation', action='store_true', help='Use the explicit public capability/slot permutation trainer.')
    parser.add_argument('--parallel-games', type=int, choices=(1, 2, 4), default=1,
                        help='Record and reproduce the arena count of the recipe being tested.')
    parser.add_argument('--learning-rate', type=float,
                        help='Explicit bounded override for this fresh continuation; retain the parent optimizer.')
    parser.add_argument('--exploration-coefficient', type=float,
                        help='Enable the bounded generic legal-action exploration loss in this fresh study.')
    parser.add_argument('--exploration-entropy-floor', type=float,
                        help='Normalized entropy threshold, at most 0.25; requires exploration coefficient.')
    parser.add_argument('--exploration-batch-size', type=int,
                        help='Auxiliary sample size, 16-128; requires exploration coefficient.')
    args = parser.parse_args()
    pressure = None
    if args.exploration_coefficient is not None:
        if args.profile is None or len(args.profile) != 1:
            parser.error('An exploration trial requires one explicit target profile.')
        pressure = dict(version=1, rulesVersion=args.profile[0], boardSize=7, coefficient=args.exploration_coefficient,
            entropyFloor=args.exploration_entropy_floor if args.exploration_entropy_floor is not None else .25,
            batchSize=args.exploration_batch_size if args.exploration_batch_size is not None else 128)
    elif args.exploration_entropy_floor is not None or args.exploration_batch_size is not None:
        parser.error('Exploration thresholds require an explicit coefficient.')
    torch.set_num_threads(1)
    result = study(args.root, args.parent, args.recipe_run, args.suite, args.worker,
                   args.destination, args.name, args.steps, args.seed, args.hours_per_arm, args.budget_gb,
                   profiles=args.profile, roster_augmentation=args.roster_augmentation,
                   parallel_games=args.parallel_games, learning_rate=args.learning_rate, exploration_pressure=pressure)
    print(json.dumps(dict(state=result['state'], trials=len(result['trials']))))
