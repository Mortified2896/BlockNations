"""Freeze a completed local policy and expose its masked probabilities for Unity.

No training examples, optimizer state, credentials or private file paths enter the
release. Actor weights are unchanged; unused stochastic/metadata outputs are pruned.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
from pathlib import Path
import time

import numpy as np
import onnx
from onnx import helper, TensorProto
from onnx.reference import ReferenceEvaluator

from playtest import latest_checkpoint, read_json
from model_contract import SIZES, checkpoint_schema
from training_contract import (OPENING_ECONOMY_VERSION, checkpoint_step, run_rules_version,
                               validate_rules_version, transfer_contract)

PROBABILITIES = "policy_probabilities"


def convert(source: onnx.ModelProto) -> onnx.ModelProto:
    probabilities = [node.output[0] for node in source.graph.node if node.op_type == "Softmax"]
    if len(probabilities) != 1:
        raise ValueError("Expected one discrete policy distribution; this exporter requires a schema update otherwise.")
    model = copy.deepcopy(source)
    model.graph.node.append(helper.make_node("Identity", [probabilities[0]], [PROBABILITIES]))
    model.graph.output.clear()
    model.graph.output.append(helper.make_tensor_value_info(PROBABILITIES, TensorProto.FLOAT, ["batch", 259]))
    model = onnx.utils.Extractor(model).extract_model(["obs_0", "action_masks"], [PROBABILITIES])
    model.producer_name = "BlockNations.LearnedPlaytest"
    onnx.checker.check_model(model)
    return model


def verify(source: onnx.ModelProto, converted: onnx.ModelProto, seed: int = 42) -> dict:
    # Run the original actor distribution directly, avoiding its random action node.
    source_probability = next(node.output[0] for node in source.graph.node if node.op_type == "Softmax")
    original = copy.deepcopy(source)
    original.graph.output.clear()
    original.graph.output.append(helper.make_tensor_value_info(source_probability, TensorProto.FLOAT, ["batch", 259]))
    original = onnx.utils.Extractor(original).extract_model(["obs_0", "action_masks"], [source_probability])
    before, after = ReferenceEvaluator(original), ReferenceEvaluator(converted)
    size = next(i for i in source.graph.input if i.name == 'obs_0').type.tensor_type.shape.dim[1].dim_value
    if size not in SIZES.values(): raise ValueError('Unsupported exported observation size.')
    random = np.random.default_rng(seed)
    largest_error = 0.0
    for sample in range(24):
        observation = random.normal(0, 0.3, (1, size)).astype(np.float32)
        mask = (random.random((1, 259)) > 0.6).astype(np.float32)
        mask[0, 258] = 1  # EndTurn is always available to an owning seat.
        if sample == 0:
            observation.fill(0)
            mask.fill(0)
            mask[0, 258] = 1
        inputs = {"obs_0": observation, "action_masks": mask}
        expected = before.run(None, inputs)[0]
        actual = after.run(None, inputs)[0]
        if actual.shape != (1, 259) or not np.isfinite(actual).all():
            raise ValueError("The converted policy produced invalid probabilities.")
        np.testing.assert_allclose(actual, expected, rtol=1e-6, atol=1e-7)
        np.testing.assert_allclose(actual.sum(axis=1), 1, rtol=1e-5)
        if np.max(actual[mask == 0], initial=0) > 1e-7:
            raise ValueError("A masked action retained probability.")
        largest_error = max(largest_error, float(np.max(np.abs(expected - actual))))
    return {"samples": 24, "maximumProbabilityError": largest_error, "maskedActionsVerified": True}


def export(run: Path, destination: Path, checkpoint: Path | None = None) -> dict:
    manifest = read_json(run / "run.json")
    arena = read_json(run / "arena-status.json")
    rules = run_rules_version(run)
    if manifest.get("boardSize") != 7 or arena.get("simulationVersion") != rules:
        raise ValueError("The browser playtest requires a 7x7 checkpoint under the current rules.")
    contract = read_json(run / 'checkpoint-contract.json')
    if (contract.get('rulesVersion') != rules or contract.get('boardSize') != 7 or
            contract.get('openingEconomyVersion') != OPENING_ECONOMY_VERSION or
            type(contract.get('minimumCheckpointStep')) is not int):
        raise ValueError('The checkpoint has no compatible recorded training contract.')
    # Read the pair as one snapshot. Retention may retire a selected pair before
    # either read; retry selection, never fall back to the overwritten checkpoint.pt.
    requested = checkpoint
    for attempt in range(3):
        checkpoint = requested or latest_checkpoint(run)
        if checkpoint_step(checkpoint) <= contract['minimumCheckpointStep']:
            raise ValueError('Train a new checkpoint after the recorded rules/opening transition before exporting.')
        try:
            weights = checkpoint.read_bytes()
            original_bytes = checkpoint.with_suffix('.onnx').read_bytes()
            break
        except FileNotFoundError:
            if requested or attempt == 2:
                raise
    if requested:
        frozen = read_json(checkpoint.parent/'manifest.json')
        if (frozen.get('rulesVersion') != rules or frozen.get('boardSize') != 7 or
                frozen.get('openingEconomyVersion') != OPENING_ECONOMY_VERSION or
                frozen.get('sha256') != hashlib.sha256(weights).hexdigest()):
            raise ValueError('The selected frozen checkpoint does not match its provenance manifest.')
    return write_release(checkpoint, weights, original_bytes, destination, rules)


def write_release(checkpoint, weights, original_bytes, destination, rules, provenance=None):
    source = onnx.load_model_from_string(original_bytes)
    converted = convert(source)
    verification = verify(source, converted)
    contents = converted.SerializeToString()
    destination.mkdir(parents=True, exist_ok=True)
    model = destination / "Compact7.onnx"
    model.write_bytes(contents)
    result = {
        "schema": checkpoint_schema(checkpoint), "actionSchema": 2, "boardSize": 7, "observationSize": SIZES[checkpoint_schema(checkpoint)], "actionCount": 259,
        "rulesVersion": rules, "openingEconomyVersion": OPENING_ECONOMY_VERSION,
        "modelVersion": checkpoint.stem,
        "modelSha256": hashlib.sha256(contents).hexdigest(),
        "checkpointSha256": hashlib.sha256(weights).hexdigest(),
        "sourceOnnxSha256": hashlib.sha256(original_bytes).hexdigest(),
        "probabilityOutput": PROBABILITIES, "modelBytes": len(contents),
        "exportedUnixSeconds": int(time.time()), "verification": verification,
        "difficultyStatus": "experimental_sampling_presets",
    }
    if provenance:
        result.update(provenance)
    (destination / "manifest.json").write_text(json.dumps(result, indent=2) + "\n")
    return result


def evaluated_transfer(manifest, receipt, target, config_sha=None):
    """A transfer is evaluated compatibility, never new-rules training provenance."""
    contract = transfer_contract(manifest['rulesVersion'], target)
    if (receipt.get('state') != 'completed' or receipt.get('boardSize') != 7 or
            receipt.get('openingEconomyVersion') != OPENING_ECONOMY_VERSION or
            receipt.get('rulesVersion') != target or not receipt.get('results') or
            receipt.get('candidate', {}).get('sha256') != manifest.get('sha256') or
            receipt.get('candidate', {}).get('trainedRulesVersion') != manifest['rulesVersion'] or
            receipt.get('candidate', {}).get('transferRules') != contract or
            (config_sha is not None and receipt.get('candidate', {}).get('configSha256') != config_sha) or
            not isinstance(receipt.get('suiteId'), str) or len(receipt['suiteId']) != 64):
        raise ValueError('A rules transfer requires a completed matching frozen-candidate evaluation.')
    return contract


def export_frozen(manifest_path, destination, target_rules=None, evaluation_receipt=None, sampling_mode='Hard'):
    # Immutable release selection must not depend on a stopped run's mutable
    # live arena or missing historical supervisor contract.
    from evaluate_suite import frozen_actor
    from frozen_policy import SAMPLING_MODES
    if sampling_mode not in SAMPLING_MODES:
        raise ValueError('Choose a supported release sampling mode.')
    manifest = read_json(manifest_path)
    source_rules = validate_rules_version(manifest.get('rulesVersion'))
    target = validate_rules_version(target_rules or source_rules)
    actor, identity = frozen_actor(manifest_path, source_rules)  # Hash, finite actor and compatible shapes.
    checkpoint = Path(manifest['checkpoint'])
    if (checkpoint.is_symlink() or checkpoint.with_suffix('.onnx').is_symlink() or
            checkpoint_step(checkpoint) != manifest.get('step')):
        raise ValueError('Use a numbered frozen checkpoint pair with its recorded step.')
    parent = manifest.get('weightTransfer', {})
    if parent and manifest['step'] <= parent.get('parentStep', manifest['step']):
        raise ValueError('Transferred weights must train before claiming the new training rules.')
    weights, original_bytes = checkpoint.read_bytes(), checkpoint.with_suffix('.onnx').read_bytes()
    if hashlib.sha256(weights).hexdigest() != actor.sha256:
        raise ValueError('The frozen checkpoint changed during export.')
    provenance = {'trainedRulesVersion': source_rules, 'samplingMode': sampling_mode}
    if target != source_rules:
        receipt = read_json(evaluation_receipt) if evaluation_receipt is not None else {}
        provenance['transferRules'] = evaluated_transfer(manifest, receipt, target, identity['configSha256'])
        if not any(case.get('difficulty') == sampling_mode and case.get('games', 0) > 0 for case in receipt['results']):
            raise ValueError('The transferred release needs evaluation using its selected sampling mode.')
        provenance['evaluationSuiteId'] = receipt['suiteId']
    # Verify the ONNX companion against the selected checkpoint actor itself,
    # in addition to conversion parity. A neighbouring file is not proof.
    converted = convert(onnx.load_model_from_string(original_bytes))
    evaluator = ReferenceEvaluator(converted)
    random = np.random.default_rng(42)
    for _ in range(8):
        observation = random.normal(0, 0.3, (1, actor.observation_size)).astype(np.float32)
        mask = (random.random((1, 259)) > 0.6).astype(np.float32); mask[0, 258] = 1
        expected = actor.probabilities(observation, mask == 0)
        actual = evaluator.run(None, {'obs_0': observation, 'action_masks': mask})[0]
        np.testing.assert_allclose(actual, expected, rtol=1e-4, atol=1e-5)
    provenance['checkpointActorParitySamples'] = 8
    return write_release(checkpoint, weights, original_bytes, destination, target, provenance)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--run", type=Path)
    source.add_argument("--frozen-manifest", type=Path, help="Immutable, hash-checked evaluated actor selection.")
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--checkpoint", type=Path, help="Explicit frozen evaluation checkpoint with a matching provenance manifest.")
    parser.add_argument("--target-rules-version", help="An explicit compatible transfer; original training rules remain recorded.")
    parser.add_argument("--evaluation-receipt", type=Path, help="Completed target-rules evaluation required for a rules transfer.")
    parser.add_argument("--sampling-mode", choices=('Policy', 'Easy', 'Medium', 'Hard'), default='Hard')
    args = parser.parse_args()
    if args.frozen_manifest:
        if args.checkpoint: parser.error('--checkpoint belongs to --run; use the frozen manifest selection instead.')
        result = export_frozen(args.frozen_manifest.resolve(), args.destination.resolve(), args.target_rules_version, args.evaluation_receipt, args.sampling_mode)
    else:
        if args.target_rules_version or args.evaluation_receipt: parser.error('Rules transfers require --frozen-manifest.')
        result = export(args.run.resolve(), args.destination.resolve(), args.checkpoint.resolve() if args.checkpoint else None)
    print(json.dumps(result, indent=2))
