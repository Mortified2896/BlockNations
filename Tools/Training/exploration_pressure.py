"""Optional bounded exploration repair for a saturated learned distribution.

This is an auxiliary actor loss on fair learner observations, not action
replacement, a tactical prior, a reward change or human imitation. Disabled
unless the owned run contains an explicit compatible development recipe.
"""
import json
from pathlib import Path

import numpy as np
from mlagents.torch_utils import torch
from mlagents.trainers.buffer import BufferKey
from mlagents.trainers.torch_entities.utils import ModelUtils
from mlagents.trainers.trajectory import ObsUtil

from training_contract import RULES_VERSION, OBSERVATIONS, ACTIONS, validate_rules_version


def masked_uniform_kl(logits, allowed, entropy_floor):
    if (type(entropy_floor) not in (float, int) or not 0 < entropy_floor <= .25 or
            logits.ndim != 2 or logits.shape != allowed.shape or
            not torch.isfinite(logits).all() or not torch.isfinite(allowed).all() or
            not ((allowed == 0) | (allowed == 1)).all()):
        raise ValueError('Exploration pressure requires finite logits and authoritative binary masks.')
    count = allowed.sum(dim=1)
    if (count < 1).any():
        raise ValueError('Each observed state must have a legal choice.')
    log_probability = torch.log_softmax(logits.masked_fill(allowed == 0, -torch.inf), dim=1)
    legal_log = torch.where(allowed != 0, log_probability, torch.zeros_like(log_probability))
    probabilities = torch.exp(log_probability)
    entropy = -(probabilities * legal_log).sum(dim=1)
    normalized = entropy / count.clamp(min=2).log()
    eligible = (count > 1) & (normalized.detach() < entropy_floor)
    kl = -legal_log.sum(dim=1)/count - count.log()
    # The detached gate stops pressure once meaningful exploration is present.
    # Return a connected zero for forced/already-diverse batches.
    loss = torch.where(eligible, kl, torch.zeros_like(kl)).sum()/eligible.sum().clamp(min=1)
    return loss, eligible, normalized


class ExplorationPressure:
    def __init__(self, run, rules_version=RULES_VERSION):
        rules_version = validate_rules_version(rules_version)
        self.recipe = None
        path = Path(run)/'exploration-pressure.json'
        if not path.exists():
            return
        recipe = json.loads(path.read_text())
        manifest = json.loads((Path(run)/'run.json').read_text())
        if (type(recipe.get('version')) is not int or recipe['version'] != 1 or recipe.get('rulesVersion') != rules_version or
                manifest.get('owner') != 'BlockNations.LocalTraining.v1' or manifest.get('schema') != 2 or
                recipe.get('boardSize') != manifest.get('boardSize') or
                type(recipe.get('coefficient')) not in (float, int) or not 0 < recipe['coefficient'] <= .02 or
                type(recipe.get('entropyFloor')) not in (float, int) or not 0 < recipe['entropyFloor'] <= .25 or
                type(recipe.get('batchSize')) is not int or not 16 <= recipe['batchSize'] <= 128):
            raise ValueError('Invalid bounded development exploration recipe.')
        self.recipe = recipe

    def prepare(self, policy, buffer, step):
        if self.recipe is None:
            return None
        if policy.use_recurrent or policy.sequence_length != 1 or len(policy.behavior_spec.observation_specs) != 1:
            raise ValueError('Exploration repair currently requires the feed-forward schema-v2 policy.')
        observations = np.asarray(ObsUtil.from_buffer(buffer, 1)[0], dtype=np.float32)
        masks = np.asarray(buffer[BufferKey.ACTION_MASK], dtype=np.float32)
        valid = np.flatnonzero(np.asarray(buffer[BufferKey.MASKS], dtype=bool))
        if observations.shape != (len(masks), OBSERVATIONS) or masks.shape[1:] != (ACTIONS,):
            raise ValueError('Exploration batch does not match the learned tensor contract.')
        if not len(valid):
            return None
        random = np.random.default_rng(int(step))
        selected = random.choice(valid, min(len(valid), self.recipe['batchSize']), replace=False)
        return observations[selected].copy(), masks[selected].copy()

    def update(self, policy, optimizer, batch):
        if self.recipe is None or batch is None:
            return None
        observations, masks = batch
        observation = ModelUtils.list_to_tensor(observations)
        allowed = ModelUtils.list_to_tensor(masks)
        if not torch.isfinite(observation).all():
            raise ValueError('Non-finite learner observations cannot enter exploration repair.')
        encoded, _ = policy.actor.network_body([observation])
        distributions = policy.actor.action_model._get_dists(encoded, allowed)
        if len(distributions.discrete) != 1:
            raise ValueError('Exploration repair requires one masked discrete action branch.')
        kl, eligible, normalized = masked_uniform_kl(
            distributions.discrete[0].logits, allowed, self.recipe['entropyFloor'])
        stats = {'Policy/Exploration Eligible Fraction': float(eligible.float().mean().detach().cpu()),
                 'Policy/Exploration Normalized Entropy': float(normalized.mean().detach().cpu()),
                 'Losses/Exploration KL': float(kl.detach().cpu())}
        if not eligible.any():
            return stats
        loss = self.recipe['coefficient'] * kl
        if not torch.isfinite(loss):
            raise ValueError('Non-finite exploration loss.')
        optimizer.zero_grad(set_to_none=True)
        loss.backward()
        norm = torch.nn.utils.clip_grad_norm_(policy.actor.parameters(), .5, error_if_nonfinite=True)
        optimizer.step()
        stats['Policy/Exploration Gradient Norm'] = float(norm.detach().cpu())
        return stats
