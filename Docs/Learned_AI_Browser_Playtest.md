# Learned AI browser playtest

This prepares a 7×7 single-player game with Easy, Medium and Hard learned-AI presets. It is a technical playtest candidate. The public Cloudflare release remains the previously reviewed local-AI build until the owner returns to review the trained opponent.

## Training and release are separate

The Mac continues its existing four-arena C# simulation and Python PPO learner. The optional Unity inspector only reads spectator data; closing it does not pause training. Keep it closed when nobody is watching to avoid rendering overhead. The shared 20 GB storage budget includes all saved runs, with a final checkpoint reserve; bounded retention can keep usage below the cap indefinitely.

The browser receives a frozen actor export, not the trainer, optimizer, checkpoint history, local file paths or human match records. Cloudflare serves static files. Unity Inference Engine 2.6.1 evaluates the model on the player's device using the CPU backend, which compiles to WebAssembly for this build. No Codex sign-in, OpenAI API key or remote model request is involved.

The initial frozen candidate is `BlockNationsSeatV2-4139990`: 1,803,075 ONNX bytes, schema 2, rules `blocknations-simulation-v3`. ONNX is a portable model format containing learned parameters and their computation graph. The manifest records the source checkpoint hash, model hash, compatible board/rules/schema and probability output. The release asset explicitly references its models.

The model is bundled in Unity's data asset for this first release. Players download it with the game. Browser caching can avoid repeated transfers, but clearing site data, cache eviction or a changed bundle can require another download. Separate model loading/caching is future work. Aim for a model under 5 MB and measure actual phone memory/latency before increasing it; this is our product budget rather than a hosting limit.

## Initial difficulty and variety

All three presets initially use the same frozen weights. Easy samples more broadly; Medium follows preferred actions more closely; Hard concentrates on the most likely actions. Ties still vary. Sampling uses only legal choices, and difficulty never changes information access or inference work.

These names are experimental presets, not measured strength bands. A narrower distribution is not proof of better play. Before promotion, compare frozen candidates with reproducible seeds and swapped starting positions, inspect human games and verify that the resulting opponents are distinguishable and enjoyable. The owner must authorize a watched comparison tournament before it starts. Ordinary training self-play remains authorized continuously.

The release catalog can later reference different evaluated checkpoints for each difficulty without changing gameplay input or observation rules. Preserve varied choices among similarly useful moves. Do not add unit-specific counters simply to beat the current meta.

## Information boundary

Both starting city locations are public in the current game. The learned policy receives those locations, its own gold/resources and units, visible enemy unit types/health/positions, explored/visible tiles and seat-legal actions. It does not receive enemy gold, enemy recruitment/action counters, hidden enemy units or persistent enemy identities. It currently has no recurrent memory for estimating hidden enemy resources/history.

The shared simulation holds the full world to resolve moves and terminal outcomes. Its seat projection feeds the policy. Hidden occupancy must not change the action mask: a move may be attempted into fog and then stop on surprise. Enemy private state and hidden blockers are covered by focused invariance checks for both seats and both stages of the policy decision.

The Unity runtime grants income and resets resources at the turn boundary through the shared rules adapter, reobserves after every command and validates commands before applying them. Its command safeguard depends on the acting seat's own resources. Inference failures appear in the gameplay HUD rather than silently substituting an older opponent.

## Board and save compatibility

New single-player games default to 7×7; the larger-map choices are hidden there. Compact games open with the whole board centered between the HUD and controls; pan/zoom and pending camera restores remain available. PBp board settings and supported 11×11/15×15 saves retain their existing meanings. Missing legacy map presets still resolve to the original small board. Old Normal, Rider Focus and Hard Tactician saves retain their opponents, and those controls remain available for development comparisons. The retired serialized opponent value 3 stays reserved; learned variants use values 4–6.

## Export and build

Use the configured trainer environment, whose Python has PyTorch, ONNX and NumPy:

```sh
/Users/Jo/.local/share/blocknations-ml/venv/bin/python Tools/Training/export_playtest_policy.py \
  --run '<existing 7x7 run directory>' \
  --destination Assets/Resources/LearnedAI
```

The export reads an immutable numbered checkpoint/ONNX pair. It exposes the actor probabilities, removes the training-time random action sampling/output metadata and verifies parity against the original actor. It does not modify or resume training.

Run `LearnedPolicyReleaseBuilder.Build` in an isolated Unity validation project to explicitly import/update model references. With `BLOCKNATIONS_POLICY_VALIDATION_DATA` pointing at reference fixtures, it also checks the actual Unity CPU backend. The browser builder rejects missing references or model/manifest hash mismatches.

```sh
python3 Tools/WebRelease/build.py --output Build/LearnedAI7Web
python3 Tools/WebRelease/audit.py Build/LearnedAI7Web
```

Only the generated browser artifact can be deployed. The isolated snapshot excludes ML-Agents, training scenes/models/code, Unity AI Assistant and PBp credentials. Direct Inference Engine remains because the game needs local inference. Release metadata identifies the frozen policy.

The reviewed candidate is preserved at `Build/LearnedAI7Web`, separately from the normal deployment output. Point an isolated static-assets preview at that directory for inspection; the hosted preview also exercises tester admission. Publishing still requires selecting the reviewed artifact deliberately.

## Validation and acceptance

On 2026-10-09:

- Export parity: 24 masked input fixtures, maximum actor probability difference zero.
- Actual Unity CPU parity: eight fixtures, maximum probability difference `5.364418E-07`.
- Direct C# information-boundary harness: 26 checks passed.
- Focused Unity EditMode tests: 29 passed, covering private-information invariance, hidden blockers, legal/seeded sampling, board compatibility, model references and serialized opponents.

The export audit found no credentials in compressed or decompressed game files, including comparisons against two PBp credentials and five local authentication values. Its signature checks respect the boundaries of IL2CPP version 39 managed literals, preventing adjacent language codes/identifiers from being mistaken for one key; unknown metadata retains the raw scan. Fourteen release-tool tests passed, including synthetic credential rejection within/outside managed literals and independent known-value checks. Actual browser gameplay, artifact sizes and physical phone acceptance are recorded separately. Training throughput or changing self-play Elo does not establish browser performance or human playing strength.

The same frozen artifact was then exercised in an isolated local static-assets preview, independently of the hosted tester-admission configuration:

- Desktop Chromium at 1280×720: Easy, Medium and Hard selected correctly, started 7×7 games and completed local AI turns. Human recruitment/movement, menu return and full-page reload/Continue were exercised. The human's gold remained on the HUD during the AI turn.
- An isolated synthetic legacy save continued as 11×11 with Hard Tactician; the new-game default did not reinterpret its board or opponent.
- Emulated mobile Chromium at 390×844, device scale factor 2 and touch input: all three difficulty buttons and Start Game fit in the setup pane; the whole board and Menu/Next fit during gameplay. Hard completed a turn and saved the correct 7×7 opponent and round.
- Browser resource requests in these sessions remained on the local preview origin. No external model/API requests or learned-policy runtime failures were observed. The headless browser still emitted graphics/audio warnings and a missing-favicon message; those are separate from inference acceptance.
- The generated export contains seven files totaling 30.76 MiB; its largest file is 17.14 MiB. Its source digest matches the validated Assets, Packages and ProjectSettings. The 1.8 MB model is included in the compressed Unity data file.

Local receipts and screenshots are under ignored `Logs/LearnedAI/` and `output/playwright/`; they contain synthetic browser playtests rather than private player records. Physical iPhone/Android timing, Safari acceptance, completed-match strength and separation among difficulty bands remain unverified. The candidate has not been published to Cloudflare or entered into a comparison tournament. The training viewer and owned preview/browser processes are closed after inspection so only the authorized training workload needs to continue.
