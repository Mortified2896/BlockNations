"""Periodic fixed-reference evaluation and guarded private playtest promotion.

Separate optional processes; never update learner weights or launch a viewer.
"""
import argparse
import fcntl
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time
import uuid

from playtest import atomic_json, read_json, latest_checkpoint

OWNER = 'BlockNations.AutomaticEvaluation.v1'
CONFIG = 'automatic-evaluation.json'
STATUS = 'automatic-evaluation-status.json'
RESERVATION = 256 * 1024 * 1024


def alive(pid):
    if type(pid) is not int or pid <= 0:
        return False
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


def settings(run):
    run = Path(run)
    config = read_json(run / CONFIG)
    if not config or config.get('enabled') is False:
        return None
    manifest = read_json(run / 'run.json')
    root = run.parent.parent
    suite = Path(config.get('suite', ''))
    if (run.is_symlink() or run.resolve() != run.absolute() or
            manifest.get('owner') != 'BlockNations.LocalTraining.v1' or manifest.get('schema') != 2 or
            manifest.get('boardSize') != 7 or manifest.get('observationSize') != 3120 or
            manifest.get('actionCount') != 259 or manifest.get('openingEconomyVersion') != 2 or
            config.get('owner') != OWNER or config.get('version') != 1 or
            config.get('runId') != run.name or config.get('enabled') is not True or
            type(config.get('intervalDecisions')) is not int or not 100_000 <= config['intervalDecisions'] <= 100_000_000 or
            not suite.is_absolute() or not suite.is_relative_to(root) or not suite.is_file() or
            any(p.is_symlink() for p in (suite, *suite.parents) if p.is_relative_to(root)) or
            read_json(root / '.blocknations-training-root.json').get('owner') != 'BlockNations.LocalTraining.v1'):
        raise ValueError('Automatic evaluation requires an owned 7x7 run and fixed suite inside its training root.')
    if type(config.get('replicatedPromotion', False)) is not bool:
        raise ValueError('Replicated promotion must be explicitly enabled or disabled.')
    if config.get('replicatedPromotion', False):
        from tournament_selection import policy
        policy(config.get('promotionMargins'))
    return config


def decisions(run):
    progress = read_json(Path(run) / 'board-7-progress.json')
    count = progress.get('decisions')
    if (progress.get('version') != 2 or progress.get('boardSize') != 7 or
            type(count) is not int or count < 0):
        raise ValueError('Awaiting valid cumulative training decisions.')
    return count


def ready(run, supervisor):
    age = time.time() - (Path(run) / 'supervisor-status.json').stat().st_mtime
    return (age < 90 and supervisor.get('state') == 'running' and alive(supervisor.get('trainerPid')) and
            read_json(Path(run) / 'arena-status.json').get('trainerConnected') is True and
            not read_json(Path(run) / 'arena-status.json').get('paused') and
            supervisor.get('usedBytes', 2**63) + supervisor.get('reserveBytes', RESERVATION) + RESERVATION <
            supervisor.get('budgetBytes', 0) and
            shutil.disk_usage(run).free > 20_000_000_000 + RESERVATION)


def stop_process(process):
    if process and process.poll() is None:
        process.send_signal(signal.SIGINT)
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGTERM)
            process.wait(timeout=5)


def perform_job(run, worker, job_path):
    from mlagents.torch_utils import torch
    torch.set_num_threads(1)
    from evaluate_milestone import freeze
    from evaluate_suite import prepare, run as evaluate
    from playtest_champion import resolve_champion, select

    job = read_json(job_path)
    if job.get('owner') != OWNER or job.get('runId') != run.name:
        raise ValueError('Evaluation job belongs to a different run.')
    try:
        config = settings(run)
        incumbent = read_json(run / 'playtest-champion.json')
        resolve_champion(run)
        _, _, _, definition = prepare(run / incumbent['candidateManifest'], Path(config['suite']))
        if definition['suiteId'] != incumbent['suiteId']:
            from tournament_selection import baseline, refresh
            job.update(state='refreshing_baseline', reason='Re-evaluating the same champion with the current benchmark.')
            atomic_json(job_path, job)
            receipt = baseline(run, run/incumbent['candidateManifest'], Path(config['suite']), worker)
            incumbent = refresh(run, receipt)
        for retry in range(3):
            try:
                source = latest_checkpoint(run)
                step = int(source.stem.rsplit('-', 1)[1])
                existing = run/'frozen-evaluations'/('milestone-'+str(step))/'manifest.json'
                if existing.exists():
                    from evaluate_suite import frozen_actor
                    actor, _ = frozen_actor(existing, read_json(run/'run.json')['rulesVersion'])
                    if actor.sha256 != hashlib.sha256(source.read_bytes()).hexdigest():
                        raise ValueError('Existing frozen checkpoint differs from the selected numbered pair.')
                    manifest = existing
                else:
                    manifest = freeze(run, source, step, (run / 'trainer.yaml').read_bytes(),
                                      hashlib.sha256((run / 'training-opponents.json').read_bytes()).hexdigest())
                break
            except FileNotFoundError:
                if retry == 2:
                    raise
        atomic_json(manifest.parent / 'automatic-owner.json', dict(owner=OWNER, runId=run.name))
        job.update(state='evaluating', candidateStep=step, candidateManifest=str(manifest.relative_to(run)))
        atomic_json(job_path, job)
        result = evaluate(manifest, Path(config['suite']), worker, job_path.parent / 'results')
        job.update(games=sum(r['games'] for r in result['results']), suiteId=result['suiteId'])
        confirmation = None
        if config.get('replicatedPromotion', False):
            from tournament_selection import baseline, confirmation_suite, review
            repeat_suite = job_path.parent/'confirmation-suite.json'
            atomic_json(repeat_suite, confirmation_suite(read_json(Path(config['suite']))))
            job.update(state='confirming', reason='Comparing both actors on a separate seed pack.')
            atomic_json(job_path, job)
            old_repeat_path = baseline(run, run/incumbent['candidateManifest'], repeat_suite, worker)
            new_repeat = evaluate(manifest, repeat_suite, worker, job_path.parent/'confirmation-results')
            confirmation = dict(incumbent=old_repeat_path, candidate=job_path.parent/'confirmation-results/suite.json')
            job['review'] = review(run, read_json(run/incumbent['evaluation']), result,
                                   read_json(old_repeat_path), new_repeat, step, config.get('promotionMargins'))
        try:
            kwargs = dict(confirmation=confirmation, margins=config.get('promotionMargins')) if confirmation else {}
            selected = select(run, manifest, job_path.parent / 'results/suite.json', **kwargs)
            job.update(state='promoted', champion=selected['checkpoint'], reason='Passed the fixed-reference promotion gate.')
        except ValueError as error:
            if not str(error).startswith(('Candidate increased', 'Candidate regressed', 'Candidate did not improve')):
                raise
            job.update(state='retained', champion=incumbent['checkpoint'], reason=str(error))
        from reference_score import publish
        publish(run, manifest, job_path.parent / 'results/suite.json')
    except BaseException as error:
        job.update(state='cancelled' if isinstance(error, KeyboardInterrupt) else 'failed', reason=str(error))
    finally:
        job['finishedUnix'] = time.time()
        atomic_json(job_path, job)
    return job


def retire(run):
    """Bound only our completed evidence; preserve selected and manual models."""
    base = run / 'evaluations/automatic'
    champion = read_json(run / 'playtest-champion.json').get('candidateManifest')
    selected = (run / champion).parent if champion else None
    protected_evidence = [read_json(run/'playtest-champion.json')]
    protected_evidence += [read_json(p) for p in (run/'champion-history').glob('*.json')]
    jobs = sorted(base.glob('*/job.json'), key=lambda p: p.stat().st_mtime, reverse=True)
    for path in jobs[3:]:
        job = read_json(path)
        if job.get('owner') != OWNER or job.get('runId') != run.name or job.get('state') not in ('retained', 'promoted', 'failed', 'cancelled'):
            continue
        model = run / job.get('candidateManifest', 'missing')
        folder = model.parent
        if any(any((run/relative).is_relative_to(path.parent) for relative in
                   [item.get('evaluation', 'missing'), *item.get('confirmation', {}).values()])
               for item in protected_evidence):
            continue
        if selected == folder or any((run/item.get('candidateManifest', 'missing')).parent == folder for item in protected_evidence):
            continue
        if (folder.is_relative_to(run / 'frozen-evaluations') and not folder.is_symlink() and
                read_json(folder / 'automatic-owner.json') == dict(owner=OWNER, runId=run.name)):
            pins = read_json(run / 'pins.json').get('files', [])
            if any((run / p).is_relative_to(folder) for p in pins):
                continue
            shutil.rmtree(folder)
        if not path.parent.is_symlink():
            shutil.rmtree(path.parent)


class Scheduler:
    def __init__(self, run, worker):
        self.run, self.worker = Path(run), Path(worker)
        previous = read_json(self.run / STATUS)
        if previous and (previous.get('owner') != OWNER or previous.get('runId') != self.run.name):
            raise ValueError('Unrelated evaluation status was preserved.')
        self.state = dict(previous, owner=OWNER, runId=self.run.name, pid=os.getpid(), state='waiting')
        self.process = self.log = self.job_path = None

    def publish(self):
        self.state['updatedUnix'] = time.time()
        atomic_json(self.run / STATUS, self.state)

    def tick(self):
        config = settings(self.run)
        supervisor = read_json(self.run / 'supervisor-status.json')
        running = config is not None and ready(self.run, supervisor)
        if self.process:
            cancelled = False
            if not running or time.time() - self.state['startedUnix'] > 1800:
                stop_process(self.process)
                cancelled = True
            if self.process.poll() is None:
                self.state['stage'] = read_json(self.job_path).get('state', 'evaluating')
                self.publish()
                return
            job = read_json(self.job_path)
            if job.get('state') not in ('promoted', 'retained', 'failed', 'cancelled'):
                job.update(state='cancelled' if cancelled else 'failed',
                           reason='Evaluation cancelled for pause, stop, storage headroom or timeout.' if cancelled else
                           'Evaluation process stopped before reporting a result.')
                atomic_json(self.job_path, job)
            self.state.update(state='waiting', lastOutcome=job.get('state', 'failed'),
                              lastReason=job.get('reason', 'Evaluation process stopped before reporting a result.'),
                              lastCandidateStep=job.get('candidateStep', self.state['candidateStep']))
            self.state['reviewRequired'] = job.get('review', {}).get('state') == 'review_required'
            self.state['stage'] = ''
            self.process = None
            self.log.close()
            self.log = None
            retire(self.run)
        review = read_json(self.run/'training-review.json')
        if review.get('owner') == 'BlockNations.TrainingReview.v1' and review.get('runId') == self.run.name and review.get('state') == 'review_required':
            self.state.update(state='review_required', reason=review['reason'])
            self.publish()
            return
        if not running:
            self.state.update(state='waiting', reason='Waiting for active training and storage headroom.')
            self.publish()
            return
        count = decisions(self.run)
        threshold = self.state.get('lastEvaluatedDecisions', 0) + config['intervalDecisions']
        request = self.run/'automatic-evaluation.request'
        requested = request.is_file()
        self.state.update(currentDecisions=count, nextAtDecisions=threshold)
        if count < threshold and not requested:
            self.state['reason'] = 'Waiting for the next benchmark milestone.'
            self.publish()
            return
        source = latest_checkpoint(self.run, allow_pending=True)
        step = int(source.stem.rsplit('-', 1)[1]) if source else 0
        if step <= self.state.get('lastCandidateStep', 0) and not requested:
            self.publish()
            return
        # A scheduler crash can leave its isolated job finishing independently.
        # The job holds a second lock so another scheduler never overlaps it.
        with (self.run / 'automatic-evaluation-job.lock').open('a') as lock:
            try:
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                self.state.update(reason='An existing evaluation is still finishing.')
                self.publish()
                return
        folder = self.run / 'evaluations/automatic' / (str(step) + '-' + uuid.uuid4().hex[:8])
        folder.mkdir(parents=True, exist_ok=False)
        self.job_path = folder / 'job.json'
        atomic_json(self.job_path, dict(owner=OWNER, runId=self.run.name, state='queued', decisions=count))
        command = [sys.executable, str(Path(__file__).absolute()), '--run', str(self.run),
                   '--worker', str(self.worker), '--job', str(self.job_path)]
        if sys.platform == 'darwin':
            command = ['/usr/bin/nice', '-n', '10'] + command
        self.log = (folder / 'evaluation.log').open('wb')
        self.process = subprocess.Popen(command, stdout=self.log, stderr=subprocess.STDOUT, start_new_session=True)
        request.unlink(missing_ok=True)
        self.state.update(state='evaluating', candidateStep=step, lastEvaluatedDecisions=count,
                          startedUnix=time.time(), job=str(self.job_path.relative_to(self.run)),
                          nextAtDecisions=count + config['intervalDecisions'])
        self.publish()

    def close(self):
        stop_process(self.process)
        if self.log:
            self.log.close()
        self.state.update(state='stopped', pid=0)
        self.publish()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, required=True)
    parser.add_argument('--worker', type=Path, required=True)
    parser.add_argument('--job', type=Path)
    args = parser.parse_args()
    run = args.run.absolute()
    settings(run)
    if args.job:
        with (run / 'automatic-evaluation-job.lock').open('a') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            result = perform_job(run, args.worker, args.job)
            return 0 if result['state'] in ('promoted', 'retained') else 1
    with (run / 'automatic-evaluation.lock').open('a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            return 0
        scheduler = Scheduler(run, args.worker)
        stopped = False
        def stop(*_):
            nonlocal stopped
            stopped = True
        signal.signal(signal.SIGINT, stop)
        signal.signal(signal.SIGTERM, stop)
        try:
            while not stopped:
                try:
                    scheduler.tick()
                except (OSError, ValueError) as error:
                    scheduler.state.update(state='error', reason=str(error))
                    scheduler.publish()
                time.sleep(5)
        finally:
            scheduler.close()
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
