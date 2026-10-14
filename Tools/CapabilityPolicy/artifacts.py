"""Strict architecture-specific checkpoint checks before evaluation or resume."""
import hashlib
import io
import math
from pathlib import Path
import sys

import torch
from mlagents.trainers.settings import NetworkSettings

sys.path.insert(0, str(Path(__file__).parents[1]/'Training'))
from model_contract import spec
from policy import CapabilityActor


def checkpoint(path, width):
    path = Path(path).absolute()
    if path.is_symlink() or path.stat().st_size > 20_000_000:
        raise ValueError('Use a bounded unlinked capability checkpoint.')
    contents = path.read_bytes()
    saved = torch.load(io.BytesIO(contents), map_location='cpu')
    contract = spec(4)
    actor = CapabilityActor(contract.observation_specs, NetworkSettings(hidden_units=width, num_layers=2), contract.action_spec)
    actor.load_state_dict(saved['Policy'], strict=True)
    if saved['Policy'].keys() != saved['Optimizer:critic'].keys() or any(
            not torch.equal(value, saved['Optimizer:critic'][name]) for name, value in saved['Policy'].items()):
        raise ValueError('Capability checkpoint must retain the identical shared actor/value network.')
    tensors = 0
    def finite(value):
        nonlocal tensors
        if isinstance(value, torch.Tensor):
            tensors += 1
            if not torch.isfinite(value).all():
                raise ValueError('Capability checkpoint has non-finite actor, value or optimizer state.')
        elif isinstance(value, dict):
            for item in value.values(): finite(item)
        elif isinstance(value, (list, tuple)):
            for item in value: finite(item)
        elif isinstance(value, float) and not math.isfinite(value):
            raise ValueError('Capability checkpoint has non-finite optimizer settings.')
    finite(saved)
    optimizer = torch.optim.Adam(actor.parameters(), lr=.0003)
    optimizer.load_state_dict(saved['Optimizer:value_optimizer'])
    counters = {}
    for name, parameter in actor.named_parameters():
        if not parameter.requires_grad: continue
        state = optimizer.state.get(parameter, {})
        if (set(state) != {'step', 'exp_avg', 'exp_avg_sq'} or state['step'].numel() != 1 or
                state['step'].item() <= 0 or state['step'].item() != int(state['step'].item()) or
                any(state[key].shape != parameter.shape for key in ('exp_avg', 'exp_avg_sq')) or
                (state['exp_avg_sq'] < 0).any()):
            raise ValueError('Capability Adam state/counters do not match the trained architecture.')
        counters[name] = int(state['step'].item())
    step = saved['global_step']['_GlobalSteps__global_step']
    if step.numel() != 1 or step.item() <= 0 or step.item() != int(step.item()):
        raise ValueError('Use a completed positive capability learning-step checkpoint.')
    return actor, dict(step=int(step.item()), checkpointSha256=hashlib.sha256(contents).hexdigest(),
                       finiteTensors=tensors, optimizerSteps=counters,
                       trainableParameters=sum(p.numel() for p in actor.parameters() if p.requires_grad))
