import json
import os
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import numpy as np
from mlagents_envs.base_env import DecisionSteps
from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import SimpleActor

import evaluate_rosters as probing
from evaluate_rosters import RosterProbeActor, MEASUREMENT
from dotnet_environment import SPEC
from mlagents.torch_utils import torch
from training_contract import ACTIONS, OBSERVATIONS, CORE_THREE_RULES, OPENING_ECONOMY_VERSION, POLICY_CATALOG

WORKER = Path(os.environ.get('BLOCKNATIONS_TEST_WORKER', 'missing-worker'))


class RecordingActor:
    name, sha256 = 'fixture-frozen-policy', 'f'*64

    def choose(self, steps, random, difficulty, **context):
        self.steps = steps
        return np.array([[np.flatnonzero(~row)[-1]] for row in steps.action_mask[0]], dtype=np.int32)


class RecruitmentMaskTests(unittest.TestCase):
    def setUp(self):
        self.actor = RecordingActor()
        self.environment = SimpleNamespace(roster=POLICY_CATALOG,
                                           enabled_roster=tuple(n for n in POLICY_CATALOG if n != 'scout'))
        mask = np.ones((2, ACTIONS), dtype=np.bool_)
        mask[0, [8, 129, 258]] = False
        mask[1, [242+POLICY_CATALOG.index(name) for name in self.environment.enabled_roster]+[258]] = False
        self.steps = DecisionSteps([np.arange(2*OBSERVATIONS, dtype=np.float32).reshape(2, OBSERVATIONS)],
                                  np.array([0.0, 0.25], np.float32), np.array([0, 2], np.int32), [mask],
                                  np.array([4, 4], np.int32), np.array([0.0, 0.5], np.float32))

    def choose(self, allowed=('archer',)):
        return RosterProbeActor(self.actor, list(allowed)).choose(self.steps, np.random.default_rng(42), 'Policy',
                                                               environment=self.environment, seat=0)

    def test_only_forbidden_recruits_change_and_observations_identity_stays_fair(self):
        original = self.steps.action_mask[0].copy()
        self.choose()
        projected = self.actor.steps
        self.assertIs(projected.obs[0], self.steps.obs[0])
        for field in ('reward', 'agent_id', 'group_id', 'group_reward'):
            self.assertIs(getattr(projected, field), getattr(self.steps, field))
        np.testing.assert_array_equal(self.steps.action_mask[0], original)
        expected = original.copy()
        for index, name in enumerate(POLICY_CATALOG):
            if name != 'archer': expected[:, 242+index] = True
        np.testing.assert_array_equal(projected.action_mask[0], expected)
        np.testing.assert_array_equal(projected.action_mask[0][:, :242], original[:, :242])
        self.assertFalse(projected.action_mask[0][:, 258].any())

    def test_all_enabled_units_preserve_original_legal_mask(self):
        self.choose(self.environment.enabled_roster)
        np.testing.assert_array_equal(self.actor.steps.action_mask[0], self.steps.action_mask[0])

    def test_disabled_or_unknown_recruit_fails_before_actor_inference(self):
        for names in [('scout',), ('unknown',)]:
            with self.assertRaisesRegex(ValueError, 'absent or disabled'): self.choose(names)
        self.assertFalse(hasattr(self.actor, 'steps'))

    def test_no_action_is_invented_if_restriction_removes_every_choice(self):
        self.steps.action_mask[0][1] = True
        self.steps.action_mask[0][1, 242+POLICY_CATALOG.index('rider')] = False
        with self.assertRaisesRegex(ValueError, 'every legal choice'): self.choose()
        self.assertFalse(hasattr(self.actor, 'steps'))

    def test_forbidden_choice_is_rejected_even_if_delegate_misbehaves(self):
        with patch.object(self.actor, 'choose', return_value=np.array([[258], [242+POLICY_CATALOG.index('rider')]])):
            with self.assertRaisesRegex(ValueError, 'forbidden recruitment'): self.choose()


class AblationReceiptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name).resolve()
        (self.root/probing.ROOT_MARKER).write_text(json.dumps(dict(owner='BlockNations.LocalTraining.v1')))
        self.manifest = self.root/'manifest.json'
        checkpoint, config = self.root/'BlockNationsSeatV2-100.pt', self.root/'trainer.yaml'
        network = dict(hidden_units=8, num_layers=1, normalize=False)
        torch.manual_seed(124)
        actor = SimpleActor(SPEC.observation_specs, NetworkSettings(**network), SPEC.action_spec)
        torch.save({'Policy': actor.state_dict()}, checkpoint)
        config.write_text(json.dumps({'behaviors': {'BlockNationsSeatV2': {'network_settings': network}}}))
        self.manifest.write_text(json.dumps(dict(checkpoint=str(checkpoint), trainerConfig=str(config),
            sha256=probing.digest(checkpoint), rulesVersion=CORE_THREE_RULES, boardSize=7,
            openingEconomyVersion=OPENING_ECONOMY_VERSION)))
        self.worker = WORKER if WORKER.is_file() else self.root/'fake-worker'
        if not self.worker.is_file(): self.worker.write_bytes(b'fixture-worker')
        self.definition = dict(version=1, measurement=MEASUREMENT, rulesVersion=CORE_THREE_RULES, boardSize=7,
            workerSha256=probing.worker_digest(self.worker), openingEconomyVersion=OPENING_ECONOMY_VERSION,
            cases=[dict(name='control', seed=42101, gamesPerCell=4, difficulty='Policy', referenceDifficulty='Policy',
                        reference=dict(kind='self'), allowedRecruits=None)])
        self.suite = self.root/'definition.json'; self.destination = self.root/'benchmarks/ablation'
        self.save()

    def tearDown(self): self.temp.cleanup()

    def save(self): self.suite.write_text(json.dumps(self.definition))

    def run_probe(self):
        return probing.run(self.manifest, self.suite, self.worker, self.destination, self.root)

    def test_condition_has_own_identity_and_unrestricted_control_uses_same_actor(self):
        captured = []
        def measured(worker, directory, candidate, reference, **options):
            captured.append((candidate, reference)); return dict(games=16, wins=8, losses=8, interruptions=0, records=[])
        with patch.object(probing, 'evaluate', side_effect=measured): original = self.run_probe()
        self.assertIs(captured[0][0], captured[0][1])
        self.assertEqual(original['state'], 'completed')
        self.destination = self.root/'benchmarks/archer'
        self.definition['cases'][0]['allowedRecruits'] = ['archer']; self.save()
        with patch.object(probing, 'evaluate', side_effect=measured): restricted = self.run_probe()
        self.assertNotEqual(original['suiteId'], restricted['suiteId'])
        self.assertIsInstance(captured[1][0], RosterProbeActor)
        self.assertIs(captured[1][0].actor, captured[1][1])
        self.assertEqual(restricted['results'][0]['allowedRecruits'], ['archer'])

    def test_missing_malformed_or_duplicate_condition_fails_before_prepare(self):
        for value in ['missing', [], ['archer', 'archer'], ['Archer'], 'archer']:
            case = self.definition['cases'][0]
            if value == 'missing': case.pop('allowedRecruits', None)
            else: case['allowedRecruits'] = value
            self.save()
            with patch.object(probing, 'prepare') as prepare:
                with self.assertRaises(ValueError): self.run_probe()
                prepare.assert_not_called()
            self.assertFalse(self.destination.exists())

    def test_failure_is_recorded_and_previous_receipt_cannot_be_overwritten(self):
        with patch.object(probing, 'evaluate', side_effect=RuntimeError('fixture failure')):
            with self.assertRaisesRegex(RuntimeError, 'fixture failure'): self.run_probe()
        before = (self.destination/'suite.json').read_bytes()
        self.assertEqual(json.loads(before)['state'], 'failed')
        with self.assertRaisesRegex(ValueError, 'new canonical'): self.run_probe()
        self.assertEqual(before, (self.destination/'suite.json').read_bytes())

    def test_changed_inputs_fail_and_keep_completed_case_on_disk(self):
        def measured(worker, directory, *args, **options):
            directory.mkdir(); (directory/'evaluation.json').write_text(json.dumps(dict(games=16)))
            self.suite.write_text(self.suite.read_text()+'\n')
            return dict(records=[], games=16, wins=8, losses=8, interruptions=0)
        with patch.object(probing, 'evaluate', side_effect=measured):
            with self.assertRaisesRegex(ValueError, 'changed during ablation'): self.run_probe()
        receipt = json.loads((self.destination/'suite.json').read_text())
        self.assertEqual(receipt['state'], 'failed'); self.assertEqual(receipt['results'], [])
        self.assertEqual(json.loads((self.destination/'control/evaluation.json').read_text())['games'], 16)

    def test_operator_interrupt_is_terminal_and_preserves_receipt(self):
        with patch.object(probing, 'evaluate', side_effect=KeyboardInterrupt):
            with self.assertRaises(KeyboardInterrupt): self.run_probe()
        receipt = json.loads((self.destination/'suite.json').read_text())
        self.assertEqual(receipt['state'], 'interrupted'); self.assertEqual(receipt['error'], 'KeyboardInterrupt')

    def test_shared_reserve_rejects_before_matches_or_output(self):
        with patch.object(probing, 'size', return_value=19_488_000_000), patch.object(probing, 'evaluate') as matches:
            with self.assertRaisesRegex(ValueError, 'reserves'): self.run_probe()
            matches.assert_not_called()
        self.assertFalse(self.destination.exists())

    @unittest.skipUnless(WORKER.is_file(), 'Requires the shared C# worker.')
    def test_real_worker_restricts_only_candidate_and_preserves_balanced_roles(self):
        self.definition['cases'].append(dict(self.definition['cases'][0], name='archer-only', allowedRecruits=['archer']))
        self.save(); result = self.run_probe()
        self.assertEqual(result['state'], 'completed')
        self.assertEqual(result['candidate']['sha256'], json.loads(self.manifest.read_text())['sha256'])
        for name in ('control', 'archer-only'):
            case = json.loads((self.destination/name/'evaluation.json').read_text())
            self.assertEqual(case['games'], 16)
            self.assertEqual(case['captures']+case['interruptions'], 16)
            for first in (0, 1):
                for seat in (0, 1):
                    self.assertEqual(sum(r['firstSeat']==first and r['candidateSeat']==seat for r in case['records']),4)
            if name == 'archer-only':
                self.assertGreater(case['candidateRecruits'].get('archer',0),0)
                self.assertLessEqual(set(case['candidateRecruits']), {'archer'})
            for cell in (self.destination/name).glob('first-*-candidate-*'):
                self.assertEqual(json.loads((cell/'arena-status.json').read_text())['rejections'],0)


if __name__ == '__main__': unittest.main()
