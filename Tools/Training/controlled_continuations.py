"""Sequential paired continuation studies with immutable inputs and shared limits.

Each seed receives a no-pressure control and an exploration arm from the same
frozen parent. Individual studies own their trainer and finish before the next.
This controller never selects or publishes a playing model.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import signal
import shutil
import subprocess
import sys
import threading
import time

from bounded_log import BoundedLog
from evaluate_suite import frozen_actor, worker_digest
from exploration_pressure import validate_recipe
from rule_profile_study import experiment_options
from supervisor import ROOT_MARKER, atomic_json, read_json, size
from training_contract import CORE_THREE_RULES

SHARED_BUDGET = 20_000_000_000
CHECKPOINT_RESERVE = 512_000_000
FREE_DISK_RESERVE = 20_512_000_000


def fingerprints(paths):
    return {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}


def check_limits(root):
    if size(root) > SHARED_BUDGET-CHECKPOINT_RESERVE or shutil.disk_usage(root).free < FREE_DISK_RESERVE:
        raise RuntimeError('Comparison stopped at the shared storage or free-disk reserve.')


def run(root, parent, recipe_run, suite, worker, destination, *, seeds, steps=1_000_000,
        learning_rate=.000075, coefficient=.005, entropy_floor=.25, batch_size=128, hours=3):
    experiment_options(4, learning_rate)
    pressure = validate_recipe(dict(version=1, rulesVersion=CORE_THREE_RULES, boardSize=7,
        coefficient=coefficient, entropyFloor=entropy_floor, batchSize=batch_size), CORE_THREE_RULES, 7)
    if (root.is_symlink() or root.resolve() != root.absolute() or read_json(root/ROOT_MARKER).get('owner') != 'BlockNations.LocalTraining.v1' or
            destination.exists() or not destination.resolve().is_relative_to(root/'benchmarks') or
            recipe_run.parent != root/'runs' or recipe_run.is_symlink() or
            read_json(recipe_run/'run.json').get('owner') != 'BlockNations.LocalTraining.v1' or
            parent.is_symlink() or not parent.resolve().is_relative_to(root/'runs') or
            not isinstance(seeds, (list, tuple)) or not 1 <= len(seeds) <= 3 or
            any(type(seed) is not int or not 0 <= seed < 2**31 for seed in seeds) or
            len(set(seeds)) != len(seeds) or type(steps) is not int or not 2048 <= steps <= 10_000_000 or
            type(hours) not in (int, float) or not math.isfinite(hours) or not 0 < hours <= 12):
        raise ValueError('Use a new owned benchmark, frozen parent and bounded independent continuation seeds.')
    lock = root/'controlled-continuations.lock'
    if (root/'rule-profile-study.lock').exists() or lock.exists():
        raise ValueError('Finish the existing controlled study before starting another comparison.')
    _, actor = frozen_actor(parent, CORE_THREE_RULES)
    owned = read_json(parent)
    if type(owned.get('step')) is not int or owned['step'] < 1:
        raise ValueError('The preserved parent requires an immutable training step.')
    paths = list(Path(__file__).parent.glob('*.py')) + [parent, Path(owned['checkpoint']), Path(owned['trainerConfig']),
        suite, recipe_run/'training-opponents.json']
    pinned = fingerprints(paths); worker_sha = worker_digest(worker)
    check_limits(root)
    plan = dict(version=1, owner='BlockNations.LocalTraining.v1', rulesVersion=CORE_THREE_RULES, boardSize=7,
        parentManifest=str(parent), parentActor=actor, recipeRun=str(recipe_run), suite=str(suite), worker=str(worker),
        workerSha256=worker_sha, seeds=list(seeds), addedSteps=steps, workerCount=4, learningRate=learning_rate,
        explorationPressure=pressure, hoursPerArm=hours, sharedBudgetBytes=SHARED_BUDGET, pinnedSources=pinned)
    state = dict(version=1, owner=plan['owner'], state='running', pid=os.getpid(), startedUnix=time.time(), completed=[])
    stopping, process, capture, created = False, None, None, False
    def stop(*_):
        nonlocal stopping
        stopping = True
    previous = {}
    def guard():
        if stopping or (destination/'stop.request').exists():
            raise RuntimeError('Comparison stopped by request; saved checkpoints remain preserved.')
        check_limits(root)
        if fingerprints(paths) != pinned or worker_digest(worker) != worker_sha:
            raise RuntimeError('Pinned continuation inputs changed; do not mix these results.')
    descriptor = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    with os.fdopen(descriptor, 'w') as output:
        output.write(str(os.getpid()))
    try:
        destination.mkdir(parents=True, exist_ok=False)
        created = True
        atomic_json(destination/'plan.json', plan)
        paths.append(destination/'plan.json')
        pinned[str(destination/'plan.json')] = fingerprints([destination/'plan.json'])[str(destination/'plan.json')]
        state['planSha256'] = pinned[str(destination/'plan.json')]
        atomic_json(destination/'queue.json', state)
        previous = {event: signal.signal(event, stop) for event in (signal.SIGINT, signal.SIGTERM)}
        for seed in seeds:
            for arm in ('control', 'exploration'):
                guard()
                name = f'xp-{owned["step"]}-s{seed}-{arm}'
                target = destination/f'seed-{seed}'/arm
                command = [sys.executable, str(Path(__file__).with_name('rule_profile_study.py')),
                    '--root', str(root), '--parent', str(parent), '--recipe-run', str(recipe_run), '--suite', str(suite),
                    '--worker', str(worker), '--destination', str(target), '--name', name, '--steps', str(steps),
                    '--seed', str(seed), '--hours-per-arm', str(hours), '--budget-gb', '20', '--profile', CORE_THREE_RULES,
                    '--parallel-games', '4', '--learning-rate', str(learning_rate)]
                if arm == 'exploration':
                    command += ['--exploration-coefficient', str(coefficient), '--exploration-entropy-floor', str(entropy_floor),
                                '--exploration-batch-size', str(batch_size)]
                state.update(currentSeed=seed, currentArm=arm, currentDestination=str(target), command=command)
                atomic_json(destination/'queue.json', state)
                log = BoundedLog(destination/f'seed-{seed}-{arm}.log')
                try:
                    process = subprocess.Popen(command, cwd=root, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
                except BaseException:
                    log.close()
                    raise
                def drain(child=process, output=log):
                    try:
                        while chunk := child.stdout.read1(4096): output.append(chunk)
                    finally:
                        child.stdout.close(); output.close()
                capture = threading.Thread(target=drain, daemon=True); capture.start()
                state['studyPid'] = process.pid
                atomic_json(destination/'queue.json', state)
                while process.poll() is None:
                    guard(); time.sleep(5)
                capture.join(timeout=5)
                if process.returncode != 0:
                    raise RuntimeError('Controlled continuation failed; preserve its bounded log and saved weights.')
                receipt = read_json(target/'study.json')
                if receipt.get('state') != 'completed' or len(receipt.get('trials', [])) != 1:
                    raise RuntimeError('Controlled continuation failed; preserve its bounded log and saved weights.')
                state['completed'].append(dict(seed=seed, arm=arm, study=str(target/'study.json')))
                atomic_json(destination/'queue.json', state)
        state.update(state='completed', studyPid=0)
    except BaseException as error:
        state.update(state='failed', error=str(error))
        raise
    finally:
        if process is not None and process.poll() is None:
            process.send_signal(signal.SIGINT)
            try: process.wait(timeout=60)
            except subprocess.TimeoutExpired:
                # Keep the handle and report it for inspection; killing a study
                # could orphan its trainer during its final checkpoint save.
                state['studyStillAlive'] = process.pid
        if capture is not None: capture.join(timeout=5)
        state['finishedUnix'] = time.time()
        if created: atomic_json(destination/'queue.json', state)
        for event, handler in previous.items(): signal.signal(event, handler)
        if not state.get('studyStillAlive') and lock.exists() and lock.read_text() == str(os.getpid()):
            lock.unlink()
    return state


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('root', 'parent', 'recipe-run', 'suite', 'worker', 'destination'):
        parser.add_argument('--'+name, type=Path, required=True)
    parser.add_argument('--seed', type=int, action='append', required=True)
    parser.add_argument('--steps', type=int, default=1_000_000)
    parser.add_argument('--learning-rate', type=float, default=.000075)
    parser.add_argument('--coefficient', type=float, default=.005)
    parser.add_argument('--entropy-floor', type=float, default=.25)
    parser.add_argument('--batch-size', type=int, default=128)
    parser.add_argument('--hours-per-arm', type=float, default=3)
    args = parser.parse_args()
    result = run(args.root, args.parent, args.recipe_run, args.suite, args.worker, args.destination,
        seeds=args.seed, steps=args.steps, learning_rate=args.learning_rate, coefficient=args.coefficient,
        entropy_floor=args.entropy_floor, batch_size=args.batch_size, hours=args.hours_per_arm)
    print(json.dumps(dict(state=result['state'], completed=len(result['completed']))))
