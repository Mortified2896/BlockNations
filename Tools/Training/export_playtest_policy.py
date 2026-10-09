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
    random = np.random.default_rng(seed)
    largest_error = 0.0
    for sample in range(24):
        observation = random.normal(0, 0.3, (1, 3120)).astype(np.float32)
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


def export(run: Path, destination: Path) -> dict:
    manifest = read_json(run / "run.json")
    arena = read_json(run / "arena-status.json")
    if manifest.get("boardSize") != 7 or arena.get("simulationVersion") != "blocknations-simulation-v3":
        raise ValueError("This first browser release requires a 7x7 checkpoint under rules v3.")
    checkpoint = latest_checkpoint(run)
    # Read immutable numbered files before training retention can remove them.
    weights = checkpoint.read_bytes()
    original_bytes = checkpoint.with_suffix(".onnx").read_bytes()
    source = onnx.load_model_from_string(original_bytes)
    converted = convert(source)
    verification = verify(source, converted)
    contents = converted.SerializeToString()
    destination.mkdir(parents=True, exist_ok=True)
    model = destination / "Compact7.onnx"
    model.write_bytes(contents)
    result = {
        "schema": 2, "boardSize": 7, "observationSize": 3120, "actionCount": 259,
        "rulesVersion": "blocknations-simulation-v3", "modelVersion": checkpoint.stem,
        "modelSha256": hashlib.sha256(contents).hexdigest(),
        "checkpointSha256": hashlib.sha256(weights).hexdigest(),
        "sourceOnnxSha256": hashlib.sha256(original_bytes).hexdigest(),
        "probabilityOutput": PROBABILITIES, "modelBytes": len(contents),
        "exportedUnixSeconds": int(time.time()), "verification": verification,
        "difficultyStatus": "experimental_sampling_presets",
    }
    (destination / "manifest.json").write_text(json.dumps(result, indent=2) + "\n")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--run", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(export(args.run.resolve(), args.destination.resolve()), indent=2))
