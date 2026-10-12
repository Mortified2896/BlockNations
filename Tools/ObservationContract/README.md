# Opt-in observation contract v3

This development tool prepares the next public-information contract without activating it in the live learner, shipping game or private playtest. It compiles the same rules and fair observation source as Unity; its checks use synthetic positions, not private human records. No trainer or viewer is started.

The existing 3,120-float schema-v2 vector remains an exact prefix. The suffix adds 121 canonical board channels and 16 stable recruit-slot channels for the public `AttackEndsMovement` capability, one flag for the observing seat's public first/second starting role, and a schema-v3 marker. The total is **3,259 floats**. The prefix retains its own v2 marker. Observation version 3 and action version 2 are deliberately separate: all 259 source, move, attack, recruit and EndTurn indices keep their meanings. Disabled Scout definitions remain represented and cannot become recruitable through this adapter.

`SimulationObservationSource.Context.PublicFirstSeat` supplies the explicit `MatchState.FirstSeat` metadata. An unspecified starting seat is rejected rather than inferred from current turn, viewer or unit ownership. The new encoder also rejects hidden enemy units and live opponent action counters. Public capability values are not strategy bonuses, unit quotas or inferred enemy wallets. No observation history or recurrent memory has been added; enemy identity tracking and economic inference remain future work derived from legitimate observations.

## Validation and preparation

Use a .NET 8 SDK. From the repository root:

```sh
dotnet run --project Tools/ObservationContract/BlockNations.ObservationContract.csproj --configuration Release -- --self-test
dotnet run --project Tools/ObservationContract/BlockNations.ObservationContract.csproj --configuration Release -- --fixtures Build/ObservationV3-fixtures.json
```

Create `Build/` if absent; the fixture file must be new. Six C# check groups exercise all supported board sizes, both seats/starting roles, unchanged prefix/action mappings, rotation including even-sized padding, public capabilities, hidden blockers/resources/identities, neutral visible-enemy counters, actual post-attack legality and rejection guards. The fixture writer emits 100 fair root/conditional decisions for 5/6/7/9/11 boards; it refuses an existing file or output above 8 MiB.

The Python tool requires the existing pinned local trainer environment with Torch, ML-Agents, NumPy and ONNX. It supports only the audited unnormalized, memory-free, 128×2 feed-forward PPO checkpoint. Use that environment's Python executable:

```sh
python -m unittest discover -s Tools/ObservationContract -v
python Tools/ObservationContract/migrate_checkpoint.py \
  --source-manifest /path/to/frozen-evaluations/milestone/manifest.json \
  --fixtures Build/ObservationV3-fixtures.json \
  --destination Build/ObservationV3-prepared
```

The source must be an immutable numbered v2 checkpoint under the current core-three 7×7 rules/opening. Checkpoint and recipe hashes must match its frozen manifest. Migration appends zero columns to the actor and critic input layers and their associated Adam first/second moments. All existing values, counters, optimizer settings and the global step are retained; source weights are never changed. A resumed synthetic Adam step separately verifies that the new features can receive gradients. That smoke is an optimizer compatibility check, not a learning experiment or evidence of stronger play.

Before creating the destination, the tool strictly loads the old/new actor and critic and compares their outputs on the C# fixtures, verifies top actions/masks, exports a compact probability-only ONNX and compares its dynamic-batch output against Torch. Appended zero weights mathematically preserve the initial function; receipts state numerical tolerances because different matrix widths/backends can change floating-point reductions. Unsupported normalizers, recurrent networks, dimensions, optimizer associations, non-finite tensors and stale inputs fail explicitly. The destination and files are exclusive; partial outcomes remain for inspection and are never overwritten.

The prepared folder contains a new `BlockNationsSeatV3-<step>.pt`, `trainer.json`, `CandidateV3.onnx` and a `manifest.json` marked **`prepared_only`**. ONNX strips trace docstrings/private source paths and carries no optimizer state or demonstrations. It is not compatible with the current schema-v2 game loader. Export/parity success does not establish Unity/mobile inference acceptance, latency or new playing strength.

## Activation after the current experiment

Do not switch the running paired exploration trials or main supervisor. First finish their original six arms and evaluations. A subsequent separate recipe must explicitly provide a v3 worker protocol and environment spec, frozen-opponent adapters that project the unchanged prefix for v2 references, and v3 checkpoint/export/runtime gates. Resume from the prepared actor **and critic/optimizer**, retain the original parent, use matched v2 controls, both starting roles and held-out seeds, and replicate promising changes in at least three continuations. Never select a new actor merely because preparation or training completed. Saved-game and PBp formats need no change for this observation contract.

Prepared development artifacts normally live in ignored `Build/`; actual training runs and their checkpoint/evaluation artifacts remain subject to the shared 20 GB supervisor budget. No prepared export is automatically published or loaded by the private playtest.

The separately authorized [information training implementation](../../Docs/AI_Information_Training.md) now provides opt-in worker/environment/evaluator/export/runtime support and isolated comparisons. This preparation tool still does not switch the live learner or select a release. Observation v4 adds explicitly historical sightings as a separate subsequent treatment.
