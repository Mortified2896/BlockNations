import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from mlagents.torch_utils import torch
from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import SimpleActor

from dotnet_environment import SPEC
from evaluate_suite import digest, worker_digest, prepare, run
from training_contract import RULES_VERSION, OPENING_ECONOMY_VERSION

WORKER = Path(os.environ.get('BLOCKNATIONS_TEST_WORKER', 'missing-worker'))


class BenchmarkSuiteTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.manifest = self.root/'manifest.json'
        checkpoint, config = self.root/'BlockNationsSeatV2-100.pt', self.root/'trainer.yaml'
        network = dict(hidden_units=8, num_layers=1, normalize=False)
        actor = SimpleActor(SPEC.observation_specs, NetworkSettings(**network), SPEC.action_spec)
        torch.save({'Policy': actor.state_dict()}, checkpoint)
        config.write_text(json.dumps({'behaviors': {'BlockNationsSeatV2': {'network_settings': network}}}))
        self.manifest.write_text(json.dumps(dict(checkpoint=str(checkpoint), trainerConfig=str(config),
            sha256=digest(checkpoint), rulesVersion=RULES_VERSION, boardSize=7,
            openingEconomyVersion=OPENING_ECONOMY_VERSION)))
        self.plan = dict(version=1, rulesVersion=RULES_VERSION, boardSize=7,
            workerSha256=worker_digest(WORKER) if WORKER.is_file() else '0'*64,
            openingEconomyVersion=OPENING_ECONOMY_VERSION, cases=[dict(name='self-policy',
                seed=42, gamesPerCell=4, difficulty='Policy', referenceDifficulty='Policy', reference=dict(kind='self'))])
        self.suite = self.root/'suite-input.json'
        self.write()

    def tearDown(self):
        self.temp.cleanup()

    def write(self):
        self.suite.write_text(json.dumps(self.plan))

    def test_reference_content_and_contract_cannot_silently_change(self):
        m = json.loads(self.manifest.read_text())
        self.plan['cases'][0]['reference'] = dict(kind='frozen', manifest=str(self.manifest),
            checkpointSha256=m['sha256'], configSha256=digest(m['trainerConfig']))
        self.write()
        _, _, _, initial = prepare(self.manifest, self.suite)
        self.assertEqual(initial['suiteId'], prepare(self.manifest, self.suite)[3]['suiteId'])
        Path(m['trainerConfig']).write_text(Path(m['trainerConfig']).read_text()+'\n')
        with self.assertRaisesRegex(ValueError, 'Fixed reference differs'):
            prepare(self.manifest, self.suite)
        m['openingEconomyVersion'] = 1
        self.manifest.write_text(json.dumps(m))
        with self.assertRaisesRegex(ValueError, 'compatible frozen'):
            prepare(self.manifest, self.suite)

    def test_invalid_quota_or_duplicate_case_fails_before_writing(self):
        original = self.plan['cases'][0]
        for cases in ([dict(original, gamesPerCell=5)], [dict(original), dict(original)]):
            self.plan['cases'] = cases
            self.write()
            with self.assertRaises(ValueError):
                run(self.manifest, self.suite, WORKER, self.root/'output')
            self.assertFalse((self.root/'output').exists())

    def test_partial_failure_is_explicit_and_cannot_overwrite_previous_receipts(self):
        destination = self.root/'output'
        worker = self.root/'fake-worker'; worker.write_bytes(b'fixture')
        self.plan['workerSha256'] = worker_digest(worker); self.write()
        with patch('evaluate_suite.evaluate', side_effect=RuntimeError('worker failed')):
            with self.assertRaisesRegex(RuntimeError, 'worker failed'):
                run(self.manifest, self.suite, worker, destination)
        receipt = json.loads((destination/'suite.json').read_text())
        self.assertEqual(receipt['state'], 'failed')
        self.assertEqual(receipt['results'], [])
        with self.assertRaises(FileExistsError):
            run(self.manifest, self.suite, worker, destination)
        self.assertEqual(receipt, json.loads((destination/'suite.json').read_text()))

    def test_changed_worker_is_rejected_before_any_match_or_output(self):
        worker = self.root/'changed-worker'; worker.write_bytes(b'different implementation')
        with patch('evaluate_suite.evaluate') as evaluate:
            with self.assertRaisesRegex(ValueError, 'Worker differs'):
                run(self.manifest, self.suite, worker, self.root/'output')
            evaluate.assert_not_called()
        self.assertFalse((self.root/'output').exists())

    def test_native_apphost_digest_includes_changed_managed_rules(self):
        worker = self.root/'fixture-worker'; worker.write_bytes(b'unchanged apphost')
        rules = self.root/'fixture-worker.dll'; rules.write_bytes(b'old rules')
        before = worker_digest(worker)
        rules.write_bytes(b'new rules')
        self.assertNotEqual(before, worker_digest(worker))

    @unittest.skipUnless(WORKER.is_file(), 'Requires the shared C# worker.')
    def test_real_worker_receipt_matches_balanced_cases_and_complete_match_records(self):
        destination = self.root/'output'
        result = run(self.manifest, self.suite, WORKER, destination)
        self.assertEqual(result['state'], 'completed')
        self.assertEqual(result['workerSha256'], worker_digest(WORKER))
        case = json.loads((destination/'self-policy/evaluation.json').read_text())
        self.assertEqual(case['games'], 16)
        self.assertEqual(len(case['records']), 16)
        for first in (0, 1):
            for seat in (0, 1):
                self.assertEqual(sum(r['firstSeat']==first and r['candidateSeat']==seat for r in case['records']), 4)
        self.assertEqual(case['wins']+case['losses']+case['interruptions'],16)
        self.assertEqual(result['results'][0]['wins'], case['wins'])


if __name__ == '__main__':
    unittest.main()
