"""Wait for one learned-step milestone, freeze it, and run a fixed benchmark.

An owned, bounded development job. It never stops/restarts training, launches a
viewer, chooses a release, or changes the model used by a human playtest.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import tempfile
import time

from evaluate_suite import frozen_actor, worker_digest, run as evaluate
from playtest import latest_checkpoint, atomic_json
from training_contract import RULES_VERSION, OPENING_ECONOMY_VERSION, run_rules_version


def digest(data):
    return hashlib.sha256(data).hexdigest()


def checked_run(run):
    run = Path(run)
    if run.is_symlink() or run.resolve() != run.absolute():
        raise ValueError('Use the real owned run directory, without symbolic links.')
    manifest = json.loads((run/'run.json').read_text())
    run_rules_version(run)
    if (manifest.get('owner') != 'BlockNations.LocalTraining.v1' or
            manifest.get('schema') != 2 or manifest.get('boardSize') != 7 or
            manifest.get('observationSize') != 3120 or manifest.get('actionCount') != 259 or
            manifest.get('openingEconomyVersion') != OPENING_ECONOMY_VERSION):
        raise ValueError('The milestone belongs to an incompatible training run.')
    return run


def freeze(run, source, after_step, config, recipe_sha):
    rules_version = run_rules_version(run)
    boundary = json.loads((run/'checkpoint-contract.json').read_text()) if (run/'checkpoint-contract.json').exists() else {}
    step = int(source.stem.rsplit('-',1)[1])
    if rules_version != RULES_VERSION and (boundary.get('rulesVersion') != rules_version or
            step <= boundary.get('minimumCheckpointStep', step)):
        raise ValueError('Transferred weights must train under the new rules before being labeled as a new-rules checkpoint.')
    base = run/'frozen-evaluations'
    if base.is_symlink():
        raise ValueError('Frozen evaluation storage must remain inside its owned run.')
    base.mkdir(exist_ok=True)
    final = base/f'milestone-{after_step}'
    if final.exists():
        raise ValueError('This milestone is already frozen; do not overwrite its evidence.')
    temporary = Path(tempfile.mkdtemp(prefix='.milestone-',dir=base))
    try:
        # Read the immutable pair before writing. Retention can retire either
        # source; the caller retries selection without leaving a partial freeze.
        sources = [source, source.with_suffix('.onnx')]
        contents = []
        for path in sources:
            if path.is_symlink() or path.stat().st_size > 100_000_000:
                raise ValueError('Invalid or oversized numbered checkpoint pair.')
            contents.append(path.read_bytes())
        for path, data in zip(sources, contents):
            (temporary/path.name).write_bytes(data)
        (temporary/'trainer.yaml').write_bytes(config)
        manifest = dict(checkpoint=str(temporary/source.name),trainerConfig=str(temporary/'trainer.yaml'),
            sha256=digest(contents[0]),step=step,afterStep=after_step,
            boardSize=7,rulesVersion=rules_version,openingEconomyVersion=OPENING_ECONOMY_VERSION,
            trainingRecipeSha256=recipe_sha,trainerConfigSha256=digest(config),frozenUnix=time.time())
        run_manifest = json.loads((run/'run.json').read_text())
        if 'weightTransfer' in run_manifest:
            manifest['weightTransfer'] = run_manifest['weightTransfer']
        atomic_json(temporary/'manifest.json',manifest)
        actor, _ = frozen_actor(temporary/'manifest.json', rules_version)  # Strict shapes and finite weights.
        manifest.update(actorId=actor.actor_id,checkpoint=str(final/source.name),trainerConfig=str(final/'trainer.yaml'))
        atomic_json(temporary/'manifest.json',manifest)
        temporary.rename(final)
        return final/'manifest.json'
    finally:
        if temporary.exists():
            shutil.rmtree(temporary)


def milestone(run, after_step, suite, worker, destination, *, wait_seconds=14400, poll_seconds=30):
    if (type(after_step) is not int or not 1 <= after_step < 2**62 or
            not 0 < wait_seconds <= 86400 or not 1 <= poll_seconds <= 60):
        raise ValueError('Choose a positive bounded milestone, wait and polling interval.')
    run = checked_run(run)
    destination = Path(destination)
    if destination.exists():
        raise ValueError('Evaluation destination already exists.')
    suite = Path(suite)
    suite_contents = suite.read_bytes()
    if json.loads(suite_contents).get('rulesVersion', RULES_VERSION) != run_rules_version(run):
        raise ValueError('Milestone evaluation must use the recorded run rules.')
    if worker_digest(worker) != json.loads(suite_contents).get('workerSha256'):
        raise ValueError('Worker differs from the fixed benchmark definition.')
    source_files = [Path(__file__).with_name(name) for name in (
        'evaluate_milestone.py','evaluate_suite.py','evaluate_policy.py','frozen_policy.py',
        'playtest_sampling.py','playtest.py','dotnet_environment.py','training_opponents.py','training_contract.py')]
    source_hashes = {path:digest(path.read_bytes()) for path in source_files}
    config = (run/'trainer.yaml').read_bytes()
    if len(config) > 256_000:
        raise ValueError('Oversized trainer configuration.')
    recipe_path = run/'training-opponents.json'
    recipe = recipe_path.read_bytes() if recipe_path.exists() else b''
    status = json.loads((run/'supervisor-status.json').read_text())
    trainer_pid = status['trainerPid']
    deadline = time.monotonic()+wait_seconds
    while True:
        checked_run(run)
        current_recipe = recipe_path.read_bytes() if recipe_path.exists() else b''
        if (run/'trainer.yaml').read_bytes() != config or current_recipe != recipe:
            raise ValueError('Training configuration changed while waiting; create a new measured milestone.')
        if suite.read_bytes() != suite_contents:
            raise ValueError('The fixed benchmark definition changed while waiting.')
        if any(digest(path.read_bytes()) != sha for path,sha in source_hashes.items()):
            raise ValueError('Evaluation code changed while waiting; create a new measured milestone.')
        status = json.loads((run/'supervisor-status.json').read_text())
        if status.get('state') != 'running' or status.get('trainerPid') != trainer_pid:
            raise RuntimeError('The measured trainer stopped or changed; this job does not restart it.')
        os.kill(trainer_pid,0)  # A status file alone does not prove a live learner.
        # Retention races are retried only during selection/copy, never after a
        # benchmark begins. Invalid weights/provenance remain explicit failures.
        for retry in range(3):
            try:
                source = latest_checkpoint(run,allow_pending=True)
                if source is not None and int(source.stem.rsplit('-',1)[1]) >= after_step:
                    manifest = freeze(run,source,after_step,config,digest(recipe))
                    return evaluate(manifest,suite,worker,destination)
                break
            except FileNotFoundError:
                if retry == 2:
                    raise
        if time.monotonic() >= deadline:
            raise TimeoutError('The checkpoint milestone was not reached within this job\'s wait budget.')
        time.sleep(min(poll_seconds,max(0,deadline-time.monotonic())))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run',type=Path,required=True)
    parser.add_argument('--after-step',type=int,required=True)
    parser.add_argument('--suite',type=Path,required=True)
    parser.add_argument('--worker',type=Path,required=True)
    parser.add_argument('--destination',type=Path,required=True)
    parser.add_argument('--wait-hours',type=float,default=4)
    args = parser.parse_args()
    from mlagents.torch_utils import torch
    torch.set_num_threads(2)
    job = args.destination.with_name(args.destination.name+'.job.json')
    job.parent.mkdir(parents=True,exist_ok=True)
    state = dict(state='waiting',pid=os.getpid(),afterStep=args.after_step,run=str(args.run),startedUnix=time.time())
    with job.open('x') as output:  # Two jobs cannot silently own the same evaluation.
        json.dump(state,output)
    try:
        result = milestone(args.run,args.after_step,args.suite,args.worker,args.destination,
                           wait_seconds=args.wait_hours*3600)
        state.update(state='completed',suiteId=result['suiteId'],candidate=result['candidate'])
    except BaseException as error:
        state.update(state='failed',error=str(error))
        raise
    finally:
        state['finishedUnix'] = time.time()
        atomic_json(job,state)
    print(json.dumps(dict(state=result['state'],suiteId=result['suiteId'],candidate=result['candidate'])))
