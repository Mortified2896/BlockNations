import json
import fcntl
import os
import subprocess
import sys
from pathlib import Path
import tempfile
import time
import unittest
from unittest.mock import Mock, patch

from automatic_evaluation import OWNER, CONFIG, STATUS, Scheduler, settings, decisions, ready, retire, perform_job
from automatic_evaluation_session import AutomaticEvaluationSession


class AutomaticEvaluationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name).resolve()
        self.run = self.root / 'runs/saved'
        self.run.mkdir(parents=True)
        self.write(self.root / '.blocknations-training-root.json', {'owner': 'BlockNations.LocalTraining.v1'})
        self.write(self.run / 'run.json', dict(owner='BlockNations.LocalTraining.v1', schema=2, boardSize=7,
                                              observationSize=3120, actionCount=259, openingEconomyVersion=2))
        self.suite = self.root / 'suite.json'
        self.write(self.suite, {})
        self.config = dict(owner=OWNER, version=1, runId='saved', enabled=True, intervalDecisions=2000000, suite=str(self.suite))
        self.write(self.run / CONFIG, self.config)
        self.write(self.run / 'board-7-progress.json', dict(version=2, boardSize=7, decisions=90000000))
        self.write(self.run / 'arena-status.json', dict(trainerConnected=True, paused=False))
        self.supervisor = dict(state='running', trainerPid=os.getpid(), usedBytes=5_000_000_000,
                               reserveBytes=512_000_000, budgetBytes=20_000_000_000)
        self.write(self.run / 'supervisor-status.json', self.supervisor)
        self.worker = self.root / 'worker'
        self.worker.write_bytes(b'fixture')

    def tearDown(self):
        self.temp.cleanup()

    def write(self, path, value):
        path.write_text(json.dumps(value))

    def test_settings_preserve_other_boards_and_reject_linked_suites(self):
        self.assertEqual(settings(self.run), self.config)
        self.config['intervalDecisions'] = True
        self.write(self.run / CONFIG, self.config)
        with self.assertRaises(ValueError): settings(self.run)
        self.config['intervalDecisions'] = 2000000
        self.write(self.run / CONFIG, self.config)
        real = self.suite.with_name('real.json'); self.suite.rename(real); self.suite.symlink_to(real)
        with self.assertRaises(ValueError): settings(self.run)

    def test_global_progress_is_separate_from_session_games_and_elo(self):
        self.write(self.run / 'arena-status.json', dict(games=16000, decisions=5000))
        self.write(self.run / 'match-elo.json', dict(elo=9000))
        self.assertEqual(decisions(self.run), 90000000)
        self.write(self.run / 'board-7-progress.json', dict(version=2, boardSize=7, decisions=-1))
        with self.assertRaises(ValueError): decisions(self.run)

    def test_pauses_stale_supervisors_and_storage_pressure_do_not_start_evaluation(self):
        with patch('automatic_evaluation.shutil.disk_usage', return_value=Mock(free=30_000_000_000)):
            self.assertTrue(ready(self.run, self.supervisor))
            self.assertFalse(ready(self.run, dict(self.supervisor, usedBytes=19_500_000_000)))
            self.write(self.run / 'arena-status.json', dict(trainerConnected=True, paused=True))
            self.assertFalse(ready(self.run, self.supervisor))
            self.write(self.run / 'arena-status.json', dict(trainerConnected=True))
            os.utime(self.run / 'supervisor-status.json', (1, 1))
            self.assertFalse(ready(self.run, self.supervisor))

    @patch('automatic_evaluation.subprocess.Popen')
    @patch('automatic_evaluation.latest_checkpoint')
    @patch('automatic_evaluation.ready', return_value=True)
    def test_single_job_and_persisted_milestone_survive_resume(self, ready_mock, latest, launch):
        latest.return_value = self.root / 'BlockNationsSeatV2-77000000.pt'
        process = Mock(); process.poll.return_value = None; launch.return_value = process
        scheduler = Scheduler(self.run, self.worker)
        scheduler.tick(); scheduler.tick()
        launch.assert_called_once()
        self.assertEqual(scheduler.state['nextAtDecisions'], 92000000)
        self.assertEqual(scheduler.state['lastEvaluatedDecisions'], 90000000)
        self.assertNotIn('--env', launch.call_args.args[0])
        job = json.loads(scheduler.job_path.read_text())
        job.update(state='retained', candidateStep=77000000, reason='No improvement')
        self.write(scheduler.job_path, job)
        process.poll.return_value = 0
        scheduler.tick()
        self.assertEqual(scheduler.state['lastOutcome'], 'retained')
        resumed = Scheduler(self.run, self.worker)
        resumed.tick(); launch.assert_called_once()
        self.write(self.run / 'board-7-progress.json', dict(version=2, boardSize=7, decisions=94000000))
        resumed.tick(); launch.assert_called_once()  # No new checkpoint; no repeated candidate.
        latest.return_value = self.root / 'BlockNationsSeatV2-79000000.pt'
        process.poll.return_value = None
        resumed.tick(); self.assertEqual(launch.call_count, 2)
        resumed.close()

    @patch('automatic_evaluation.subprocess.Popen')
    @patch('automatic_evaluation.latest_checkpoint')
    def test_pause_cancels_only_its_evaluation_and_marks_partial_results(self, latest, launch):
        latest.return_value = self.root / 'BlockNationsSeatV2-77000000.pt'
        process = Mock(); process.poll.return_value = None; launch.return_value = process
        scheduler = Scheduler(self.run, self.worker)
        with patch('automatic_evaluation.ready', return_value=True): scheduler.tick()
        with patch('automatic_evaluation.ready', return_value=False), \
                patch('automatic_evaluation.stop_process', side_effect=lambda p:setattr(p.poll, 'return_value', 0)) as stop:
            scheduler.tick()
        stop.assert_called_once_with(process)
        self.assertEqual(scheduler.state['lastOutcome'], 'cancelled')
        self.assertFalse((self.run / 'stop.request').exists())
        self.assertFalse((self.run / 'training-control.json').exists())

    def test_retention_preserves_selected_and_manual_models(self):
        for i in range(6):
            folder = self.run / 'frozen-evaluations' / ('milestone-' + str(i))
            folder.mkdir(parents=True)
            self.write(folder / 'automatic-owner.json', dict(owner=OWNER, runId='saved'))
            jobdir = self.run / 'evaluations/automatic' / str(i); jobdir.mkdir(parents=True)
            self.write(jobdir / 'job.json', dict(owner=OWNER, runId='saved', state='retained',
                                               candidateManifest=str((folder/'manifest.json').relative_to(self.run))))
            os.utime(jobdir / 'job.json', (100+i, 100+i))
        self.write(self.run / 'playtest-champion.json', dict(candidateManifest='frozen-evaluations/milestone-0/manifest.json'))
        manual = self.run / 'frozen-evaluations/manual'; manual.mkdir()
        retire(self.run)
        self.assertTrue(manual.exists())
        self.assertTrue((self.run / 'frozen-evaluations/milestone-0').exists())
        self.assertFalse((self.run / 'frozen-evaluations/milestone-1').exists())
        self.assertTrue((self.run / 'frozen-evaluations/milestone-5').exists())

    @patch('automatic_evaluation_session.subprocess.Popen')
    def test_future_supervisor_reuses_independently_attached_evaluator(self, launch):
        self.write(self.run / STATUS, dict(owner=OWNER, runId='saved', pid=os.getpid(), state='waiting'))
        session = AutomaticEvaluationSession(self.run, self.worker)
        session.poll(); launch.assert_not_called()
        (self.run / STATUS).unlink(); session.next_launch = 0
        session.poll(); launch.assert_called_once()
        self.assertIn('automatic_evaluation.py', launch.call_args.args[0][1])

    def test_process_lock_prevents_two_scheduler_writers(self):
        script = Path(__file__).with_name('automatic_evaluation.py')
        command = [sys.executable, str(script), '--run', str(self.run), '--worker', str(self.worker)]
        process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            deadline = time.monotonic() + 5
            while not (self.run / STATUS).exists() and time.monotonic() < deadline:
                time.sleep(.05)
            self.assertTrue((self.run / STATUS).exists())
            duplicate = subprocess.run(command, capture_output=True, timeout=5)
            self.assertEqual(duplicate.returncode, 0, duplicate.stderr)
            self.assertEqual(json.loads((self.run / STATUS).read_text())['pid'], process.pid)
        finally:
            process.terminate(); process.wait(timeout=10)

    def test_complete_job_uses_existing_promotion_gate_and_preserves_rejected_champion(self):
        source = self.root / 'BlockNationsSeatV2-77.pt';source.write_bytes(b'fixture')
        folder = self.run / 'frozen-evaluations/milestone-77';folder.mkdir(parents=True)
        manifest = folder / 'manifest.json';self.write(manifest, {})
        self.write(self.run / 'trainer.yaml', {})
        self.write(self.run / 'training-opponents.json', {})
        champion = dict(candidateManifest='frozen-evaluations/incumbent/manifest.json', suiteId='fixed', checkpoint='old')
        self.write(self.run / 'playtest-champion.json', champion)
        job_dir = self.run / 'evaluations/automatic/77';job_dir.mkdir(parents=True)
        job_path = job_dir / 'job.json';self.write(job_path, dict(owner=OWNER, runId='saved', state='queued'))
        before = (self.run / 'playtest-champion.json').read_bytes()
        with patch('evaluate_suite.prepare', return_value=(None, None, None, dict(suiteId='fixed'))), \
                patch('playtest_champion.resolve_champion', return_value=source), \
                patch('automatic_evaluation.latest_checkpoint', return_value=source), \
                patch('evaluate_milestone.freeze', return_value=manifest), \
                patch('evaluate_suite.run', return_value=dict(results=[dict(games=64)], suiteId='fixed')) as evaluate, \
                patch('reference_score.publish'), \
                patch('playtest_champion.select', side_effect=ValueError('Candidate regressed against a fixed opponent or starting role; champion preserved.')) as select:
            result = perform_job(self.run, self.worker, job_path)
        evaluate.assert_called_once();select.assert_called_once_with(self.run, manifest, job_dir / 'results/suite.json')
        self.assertEqual(result['state'], 'retained')
        self.assertEqual(result['games'], 64)
        self.assertEqual((self.run / 'playtest-champion.json').read_bytes(), before)

    def test_incompatible_benchmark_never_evaluates_or_promotes(self):
        self.write(self.run / 'playtest-champion.json', dict(candidateManifest='old.json', suiteId='old'))
        job_path = self.run / 'job.json';self.write(job_path, dict(owner=OWNER, runId='saved'))
        with patch('playtest_champion.resolve_champion'), \
                patch('evaluate_suite.prepare', return_value=(None, None, None, dict(suiteId='changed'))), \
                patch('evaluate_suite.run') as evaluate, patch('playtest_champion.select') as select:
            result = perform_job(self.run, self.worker, job_path)
        evaluate.assert_not_called();select.assert_not_called()
        self.assertEqual(result['state'], 'failed')

    @patch('automatic_evaluation.subprocess.Popen')
    @patch('automatic_evaluation.latest_checkpoint')
    @patch('automatic_evaluation.ready', return_value=True)
    def test_restarted_scheduler_cannot_overlap_an_existing_job(self, ready_mock, latest, launch):
        latest.return_value = self.root / 'BlockNationsSeatV2-77000000.pt'
        with (self.run / 'automatic-evaluation-job.lock').open('a') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            Scheduler(self.run, self.worker).tick()
            launch.assert_not_called()
