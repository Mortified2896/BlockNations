"""Compare the actual pinned ML-Agents networks on fair C# synthetic fixtures."""
from __future__ import annotations

import base64
import hashlib
import io
import json
from pathlib import Path

import numpy as np
import torch

from migrate_checkpoint import LEGACY_OBSERVATIONS, OBSERVATIONS, ACTIONS, RULES


def fixtures(path: Path):
    if path.stat().st_size > 8 * 1024 * 1024:
        raise ValueError('Fixture input exceeds 8 MiB.')
    contents = path.read_bytes()
    data = json.loads(contents)
    expected = dict(version=1, observationSchema=3, actionSchema=2, legacyObservationSize=LEGACY_OBSERVATIONS,
                    observationSize=OBSERVATIONS, actionCount=ACTIONS, encoding='float32-le', rulesVersion=RULES)
    if (any(data.get(key) != value for key, value in expected.items()) or not isinstance(data.get('fixtures'), list) or
            not 1 <= len(data['fixtures']) <= 200):
        raise ValueError('Incompatible observation fixture contract.')
    legacy, expanded, masks = [], [], []
    for record in data['fixtures']:
        before = np.frombuffer(base64.b64decode(record['legacy'], validate=True), dtype='<f4').copy()
        after = np.frombuffer(base64.b64decode(record['expanded'], validate=True), dtype='<f4').copy()
        available = record['available']
        if (record.get('boardSize') not in (5, 6, 7, 9, 11) or type(record.get('seat')) is not int or record['seat'] not in (0, 1) or
                type(record.get('firstSeat')) is not int or record['firstSeat'] not in (0, 1) or
                type(record.get('source')) is not int or not -1 <= record['source'] < 121 or
                before.shape != (LEGACY_OBSERVATIONS,) or after.shape != (OBSERVATIONS,) or
                not np.isfinite(before).all() or not np.isfinite(after).all() or
                not np.array_equal(before, after[:LEGACY_OBSERVATIONS]) or before[-1] != 2 or after[-1] != 3 or
                after[-2] != int(record['seat'] == record['firstSeat']) or
                not np.isin(after[LEGACY_OBSERVATIONS:-2], (0, 1)).all() or
                not isinstance(available, list) or len(available) != len(set(available)) or not 1 <= len(available) <= ACTIONS or
                any(type(value) is not int or not 0 <= value < ACTIONS for value in available) or ACTIONS-1 not in available):
            raise ValueError('Fixture vectors, public role or action masks are invalid.')
        mask = np.zeros(ACTIONS, dtype=np.float32)
        mask[available] = 1
        legacy.append(before); expanded.append(after); masks.append(mask)
    return np.stack(legacy), np.stack(expanded), np.stack(masks), hashlib.sha256(contents).hexdigest()


def models(network, observations, checkpoint):
    from mlagents_envs.base_env import BehaviorSpec, ObservationSpec, DimensionProperty, ObservationType, ActionSpec
    from mlagents.trainers.settings import NetworkSettings
    from mlagents.trainers.torch_entities.networks import SimpleActor, ValueNetwork
    spec = BehaviorSpec([ObservationSpec((observations,), (DimensionProperty.NONE,), ObservationType.DEFAULT, 'VectorSensor')],
                        ActionSpec.create_discrete((ACTIONS,)))
    actor = SimpleActor(spec.observation_specs, NetworkSettings(**network), spec.action_spec)
    critic = ValueNetwork(['extrinsic'], spec.observation_specs, NetworkSettings(**network))
    actor.load_state_dict(checkpoint['Policy'], strict=True)
    critic.load_state_dict(checkpoint['Optimizer:critic'], strict=True)
    actor.eval(); critic.eval()
    return actor, critic


def probabilities(actor, values, allowed):
    encoded, _ = actor.network_body([values])
    return actor.action_model._get_dists(encoded, allowed).discrete[0].probs


def verify(parent, candidate, network, fixture_path):
    legacy, expanded, masks, digest = fixtures(fixture_path)
    before_actor, before_critic = models(network, LEGACY_OBSERVATIONS, parent)
    after_actor, after_critic = models(network, OBSERVATIONS, candidate)
    with torch.no_grad():
        old, new, allowed = map(torch.from_numpy, (legacy, expanded, masks))
        before = probabilities(before_actor, old, allowed).numpy()
        after = probabilities(after_actor, new, allowed).numpy()
        old_value = before_critic([old])[0]['extrinsic'].numpy()
        new_value = after_critic([new])[0]['extrinsic'].numpy()
    # Appending zeros is mathematically exact. Different matrix widths can select
    # a different floating-point reduction, so check a stated numeric tolerance.
    np.testing.assert_allclose(after, before, rtol=2e-5, atol=1e-6)
    np.testing.assert_allclose(new_value, old_value, rtol=2e-5, atol=1e-5)
    np.testing.assert_allclose(after.sum(axis=1), 1, rtol=1e-5, atol=1e-6)
    if (not np.isfinite(after).all() or not np.isfinite(new_value).all() or
            np.max(after[masks == 0], initial=0) > 1e-7 or not np.array_equal(after.argmax(axis=1), before.argmax(axis=1))):
        raise ValueError('Candidate changed a top action or violated its mask.')
    result = dict(fixtures=len(legacy), fixtureSha256=digest,
        maximumProbabilityError=float(np.max(np.abs(after-before))), maximumValueError=float(np.max(np.abs(new_value-old_value))),
        topActionsPreserved=True, maskedActionsVerified=True, probabilityAbsoluteTolerance=1e-6,
        valueAbsoluteTolerance=1e-5, relativeTolerance=2e-5)
    return result, after_actor, expanded, masks


class ProbabilityExport(torch.nn.Module):
    def __init__(self, actor):
        super().__init__()
        self.actor = actor

    def forward(self, obs_0, action_masks):
        return probabilities(self.actor, obs_0, action_masks)


def export(actor, expanded, masks):
    import onnx
    from onnx.reference import ReferenceEvaluator
    actor.requires_grad_(False)
    stream = io.BytesIO()
    inputs = tuple(torch.from_numpy(value[:1]) for value in (expanded, masks))
    torch.onnx.export(ProbabilityExport(actor), inputs, stream, export_params=True, opset_version=13,
        input_names=['obs_0', 'action_masks'], output_names=['policy_probabilities'],
        dynamic_axes={'obs_0': {0: 'batch'}, 'action_masks': {0: 'batch'}, 'policy_probabilities': {0: 'batch'}})
    model = onnx.load_model_from_string(stream.getvalue())
    # Torch trace strings can include local source paths. The candidate export
    # carries only weights, operators and tensor names, without those docstrings.
    model.doc_string = ''; model.graph.doc_string = ''; model.metadata_props.clear()
    model.producer_name = 'BlockNations.ObservationContract'
    for item in list(model.graph.node) + list(model.graph.input) + list(model.graph.output) + list(model.graph.value_info):
        item.doc_string = ''
    onnx.checker.check_model(model)
    expected_inputs = {'obs_0': OBSERVATIONS, 'action_masks': ACTIONS}
    if ({item.name for item in model.graph.input} != set(expected_inputs) or
            len(model.graph.output) != 1 or model.graph.output[0].name != 'policy_probabilities'):
        raise ValueError('Export has unexpected inputs/outputs.')
    for item in model.graph.input:
        if item.type.tensor_type.shape.dim[1].dim_value != expected_inputs[item.name]:
            raise ValueError('Export has incompatible input dimensions.')
    evaluator = ReferenceEvaluator(model)
    maximum_error = 0.0
    # Exercise dynamic batches separately; the runtime need not send a fixed
    # batch of fixtures, and each action mask is genuinely from the C# engine.
    for start in range(0, len(expanded), 17):
        values, allowed = expanded[start:start+17], masks[start:start+17]
        actual = evaluator.run(None, {'obs_0': values, 'action_masks': allowed})[0]
        with torch.no_grad():
            expected = probabilities(actor, torch.from_numpy(values), torch.from_numpy(allowed)).numpy()
        np.testing.assert_allclose(actual, expected, rtol=2e-5, atol=1e-6)
        if (actual.shape != (len(values), ACTIONS) or not np.isfinite(actual).all() or
                np.max(actual[allowed == 0], initial=0) > 1e-7):
            raise ValueError('ONNX violated shape, finite output or action masking.')
        maximum_error = max(maximum_error, float(np.max(np.abs(actual-expected))))
    contents = model.SerializeToString()
    if b'/Users/' in contents or b'/home/' in contents or b'api_key' in contents.lower() or b'sk-proj-' in contents:
        raise ValueError('ONNX export contains private path or credential-like metadata.')
    return contents, dict(samples=len(expanded), maximumProbabilityError=maximum_error,
        maskedActionsVerified=True, modelBytes=len(contents), modelSha256=hashlib.sha256(contents).hexdigest())
