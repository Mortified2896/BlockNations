import itertools
import json
import os
from pathlib import Path
import tempfile
import unittest
import uuid

import numpy as np
from mlagents_envs.base_env import ActionTuple, DecisionSteps, TerminalSteps

from dotnet_environment import DotNetEnvironment, NAMES, SPEC
from roster_augmentation import (CAPABILITY_CHANNELS, CAPABILITY_OFFSET, RECRUIT_OFFSET,
    RosterAugmentedEnvironment, load_recipe, permute_inputs, restore_actions)
from training_contract import ACTIONS, OBSERVATIONS, RULES_VERSION

WORKER = Path(os.environ.get('BLOCKNATIONS_TEST_WORKER', 'missing-worker'))


class FakeEnvironment:
    roster = ('one', 'two', 'three', 'four')
    behavior_specs = {'seat': SPEC}

    def __init__(self):
        observations = np.arange(OBSERVATIONS, dtype=np.float32)[None, :]
        mask = np.zeros((1, ACTIONS), dtype=bool)
        mask[:, RECRUIT_OFFSET+4:ACTIONS-1] = True
        self.decisions = DecisionSteps([observations], np.array([.5]), np.array([2]), [mask],
                                       np.array([7]), np.array([.25]))
        self.terminals = TerminalSteps([observations.copy()], np.array([-1.]), np.array([True]),
                                      np.array([0]), np.array([3]), np.array([.75]))
        self.last = None
        self.closed = False

    def get_steps(self, name):
        return self.decisions, self.terminals

    def set_actions(self, name, action):
        self.last = (name, None, action.discrete.copy())

    def set_action_for_agent(self, name, agent, action):
        self.last = (name, agent, action.discrete.copy())

    def reference_actions(self):
        return self.decisions.obs[0].copy()

    def step(self):
        pass

    def reset(self):
        pass

    def close(self):
        self.closed = True


class RosterAugmentationTests(unittest.TestCase):
    def test_every_four_slot_permutation_roundtrips_capabilities_masks_and_legal_actions(self):
        observation = np.arange(OBSERVATIONS, dtype=np.float32)[None, :]
        mask = np.random.default_rng(42).random((1, ACTIONS)) < .3
        mask[:, RECRUIT_OFFSET+4:ACTIONS-1] = True
        for order in itertools.permutations(range(4)):
            permutation = np.array([order], dtype=np.int32)
            transformed, transformed_mask = permute_inputs(observation, mask, permutation)
            original, original_mask = permute_inputs(transformed, transformed_mask, np.argsort(permutation, axis=1))
            np.testing.assert_array_equal(original, observation)
            np.testing.assert_array_equal(original_mask, mask)
            np.testing.assert_array_equal(transformed[:, :CAPABILITY_OFFSET], observation[:, :CAPABILITY_OFFSET])
            np.testing.assert_array_equal(transformed[:, CAPABILITY_OFFSET+4*CAPABILITY_CHANNELS:],
                                          observation[:, CAPABILITY_OFFSET+4*CAPABILITY_CHANNELS:])
            for allowed in np.flatnonzero(~transformed_mask[0]):
                actual = restore_actions(np.array([[allowed]], dtype=np.int32), permutation)[0, 0]
                self.assertFalse(mask[0, actual])
                if allowed < RECRUIT_OFFSET or allowed == ACTIONS-1:
                    self.assertEqual(allowed, actual)

    def test_batch_agents_use_their_own_mapping_and_inputs_are_not_mutated(self):
        observation = np.arange(2*OBSERVATIONS, dtype=np.float32).reshape(2, OBSERVATIONS)
        mask = np.zeros((2, ACTIONS), dtype=bool)
        mask[0, RECRUIT_OFFSET] = True
        order = np.array([[3, 2, 1, 0], [1, 0, 3, 2]])
        before = observation.copy()
        transformed, masks = permute_inputs(observation, mask, order)
        self.assertTrue(masks[0, RECRUIT_OFFSET+3])
        self.assertFalse(masks[1, RECRUIT_OFFSET+3])
        np.testing.assert_array_equal(restore_actions(np.array([[242], [242]]), order), [[245], [243]])
        np.testing.assert_array_equal(observation, before)
        self.assertFalse(np.shares_memory(transformed, observation))

    def test_non_bijections_wrong_shapes_nonbinary_masks_and_padded_actions_fail(self):
        observations = np.zeros((1, OBSERVATIONS), dtype=np.float32)
        masks = np.zeros((1, ACTIONS), dtype=bool)
        for invalid in ([[0, 0]], [[0, 2]], [[0., 1.]], [[]], [[0], [0]]):
            with self.assertRaises(ValueError): permute_inputs(observations, masks, invalid)
        with self.assertRaises(ValueError): permute_inputs(observations, masks.astype(float), [[0]])
        with self.assertRaises(ValueError): permute_inputs(observations[:, :-1], masks, [[0]])
        with self.assertRaises(ValueError): restore_actions(np.array([[258], [258]]), [[0]])
        with self.assertRaises(ValueError): restore_actions(np.array([[243]]), [[0]])

    def test_cached_decisions_rewards_groups_and_terminal_interruptions_are_preserved(self):
        inner = FakeEnvironment()
        environment = RosterAugmentedEnvironment(inner, 42)
        decisions, terminals = environment.get_steps('seat')
        self.assertIs(decisions, environment.get_steps('seat')[0])
        for key in ('reward', 'agent_id', 'group_id', 'group_reward'):
            np.testing.assert_array_equal(getattr(decisions, key), getattr(inner.decisions, key))
            np.testing.assert_array_equal(getattr(terminals, key), getattr(inner.terminals, key))
        np.testing.assert_array_equal(terminals.interrupted, inner.terminals.interrupted)
        np.testing.assert_array_equal(environment.reference_actions(), inner.decisions.obs[0])
        # Capability values identify the original slot without consulting the adapter's cache.
        original_slot = int(round((decisions.obs[0][0, CAPABILITY_OFFSET]-CAPABILITY_OFFSET)/CAPABILITY_CHANNELS))
        environment.set_actions('seat', ActionTuple(discrete=np.array([[242]], dtype=np.int32)))
        self.assertEqual(inner.last[2][0, 0], RECRUIT_OFFSET+original_slot)
        environment.set_action_for_agent('seat', 2, ActionTuple(discrete=np.array([[258]], dtype=np.int32)))
        self.assertEqual(inner.last[1], 2)
        self.assertEqual(inner.last[2][0, 0], 258)
        environment.step()
        self.assertIsNot(decisions, environment.get_steps('seat')[0])
        environment.close()
        self.assertTrue(inner.closed)

    def test_empty_inactive_sdk_steps_do_not_require_a_mask(self):
        inner = FakeEnvironment()
        inner.decisions, inner.terminals = DecisionSteps.empty(SPEC), TerminalSteps.empty(SPEC)
        environment = RosterAugmentedEnvironment(inner, 42)
        decisions, terminals = environment.get_steps('seat')
        self.assertEqual(len(decisions), 0)
        self.assertEqual(len(terminals), 0)

    def test_same_seed_reproduces_presentations_and_reset_invalidates_old_choices(self):
        first = RosterAugmentedEnvironment(FakeEnvironment(), 123)
        second = RosterAugmentedEnvironment(FakeEnvironment(), 123)
        for _ in range(5):
            left, _ = first.get_steps('seat')
            right, _ = second.get_steps('seat')
            np.testing.assert_array_equal(left.obs[0], right.obs[0])
            first.reset(); second.reset()
            self.assertIsNot(left, first.get_steps('seat')[0])
            second.get_steps('seat')

    def test_recipe_requires_owned_compatible_feed_forward_experiment(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            manifest = dict(owner='BlockNations.LocalTraining.v1', schema=2, boardSize=7)
            recipe = dict(version=1, rulesVersion=RULES_VERSION, boardSize=7, mode='per-decision', seed=42)
            (run/'run.json').write_text(json.dumps(manifest))
            (run/'trainer.yaml').write_text(json.dumps({'behaviors': {'BlockNationsSeatV2': {'network_settings': {}}}}))
            with self.assertRaises(FileNotFoundError): load_recipe(run)
            (run/'roster-augmentation.json').write_text(json.dumps(recipe))
            self.assertEqual(load_recipe(run), recipe)
            for field, value in [('version', True), ('boardSize', 11), ('mode', 'forced-unit'), ('seed', -1)]:
                changed = dict(recipe, **{field: value})
                (run/'roster-augmentation.json').write_text(json.dumps(changed))
                with self.assertRaises(ValueError): load_recipe(run)
            (run/'roster-augmentation.json').write_text(json.dumps(recipe))
            (run/'trainer.yaml').write_text(json.dumps({'behaviors': {'BlockNationsSeatV2': {'network_settings': {'memory': {'memory_size': 64}}}}}))
            with self.assertRaises(ValueError): load_recipe(run)

    @unittest.skipUnless(WORKER.is_file(), 'Set BLOCKNATIONS_TEST_WORKER for the actual C# boundary test.')
    def test_actual_worker_recruits_the_presented_capability_from_every_original_slot(self):
        with tempfile.TemporaryDirectory() as directory:
            environment = RosterAugmentedEnvironment(DotNetEnvironment(WORKER, directory, 1, 42, 7, False,
                2, uuid.uuid4().hex, opening_economy_version=2, first_seat=0), 123)
            try:
                for original_slot in range(4):
                    environment.reset()
                    decisions, _ = environment.get_steps(NAMES[0])
                    source = next(action for action in np.flatnonzero(~decisions.action_mask[0][0]) if action != 258)
                    environment.set_actions(NAMES[0], ActionTuple(discrete=np.array([[source]], dtype=np.int32)))
                    environment.step()
                    decisions, _ = environment.get_steps(NAMES[0])
                    actual, _ = environment.inner.get_steps(NAMES[0])
                    original = actual.obs[0][0, CAPABILITY_OFFSET:CAPABILITY_OFFSET+4*CAPABILITY_CHANNELS].reshape(4, CAPABILITY_CHANNELS)
                    presented = decisions.obs[0][0, CAPABILITY_OFFSET:CAPABILITY_OFFSET+4*CAPABILITY_CHANNELS].reshape(4, CAPABILITY_CHANNELS)
                    slot = next(i for i, block in enumerate(presented) if np.array_equal(block, original[original_slot]))
                    environment.set_action_for_agent(NAMES[0], int(decisions.agent_id[0]),
                        ActionTuple(discrete=np.array([[RECRUIT_OFFSET+slot]], dtype=np.int32)))
                    environment.step()
                    self.assertIn('recruits '+environment.roster[original_slot], environment.stats[0]['lastAction'])
                    self.assertEqual(environment.stats[0]['gold0'], 2-int(round(original[original_slot, 11]*10)))
                    self.assertEqual(environment.stats[0]['rejections'], 0)
            finally:
                environment.close()


if __name__ == '__main__':
    unittest.main()
