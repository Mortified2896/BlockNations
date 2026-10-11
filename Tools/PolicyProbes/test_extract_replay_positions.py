import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).parent))
import extract_replay_positions as extraction
import score_positions as scoring
from test_score_positions import BoundaryActor


class ReplayExtractionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.probe = Path(os.environ.get('BLOCKNATIONS_TEST_PROBE', Path(__file__).parent / 'bin/Release/net8.0/BlockNations.PolicyProbes')).absolute()
        if not cls.probe.is_file():
            raise unittest.SkipTest('Build the actual C# policy-probe tool first.')
        cls.fixture = subprocess.run([str(cls.probe), '--replay-fixture'], capture_output=True, check=True).stdout

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name).resolve()
        (self.root / extraction.ROOT_MARKER).write_text(json.dumps({'owner': 'BlockNations.LocalTraining.v1'}))
        self.replay = self.root / 'benchmarks/test/workers/0/replays/recent-0.json'
        self.replay.parent.mkdir(parents=True)
        self.replay.write_bytes(self.fixture)
        self.cases = self.root / 'benchmarks/cases.json'
        self.provenance = self.root / 'benchmarks/provenance.json'

    def tearDown(self):
        self.temp.cleanup()

    def extract(self, maximum=32):
        return extraction.extract([self.replay], self.probe, self.cases, self.provenance, self.root, maximum)

    def test_verified_recorded_history_scores_with_actual_projection_and_numeric_actor_boundary(self):
        receipt = self.extract()
        self.assertEqual(receipt['sources'][0]['verifiedTransitions'],
                         len(json.loads(self.fixture)['frames']) - 1)
        actor = BoundaryActor()
        manifest = self.root / 'manifest.json'; manifest.write_text('{}')
        with patch.object(scoring, 'frozen_actor', return_value=(actor, {'actorId': 'boundary-test'})):
            scores = scoring.score(manifest, self.probe, self.cases, self.root / 'scores.json', self.root, provenance=self.provenance)
        self.assertEqual(scores['measurement'], extraction.MEASUREMENT)
        self.assertTrue(any(r['availableRangedAttacks'] > 0 for r in scores['results']))
        self.assertTrue(any(r['rangedAttackProbability'] > 0 for r in scores['results']))
        self.assertTrue(actor.inputs)
        self.assertEqual(scores['caseProvenance']['recordedGames'], 1)
        self.assertTrue(all(0 <= r['recordedActionProbability'] <= 1 for r in scores['results']))
        ended = [r for r in scores['results'] if r['recordedActionKinds'] == ['EndTurn']]
        self.assertTrue(ended)
        self.assertTrue(any(r['availableRangedAttacks'] for r in ended))
        for row in ended:
            self.assertAlmostEqual(row['recordedActionProbability'], row['endTurnProbability'])
        self.assertTrue(any(r['recordedActionKinds'] == ['Move'] and r['visibleWinProbability'] > 0 for r in scores['results']))

    def test_recorded_action_labels_do_not_change_numeric_actor_inputs(self):
        self.extract()
        labelled = json.loads(self.cases.read_text())
        plain = [{k:v for k,v in case.items() if k != 'recordedChoices'} for case in labelled]
        unlabelled = self.root/'benchmarks/plain.json'; unlabelled.write_text(json.dumps(plain))
        outputs = [subprocess.run([str(self.probe),'--cases',str(path)],capture_output=True,check=True).stdout
                   for path in (self.cases,unlabelled)]
        projections = [json.loads(output) for output in outputs]
        for a,b in zip(projections[0]['cases'],projections[1]['cases']):
            self.assertEqual([(r['model'],r['available']) for r in a['decisions']],
                             [(r['model'],r['available']) for r in b['decisions']])

    def test_malformed_recorded_choices_are_rejected_before_inference(self):
        self.extract()
        source = json.loads(self.cases.read_text())
        manifest = self.root/'manifest.json'; manifest.write_text('{}')
        for choices in ([dict(source=-1,encoded=0)], [dict(source=999,encoded=258)],
                        [dict(source=-1,encoded=258)]*2, []):
            changed = [dict(source[0], recordedChoices=choices)]
            cases = self.root/'benchmarks/malformed.json'; cases.write_text(json.dumps(changed))
            with patch.object(scoring, 'frozen_actor') as inference:
                with self.assertRaises(subprocess.CalledProcessError):
                    scoring.score(manifest,self.probe,cases,self.root/'invalid.json',self.root)
                inference.assert_not_called()
            self.assertFalse((self.root/'invalid.json').exists())

    def test_recorded_labels_require_replay_provenance(self):
        self.extract()
        manifest = self.root/'manifest.json'; manifest.write_text('{}')
        with patch.object(scoring, 'frozen_actor') as inference:
            with self.assertRaisesRegex(ValueError, 'require verified replay provenance'):
                scoring.score(manifest,self.probe,self.cases,self.root/'invalid.json',self.root)
            inference.assert_not_called()
        self.assertFalse((self.root/'invalid.json').exists())

    def test_tampered_trajectory_creates_no_artifacts(self):
        replay = json.loads(self.fixture); replay['frames'][1]['gold'][0] += 1
        self.replay.write_text(json.dumps(replay))
        with self.assertRaises(subprocess.CalledProcessError):
            self.extract()
        self.assertFalse(self.cases.exists()); self.assertFalse(self.provenance.exists())

    def test_limits_protected_files_and_storage_guard(self):
        for maximum in (0, 33, True):
            with self.assertRaises(ValueError): self.extract(maximum)
        with patch.object(extraction, 'size', return_value=19_600_000_000):
            with self.assertRaisesRegex(ValueError, 'storage reserve'): self.extract()
        self.cases.write_text('protected')
        with self.assertRaises(ValueError): self.extract()
        self.assertEqual(self.cases.read_text(), 'protected')

    def test_human_source_and_symlink_are_rejected(self):
        human = self.root / 'runs/private/human/replays/recent-0.json'
        human.parent.mkdir(parents=True); human.write_bytes(self.fixture)
        with self.assertRaisesRegex(ValueError, 'human-match'):
            extraction.extract([human], self.probe, self.cases, self.provenance, self.root)
        alias = self.replay.parent / 'recent-1.json'; alias.symlink_to(self.replay)
        with self.assertRaisesRegex(ValueError, 'canonical'):
            extraction.extract([alias], self.probe, self.cases, self.provenance, self.root)

    def test_provenance_cannot_hide_changed_case_or_replay_inputs(self):
        self.extract()
        manifest = self.root / 'manifest.json'; manifest.write_text('{}')
        output = self.root / 'scores.json'
        original = self.cases.read_text(); self.cases.write_text(original + ' ')
        with self.assertRaisesRegex(ValueError, 'provenance'):
            scoring.score(manifest, self.probe, self.cases, output, self.root, provenance=self.provenance)
        self.cases.write_text(original); self.replay.write_bytes(self.fixture + b' ')
        with self.assertRaisesRegex(ValueError, 'source changed'):
            scoring.score(manifest, self.probe, self.cases, output, self.root, provenance=self.provenance)
        self.assertFalse(output.exists())


if __name__ == '__main__':
    unittest.main()
