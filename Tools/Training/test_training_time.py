import json
from pathlib import Path
import tempfile
import unittest
from training_time import TrainingTime


class TrainingTimeTests(unittest.TestCase):
    def test_active_time_excludes_pause_and_reconnect(self):
        clock = TrainingTime('run', 5)
        for now, active in [(0, False), (10, True), (12, True), (20, False),
                            (100, False), (110, True), (112, True)]:
            clock.tick(now, active)
        self.assertEqual(clock.snapshot()['totalSeconds'], 4)

    def test_resume_keeps_total_without_counting_stopped_time(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            clock = TrainingTime('run', 5, 3600)
            (run / 'training-time.json').write_text(json.dumps(clock.snapshot()))
            resumed = TrainingTime.load(run, 'run', 5)
            resumed.tick(10000, True); resumed.tick(10002, True)
            self.assertEqual(resumed.snapshot()['totalSeconds'], 3602)
            self.assertFalse(resumed.snapshot()['estimated'])
            self.assertEqual(TrainingTime.load(run, 'other', 5).snapshot()['totalSeconds'], 0)

    def test_legacy_logs_sum_sessions_across_rotation_and_mark_estimate(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            def report(seconds):
                return f'[INFO] BlockNationsSeatV2. Step: 1000. Time Elapsed: {seconds} s. Training.\n'
            (run / 'trainer.log.1').write_text(report(100) + report(200))
            (run / 'trainer.log').write_text(report(300) + report(20) + report(40))
            clock = TrainingTime.load(run, 'run', 5)
            self.assertEqual(clock.snapshot()['totalSeconds'], 340)
            self.assertTrue(clock.snapshot()['estimated'])
            (run / 'training-time.json').write_text(json.dumps(clock.snapshot()))
            self.assertEqual(TrainingTime.load(run, 'run', 5).snapshot(), clock.snapshot())
