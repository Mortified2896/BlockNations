"""Bounded, power-controlled development PPO trial with the C# v4 environment.

Artifacts stay in benchmarks, outside the ordinary frozen-actor catalog. This is
an architecture feasibility trial, never an automatic private/public promotion.
"""
import argparse
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import uuid

sys.path.insert(0, str(Path(__file__).parents[1]/'Training'))
from supervisor import ROOT_MARKER, atomic_json, size
from evaluate_suite import worker_digest
from information_study_runtime import PowerGuard
from training_contract import CORE_THREE_RULES, SUPPORTED_RULES
from policy import ARCHITECTURE
from trainer import TRAINER, register
from artifacts import checkpoint


@contextmanager
def exclusive(root):
    """Claim both existing training locks; never signal another process."""
    controller = root/'power-controller.json'
    if controller.exists() and json.loads(controller.read_text()).get('enabled') is True:
        raise RuntimeError('Disable the ordinary auto-start controller for this bounded trial, then restore it afterwards.')
    claimed = []
    try:
        for name in ('information-study.lock', 'supervisor.lock'):
            lock = root/name
            if lock.is_symlink():
                raise ValueError('Training locks must be ordinary owned files.')
            if lock.exists():
                previous = lock.read_text(); pid = int(previous)
                if pid <= 0:
                    raise ValueError('Invalid training process identity.')
                try:
                    os.kill(pid, 0)
                except ProcessLookupError:
                    if lock.read_text() != previous:
                        raise RuntimeError('Training ownership changed during scheduling.')
                    lock.unlink()
                else:
                    raise RuntimeError('Another learner is alive; schedule this trial after it saves/stops.')
            descriptor = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
            try:
                os.write(descriptor, str(os.getpid()).encode())
            finally:
                os.close(descriptor)
            claimed.append(lock)
        yield
    finally:
        for lock in reversed(claimed):
            if lock.exists() and not lock.is_symlink() and lock.read_text() == str(os.getpid()):
                lock.unlink()


def recipe(steps, width):
    return dict(behaviors={'BlockNationsSeatV4': dict(trainer_type=TRAINER,
        hyperparameters=dict(batch_size=64, buffer_size=1024, learning_rate=.0003,
                             beta=.005, epsilon=.2, lambd=.95, num_epoch=3,
                             learning_rate_schedule='constant', shared_critic=True),
        network_settings=dict(normalize=False, hidden_units=width, num_layers=2),
        reward_signals=dict(extrinsic=dict(gamma=.995, strength=1.0)),
        max_steps=steps, time_horizon=64, summary_freq=1024, keep_checkpoints=2,
        checkpoint_interval=4096,
        self_play=dict(save_steps=1024, team_change=1024, swap_steps=128, window=4,
                       play_against_latest_model_ratio=.5, initial_elo=1200))},
        engine_settings=dict(time_scale=1, target_frame_rate=60), torch_settings=dict(device='cpu'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('root', 'destination', 'worker'):
        parser.add_argument('--'+name, type=Path, required=True)
    parser.add_argument('--steps', type=int, default=8192)
    parser.add_argument('--width', type=int, choices=(16, 32, 64), default=32)
    parser.add_argument('--seed', type=int, default=207)
    parser.add_argument('--board-size', type=int, choices=(5, 6, 7, 9, 11), default=7)
    parser.add_argument('--rules-version', choices=SUPPORTED_RULES, default=CORE_THREE_RULES)
    parser.add_argument('--hours', type=float, default=.25)
    parser.add_argument('--resume', action='store_true')
    args = parser.parse_args()
    root, destination, worker = (p.absolute() for p in (args.root, args.destination, args.worker))
    if (root.resolve() != root or destination.resolve() != destination or
            not destination.is_relative_to(root/'benchmarks') or destination == root/'benchmarks' or
            json.loads((root/ROOT_MARKER).read_text()).get('owner') != 'BlockNations.LocalTraining.v1' or
            not 2048 <= args.steps <= 1000000 or not 0 <= args.seed < 2**31 or not 0 < args.hours <= 6):
        raise ValueError('Use a bounded owned benchmark trial and an explicit compatible worker.')
    PowerGuard(True).check()
    with exclusive(root):
        run(args, root, destination, worker)


def run(args, root, destination, worker):
    sources = list(Path(__file__).parent.glob('*.py'))+list((Path(__file__).parents[1]/'Training').glob('*.py'))
    pins = {str(p.relative_to(Path(__file__).parents[1])): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
    identity = dict(owner='BlockNations.CapabilityPrototype.v1', architecture=ARCHITECTURE,
                    observationVersion=4, actionVersion=2, rulesVersion=args.rules_version,
                    boardSize=args.board_size, openingEconomyVersion=2, seed=args.seed, width=args.width,
                    workerSha256=worker_digest(worker), sources=pins,
                    partition='training', promotion='disabled', initialization='new-architecture-random-weights')
    receipt = destination/'prototype.json'
    if args.resume:
        previous = json.loads(receipt.read_text())
        if any(previous.get(k) != v for k, v in identity.items()) or args.steps < previous['targetSteps']:
            raise ValueError('Resume requires the same pinned architecture/recipe/worker and an explicit nondecreasing step target.')
        _, resumed = checkpoint(destination/'checkpoints/capability/BlockNationsSeatV4/checkpoint.pt', args.width)
    else:
        destination.mkdir(parents=True, exist_ok=False)
    config = destination/'trainer.json'
    atomic_json(config, recipe(args.steps, args.width))
    state = dict(identity, state='starting', targetSteps=args.steps, startedUnix=time.time(), pid=os.getpid())
    if args.resume:
        state['resumedFrom'] = resumed
    atomic_json(receipt, state)
    power = PowerGuard(True); power.check()
    deadline = time.monotonic()+args.hours*3600
    next_storage = 0
    def guard():
        nonlocal next_storage
        try:
            power.check()
            if time.monotonic() >= deadline:
                raise RuntimeError('Bounded trial wall-clock limit reached.')
            if (destination/'stop.request').exists():
                raise RuntimeError('Trial stop requested.')
            if time.monotonic() >= next_storage:
                next_storage = time.monotonic()+30
                if size(root) > 19_488_000_000 or shutil.disk_usage(root).free < 20_512_000_000:
                    raise RuntimeError('Shared training storage reserve reached.')
        except RuntimeError as error:
            state.update(stopReason=str(error))
            # The SDK catches KeyboardInterrupt and saves its current PPO state.
            raise KeyboardInterrupt from error
    guard()
    os.environ.pop('BLOCKNATIONS_EPISODE_STARTS', None)
    os.environ.update(BLOCKNATIONS_SIMULATION_WORKER=str(worker), BLOCKNATIONS_RATING_RUN=str(destination),
        BLOCKNATIONS_PARALLEL_GAMES='2', BLOCKNATIONS_SIMULATION_SEED=str(args.seed),
        BLOCKNATIONS_RATING_BOARD=str(args.board_size), BLOCKNATIONS_SIMULATION_CURRICULUM='false',
        BLOCKNATIONS_SIMULATION_DISTANCE='2', BLOCKNATIONS_RATING_SESSION=uuid.uuid4().hex,
        BLOCKNATIONS_OPENING_ECONOMY_VERSION='2', BLOCKNATIONS_RULES_VERSION=args.rules_version,
        BLOCKNATIONS_OBSERVATION_VERSION='4', BLOCKNATIONS_HUMAN_IMITATION='disabled',
        BLOCKNATIONS_DIAGNOSTIC_FULL_VISION_SEATS='0', BLOCKNATIONS_TORCH_THREADS='1')
    from mlagents import torch_utils
    torch_utils.torch.set_num_threads(1)
    from mlagents.trainers import learn
    from dotnet_environment import install, BatchedSimulationManager
    register()
    install(learn, lambda: None)
    class GuardedManager(BatchedSimulationManager):
        def _step(self):
            guard()
            return super()._step()
    learn.SubprocessEnvManager = GuardedManager
    sys.argv = ['capability-ppo', str(config), '--run-id', 'capability', '--results-dir', str(destination/'checkpoints'),
                '--seed', str(args.seed), '--torch-device', 'cpu', '--timeout-wait', '300']+(['--resume'] if args.resume else [])
    state['state'] = 'running'; atomic_json(receipt, state)
    assertion = None
    try:
        if sys.platform == 'darwin':
            assertion = subprocess.Popen(['/usr/bin/caffeinate', '-i', '-s', '-w', str(os.getpid())],
                                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        learn.main()
        _, verified = checkpoint(destination/'checkpoints/capability/BlockNationsSeatV4/checkpoint.pt', args.width)
        state['checkpoint'] = verified
        if args.resume and (verified['step'] < resumed['step'] or any(
                verified['optimizerSteps'][key] < value for key, value in resumed['optimizerSteps'].items())):
            raise RuntimeError('The resumed capability checkpoint lost learning/optimizer counters.')
        if not state.get('stopReason') and verified['step'] < args.steps:
            raise RuntimeError('The SDK returned before the requested capability learning budget.')
        arena = json.loads((destination/'arena-status.json').read_text())
        expected = dict(boardSize=args.board_size, schema=4, simulationVersion=args.rules_version,
                        simulationBackend='standalone-dotnet', diagnosticFullVisionSeats=0, workerCount=2)
        if (any(arena.get(key) != value for key, value in expected.items()) or arena.get('failure') or
                type(arena.get('rejections')) is not int or arena['rejections'] != 0 or
                type(arena.get('games')) is not int or arena['games'] <= 0):
            raise RuntimeError('The actual prototype environment failed its fair-contract, match-progress or legality check.')
        state['arena'] = {key: arena.get(key) for key in [*expected, 'games', 'captures', 'interruptions', 'rejections', 'decisions']}
        state['state'] = 'saved' if state.get('stopReason') else 'finished'
    except BaseException as error:
        state.update(state='failed', error=type(error).__name__+': '+str(error))
        raise
    finally:
        if assertion is not None:
            assertion.terminate()
            assertion.wait(timeout=5)
        state.update(finishedUnix=time.time())
        atomic_json(receipt, state)


if __name__ == '__main__':
    main()
