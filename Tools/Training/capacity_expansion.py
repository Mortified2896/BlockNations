"""Function-preserving 128 -> 256 expansion of the audited feed-forward PPO.

The original actor/critic remain in the leading block. New hidden features are
random and distinct, old hidden outputs do not depend on them, and new head
columns start at zero. Thus the initial policy/value are unchanged while the new
features can learn after the heads receive their first gradient. Old Adam moments,
per-tensor counters and the global step survive; added moments start at zero.
"""
import copy
import math

from mlagents.torch_utils import torch
from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import SimpleActor, ValueNetwork
from model_contract import spec

INPUT = 'network_body._body_endoder.seq_layers.0.weight'
MIDDLE = 'network_body._body_endoder.seq_layers.2.weight'
ACTOR_HEAD = 'action_model._discrete_distribution.branches.0.weight'
CRITIC_HEAD = 'value_heads.value_heads.extrinsic.weight'
METADATA = ('version_number', 'is_continuous_int_deprecated', 'continuous_act_size_vector',
            'discrete_act_size_vector', 'act_size_vector_deprecated', 'memory_size_vector')


def _modules(network, schema):
    settings = NetworkSettings(**network)
    contract = spec(schema)
    return (SimpleActor(contract.observation_specs, settings, contract.action_spec),
            ValueNetwork(['extrinsic'], contract.observation_specs, settings))


def _tensor(value, expected):
    if (not torch.is_tensor(value) or value.device.type != 'cpu' or value.dtype != expected.dtype
            or value.shape != expected.shape or not bool(torch.isfinite(value).all())):
        raise ValueError('Capacity expansion needs finite CPU tensors with the audited shapes and dtypes.')


def expand(checkpoint, network, schema, seed):
    """Return a detached expanded checkpoint/config; never change the parent."""
    if (not isinstance(network, dict) or network.get('normalize') is not False
            or network.get('memory') is not None or network.get('hidden_units') != 128
            or network.get('num_layers') != 2 or type(seed) is not int or not 0 <= seed < 2**31):
        raise ValueError('Capacity experiment supports the unnormalized memory-free 128x2 parent only.')
    if not isinstance(checkpoint, dict) or set(checkpoint) != {'Policy', 'global_step', 'Optimizer:critic', 'Optimizer:value_optimizer'}:
        raise ValueError('Unsupported PPO checkpoint structure.')
    target_network = copy.deepcopy(network)
    target_network['hidden_units'] = 256
    with torch.random.fork_rng(devices=[]):
        torch.manual_seed(seed)
        before_actor, before_critic = _modules(network, schema)
        after_actor, after_critic = _modules(target_network, schema)
    actor, critic = checkpoint['Policy'], checkpoint['Optimizer:critic']
    for actual, module in ((actor, before_actor), (critic, before_critic)):
        expected = module.state_dict()
        if not isinstance(actual, dict) or set(actual) != set(expected):
            raise ValueError('Unsupported actor/critic parameter names.')
        for name, value in actual.items():
            _tensor(value, expected[name])
            if name in METADATA and not torch.equal(value, expected[name]):
                raise ValueError('Invalid actor metadata.')
        module.load_state_dict(actual, strict=True)
    if actor['discrete_act_size_vector'].item() != 259 or actor['memory_size_vector'].item() != 0:
        raise ValueError('Unsupported action/recurrent-memory metadata.')
    step = checkpoint['global_step']
    if (not isinstance(step, dict) or set(step) != {'_GlobalSteps__global_step'}
            or not torch.is_tensor(step['_GlobalSteps__global_step'])
            or step['_GlobalSteps__global_step'].device.type != 'cpu'
            or step['_GlobalSteps__global_step'].dtype != torch.int64
            or step['_GlobalSteps__global_step'].shape != (1,) or step['_GlobalSteps__global_step'].item() < 0):
        raise ValueError('Invalid global step.')
    optimizer = checkpoint['Optimizer:value_optimizer']
    if (not isinstance(optimizer, dict) or set(optimizer) != {'state', 'param_groups'}
            or not isinstance(optimizer['param_groups'], list) or len(optimizer['param_groups']) != 1
            or optimizer['param_groups'][0].get('params') != list(range(18))
            or optimizer['param_groups'][0].get('amsgrad') is not False
            or set(optimizer['state']) != set(range(6, 18))):
        raise ValueError('Unsupported Adam parameter association.')
    group = optimizer['param_groups'][0]
    if (any(type(group.get(k)) not in (int, float) or not math.isfinite(group[k]) or group[k] <= 0 for k in ('lr', 'eps'))
            or not isinstance(group.get('betas'), (tuple, list)) or len(group['betas']) != 2
            or any(type(b) not in (int, float) or not math.isfinite(b) or not 0 <= b < 1 for b in group['betas'])
            or group.get('weight_decay') != 0 or group.get('maximize', False) is not False):
        raise ValueError('Unsupported Adam hyperparameters.')
    parameters = list(before_actor.parameters())+list(before_critic.parameters())
    if len(parameters) != 18 or any(p.requires_grad for p in parameters[:6]):
        raise ValueError('SDK parameter ordering differs from the audited optimizer.')
    for index in range(6, 18):
        state = optimizer['state'][index]
        if not isinstance(state, dict) or set(state) != {'step', 'exp_avg', 'exp_avg_sq'}:
            raise ValueError('Unsupported Adam state.')
        _tensor(state['step'], torch.tensor(0.))
        if state['step'].item() < 0 or state['step'].item() != int(state['step'].item()):
            raise ValueError('Invalid Adam counter.')
        for key in ('exp_avg', 'exp_avg_sq'):
            _tensor(state[key], parameters[index])
        if bool((state['exp_avg_sq'] < 0).any()):
            raise ValueError('Invalid Adam second moment.')
    result = copy.deepcopy(checkpoint)
    for key, old, module, head in (('Policy', actor, after_actor, ACTOR_HEAD),
                                   ('Optimizer:critic', critic, after_critic, CRITIC_HEAD)):
        expanded = module.state_dict()
        for name, value in old.items():
            if name == MIDDLE:
                expanded[name][:128, :] = 0
                expanded[name][:128, :128] = value
            elif name == head:
                expanded[name].zero_()
                expanded[name][:, :128] = value
            elif expanded[name].shape == value.shape:
                expanded[name].copy_(value)
            elif value.ndim == 2:
                expanded[name][:128, :] = value
            else:
                expanded[name][:128] = value
        result[key] = expanded
    new_parameters = list(after_actor.parameters())+list(after_critic.parameters())
    for index in range(6, 18):
        for key in ('exp_avg', 'exp_avg_sq'):
            value = optimizer['state'][index][key]
            expanded = torch.zeros_like(new_parameters[index])
            slices = tuple(slice(0, length) for length in value.shape)
            expanded[slices] = value
            result['Optimizer:value_optimizer']['state'][index][key] = expanded
    return result, target_network
