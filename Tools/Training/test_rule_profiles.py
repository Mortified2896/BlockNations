"""Rules transfer must preserve provenance, slot meanings and fair legal choices."""
import json
import os
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
import uuid

import numpy as np
from mlagents.torch_utils import torch
from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import SimpleActor
from mlagents_envs.base_env import ActionTuple

from decoupled_runtime import configure
from dotnet_environment import DotNetEnvironment, SPEC, NAMES
from evaluate_milestone import freeze
from evaluate_suite import digest, frozen_actor, prepare, run as evaluate, worker_digest
from match_rating import policy_id
from rule_profile_study import arm_files, profile_suite
from supervisor import rules_options
from training_contract import (RULES_VERSION, CORE_THREE_RULES, ARCHER_VISION_RULES,
                               SUPPORTED_RULES, transfer_contract, validate_transfer)
from training_opponents import load_recipe, entry_id

WORKER = Path(os.environ.get('BLOCKNATIONS_TEST_PROFILE_WORKER', 'missing-profile-worker'))


class RuleTransferTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.parent = self.root/'runs'/'parent'; self.parent.mkdir(parents=True)
        self.folder = self.parent/'frozen-evaluations'/'reference'; self.folder.mkdir(parents=True)
        self.checkpoint = self.folder/'BlockNationsSeatV2-100.pt'
        self.config = self.folder/'trainer.yaml'
        actor = SimpleActor(SPEC.observation_specs, NetworkSettings(hidden_units=8, num_layers=1), SPEC.action_spec)
        torch.save({'Policy':actor.state_dict()}, self.checkpoint)
        self.config.write_text(json.dumps({'behaviors':{'BlockNationsSeatV2':{
            'network_settings':{'hidden_units':8, 'num_layers':1, 'normalize':False},
            'hyperparameters':{'beta':.1, 'learning_rate_schedule':'constant'}}}}))
        self.manifest = self.folder/'manifest.json'
        self.manifest.write_text(json.dumps(dict(checkpoint=str(self.checkpoint), trainerConfig=str(self.config),
            rulesVersion=RULES_VERSION, boardSize=7, openingEconomyVersion=2, step=100, sha256=digest(self.checkpoint))))
        (self.parent/'run.json').write_text(json.dumps(dict(owner='BlockNations.LocalTraining.v1', schema=2,
            boardSize=7, openingEconomyVersion=2, observationSize=3120, actionCount=259)))
        self.entry = dict(kind='frozen-policy', weight=1, sampling='Policy', samplingSettings=None,
            checkpoint=str(self.checkpoint.relative_to(self.parent)), trainerConfig=str(self.config.relative_to(self.parent)),
            checkpointSha256=digest(self.checkpoint), configSha256=digest(self.config), actorId=policy_id(actor.state_dict()))
        self.recipe = dict(version=2, rulesVersion=RULES_VERSION, boardSize=7, openingEconomyVersion=2,
            probability=.4, opponents=[dict(kind='fair-tactician', weight=1, workBudget=32), self.entry])
        (self.parent/'training-opponents.json').write_text(json.dumps(self.recipe))
        self.suite = self.root/'suite.json'
        self.suite.write_text(json.dumps(dict(version=1, rulesVersion=RULES_VERSION, boardSize=7, openingEconomyVersion=2,
            workerSha256='0'*64, cases=[dict(name='self', seed=42, gamesPerCell=4, difficulty='Policy',
                referenceDifficulty='Policy', reference=dict(kind='self'))])))
        self.worker = WORKER if WORKER.is_file() else self.root/'fake-worker'
        if not self.worker.exists(): self.worker.write_bytes(b'fixture')

    def tearDown(self):
        self.temporary.cleanup()

    def target(self):
        return arm_files(self.root, 'new-core3', self.manifest, self.parent, CORE_THREE_RULES, self.worker, 4096, 42)

    def test_shape_or_slot_reassignment_cannot_claim_compatible_transfer(self):
        record = transfer_contract(RULES_VERSION, CORE_THREE_RULES)
        validate_transfer(RULES_VERSION, CORE_THREE_RULES, record)
        for invalid in (None, dict(record, policyCatalog=['archer','rider','warrior']),
                        dict(record, observationSize=100), dict(record, sourceRulesVersion=ARCHER_VISION_RULES)):
            with self.assertRaises(ValueError): validate_transfer(RULES_VERSION, CORE_THREE_RULES, invalid)
        with self.assertRaises(ValueError): validate_transfer('old-unknown', CORE_THREE_RULES, record)

    def test_resuming_keeps_recorded_rules_and_legacy_runs_keep_baseline(self):
        self.assertEqual(rules_options(self.parent, None, True), RULES_VERSION)
        with self.assertRaises(ValueError): rules_options(self.parent, CORE_THREE_RULES, True)
        run, _, _ = self.target()
        self.assertEqual(rules_options(run, None, True), CORE_THREE_RULES)
        with self.assertRaises(ValueError): rules_options(run, RULES_VERSION, True)

    def test_transfer_preserves_parent_and_distinguishes_original_training_from_target_rules(self):
        before = self.checkpoint.read_bytes(); original_manifest = self.manifest.read_bytes()
        run, environment, provenance = self.target()
        self.assertEqual(self.checkpoint.read_bytes(), before)
        self.assertEqual(self.manifest.read_bytes(), original_manifest)
        self.assertEqual((run/'checkpoints'/run.name/'BlockNationsSeatV2/checkpoint.pt').read_bytes(), before)
        self.assertEqual(provenance['trainedRulesVersion'], RULES_VERSION)
        self.assertEqual(provenance['targetRulesVersion'], CORE_THREE_RULES)
        self.assertEqual(environment['BLOCKNATIONS_RULES_VERSION'], CORE_THREE_RULES)
        self.assertEqual(json.loads((run/'checkpoint-contract.json').read_text())['minimumCheckpointStep'], 100)
        with self.assertRaises(ValueError): load_recipe(run)
        league = load_recipe(run, CORE_THREE_RULES)
        copied = run/Path(self.entry['checkpoint']).parent/'manifest.json'
        self.assertEqual(json.loads(copied.read_text())['rulesVersion'], RULES_VERSION)
        self.assertNotEqual(entry_id(league['opponents'][1], CORE_THREE_RULES), entry_id(self.entry))
        plan = json.loads((run/'trainer.yaml').read_text())['behaviors']['BlockNationsSeatV2']
        self.assertEqual(plan['hyperparameters']['beta'], .1)
        self.assertEqual(plan['max_steps'], 4196)
        league['opponents'][1].pop('transferRules')
        (run/'training-opponents.json').write_text(json.dumps(league))
        with self.assertRaises(ValueError): load_recipe(run, CORE_THREE_RULES)

    def test_new_rules_checkpoint_requires_actual_training_beyond_parent_boundary(self):
        run, _, _ = self.target()
        saved = run/'checkpoints'/run.name/'BlockNationsSeatV2'
        source = saved/'BlockNationsSeatV2-100.pt'
        source.write_bytes(self.checkpoint.read_bytes()); source.with_suffix('.onnx').write_bytes(b'fixture')
        config = (run/'trainer.yaml').read_bytes()
        with self.assertRaisesRegex(ValueError, 'must train'):
            freeze(run, source, 100, config, 'recipe')
        source = saved/'BlockNationsSeatV2-200.pt'
        source.write_bytes(self.checkpoint.read_bytes()); source.with_suffix('.onnx').write_bytes(b'fixture')
        manifest = freeze(run, source, 200, config, 'recipe')
        data = json.loads(manifest.read_text())
        self.assertEqual(data['rulesVersion'], CORE_THREE_RULES)
        self.assertEqual(data['weightTransfer']['trainedRulesVersion'], RULES_VERSION)
        actor, _ = frozen_actor(manifest, CORE_THREE_RULES)
        self.assertTrue(actor.actor_id)

    def test_fixed_profile_suites_share_cases_but_record_different_rules_and_transfer(self):
        identities = []
        for rules in SUPPORTED_RULES:
            target = profile_suite(self.suite, rules, self.worker, self.root/(rules+'.json'))
            transfer = transfer_contract(RULES_VERSION, rules) if rules != RULES_VERSION else None
            if transfer:
                with self.assertRaises(ValueError): prepare(self.manifest, target)
            _, identity, _, definition = prepare(self.manifest, target, candidate_transfer=transfer)
            identities.append(definition['suiteId'])
            self.assertEqual(definition['rulesVersion'], rules)
            if transfer:
                self.assertEqual(identity['trainedRulesVersion'], RULES_VERSION)
                self.assertEqual(identity['transferRules'], transfer)
        self.assertEqual(len(set(identities)), 3)

    def test_frozen_reference_paths_and_transfers_are_prepared_for_every_profile(self):
        suite = json.loads(self.suite.read_text())
        suite['cases'].append(dict(name='old-policy', seed=10001, gamesPerCell=4, difficulty='Policy',
            referenceDifficulty='Policy', reference=dict(kind='frozen', manifest=str(self.manifest),
                checkpointSha256=digest(self.checkpoint), configSha256=digest(self.config))))
        self.suite.write_text(json.dumps(suite))
        for rules in SUPPORTED_RULES:
            target = profile_suite(self.suite, rules, self.worker, self.root/(rules+'-frozen.json'))
            transfer = transfer_contract(RULES_VERSION, rules) if rules != RULES_VERSION else None
            _, _, cases, definition = prepare(self.manifest, target, candidate_transfer=transfer)
            self.assertEqual(len(cases), 2)
            reference = definition['cases'][1]['reference']
            self.assertEqual(reference['sha256'], digest(self.checkpoint))
            self.assertEqual(reference.get('transferRules'), transfer)
            if transfer:
                self.assertEqual(reference['trainedRulesVersion'], RULES_VERSION)

    def test_native_execution_cannot_silently_change_an_owned_run_profile(self):
        run, _, _ = self.target()
        args = SimpleNamespace(backend='dotnet', worker=self.worker, parallel_games=1, seed=42,
            board_size=7, curriculum=False, curriculum_distance=2, rules_version=RULES_VERSION)
        with self.assertRaisesRegex(ValueError, 'Execution rules differ'): configure(args, run, {})

    def test_a_scout_only_reference_is_rejected_for_the_three_unit_profile(self):
        run, _, _ = self.target()
        recipe = json.loads((run/'training-opponents.json').read_text())
        recipe['opponents'][0]['recruitType'] = 'scout'
        (run/'training-opponents.json').write_text(json.dumps(recipe))
        with self.assertRaisesRegex(ValueError, 'Scout-only'):
            load_recipe(run, CORE_THREE_RULES)

    @unittest.skipUnless(WORKER.is_file(), 'Build the multi-profile C# worker.')
    def test_transferred_policy_evaluation_reports_actual_profile_and_balanced_starting_cells(self):
        suite = profile_suite(self.suite, CORE_THREE_RULES, WORKER, self.root/'core-suite.json')
        result = evaluate(self.manifest, suite, WORKER, self.root/'evaluation',
            candidate_transfer=transfer_contract(RULES_VERSION, CORE_THREE_RULES))
        self.assertEqual(result['state'], 'completed')
        row = result['results'][0]
        self.assertEqual(row['rulesVersion'], CORE_THREE_RULES)
        self.assertEqual(row['games'], 16)
        self.assertEqual(row['candidateRecruits'].get('scout', 0), 0)
        for cell in row['startingPositionResults']:
            self.assertEqual(cell['games'], 8)


@unittest.skipUnless(WORKER.is_file(), 'Build the multi-profile C# worker.')
class ProfileWorkerTests(unittest.TestCase):
    def test_public_roster_slots_and_authoritative_scout_masks_are_consistent(self):
        for rules in SUPPORTED_RULES:
            with tempfile.TemporaryDirectory() as directory:
                env = DotNetEnvironment(WORKER, directory, 4, 42, 7, False, 2, uuid.uuid4().hex,
                    first_seat=0, rules_version=rules)
                try:
                    env.reset()
                    self.assertEqual(env.roster, ('archer','rider','scout','warrior'))
                    choices = env.reference_actions(0, 'archer', 32)
                    for agent, choice in choices.items():
                        env.set_action_for_agent(NAMES[0], agent, ActionTuple(discrete=np.array([[choice]],dtype=np.int32)))
                    env.step()
                    decisions, _ = env.get_steps(NAMES[0])
                    self.assertEqual(len(decisions), 4)
                    mask = decisions.action_mask[0]
                    self.assertTrue((~mask[:,242]).all())  # Archer retained at slot 0.
                    self.assertTrue((~mask[:,245]).all())  # Warrior retained at slot 3.
                    self.assertTrue((mask[:,244] == (rules == CORE_THREE_RULES)).all())
                    self.assertEqual(sum(s['rejections'] for s in env.stats), 0)
                    self.assertTrue(all(s['simulationVersion'] == rules for s in env.stats))
                finally:
                    env.close()


if __name__ == '__main__': unittest.main()
