"""Bounded actual-learning benchmarks with matched weights, recipes and CPU budgets."""
import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import pstats
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import uuid

from supervisor import trainer_config, process_tree_rss
from playtest import latest_checkpoint
from training_contract import RULES_VERSION, OPENING_ECONOMY_VERSION, run_rules_version
from training_opponents import load_recipe, entries, owned_frozen_path


def benchmark_case(value):
    try:
        workers, threads = map(int, value.split(':'))
    except (TypeError, ValueError) as error:
        raise argparse.ArgumentTypeError('Use arenas:threads, for example 8:1.') from error
    if workers not in (1, 2, 4, 8, 16) or threads not in (1, 2, 4, 8):
        raise argparse.ArgumentTypeError('Use 1/2/4/8/16 arenas and 1/2/4/8 CPU threads.')
    return workers, threads


def snapshot_inputs(directory, recipe_run, checkpoint):
    """Read once, outside all measured cases. Never modify the source run."""
    rules = run_rules_version(recipe_run) if recipe_run else RULES_VERSION
    manifest = json.loads((recipe_run/'run.json').read_text()) if recipe_run else None
    if manifest and manifest.get('boardSize') != 7:
        raise ValueError('The matched benchmark currently requires a 7x7 run.')
    recipe = load_recipe(recipe_run, rules) if recipe_run else None
    config = json.loads((recipe_run/'trainer.yaml').read_text()) if recipe_run else None
    if recipe_run and checkpoint is None:
        checkpoint = latest_checkpoint(recipe_run)
    contents = checkpoint.read_bytes() if checkpoint else None
    initial_step = 0
    if contents is not None:
        import io
        from mlagents.torch_utils import torch
        saved = torch.load(io.BytesIO(contents), map_location='cpu')
        initial_step = int(saved['global_step']['_GlobalSteps__global_step'].item())
    references = directory/'references'
    if recipe:
        references.mkdir()
        for entry in entries(recipe):
            if entry['kind'] != 'frozen-policy':
                continue
            for name in ('checkpoint', 'trainerConfig'):
                source = owned_frozen_path(recipe_run, entry[name])
                target = references/entry[name]
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source, target)
                shutil.copyfile(source.parent/'manifest.json', target.parent/'manifest.json')
    return dict(rules=rules, manifest=manifest, recipe=recipe, config=config, contents=contents,
                step=initial_step, references=references,
                checkpointSha256=hashlib.sha256(contents).hexdigest() if contents else None)


def prepare_case(run, inputs, steps):
    run.mkdir()
    config = copy.deepcopy(inputs['config']) if inputs['config'] else trainer_config(steps, steps*4)
    behavior = config['behaviors']['BlockNationsSeatV2']
    behavior['max_steps'] = inputs['step'] + steps
    behavior['checkpoint_interval'] = steps*4
    behavior['summary_freq'] = max(1000, steps)
    if inputs['contents']:
        models = run/'checkpoints'/run.name/'BlockNationsSeatV2'
        models.mkdir(parents=True)
        (models/'checkpoint.pt').write_bytes(inputs['contents'])
        behavior.pop('init_path', None)
    if inputs['manifest']:
        manifest = dict(inputs['manifest'], runId=run.name)
        (run/'run.json').write_text(json.dumps(manifest))
    if inputs['recipe']:
        shutil.copytree(inputs['references'], run, dirs_exist_ok=True)
        (run/'training-opponents.json').write_text(json.dumps(inputs['recipe']))
    (run/'trainer.yaml').write_text(json.dumps(config))
    return config


def profile_summary(path):
    statistics = pstats.Stats(str(path))
    ordered = sorted(statistics.stats.items(), key=lambda entry: entry[1][3], reverse=True)
    return [dict(function=f'{Path(key[0]).name}:{key[1]}:{key[2]}', calls=value[1],
                 ownSeconds=value[2], cumulativeSeconds=value[3]) for key, value in ordered[:20]]


def run_case(run, inputs, worker, steps, workers, threads, *, profile=None, timeout=300):
    prepare_case(run, inputs, steps)
    session = uuid.uuid4().hex
    (run/'rating-session.json').write_text(json.dumps({'session': session}))
    env = {key: value for key, value in os.environ.items() if not key.startswith('BLOCKNATIONS_')}
    env.update(BLOCKNATIONS_RATING_RUN=str(run), BLOCKNATIONS_RATING_BOARD='7', BLOCKNATIONS_RATING_SESSION=session,
               BLOCKNATIONS_SIMULATION_WORKER=str(worker.absolute()), BLOCKNATIONS_PARALLEL_GAMES=str(workers),
               BLOCKNATIONS_SIMULATION_SEED='42', BLOCKNATIONS_SIMULATION_CURRICULUM='false',
               BLOCKNATIONS_SIMULATION_DISTANCE='2', BLOCKNATIONS_RULES_VERSION=inputs['rules'],
               BLOCKNATIONS_OPENING_ECONOMY_VERSION=str(OPENING_ECONOMY_VERSION),
               BLOCKNATIONS_TORCH_THREADS=str(threads), OMP_NUM_THREADS=str(threads), MKL_NUM_THREADS=str(threads),
               PYTHONUNBUFFERED='1', PYTHONWARNINGS='ignore')
    command = [sys.executable]
    if profile:
        command += ['-m', 'cProfile', '-o', str(profile)]
    command += [str(Path(__file__).with_name('rated_training.py')), str(run/'trainer.yaml'),
                '--run-id', run.name, '--results-dir', str(run/'checkpoints'), '--seed', '42', '--torch-device', 'cpu']
    if inputs['contents']:
        command.append('--resume')
    started, peak = time.monotonic(), 0
    with (run/'trainer.log').open('wb') as log:
        process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, env=env, cwd=run)
        try:
            while process.poll() is None and time.monotonic()-started < timeout:
                peak = max(peak, process_tree_rss(process.pid) or 0)
                time.sleep(.5)
            if process.poll() is None:
                process.send_signal(signal.SIGINT)
                process.wait(timeout=30)
                raise TimeoutError('Benchmark exceeded its bounded case duration.')
        finally:
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=10)
    if process.returncode:
        raise RuntimeError((run/'trainer.log').read_text()[-8000:])
    status = json.loads((run/'arena-status.json').read_text())
    rating = json.loads((run/'match-elo.json').read_text())
    saved_steps = [int(path.stem.rsplit('-', 1)[1]) for path in run.rglob('BlockNationsSeatV2-*.pt')
                   if 'checkpoints' in path.parts]
    learned_steps = max(saved_steps)-inputs['step']
    if learned_steps < steps or status['rejections'] or status['games'] != rating['matches']:
        raise RuntimeError('Benchmark lost learning steps, legal actions or whole-match results.')
    receipt = dict(workers=workers, torchThreads=threads, rulesVersion=status['simulationVersion'],
                   decisions=status['decisions'], learnedSteps=learned_steps, matches=status['games'],
                   captures=status['captures'], interruptions=status['interruptions'], rejections=status['rejections'],
                   seconds=status['elapsedSeconds'], wallSeconds=time.monotonic()-started,
                   decisionsPerSecond=status['decisionsPerSecond'],
                   learnedStepsPerSecond=learned_steps/status['elapsedSeconds'], peakResidentBytes=peak,
                   checkpointSha256=inputs['checkpointSha256'], sourceStep=inputs['step'],
                   recipeSha256=hashlib.sha256(json.dumps(inputs['recipe'], sort_keys=True).encode()).hexdigest(),
                   profileEnabled=profile is not None,
                   note='Actual PPO continuation, inference, challengers and export; no human samples or viewer. Other workloads can compete for CPU.')
    if profile:
        receipt['profileTop'] = profile_summary(profile)
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--worker', type=Path, required=True)
    parser.add_argument('--checkpoint', type=Path)
    parser.add_argument('--recipe-run', type=Path, help="Snapshot this owned run's current checkpoint, trainer and opponent league.")
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--steps', type=int, default=4096)
    parser.add_argument('--cases', nargs='+', type=benchmark_case, default=[(1, 2), (2, 2), (4, 2), (8, 2), (16, 2)])
    parser.add_argument('--repetitions', type=int, choices=(1, 2, 3), default=1)
    parser.add_argument('--profile', action='store_true', help='Diagnostic run; profiling results are not speed comparisons.')
    args = parser.parse_args()
    if not args.worker.is_file() or not 2048 <= args.steps <= 50000:
        raise ValueError('Build the worker and choose a bounded learning benchmark.')
    if args.checkpoint and not args.checkpoint.is_file():
        raise ValueError('Checkpoint is missing.')
    if args.output.exists():
        raise ValueError('Use a new benchmark output; preserve previous measurements.')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    receipts = []
    with tempfile.TemporaryDirectory(prefix='bn-throughput-') as directory:
        base = Path(directory)
        inputs = snapshot_inputs(base, args.recipe_run, args.checkpoint)
        for repetition in range(args.repetitions):
            cases = args.cases if repetition % 2 == 0 else list(reversed(args.cases))
            for workers, threads in cases:
                name = f'benchmark-{repetition}-{workers}-{threads}'
                profile = args.output.with_name(name+'.profile') if args.profile else None
                receipt = run_case(base/name, inputs, args.worker, args.steps, workers, threads, profile=profile)
                receipt['repetition'] = repetition
                receipts.append(receipt)
                temporary = args.output.with_suffix('.tmp')
                temporary.write_text(json.dumps(receipts, indent=2)+'\n')
                temporary.replace(args.output)
                print(json.dumps({key: receipt[key] for key in ('workers', 'torchThreads', 'learnedStepsPerSecond',
                                                               'decisionsPerSecond', 'peakResidentBytes')}), flush=True)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
