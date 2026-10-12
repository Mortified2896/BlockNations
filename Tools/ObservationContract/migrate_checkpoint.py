"""Prepare an opt-in v3 actor/critic without changing the live v2 learner.

The v2 vector is an exact prefix of v3. Zero new input columns preserve the
initial function; old Adam moments/counters and the global step are retained.
This bounded adapter supports the audited, unnormalized 128x2 feed-forward PPO
checkpoint only. Runtime/trainer activation is a separate versioned operation.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import io
import json
from pathlib import Path
import re

import torch

LEGACY_OBSERVATIONS, OBSERVATIONS, ACTIONS = 3120, 3259, 259
LEGACY_BEHAVIOR, BEHAVIOR = 'BlockNationsSeatV2', 'BlockNationsSeatV3'
RULES = 'blocknations-simulation-v5-core3'
INPUT_WEIGHT = 'network_body._body_endoder.seq_layers.0.weight'


def validate_network(network):
    if (not isinstance(network, dict) or network.get('normalize') is not False or
            network.get('memory') is not None or network.get('hidden_units') != 128 or network.get('num_layers') != 2):
        raise ValueError('Migration supports the audited unnormalized 128x2 feed-forward network only.')


def _finite_tensor(value, shape):
    if (not isinstance(value, torch.Tensor) or value.device.type != 'cpu' or tuple(value.shape) != shape or
            value.dtype != torch.float32 or not bool(torch.isfinite(value).all())):
        raise ValueError('Checkpoint tensor has an unsupported shape, dtype, device or non-finite value.')


def _expanded(value, source_size=LEGACY_OBSERVATIONS, target_size=OBSERVATIONS):
    _finite_tensor(value, (128, source_size))
    return torch.cat((value, torch.zeros((128, target_size - source_size), dtype=value.dtype)), dim=1)


def migrate(checkpoint, network, *, source_size=LEGACY_OBSERVATIONS, target_size=OBSERVATIONS):
    """Return a new checkpoint; never mutate the parent or reset learning state."""
    validate_network(network)
    if (source_size, target_size) not in ((3120, 3259), (3259, 4712)):
        raise ValueError('Only explicitly versioned, unchanged-prefix extensions are supported.')
    if not isinstance(checkpoint, dict) or set(checkpoint) != {'Policy', 'global_step', 'Optimizer:critic', 'Optimizer:value_optimizer'}:
        raise ValueError('Unsupported checkpoint structure.')
    policy, critic, optimizer = (checkpoint[key] for key in ('Policy', 'Optimizer:critic', 'Optimizer:value_optimizer'))
    actor_shapes = {
        'version_number': (1,), 'is_continuous_int_deprecated': (1,), 'continuous_act_size_vector': (1,),
        'discrete_act_size_vector': (1, 1), 'act_size_vector_deprecated': (1,), 'memory_size_vector': (1,),
        INPUT_WEIGHT: (128, source_size), 'network_body._body_endoder.seq_layers.0.bias': (128,),
        'network_body._body_endoder.seq_layers.2.weight': (128, 128), 'network_body._body_endoder.seq_layers.2.bias': (128,),
        'action_model._discrete_distribution.branches.0.weight': (ACTIONS, 128),
        'action_model._discrete_distribution.branches.0.bias': (ACTIONS,)}
    critic_shapes = {INPUT_WEIGHT: (128, source_size), 'network_body._body_endoder.seq_layers.0.bias': (128,),
        'network_body._body_endoder.seq_layers.2.weight': (128, 128), 'network_body._body_endoder.seq_layers.2.bias': (128,),
        'value_heads.value_heads.extrinsic.weight': (1, 128), 'value_heads.value_heads.extrinsic.bias': (1,)}
    if not isinstance(policy, dict) or not isinstance(critic, dict) or set(policy) != set(actor_shapes) or set(critic) != set(critic_shapes):
        raise ValueError('Actor/critic names differ from the audited checkpoint contract.')
    for state, shapes in ((policy, actor_shapes), (critic, critic_shapes)):
        for key, shape in shapes.items():
            _finite_tensor(state[key], shape)
    if policy['discrete_act_size_vector'].item() != ACTIONS or policy['memory_size_vector'].item() != 0:
        raise ValueError('Action or recurrent-memory metadata is incompatible.')
    step_state = checkpoint['global_step']
    if (not isinstance(step_state, dict) or set(step_state) != {'_GlobalSteps__global_step'} or
            not isinstance(step_state['_GlobalSteps__global_step'], torch.Tensor)):
        raise ValueError('Missing audited global-step tensor.')
    global_step = step_state['_GlobalSteps__global_step']
    if global_step.shape != (1,) or global_step.dtype != torch.int64 or global_step.item() < 0:
        raise ValueError('Invalid global-step tensor.')

    # The stock optimizer includes six frozen actor metadata parameters first,
    # then six trainable actor and six critic tensors. Validate that association
    # rather than guessing an Adam slot from a coincidentally matching shape.
    if (not isinstance(optimizer, dict) or set(optimizer) != {'state', 'param_groups'} or
            not isinstance(optimizer['param_groups'], list) or len(optimizer['param_groups']) != 1 or
            optimizer['param_groups'][0].get('params') != list(range(18)) or
            optimizer['param_groups'][0].get('amsgrad') is not False or
            set(optimizer['state']) != set(range(6, 18))):
        raise ValueError('Unsupported or incomplete Adam parameter association.')
    shapes = [(128, source_size), (128,), (128, 128), (128,), (ACTIONS, 128), (ACTIONS,),
              (128, source_size), (128,), (128, 128), (128,), (1, 128), (1,)]
    for parameter, shape in zip(range(6, 18), shapes):
        state = optimizer['state'][parameter]
        if not isinstance(state, dict) or set(state) != {'step', 'exp_avg', 'exp_avg_sq'}:
            raise ValueError('Unsupported Adam state.')
        _finite_tensor(state['step'], ())
        if state['step'].item() < 0 or state['step'].item() != int(state['step'].item()):
            raise ValueError('Invalid Adam counter.')
        _finite_tensor(state['exp_avg'], shape)
        _finite_tensor(state['exp_avg_sq'], shape)
        if bool((state['exp_avg_sq'] < 0).any()):
            raise ValueError('Invalid Adam second moment.')

    migrated = copy.deepcopy(checkpoint)
    for key in ('Policy', 'Optimizer:critic'):
        migrated[key][INPUT_WEIGHT] = _expanded(checkpoint[key][INPUT_WEIGHT], source_size, target_size)
    for parameter in (6, 12):
        for name in ('exp_avg', 'exp_avg_sq'):
            migrated['Optimizer:value_optimizer']['state'][parameter][name] = _expanded(optimizer['state'][parameter][name], source_size, target_size)
    return migrated


def prepare(source_manifest: Path, destination: Path, fixture_path: Path):
    """Write a new local candidate folder, refusing stale inputs and overwrites."""
    if destination.exists():
        raise ValueError('Migration destination already exists; preserve the previous result.')
    manifest_bytes = source_manifest.read_bytes()
    source = json.loads(manifest_bytes)
    if (source.get('boardSize') != 7 or source.get('rulesVersion') != RULES or
            source.get('openingEconomyVersion') != 2 or type(source.get('step')) is not int):
        raise ValueError('Use a frozen core-three 7x7 checkpoint with the recorded opening economy.')
    checkpoint_path, config_path = Path(source['checkpoint']), Path(source['trainerConfig'])
    match = re.fullmatch(r'BlockNationsSeatV2-(\d+)\.pt', checkpoint_path.name)
    if not match or int(match[1]) != source['step']:
        raise ValueError('The parent must be an immutable numbered v2 checkpoint.')
    checkpoint_bytes, config_bytes = checkpoint_path.read_bytes(), config_path.read_bytes()
    if (hashlib.sha256(checkpoint_bytes).hexdigest() != source.get('sha256') or
            hashlib.sha256(config_bytes).hexdigest() != source.get('trainerConfigSha256')):
        raise ValueError('Frozen checkpoint/config changed after validation.')
    config = json.loads(config_bytes)
    if set(config['behaviors']) != {LEGACY_BEHAVIOR} or config['behaviors'][LEGACY_BEHAVIOR]['trainer_type'] != 'ppo':
        raise ValueError('Use the single-behavior PPO configuration.')
    network = config['behaviors'][LEGACY_BEHAVIOR]['network_settings']
    parent = torch.load(io.BytesIO(checkpoint_bytes), map_location='cpu', weights_only=True)
    result = migrate(parent, network)
    if result['global_step']['_GlobalSteps__global_step'].item() != source['step']:
        raise ValueError('Parent global step differs from its frozen manifest.')
    from verification import verify, export
    verification, actor, expanded, masks = verify(parent, result, network, fixture_path)
    onnx_contents, onnx_verification = export(actor, expanded, masks)
    config['behaviors'][BEHAVIOR] = config['behaviors'].pop(LEGACY_BEHAVIOR)
    stream = io.BytesIO()
    torch.save(result, stream)
    contents = stream.getvalue()
    config_contents = (json.dumps(config, indent=2) + '\n').encode()
    receipt = dict(version=1, status='prepared_only', observationSchema=3, actionSchema=2,
        legacyPrefixSchema=2, legacyObservationSize=LEGACY_OBSERVATIONS, observationSize=OBSERVATIONS, actionCount=ACTIONS,
        rulesVersion=RULES, boardSize=7, openingEconomyVersion=2, step=source['step'],
        parentCheckpointSha256=source['sha256'], parentActorId=source.get('actorId'),
        parentManifestSha256=hashlib.sha256(manifest_bytes).hexdigest(), parentTrainerConfigSha256=source['trainerConfigSha256'],
        checkpointSha256=hashlib.sha256(contents).hexdigest(), trainerConfigSha256=hashlib.sha256(config_contents).hexdigest(),
        newInputColumns=OBSERVATIONS-LEGACY_OBSERVATIONS, newInputWeights='zero', newAdamMoments='zero',
        optimizerSteps={str(key): int(state['step'].item()) for key, state in result['Optimizer:value_optimizer']['state'].items()},
        verification=verification, onnxVerification=onnx_verification,
        activation='requires separate v3 worker/environment/opponent/export/runtime adapters')
    destination.mkdir(parents=True, exist_ok=False)
    checkpoint_name = f'{BEHAVIOR}-{source["step"]}.pt'
    # All validation precedes creation. Exclusive files preserve unknown/partial
    # outcomes for inspection and prohibit silently replacing an earlier actor.
    for name, data in ((checkpoint_name, contents), ('CandidateV3.onnx', onnx_contents), ('trainer.json', config_contents),
                       ('manifest.json', (json.dumps(receipt, indent=2) + '\n').encode())):
        with (destination/name).open('xb') as output:
            output.write(data)
    return receipt


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-manifest', type=Path, required=True)
    parser.add_argument('--destination', type=Path, required=True)
    parser.add_argument('--fixtures', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(prepare(args.source_manifest, args.destination, args.fixtures), indent=2))
