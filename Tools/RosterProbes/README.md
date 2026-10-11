# Frozen recruitment ablations

This development diagnostic holds the candidate's weights, reference opponent, shared C# rules and fair observations fixed. It restricts only the candidate's legal recruitment choices in evaluation matches. Compare an unrestricted control, individual units and combinations to investigate latent responses and counterplay. It never changes a learner's choices, rewards, game roster or published model.

The version-1 suite uses the same bounded case format as `Tools/Training/evaluate_suite.py`, adds `measurement: "frozen-policy-recruitment-ablation-v1"`, and requires an explicit `allowedRecruits` field in every case: `null` for the unrestricted control, or a unique nonempty list of public recruit type names. The adapter validates names against the actual worker's enabled roster. Scout remains disabled under core-three rules. The reference remains unrestricted unless its separate recorded definition specifies otherwise.

```sh
/Users/Jo/.local/share/blocknations-ml/venv/bin/python Tools/RosterProbes/evaluate_rosters.py \
  --candidate-manifest '<training-root>/runs/<run>/frozen-evaluations/<milestone>/manifest.json' \
  --suite '<training-root>/benchmarks/<experiment>/definition.json' \
  --worker /Users/Jo/GitHub/BlockNations/Build/TrainingWorkerRuleProfiles/BlockNations.TrainingWorker \
  --destination '<training-root>/benchmarks/<experiment>/results' \
  --root '/Users/Jo/Library/Application Support/BlockNations/Training'
```

The destination must be new and canonical, within the marked benchmark root. The runner includes the restrictions and adapter hash in its own suite identity, pins frozen weights/configurations and evaluator sources, verifies the compiled worker, and checks the shared 20 GB budget and reserves before and after each case. Complete case records remain on disk if a later verification fails. It does not merge or overwrite partial receipts. Ctrl+C or terminating the CLI closes the active worker and records an interrupted receipt. Use one serial runner, with a single Torch thread, alongside existing studies.

`gamesPerCell` is the number per physical-seat/starting-role combination. Four combinations are evaluated, so a case with `gamesPerCell: 32` produces **128 actual matches**, split equally between the candidate playing first and second. The existing evaluator reports captures, interruptions, recruitment counts and both starting roles separately. Turn limits are not counted as completed draws. Repeated deterministic lines limit independence even with additional game counts and seeds.

Restrictions can expose behavior that the unrestricted policy rarely samples. Failure can reflect insufficient training or mask distribution shift; it does not prove that a unit is intrinsically unviable. A win using a unit does not alone establish useful ranged attacks, vision or optimal play. Check complete games and recorded actions, not just purchase counts. Evaluation positions and restrictions must not be fed back as compulsory purchases, quotas or named-unit bonuses.

```sh
BLOCKNATIONS_TEST_WORKER=/Users/Jo/GitHub/BlockNations/Build/TrainingWorkerRuleProfiles/BlockNations.TrainingWorker \
  /Users/Jo/.local/share/blocknations-ml/venv/bin/python -m unittest discover \
  -s Tools/RosterProbes -p 'test_*.py'
```

Checks cover unchanged observations and non-recruit actions, original mask preservation, disabled types, no invented legal actions, forbidden delegate choices, condition identity, input changes, protected receipts and storage reserves. The actual-worker check runs 32 balanced matches and verifies that only the candidate's recruitment is restricted and that the shared engine rejects no actions. Its small randomly initialized actor is a runner check, not a strength benchmark.
