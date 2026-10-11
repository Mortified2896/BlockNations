import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
import score_positions as scoring


class BoundaryActor:
    def __init__(self):
        self.inputs = []

    def probabilities(self, vectors, masks):
        # This actor deliberately accepts numeric inputs only, not case labels,
        # world-state fields, persistent IDs or hidden resources.
        assert vectors.dtype == np.float32 and vectors.shape[1] == 3120
        assert masks.dtype == bool and masks.shape[1] == 259
        self.inputs.append((vectors.copy(), masks.copy()))
        probabilities = (~masks).astype(float)
        return probabilities / probabilities.sum(axis=1, keepdims=True)


class PositionScoreTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.probe = Path(os.environ.get('BLOCKNATIONS_TEST_PROBE', Path(__file__).parent / 'bin/Release/net8.0/BlockNations.PolicyProbes')).absolute()
        if not cls.probe.is_file():
            raise unittest.SkipTest('Build the actual C# policy-probe tool first.')

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        # macOS temporary paths may use /var's /private/var alias. The real
        # scorer deliberately requires a canonical marked storage root.
        self.root = Path(self.temp.name).resolve()
        (self.root / scoring.ROOT_MARKER).write_text(json.dumps({'owner': 'BlockNations.LocalTraining.v1'}))
        self.manifest = self.root / 'fixture-manifest.json'
        self.manifest.write_text('{}')
        self.case = json.loads((Path(__file__).parent / 'positions.json').read_text())[0]
        self.cases = self.root / 'cases.json'
        self.cases.write_text(json.dumps([self.case]))
        self.output = self.root / 'scores.json'
        self.actor = BoundaryActor()

    def tearDown(self):
        self.temp.cleanup()

    def run_score(self, modes=('Policy', 'Hard')):
        with patch.object(scoring, 'frozen_actor', return_value=(self.actor, {'actorId': 'boundary-test'})):
            return scoring.score(self.manifest, self.probe, self.cases, self.output, self.root, modes=modes)

    def test_real_projection_preserves_rules_provenance_and_only_passes_numeric_model_data(self):
        receipt = self.run_score()
        self.assertEqual(receipt['rulesVersion'], 'blocknations-simulation-v5-core3')
        self.assertEqual(receipt['observationSchema'], 2)
        self.assertEqual(receipt['boardSize'], 7)
        self.assertEqual(receipt['sources'][str(self.cases)], scoring.sha(self.cases))
        self.assertTrue(self.actor.inputs)
        for result in receipt['results']:
            self.assertAlmostEqual(sum(a['probability'] for a in result['actions']), 1)

    def test_hidden_world_changes_do_not_change_inference_inputs(self):
        self.case['state']['units'][0]['position'] = 8
        self.case['state']['units'].append({'id': 2, 'seat': 1, 'position': 24, 'type': 'rider', 'health': -1})
        self.cases.write_text(json.dumps([self.case])); self.run_score()
        vectors, masks = self.actor.inputs[-1]
        self.output.unlink()
        self.case['state']['gold'][1] = 77
        self.case['state']['units'][1].update(id=999, movesUsed=2, attacksUsed=1)
        self.cases.write_text(json.dumps([self.case])); self.run_score()
        np.testing.assert_array_equal(vectors, self.actor.inputs[-1][0])
        np.testing.assert_array_equal(masks, self.actor.inputs[-1][1])

    def test_mixed_rule_results_are_rejected_before_inference(self):
        other = dict(self.case, name='baseline-other', rulesVersion='blocknations-simulation-v4')
        self.cases.write_text(json.dumps([self.case, other]))
        with self.assertRaisesRegex(ValueError, 'different rulesets'):
            self.run_score()
        self.assertFalse(self.actor.inputs)
        self.assertFalse(self.output.exists())

    def test_completed_receipt_cannot_be_overwritten(self):
        self.output.write_text('protected')
        with self.assertRaises(ValueError):
            self.run_score()
        self.assertEqual(self.output.read_text(), 'protected')

    def test_external_destination_and_invalid_modes_create_no_artifact(self):
        with tempfile.TemporaryDirectory() as outside:
            self.output = Path(outside) / 'scores.json'
            with self.assertRaises(ValueError):
                self.run_score()
            self.assertFalse(self.output.exists())
        self.output = self.root / 'scores.json'
        for modes in ((), ('Hard', 'Hard'), ('Unknown',)):
            with self.assertRaises(ValueError):
                self.run_score(modes)
        self.assertFalse(self.output.exists())

    def test_shared_storage_reserve_prevents_starting_probe_or_inference(self):
        with patch.object(scoring, 'size', return_value=19_600_000_000):
            with self.assertRaisesRegex(ValueError, 'storage reserve'):
                self.run_score()
        self.assertFalse(self.actor.inputs)
        self.assertFalse(self.output.exists())


if __name__ == '__main__':
    unittest.main()
