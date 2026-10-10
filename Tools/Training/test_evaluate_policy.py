import os
from pathlib import Path
import tempfile
import unittest

import numpy as np

from evaluate_policy import evaluate, wilson

WORKER = Path(os.environ.get('BLOCKNATIONS_TEST_WORKER',Path(__file__).resolve().parents[2]/'Build/TrainingWorker/BlockNations.TrainingWorker.dll'))


class EvaluationTests(unittest.TestCase):
    def test_confidence_range_includes_uncertainty_and_empty_results_have_no_win_rate(self):
        self.assertIsNone(wilson(0,0))
        low,high=wilson(50,100)
        self.assertLess(low,.5);self.assertGreater(high,.5)
        self.assertGreater(wilson(0,64)[1],0)

    @unittest.skipUnless(WORKER.is_file(),'Build the shared C# worker first.')
    def test_balanced_fixed_evaluation_keeps_turn_limits_separate_from_results(self):
        class EndTurns:
            name,sha256='end-turn-fixture','fixed-fixture'
            def choose(self,decisions,random,difficulty,**context):
                return np.full((len(decisions),1),258,dtype=np.int32)
        with tempfile.TemporaryDirectory() as directory:
            result=evaluate(WORKER,Path(directory)/'evaluation',EndTurns(),EndTurns(),games_per_cell=4)
            self.assertEqual(result['games'],16)
            self.assertEqual(result['interruptions'],16)
            self.assertEqual(result['wins']+result['losses'],0)
            self.assertIsNone(result['candidateCaptureWinRate'])
            self.assertIsNone(result['firstPlayerCaptureWinRate'])
            for first in (0,1):
                for seat in (0,1):
                    records=[r for r in result['records'] if r['firstSeat']==first and r['candidateSeat']==seat]
                    self.assertEqual(len(records),4)
                    self.assertEqual({r['worker'] for r in records},{0,1,2,3})
                    self.assertTrue(all(r['winner'] is None and r['round']==101 for r in records))


if __name__ == '__main__':
    unittest.main()
