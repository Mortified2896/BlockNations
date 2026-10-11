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
from rule_profile_study import arm_files, profile_suite, study
from roster_augmentation import load_recipe as load_augmentation
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
            'hyperparameters':{'beta':.1, 'learning_rate':.0003, 'learning_rate_schedule':'constant'}}}}))
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

    def test_explicit_recipe_options_preserve_parent_and_reproduce_parallel_gentle_continuation(self):
        before, original_config = self.checkpoint.read_bytes(), self.config.read_bytes()
        run, environment, provenance = arm_files(self.root, 'gentle-core3', self.manifest, self.parent,
            CORE_THREE_RULES, self.worker, 4096, 43, parallel_games=4, learning_rate=.000075)
        plan = json.loads((run/'trainer.yaml').read_text())['behaviors']['BlockNationsSeatV2']
        self.assertEqual(plan['hyperparameters']['learning_rate'], .000075)
        self.assertEqual(plan['hyperparameters']['beta'], .1)
        self.assertEqual(plan['hyperparameters']['learning_rate_schedule'], 'constant')
        self.assertEqual(plan['network_settings'], json.loads(original_config)['behaviors']['BlockNationsSeatV2']['network_settings'])
        self.assertEqual(self.checkpoint.read_bytes(), before)
        self.assertEqual(self.config.read_bytes(), original_config)
        self.assertEqual((run/'checkpoints'/run.name/'BlockNationsSeatV2/checkpoint.pt').read_bytes(), before)
        self.assertEqual(environment['BLOCKNATIONS_PARALLEL_GAMES'], '4')
        execution = json.loads((run/'execution-settings.json').read_text())
        self.assertEqual(execution['workerCount'], 4)
        self.assertEqual(execution['seed'], 43)
        self.assertEqual(provenance['trainingOptions']['parentLearningRate'], .0003)
        saved = run/'checkpoints'/run.name/'BlockNationsSeatV2'
        source = saved/'BlockNationsSeatV2-200.pt'
        source.write_bytes(before); source.with_suffix('.onnx').write_bytes(b'fixture')
        frozen = json.loads(freeze(run, source, 200, (run/'trainer.yaml').read_bytes(), 'recipe').read_text())
        self.assertEqual(frozen['weightTransfer']['trainingOptions'], provenance['trainingOptions'])

    def test_invalid_compute_or_learning_rate_options_fail_without_creating_runs(self):
        for workers in (0, 3, 5, True, 1.0):
            with self.assertRaises(ValueError):
                arm_files(self.root, 'invalid-options', self.manifest, self.parent, CORE_THREE_RULES,
                          self.worker, 4096, 42, parallel_games=workers)
        for rate in (0, -.1, .1, float('nan'), float('inf'), True, '0.000075'):
            with self.assertRaises(ValueError):
                arm_files(self.root, 'invalid-options', self.manifest, self.parent, CORE_THREE_RULES,
                          self.worker, 4096, 42, learning_rate=rate)
        destination = self.root/'invalid-study'
        with self.assertRaises(ValueError):
            study(self.root, self.manifest, self.parent, self.suite, self.worker, destination,
                  'trial', 4096, 42, 1, 20, learning_rate=float('nan'))
        self.assertFalse((self.root/'runs/invalid-options').exists())
        self.assertFalse(destination.exists())

    def test_exploration_continuation_preserves_weights_optimizer_copy_and_records_loss_provenance(self):
        original = self.checkpoint.read_bytes()
        pressure = dict(version=1, rulesVersion=CORE_THREE_RULES, boardSize=7,
                        coefficient=.005, entropyFloor=.25, batchSize=128)
        run, _, provenance = arm_files(self.root, 'pressure-core3', self.manifest, self.parent,
            CORE_THREE_RULES, self.worker, 4096, 45, parallel_games=4, learning_rate=.000075, exploration_pressure=pressure)
        self.assertEqual(self.checkpoint.read_bytes(), original)
        saved = run/'checkpoints'/run.name/'BlockNationsSeatV2'
        self.assertEqual((saved/'checkpoint.pt').read_bytes(), original)
        self.assertEqual(json.loads((run/'exploration-pressure.json').read_text()), pressure)
        self.assertEqual(provenance['explorationPressure'], pressure)
        metadata = json.loads((run/'run.json').read_text())
        self.assertEqual(metadata['recipeVersion'], 'rule-profile-study-v3-exploration')
        self.assertEqual(metadata['experiment'], 'matched-exploration-pressure')
        plan = json.loads((run/'trainer.yaml').read_text())['behaviors']['BlockNationsSeatV2']
        self.assertEqual(plan['hyperparameters']['beta'], .1)
        self.assertEqual(plan['network_settings'], json.loads(self.config.read_text())['behaviors']['BlockNationsSeatV2']['network_settings'])
        source = saved/'BlockNationsSeatV2-200.pt'; source.write_bytes(original); source.with_suffix('.onnx').write_bytes(b'fixture')
        manifest = json.loads(freeze(run, source, 200, (run/'trainer.yaml').read_bytes(), 'recipe').read_text())
        self.assertEqual(manifest['weightTransfer']['explorationPressure'], pressure)
        pressure['coefficient'] = .01
        self.assertEqual(provenance['explorationPressure']['coefficient'], .005)

    def test_invalid_or_combined_exploration_choices_fail_before_creating_artifacts(self):
        valid = dict(version=1, rulesVersion=CORE_THREE_RULES, boardSize=7,
                     coefficient=.005, entropyFloor=.25, batchSize=128)
        for field, value in [('coefficient', 0), ('coefficient', float('nan')), ('coefficient', .03),
                             ('entropyFloor', .3), ('batchSize', True), ('rulesVersion', RULES_VERSION), ('boardSize', 11)]:
            with self.assertRaises(ValueError):
                arm_files(self.root, 'invalid-exploration', self.manifest, self.parent, CORE_THREE_RULES,
                          self.worker, 4096, 45, exploration_pressure=dict(valid, **{field:value}))
        with self.assertRaises(ValueError):
            arm_files(self.root, 'invalid-exploration', self.manifest, self.parent, CORE_THREE_RULES,
                      self.worker, 4096, 45, exploration_pressure=valid, roster_augmentation=True)
        destination = self.root/'invalid-exploration-study'
        with self.assertRaises(ValueError):
            study(self.root, self.manifest, self.parent, self.suite, self.worker, destination,
                  'invalid-exploration', 4096, 45, 1, 20, profiles=[CORE_THREE_RULES],
                  exploration_pressure=dict(valid, coefficient=-.01))
        with self.assertRaises(ValueError):
            study(self.root, self.manifest, self.parent, self.suite, self.worker, destination,
                  'invalid-exploration', 4096, 45, 1, 20, exploration_pressure=valid)
        self.assertFalse((self.root/'runs/invalid-exploration').exists())
        self.assertFalse(destination.exists())

    def test_augmented_profile_preserves_parent_and_records_transform_in_frozen_provenance(self):
        before = self.checkpoint.read_bytes()
        run, environment, provenance = arm_files(self.root, 'augmented-core3', self.manifest, self.parent,
            CORE_THREE_RULES, self.worker, 4096, 42, roster_augmentation=True)
        transform = load_augmentation(run)
        self.assertEqual(transform['rulesVersion'], CORE_THREE_RULES)
        self.assertEqual(transform['mode'], 'per-decision')
        self.assertEqual(provenance['publicRosterTransform'], transform)
        self.assertEqual(environment['BLOCKNATIONS_RULES_VERSION'], CORE_THREE_RULES)
        self.assertEqual(self.checkpoint.read_bytes(), before)
        self.assertEqual((run/'checkpoints'/run.name/'BlockNationsSeatV2/checkpoint.pt').read_bytes(), before)
        saved = run/'checkpoints'/run.name/'BlockNationsSeatV2'
        source = saved/'BlockNationsSeatV2-200.pt'
        source.write_bytes(before); source.with_suffix('.onnx').write_bytes(b'fixture')
        manifest = freeze(run, source, 200, (run/'trainer.yaml').read_bytes(), 'recipe')
        frozen = json.loads(manifest.read_text())
        self.assertEqual(frozen['weightTransfer']['publicRosterTransform'], transform)

    def test_invalid_experiment_choices_fail_before_creating_any_run(self):
        destination = self.root/'uncreated-study'
        for profiles in ([], [CORE_THREE_RULES, CORE_THREE_RULES], ['unknown'], CORE_THREE_RULES, [{}]):
            with self.assertRaises(ValueError):
                study(self.root, self.manifest, self.parent, self.suite, self.worker,
                      destination, 'trial', 4096, 42, 1, 20, profiles=profiles)
        with self.assertRaises(ValueError):
            arm_files(self.root, 'invalid-flag', self.manifest, self.parent, CORE_THREE_RULES,
                      self.worker, 4096, 42, roster_augmentation=1)
        self.assertFalse(destination.exists())
        self.assertFalse((self.root/'runs/invalid-flag').exists())

    def test_new_profile_parent_and_recipe_can_transfer_back_without_stale_reference_permission(self):
        folder = self.parent/'frozen-evaluations/core-parent'; folder.mkdir()
        checkpoint = folder/self.checkpoint.name; checkpoint.write_bytes(self.checkpoint.read_bytes())
        config = folder/'trainer.yaml'; config.write_bytes(self.config.read_bytes())
        source = json.loads(self.manifest.read_text())
        source.update(checkpoint=str(checkpoint), trainerConfig=str(config), rulesVersion=CORE_THREE_RULES)
        manifest = folder/'manifest.json'; manifest.write_text(json.dumps(source))
        metadata = json.loads((self.parent/'run.json').read_text()); metadata['rulesVersion'] = CORE_THREE_RULES
        (self.parent/'run.json').write_text(json.dumps(metadata))
        recipe = json.loads((self.parent/'training-opponents.json').read_text()); recipe['rulesVersion'] = CORE_THREE_RULES
        recipe['opponents'][1]['transferRules'] = transfer_contract(RULES_VERSION, CORE_THREE_RULES)
        (self.parent/'training-opponents.json').write_text(json.dumps(recipe))
        before = manifest.read_bytes()
        run, _, provenance = arm_files(self.root, 'from-core3', manifest, self.parent,
            RULES_VERSION, self.worker, 4096, 42)
        self.assertEqual(provenance['trainedRulesVersion'], CORE_THREE_RULES)
        self.assertEqual(provenance['targetRulesVersion'], RULES_VERSION)
        self.assertEqual(manifest.read_bytes(), before)
        self.assertNotIn('transferRules', load_recipe(run)['opponents'][1])

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
