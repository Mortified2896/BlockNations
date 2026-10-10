"""Read-only frozen actor inference shared by evaluation and retained opponents."""
import hashlib
import io
from pathlib import Path

import numpy as np
from mlagents.torch_utils import torch
from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import SimpleActor

from playtest_sampling import sampling_weights
from training_contract import ACTIONS
from match_rating import policy_id

SAMPLING_MODES = ('Policy', 'Easy', 'Medium', 'Hard')


def evaluation_weights(probabilities, difficulty):
    if difficulty == 'Policy':
        values = np.asarray(probabilities, dtype=np.float64)
        totals = values.sum(axis=1, keepdims=True) if values.ndim == 2 else None
        if (values.ndim != 2 or values.shape[1] != ACTIONS or not np.isfinite(values).all() or
                (values < 0).any() or not np.isfinite(totals).all() or (totals <= 0).any()):
            raise ValueError('The frozen policy returned invalid action probabilities.')
        return values / totals
    return sampling_weights(probabilities, difficulty)


class FrozenPolicy:
    def __init__(self, checkpoint, network, behavior_spec, expected_sha256=None, expected_actor_id=None):
        if network.get('memory'):
            raise ValueError('Recurrent inference requires an explicit memory adapter.')
        contents = Path(checkpoint).read_bytes()
        self.sha256 = hashlib.sha256(contents).hexdigest()
        if expected_sha256 is not None and self.sha256 != expected_sha256:
            raise ValueError('Frozen opponent checkpoint changed after validation.')
        self.name = Path(checkpoint).stem
        self.actor = SimpleActor(behavior_spec.observation_specs, NetworkSettings(**network), behavior_spec.action_spec)
        state = torch.load(io.BytesIO(contents), map_location='cpu')['Policy']
        if any(not torch.isfinite(value).all() for value in state.values()):
            raise ValueError('Frozen actor contains non-finite weights.')
        self.actor.load_state_dict(state, strict=True)
        self.actor_id = policy_id(self.actor.state_dict())
        if expected_actor_id is not None and self.actor_id != expected_actor_id:
            raise ValueError('Retained actor identity does not match its weights.')
        self.actor.eval()
        self.actor.requires_grad_(False)

    def probabilities(self, observation, action_mask):
        with torch.no_grad():
            tensor = torch.as_tensor(observation)
            allowed = torch.as_tensor(~action_mask, dtype=torch.float32)
            encoded, _ = self.actor.network_body([tensor])
            probabilities = self.actor.action_model._get_dists(encoded, allowed).discrete[0].probs.cpu().numpy()
        # SDK masks have a negligible probability floor; remove it completely.
        return np.where(action_mask, 0, probabilities)

    def choose(self, decisions, random, difficulty, **context):
        weights = evaluation_weights(self.probabilities(decisions.obs[0], decisions.action_mask[0]), difficulty)
        return np.array([[random.choice(weights.shape[1], p=row)] for row in weights], dtype=np.int32)
