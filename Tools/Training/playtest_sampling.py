"""Inference-only difficulty presets matching the browser's learned action sampling."""
import numpy as np

PRESETS = {'Easy': (1.8, 0.0), 'Medium': (0.8, 0.0), 'Hard': (0.25, 0.7)}


def sampling_weights(probabilities, difficulty):
    if difficulty not in PRESETS:
        raise ValueError('Choose Easy, Medium or Hard.')
    values = np.asarray(probabilities, dtype=np.float64)
    if (values.ndim != 2 or values.shape[1] != 259 or not np.isfinite(values).all() or
            (values < 0).any() or (values.max(axis=1) <= 0).any()):
        raise ValueError('The frozen policy returned invalid action probabilities.')
    temperature, cutoff = PRESETS[difficulty]
    relative = values / values.max(axis=1, keepdims=True)
    weights = np.where(relative >= cutoff, relative ** (1.0 / temperature), 0.0)
    return weights / weights.sum(axis=1, keepdims=True)


def install(difficulty):
    # Installed only in the separate inference process. Never patches a learner,
    # changes weights/observations or writes to the installed ML-Agents package.
    if difficulty not in PRESETS:
        raise ValueError('Choose Easy, Medium or Hard.')
    from mlagents.torch_utils import torch
    from mlagents.trainers.torch_entities.distributions import CategoricalDistInstance

    def sample(distribution):
        weights = sampling_weights(distribution.probs.detach().cpu().numpy(), difficulty)
        tensor = torch.as_tensor(weights, dtype=distribution.probs.dtype, device=distribution.probs.device)
        return torch.multinomial(tensor, 1)

    CategoricalDistInstance.sample = sample
