"""Matched rule-profile continuations with preserved weights and fixed evaluation.

One bounded trial at a time, alongside the unchanged baseline supervisor. Uses
the same marked root and shared budget, never launches a viewer or promotes a
release. Source rules remain explicit when frozen actors transfer to new rules.
"""
import argparse
import copy
import hashlib
import json
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
from mlagents.torch_utils import torch
from supervisor import ROOT_MARKER, atomic_json, interrupt_trainer, read_json, size
from training_contract import RULES_VERSION, SUPPORTED_RULES, OPENING_ECONOMY_VERSION, transfer_contract, checkpoint_step
from training_opponents import load_recipe, owned_frozen_path, validate_recipe


def sha(data):
    return hashlib.sha256(data).hexdigest()


def arm_files(root, name, parent_path, recipe_run, rules, worker, steps, seed):
    parent = read_json(parent_path)
    _, identity = frozen_actor(parent_path)  # Strict shapes, finite actor and checkpoint hash.
    if parent.get('step') is None or parent.get('step') < 1:
        raise ValueError('The parent needs a recorded immutable training step.')
    recipe = copy.deepcopy(load_recipe(recipe_run))
    if recipe is None or recipe['version'] != 2:
        raise ValueError('Choose a preserved, bounded retained-opponent league.')
    run = root/'runs'/name
    run.mkdir(exist_ok=False)
    saved = run/'checkpoints'/name/'BlockNationsSeatV2'
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
    behavior = plan['behaviors']['BlockNationsSeatV2']
    behavior.update(max_steps=parent['step']+steps, checkpoint_interval=min(50000, steps), keep_checkpoints=3)
    # Preserve every optimization setting, including beta and its schedule.
    atomic_json(run/'trainer.yaml', plan)
    provenance = dict(parentManifest=str(parent_path), parentStep=parent['step'],
                      checkpointSha256=identity['sha256'], configSha256=identity['configSha256'],
                      trainedRulesVersion=parent['rulesVersion'], targetRulesVersion=rules,
                      optimizer='preserved', contract=transfer_contract(parent['rulesVersion'], rules))
    atomic_json(run/'run.json', dict(owner='BlockNations.LocalTraining.v1', schema=2, runId=name,
        boardSize=7, behavior='BlockNationsSeatV2', opening='standard', curriculum=False, seed=seed,
        rulesVersion=rules, openingEconomyVersion=OPENING_ECONOMY_VERSION, observationSize=3120, actionCount=259,
        initialWeights='preserved-parent-checkpoint', weightTransfer=provenance,
        experiment='matched-rule-profile', createdUtc=time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())))
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
    validate_recipe(run, recipe, rules)
    atomic_json(run/'training-opponents.json', recipe)
    environment = dict(os.environ, BLOCKNATIONS_RATING_RUN=str(run), BLOCKNATIONS_RATING_BOARD='7',
        BLOCKNATIONS_RATING_SESSION=uuid.uuid4().hex, OMP_NUM_THREADS='1', MKL_NUM_THREADS='1', OPENBLAS_NUM_THREADS='1',
        PYTHONUNBUFFERED='1')
    configure(SimpleNamespace(backend='dotnet', worker=worker, parallel_games=1, seed=seed, board_size=7,
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


def study(root, parent, recipe_run, suite, worker, destination, name, steps, seed, hours, budget_gb):
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
    receipt = dict(version=1, state='running', pid=os.getpid(), addedSteps=steps, seed=seed, workerCount=1,
        parentManifest=str(parent), profiles=list(SUPPORTED_RULES), workerSha256=worker_digest(worker),
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
        for rules in SUPPORTED_RULES:
            guard()
            mode = {SUPPORTED_RULES[0]:'baseline', SUPPORTED_RULES[1]:'vision2', SUPPORTED_RULES[2]:'core3'}[rules]
            run, environment, provenance = arm_files(root, 'study-'+name+'-'+mode, parent, recipe_run, rules, worker, steps, seed)
            current_run = run
            target_suite = profile_suite(suite, rules, worker, destination/(mode+'-suite.json'))
            transfer = transfer_contract(RULES_VERSION, rules) if rules != RULES_VERSION else None
            initial = evaluate(parent, target_suite, worker, destination/(mode+'-before'), candidate_transfer=transfer)
            guard()
            started = time.monotonic()
            command = [sys.executable, str(Path(__file__).with_name('rated_training.py')), str(run/'trainer.yaml'),
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
            outcome = dict(profile=rules, run=str(run), manifest=str(frozen), optimizerStepDelta=deltas,
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
    args = parser.parse_args()
    torch.set_num_threads(1)
    result = study(args.root, args.parent, args.recipe_run, args.suite, args.worker,
                   args.destination, args.name, args.steps, args.seed, args.hours_per_arm, args.budget_gb)
    print(json.dumps(dict(state=result['state'], trials=len(result['trials']))))
