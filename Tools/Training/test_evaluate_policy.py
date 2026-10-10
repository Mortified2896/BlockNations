import os
from pathlib import Path
import tempfile
import unittest

import numpy as np

from evaluate_policy import evaluate, evaluation_weights, wilson

WORKER = Path(os.environ.get('BLOCKNATIONS_TEST_WORKER',Path(__file__).resolve().parents[2]/'Build/TrainingWorker/BlockNations.TrainingWorker.dll'))


class EvaluationTests(unittest.TestCase):
    def test_unmodified_policy_preserves_legal_distribution_including_lower_probability_choices(self):
        probabilities=np.zeros((2,259)); probabilities[:,[1,4,10]]=[.6,.3,.1]
        weights=evaluation_weights(probabilities,'Policy')
        np.testing.assert_allclose(weights,probabilities)
        self.assertTrue((weights[probabilities==0]==0).all())
        self.assertEqual(evaluation_weights(probabilities,'Hard')[0,1],1)
        with self.assertRaises(ValueError):evaluation_weights(np.zeros((1,259)),'Policy')

    def test_confidence_range_includes_uncertainty_and_empty_results_have_no_win_rate(self):
        self.assertIsNone(wilson(0,0))
        low,high=wilson(50,100)
        self.assertLess(low,.5);self.assertGreater(high,.5)
        self.assertGreater(wilson(0,64)[1],0)

    @unittest.skipUnless(WORKER.is_file(),'Build the shared C# worker first.')
    def test_balanced_fixed_evaluation_keeps_turn_limits_separate_from_results(self):
        class EndTurns:
            name,sha256='end-turn-fixture','fixed-fixture'
            def __init__(self):self.modes=set()
            def choose(self,decisions,random,difficulty,**context):
                self.modes.add(difficulty)
                return np.full((len(decisions),1),258,dtype=np.int32)
        with tempfile.TemporaryDirectory() as directory:
            candidate,reference=EndTurns(),EndTurns()
            result=evaluate(WORKER,Path(directory)/'evaluation',candidate,reference,games_per_cell=4,
                            difficulty='Policy',reference_difficulty='Hard')
            self.assertEqual(candidate.modes,{'Policy'})
            self.assertEqual(reference.modes,{'Hard'})
            self.assertEqual(result['referenceDifficulty'],'Hard')
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
