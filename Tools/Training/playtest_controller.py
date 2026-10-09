"""Local frozen-difficulty playtests with no learner or simulation workers running."""
import argparse
import hashlib
import os
from pathlib import Path
import shutil
import signal
import tempfile
import time

from decoupled_runtime import ViewerSession
from playtest import PlaytestSession, latest_checkpoint, atomic_json, read_json
from supervisor import prepare_root, size


def freeze(run):
    source = latest_checkpoint(run)
    directory = run / 'frozen-playtests' / source.stem
    if directory.is_symlink() or directory.parent.is_symlink():
        raise ValueError('Frozen playtest storage must not be linked.')
    directory.mkdir(parents=True, exist_ok=True)
    target = directory / source.name
    contents = source.read_bytes()
    digest = hashlib.sha256(contents).hexdigest()
    if target.is_symlink():
        raise ValueError('Frozen playtest weights must not be linked.')
    if target.exists():
        if hashlib.sha256(target.read_bytes()).hexdigest() != digest:
            raise ValueError('The frozen playtest differs from its source; preserve it for inspection.')
    else:
        descriptor, name = tempfile.mkstemp(prefix='checkpoint-', suffix='.tmp', dir=directory)
        temporary = Path(name)
        try:
            with os.fdopen(descriptor, 'wb') as stream:
                stream.write(contents)
            temporary.replace(target)
        finally:
            temporary.unlink(missing_ok=True)
    atomic_json(directory / 'manifest.json', dict(version=1, checkpoint=source.stem, sha256=digest,
                boardSize=read_json(run/'run.json')['boardSize'], difficulties=['Easy', 'Medium', 'Hard'],
                difficultyStatus='experimental_sampling_presets'))
    return target


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, required=True)
    parser.add_argument('--env', type=Path, required=True)
    args = parser.parse_args()
    run = args.run.absolute()
    root = run.parent.parent
    prepare_root(root)
    if run.parent != root/'runs' or run.is_symlink():
        raise ValueError('Choose an owned training run.')
    if not (args.env/'Contents/Info.plist').is_file():
        raise ValueError('Choose the built Mac viewer/player.')
    # The root lock prevents a learner and an offline controller writing the same
    # run/session state. No optimizer, worker or sleep assertion is started here.
    lock = root/'supervisor.lock'
    if lock.exists():
        try:
            os.kill(int(lock.read_text()), 0)
        except (ProcessLookupError, ValueError):
            lock.unlink()
        else:
            raise ValueError('Training or another playtest controller is active.')
    descriptor = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    with os.fdopen(descriptor, 'w') as stream:
        stream.write(str(os.getpid()))
    previous = read_json(run/'supervisor-status.json')
    viewer, session = None, None
    stopping = False
    def stop(*_):
        nonlocal stopping
        stopping = True
    for event in (signal.SIGINT, signal.SIGTERM):
        signal.signal(event, stop)
    state = dict(previous, state='playtesting', supervisorPid=os.getpid(), trainerPid=0, trainerReady=False,
                 viewerPid=0, playtestAvailable=True, error='', stopReason='', backend='standalone-dotnet')
    try:
        source = latest_checkpoint(run)
        reserve = previous.get('reserveBytes', 512_000_000)
        budget = previous.get('budgetBytes', 20_000_000_000)
        # Allow space for a retained frozen source and one temporary inference copy.
        if size(root) + source.stat().st_size * 2 > budget - reserve or shutil.disk_usage(root).free < 20_000_000_000 + reserve:
            raise ValueError('Insufficient shared training storage or free disk for a frozen playtest.')
        frozen = freeze(run)
        state['frozenPlaytestCheckpoint'] = frozen.stem
        session = PlaytestSession(run, args.env, frozen_checkpoint=frozen, training_active=False)
        viewer = ViewerSession(run, args.env, enabled=True)
        viewer.initial_launch = False
        (run/'stop.request').unlink(missing_ok=True)
        # Diagnostic input from the earlier session does not authorize a new game.
        (run/'playtest.request.json').unlink(missing_ok=True)
        viewer.launch()
        if viewer.error:
            raise ValueError(viewer.error)
        while not stopping and not (run/'stop.request').exists():
            session.poll({'paused': False})
            viewer.poll({}, state)
            state.update(usedBytes=size(root), freeBytes=shutil.disk_usage(root).free,
                         updatedUtc=time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()))
            atomic_json(run/'supervisor-status.json',state)
            atomic_json(root/'active.json',dict(runId=run.name,supervisorPid=os.getpid()))
            if viewer.process.poll() is not None and session.process is None:
                break
            time.sleep(.5)
        state.update(state='stopped',stopReason='playtest_closed',playtestAvailable=False)
    except Exception as error:
        state.update(state='failed',stopReason='playtest_failure',playtestAvailable=False,error=f'{type(error).__name__}: {error}')
        raise
    finally:
        try:
            if session: session.close()
            if viewer: viewer.close()
            state.update(viewerPid=0,updatedUtc=time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()))
            atomic_json(run/'supervisor-status.json',state)
        finally:
            if lock.exists() and lock.read_text()==str(os.getpid()):lock.unlink()


if __name__=='__main__':
    main()
