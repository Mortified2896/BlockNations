"""Optional public-roster augmentation for feed-forward schema-v2 training.

Permute recruitment capability blocks and their legal-action slots together,
then restore chosen indices before the authoritative environment sees them.
Board features, rewards, turn ownership and fixed opponents remain unchanged.
This discourages memorizing a roster index rather than learning capabilities.
"""
import json
from pathlib import Path

import numpy as np
from mlagents_envs.base_env import ActionTuple, BaseEnv, DecisionSteps, TerminalSteps

from training_contract import ACTIONS, OBSERVATIONS, RULES_VERSION

RECRUIT_OFFSET = 242
CAPABILITY_OFFSET = 121 * 24
CAPABILITY_CHANNELS = 13
CAPACITY = 16
if CAPABILITY_OFFSET + CAPACITY * CAPABILITY_CHANNELS != OBSERVATIONS - 8 or RECRUIT_OFFSET + CAPACITY + 1 != ACTIONS:
    raise RuntimeError('Update the explicit augmentation adapter when the learned tensor contract changes.')


def validate_permutations(permutations, rows):
    permutations = np.asarray(permutations)
    if (permutations.ndim != 2 or permutations.shape[0] != rows or
            not 1 <= permutations.shape[1] <= CAPACITY or
            not np.issubdtype(permutations.dtype, np.integer) or
            not np.all(np.sort(permutations, axis=1) == np.arange(permutations.shape[1]))):
        raise ValueError('Choose a bijection over the actual public recruitment slots for each agent.')
    return permutations


def permute_inputs(observations, masks, permutations):
    observations, masks = np.asarray(observations), np.asarray(masks)
    if (observations.ndim != 2 or observations.shape[1] != OBSERVATIONS or
            masks.shape != (len(observations), ACTIONS) or masks.dtype != np.bool_ or
            not np.isfinite(observations).all()):
        raise ValueError('Roster augmentation requires the complete fair schema-v2 observations and binary masks.')
    permutations = validate_permutations(permutations, len(observations))
    count = permutations.shape[1]
    transformed, transformed_masks = observations.copy(), masks.copy()
    blocks = observations[:, CAPABILITY_OFFSET:CAPABILITY_OFFSET + CAPACITY * CAPABILITY_CHANNELS]
    blocks = blocks.reshape(len(observations), CAPACITY, CAPABILITY_CHANNELS)
    rows = np.arange(len(observations))[:, None]
    transformed[:, CAPABILITY_OFFSET:CAPABILITY_OFFSET + count * CAPABILITY_CHANNELS] = \
        blocks[rows, permutations].reshape(len(observations), count * CAPABILITY_CHANNELS)
    transformed_masks[:, RECRUIT_OFFSET:RECRUIT_OFFSET + count] = masks[rows, RECRUIT_OFFSET + permutations]
    return transformed, transformed_masks


def restore_actions(actions, permutations):
    actions = np.asarray(actions)
    if (actions.ndim != 2 or actions.shape[1] != 1 or
            not np.issubdtype(actions.dtype, np.integer) or
            (actions < 0).any() or (actions >= ACTIONS).any()):
        raise ValueError('Restore one valid discrete action per announced decision agent.')
    permutations = validate_permutations(permutations, len(actions))
    restored = actions.copy()
    for row, value in enumerate(actions[:, 0]):
        if RECRUIT_OFFSET <= value < RECRUIT_OFFSET + CAPACITY:
            slot = int(value) - RECRUIT_OFFSET
            if slot >= permutations.shape[1]:
                raise ValueError('A padded recruitment slot is unavailable.')
            restored[row, 0] = RECRUIT_OFFSET + permutations[row, slot]
    return restored


def load_recipe(run):
    run = Path(run)
    recipe = json.loads((run/'roster-augmentation.json').read_text())
    manifest = json.loads((run/'run.json').read_text())
    if (manifest.get('owner') != 'BlockNations.LocalTraining.v1' or manifest.get('schema') != 2 or
            type(manifest.get('boardSize')) is not int or manifest['boardSize'] not in (5, 6, 7, 9, 11) or
            recipe.get('version') != 1 or type(recipe.get('version')) is not int or
            recipe.get('rulesVersion') != RULES_VERSION or
            recipe.get('boardSize') != manifest.get('boardSize') or
            recipe.get('mode') != 'per-decision' or type(recipe.get('seed')) is not int or
            not 0 <= recipe['seed'] < 2**32):
        raise ValueError('Use an explicit compatible owned-run roster augmentation recipe.')
    config = json.loads((run/'trainer.yaml').read_text())
    network = config['behaviors']['BlockNationsSeatV2']['network_settings']
    if network.get('memory'):
        raise ValueError('Per-decision slot augmentation requires a feed-forward policy.')
    return recipe


class RosterAugmentedEnvironment(BaseEnv):
    """Compose with BaseEnv so internal fixed opponents retain original tensors.

    A presented decision and its mapping remain stable until the next real step.
    Terminal tensors are also transformed without changing interruption/reward
    semantics. Only public capability ordering and matching action slots change.
    """
    def __init__(self, environment, seed):
        self.inner = environment
        self.random = np.random.default_rng(seed)
        self.cache = {}

    def __getattr__(self, name):
        return getattr(object.__getattribute__(self, 'inner'), name)

    @property
    def behavior_specs(self):
        return self.inner.behavior_specs

    def get_steps(self, behavior_name):
        if behavior_name not in self.cache:
            decisions, terminals = self.inner.get_steps(behavior_name)
            count = len(self.inner.roster)
            if not 1 <= count <= CAPACITY:
                raise ValueError('The public roster exceeds the versioned tensor capacity.')
            def permutations(rows):
                return np.stack([self.random.permutation(count) for _ in range(rows)]) \
                    if rows else np.empty((0, count), dtype=np.int32)
            mapping = permutations(len(decisions))
            if decisions.action_mask is None:
                if len(decisions):
                    raise ValueError('Decision agents require authoritative legal-action masks.')
                original_mask = np.ones((0, ACTIONS), dtype=bool)
            elif len(decisions.action_mask) == 1:
                original_mask = decisions.action_mask[0]
            else:
                raise ValueError('Roster augmentation requires one discrete action branch.')
            observations, masks = permute_inputs(decisions.obs[0], original_mask, mapping)
            presented = DecisionSteps([observations], decisions.reward.copy(), decisions.agent_id.copy(), [masks],
                                      decisions.group_id.copy(), decisions.group_reward.copy())
            terminal_mapping = permutations(len(terminals))
            terminal_obs, _ = permute_inputs(terminals.obs[0],
                np.ones((len(terminals), ACTIONS), dtype=bool), terminal_mapping)
            ended = TerminalSteps([terminal_obs], terminals.reward.copy(), terminals.interrupted.copy(),
                                  terminals.agent_id.copy(), terminals.group_id.copy(), terminals.group_reward.copy())
            self.cache[behavior_name] = (presented, ended, mapping)
        return self.cache[behavior_name][:2]

    def set_actions(self, behavior_name, action):
        decisions, _ = self.get_steps(behavior_name)
        if not len(decisions):
            return self.inner.set_actions(behavior_name, action)
        restored = restore_actions(action.discrete, self.cache[behavior_name][2])
        return self.inner.set_actions(behavior_name, ActionTuple(discrete=restored))

    def set_action_for_agent(self, behavior_name, agent_id, action):
        decisions, _ = self.get_steps(behavior_name)
        index = decisions.agent_id_to_index[agent_id]
        mapping = self.cache[behavior_name][2][index:index+1]
        restored = restore_actions(action.discrete, mapping)
        return self.inner.set_action_for_agent(behavior_name, agent_id, ActionTuple(discrete=restored))

    def step(self):
        self.inner.step()
        self.cache.clear()

    def reset(self):
        self.inner.reset()
        self.cache.clear()

    def close(self):
        self.inner.close()
        self.cache.clear()
