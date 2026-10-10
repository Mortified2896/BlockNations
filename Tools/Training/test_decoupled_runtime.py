import json
import os
from pathlib import Path
import plistlib
import tempfile
import time
from types import SimpleNamespace
import unittest

from bounded_log import BoundedLog
from decoupled_runtime import configure, ViewerSession
from training_contract import RULES_VERSION


class DecoupledRuntimeTests(unittest.TestCase):
    def test_execution_settings_preserve_the_model_contract(self):
        with tempfile.TemporaryDirectory() as directory:
            run=Path(directory); worker=run/'worker';worker.touch()
            marker=b'{"boardSize":7,"initialWeights":"random"}'
            (run/'run.json').write_bytes(marker)
            (run/'training-control.json').write_text('{"paused":true}')
            args=SimpleNamespace(backend='dotnet',worker=worker,parallel_games=4,seed=42,board_size=7,curriculum=False,curriculum_distance=2)
            env={};configure(args,run,env)
            self.assertEqual((run/'run.json').read_bytes(),marker)
            self.assertFalse((run/'training-control.json').exists())
            self.assertEqual(env['BLOCKNATIONS_PARALLEL_GAMES'],'4')
            record=json.loads((run/'execution-settings.json').read_text())
            self.assertEqual(record['modelSchema'],2)
            self.assertEqual(record['rulesVersion'],RULES_VERSION)
            contract=json.loads((run/'checkpoint-contract.json').read_text())
            self.assertEqual(contract['minimumCheckpointStep'],0)
            self.assertEqual(contract['openingEconomyVersion'],2)

    def test_export_boundary_survives_resume_and_moves_after_a_rules_change(self):
        with tempfile.TemporaryDirectory() as directory:
            run=Path(directory); worker=run/'worker';worker.touch()
            models=run/'checkpoints'/run.name/'BlockNationsSeatV2';models.mkdir(parents=True)
            (models/'BlockNationsSeatV2-100.pt').write_bytes(b'old weights')
            args=SimpleNamespace(backend='dotnet',worker=worker,parallel_games=4,seed=42,board_size=7,curriculum=False,curriculum_distance=2)
            configure(args,run,{})
            (models/'BlockNationsSeatV2-200.pt').write_bytes(b'new weights')
            configure(args,run,{})
            self.assertEqual(json.loads((run/'checkpoint-contract.json').read_text())['minimumCheckpointStep'],100)
            (run/'execution-settings.json').write_text('{"rulesVersion":"old"}')
            configure(args,run,{})
            self.assertEqual(json.loads((run/'checkpoint-contract.json').read_text())['minimumCheckpointStep'],200)
            self.assertEqual(len(list(run.glob('checkpoint-contract-before-*.json'))),1)

    def test_optional_viewer_failure_leaves_training_status_intact(self):
        with tempfile.TemporaryDirectory() as directory:
            run=Path(directory);session=ViewerSession(run,run/'missing.app',True)
            state={'state':'running','trainerPid':123}
            session.poll({'trainerConnected':True},state)
            self.assertTrue(state['viewerError'])
            self.assertEqual(state['state'],'running')
            self.assertEqual(state['trainerPid'],123)
            session.close()

    def test_rules_transition_preserves_weights_and_archives_incomparable_rating(self):
        with tempfile.TemporaryDirectory() as directory:
            run=Path(directory); worker=run/'worker'; worker.touch()
            (run/'execution-settings.json').write_text('{"rulesVersion":"blocknations-simulation-v2"}')
            (run/'match-elo.json').write_text('{"elo":1700}')
            (run/'human-learning-status.json').write_text('{"rulesVersion":"old","usedExamples":64}')
            (run/'board-7-progress.json').write_text('{"firstPlayerWins":100}')
            recordings=run/'human-games'; recordings.mkdir(); (recordings/'record.json').write_text('{"rulesVersion":"old"}')
            (run/'checkpoint.pt').write_bytes(b'unchanged weights and optimizer')
            args=SimpleNamespace(backend='dotnet',worker=worker,parallel_games=4,seed=42,board_size=7,curriculum=False,curriculum_distance=2)
            configure(args,run,{})
            self.assertEqual((run/'checkpoint.pt').read_bytes(), b'unchanged weights and optimizer')
            self.assertFalse((run/'match-elo.json').exists())
            self.assertEqual(len(list(run.glob('match-elo-before-*.json'))), 1)
            self.assertFalse((run/'human-learning-status.json').exists())
            self.assertEqual(len(list(run.glob('human-learning-status-before-*.json'))),1)
            self.assertFalse((run/'board-7-progress.json').exists())
            self.assertTrue((recordings/'record.json').is_file())
            configure(args,run,{})
            self.assertEqual(len(list(run.glob('match-elo-before-*.json'))), 1)

    def test_closing_and_reopening_viewer_never_stops_a_learner(self):
        with tempfile.TemporaryDirectory() as directory:
            run=Path(directory);app=run/'Viewer.app';binary=app/'Contents/MacOS/viewer'
            binary.parent.mkdir(parents=True)
            with (app/'Contents/Info.plist').open('wb') as stream:
                plistlib.dump({'CFBundleExecutable':'viewer'},stream)
            # Real child process, no graphics or learner required for ownership checks.
            binary.write_text('#!/bin/sh\nexec /bin/sleep 60\n');binary.chmod(0o755)
            session=ViewerSession(run,app,True);state={'state':'running','trainerPid':123}
            session.poll({'trainerConnected':True},state)
            first=session.process.pid
            session.launch()
            self.assertEqual(session.process.pid,first, 'A second request must not duplicate a live viewer.')
            session.process.terminate()
            session.process.wait(timeout=3)
            (run/'viewer.request').touch()
            session.poll({'trainerConnected':True},state)
            self.assertNotEqual(session.process.pid,first)
            self.assertEqual(state['state'],'running')
            self.assertEqual(state['trainerPid'],123)
            self.assertFalse((run/'stop.request').exists())
            self.assertFalse((run/'training-control.json').exists())
            session.close()
            self.assertIsNotNone(session.process.poll())

    def test_viewer_diagnostics_are_bounded(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'viewer.log';log=BoundedLog(path,limit=100)
            for _ in range(100):log.append(b'x'*50)
            log.close()
            files=list(Path(directory).glob('viewer.log*'))
            self.assertLessEqual(len(files),5)
            self.assertLessEqual(sum(p.stat().st_size for p in files),500)


if __name__=='__main__':unittest.main()
