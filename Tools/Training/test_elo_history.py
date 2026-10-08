import tempfile
from pathlib import Path
import unittest
from elo_history import TrainerEloHistory


def report(step, elo, behavior='BlockNationsSeatV2'):
    return f'[INFO] {behavior}. Step: {step}. Time Elapsed: 10 s. Mean Reward: 1. Training. ELO: {elo}.\n'.encode()


class EloTests(unittest.TestCase):
    def test_fragmented_lines_ignore_other_behaviors_and_nonfinite_values(self):
        history = TrainerEloHistory('run', 5)
        chunk = report(1000, 1240)
        history.feed(chunk[:23]); history.feed(chunk[23:])
        history.feed(report(2000, 'NaN') + report(2000, 1300, 'OtherBehavior'))
        self.assertEqual(history.snapshot()['points'], [{'step': 0, 'elo': 1200}, {'step': 1000, 'elo': 1240}])

    def test_resume_duplicates_and_bounded_history_keep_start_and_latest(self):
        history = TrainerEloHistory('run', 5)
        for i in range(1, 301): history.feed(report(i * 1000, 1200 + i))
        history.feed(report(300000, 1501) + report(1000, 900))
        points = history.snapshot()['points']
        self.assertEqual(len(points), 200)
        self.assertEqual(points[0]['step'], 0)
        self.assertEqual(points[-1], {'step': 300000, 'elo': 1501})

    def test_restore_and_log_backfill_preserve_identity(self):
        import json
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            (run / 'trainer.log.1').write_bytes(report(1000, 1250))
            (run / 'trainer.log').write_bytes(report(2000, 1280))
            history = TrainerEloHistory('run', 5)
            history.backfill(run)
            path = run / 'training-elo.json'
            path.write_text(json.dumps(history.snapshot()))
            restored = TrainerEloHistory('run', 5)
            restored.restore(path)
            self.assertEqual(restored.snapshot(), history.snapshot())
            wrong_board = TrainerEloHistory('run', 11)
            wrong_board.restore(path)
            self.assertEqual(len(wrong_board.snapshot()['points']), 1)
