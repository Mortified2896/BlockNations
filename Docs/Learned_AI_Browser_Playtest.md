# Learned AI browser playtest

The owner explicitly requested publishing the three-unit game and current best evaluated AI on 2026-10-11. The release starts new 7×7 Play vs AI games with Warrior, Rider and Archer, Archer vision two and its existing shoot-then-move behavior. Its public menu has one frozen opponent and no AI selection. Development presets and serialized opponent values remain available for historical saves and comparisons. The earlier publication question is superseded by this direct authorization; the deployment receipt below identifies which artifact actually reached each host.

The earlier v3/v4 candidate details and acceptance below are historical. The new release uses the explicit `blocknations-simulation-v5-core3` target rules and completed balanced evaluation to select the actor. If the strongest actor was trained under an earlier compatible profile, its original training rules and the evaluated transfer are preserved in the release manifest; it is never relabelled as newly trained under core3. Runtime and build validation check the target board, observation schema, legal-action schema and rules.

The public runtime uses fixed `Hard` sampling. A matched Policy/Medium/Hard comparison on the preserved parent favored Hard; the selected newer actor was subsequently tested against the fixed references and parent using Hard. Its Medium setting has not received the same comparison. These settings remain development tools; the public menu offers one opponent. Sampling allows variation among retained choices, but the tests do not establish diverse tactics or human-level strength. Training continues independently; the published actor changes only through a deliberate export and authorized release.

## Training and release are separate

The Mac continues its existing eight-arena C# simulation and Python PPO learner. The optional Unity inspector only reads spectator data; closing it does not pause training. Keep it closed when nobody is watching to avoid rendering overhead. The shared 20 GB storage budget includes all saved runs, with a final checkpoint reserve; bounded retention can keep usage below the cap indefinitely.

The browser receives a frozen actor export, not the trainer, optimizer, checkpoint history, local file paths or human match records. Cloudflare serves static files. Unity Inference Engine 2.6.1 evaluates the model on the player's device using the CPU backend, which compiles to WebAssembly for this build. No Codex sign-in, OpenAI API key or remote model request is involved.

The selected frozen actor is now `BlockNationsSeatV2-163463523`: 1,803,075 ONNX bytes, schema 2, trained and evaluated under `blocknations-simulation-v5-core3`. Its checkpoint hash is `d22081cb418f604061f3cc931c07a413ad6719e1f9b17bd4fc284949e96716ba`; its probability-export hash is `97ead290b05ac15097dd7b957efeb67fcec2693a561b91798184ece8b9989645`. The manifest records the matching completed reference evaluation and the replicated direct-incumbent selection results. The [automatic evaluation correction](Automatic_AI_Evaluation.md) explains why the older reference-score gate had retained an actor that newer policies could beat directly. ONNX contains learned parameters and their computation graph; the existing release asset references the updated model through the same asset GUID.

The new actor beat the previous 33,749,992-step champion in 256/256 Hard games and 172/256 ordinary Policy games across two seed packs with balanced colours and starting roles, without interruptions. It scored 642/704 external-reference wins against the old actor's 659/704; that tradeoff remains diagnostic rather than overriding the direct result. The model export passed eight checkpoint-actor parity samples and 24 conversion samples with zero conversion error. Sixteen actual Unity CPU fixtures had maximum probability error `1.084805E-05`; all four graphics-enabled private gameplay flow tests passed, including Easy/Medium/Hard local inference, rematches and menu return. The owner retained the setup menu after reconsidering direct launch; this model refresh changes neither the menu flow nor fog/visibility rules. No no-fog option is added by this release.

Historically, the 33,749,992-step actor scored 659/704 external-reference wins against its parent's 643/704 and beat that parent in 256/256 Hard and 155/256 Policy games. Those October 11 results and publication receipts below describe the previous release. Repeated deterministic Hard lines are dependent; none of these counts establishes human difficulty or universal superiority against every strategy.

The model is bundled in Unity's data asset for this first release. Players download it with the game. Browser caching can avoid repeated transfers, but clearing site data, cache eviction or a changed bundle can require another download. Separate model loading/caching is future work. Aim for a model under 5 MB and measure actual phone memory/latency before increasing it; this is our product budget rather than a hosting limit.

## Historical initial difficulty and variety

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

Exports require a recorded compatible `checkpoint-contract.json` and a step newer than its rules/opening boundary. To export a deliberately selected evaluated candidate, add `--checkpoint '<frozen checkpoint.pt>'`; its adjacent provenance manifest and content hash must agree. Old transferred weights cannot be relabelled merely because a live arena now runs new rules.

For an immutable evaluated parent whose original training rules differ from the target release, use the explicit frozen-actor path:

```sh
/Users/Jo/.local/share/blocknations-ml/venv/bin/python Tools/Training/export_playtest_policy.py \
  --frozen-manifest '<frozen evaluated actor manifest.json>' \
  --target-rules-version blocknations-simulation-v5-core3 \
  --evaluation-receipt '<completed matching core-three suite.json>' \
  --sampling-mode Hard \
  --destination Assets/Resources/LearnedAI
```

This verifies checkpoint/configuration/ONNX content hashes, finite actor tensors, PT-to-ONNX probability parity, target-rules transfer permission and the evaluated sampling setting before creating release files. It copies no optimizer, private replay or local training path into the browser manifest.

Run `LearnedPolicyReleaseBuilder.Build` in an isolated Unity validation project to explicitly import/update model references. With `BLOCKNATIONS_POLICY_VALIDATION_DATA` pointing at reference fixtures, it also checks the actual Unity CPU backend. The browser builder rejects missing references or model/manifest hash mismatches.

```sh
python3 Tools/WebRelease/build.py --multiplayer --learned-ai --output Build/CoreThreeWeb
python3 Tools/WebRelease/audit.py Build/CoreThreeWeb
```

Only the generated browser artifact can be deployed. The isolated snapshot excludes ML-Agents, training scenes/models/code, Unity AI Assistant and PBp credentials. Direct Inference Engine remains because the game needs local inference. Release metadata identifies the frozen policy.

The published best-actor artifact is preserved at `Build/CoreThreeBestWeb`, separately from normal deployment output and the earlier parent preview at `Build/CoreThreeWeb`. It keeps the authenticated multiplayer application and the frozen three-unit single-player opponent. `Build/LearnedAI7Web` is historical. Always deploy the deliberately selected directory; do not allow another task's generated `Build/WebRelease` to replace it.

## Validation and acceptance

### Published best three-unit opponent, 2026-10-11

The owner's direct live-publication request authorized this selected release. `blocknations.moneymattersmedia.com` received worker version `5e97d797-0257-4348-b679-1ed79e3daa0b`, at 100%, with actor `BlockNationsSeatV2-33749992` and fixed Hard sampling. Its game-source digest is `1a765352da01b81bca4143123fe2d05214054639e6c5e43c240b6a5097b3dc2b`. The previous public worker `6d235add-976d-4e8a-88f9-713810c075e8` and earlier actor remain recoverable. Future training checkpoints do not automatically replace this release.

The actor passed eight checkpoint-to-export parity fixtures, 24 conversion fixtures and 24 actual Unity CPU fixtures (maximum probability difference `1.198053E-05`). Five exporter tests, 17 build/audit tests and 56 account/multiplayer worker tests passed. The credential audit inspected the compressed/decompressed artifact against four distinct configured deployment/app secrets and five local authentication values; it found no matches. The browser export contains 13 files totaling 30.81 MiB, largest 17.14 MiB, including the 1.8 MB actor and no trainer, optimizer or private human recordings.

Authenticated inspection of the actual public host confirmed the new data/Wasm filenames, a standard 7×7 setup with no opponent selector, first-turn gold two for the human first player and exactly Warrior/Rider/Archer recruitment at two gold each. Archer recruitment showed health 2/2 and vision two. The learned opponent completed a turn and returned control for round two with no console errors; human Archer movement also worked. The human's own gold remained on the HUD during the AI turn.

Anonymous release metadata, loader bytes and multiplayer lobby API remain 403 with `private, no-store`; the intentional public PWA manifest remains available and matches the artifact. Existing access and multiplayer services retained their deployed versions and bindings. No DNS, tester admission, OAuth credentials or multiplayer-store changes were made.

The four-arena 7×7 core-three learner continued throughout export, build and publication, with healthy checkpoint/export pairs and approximately 4.0 GB used of the shared 20 GB cap. The independent seed-43 continuation completed; seed 44 continues under the unchanged controlled recipe. Newer weights are assessed separately before promotion. Starting-role balance, Archer counterplay and physical phone performance still require their respective evidence; publication of the selected playtest does not complete the general training goal.

Ignored receipts and screenshots are under `Logs/Validation/CoreThreeBestWeb-20261011/` and `output/playwright/core-three-best/`.

### Three-unit release candidate, 2026-10-11

The current candidate passed 81 focused Unity EditMode checks, 24 actual Unity CPU probability fixtures (maximum difference `8.523464E-06`), five exporter checks, 17 web-release checks, 17 supervisor checks and 56 web-worker tests (including the separately merged account/menu/lobby changes). The first accepted WebGL artifact has 13 files totaling 30.81 MiB; the largest is 17.14 MiB. Its game-source digest is `a10d74209ecfe2b68cd954547165f4de6a77de52cc4dfc6765a76d380ce3e648`. A compressed/decompressed credential audit found zero matches, including checks against five configured private credential values. Private credentials and human match records are absent from the export.

Desktop Chromium at 1280×800 and touch-enabled mobile Chromium at 390×664 exercised the actual account-gated artifact with synthetic local tester sessions. Both showed one 7×7 setup without an AI selector, offered exactly Warrior/Rider/Archer at recruitment and completed a local learned-AI turn. Desktop Archer recruitment expanded visibility as expected, and a full-page reload/Continue restored the unfinished second-round match. Mobile Rider recruitment and Next worked through touch input. Browser resource requests remained on the local origin, with no inference failures or external model requests. Headless graphics/audio warnings are separate from policy acceptance. This is browser emulation, not a physical iPhone/Safari or Android latency benchmark.

The single public opponent uses the selected 32,730,121-step parent with evaluated `Hard` sampling. A matched supplementary test scored Policy 134/192, Medium 141/192 and Hard 159/192 against the same fair mixed, Warrior and Rider references, seeds and balanced roles. These are reference-opponent results, not human ratings or proof of solved first-player balance. The existing learned difficulty controls remain private development tools rather than three public choices.

Source checks and synthetic browser screenshots are preserved under ignored `Logs/Validation/CoreThreeWeb-20261011/` and `output/playwright/`. Hosted deployment and authenticated acceptance are recorded separately below; local tests do not prove a host was updated. The prior public artifact and frozen weights remain recoverable.

The first candidate reached the authenticated Cloudflare preview as worker version `a69588f3-27f9-48a8-bfa9-48c49b793e66`. Hosted inspection confirmed the one-opponent setup, three recruitment choices, Warrior health 3/3 and an AI turn returning control in round two with no console errors. A separate merged multiplayer/account change removes the duplicate footer entry; the final candidate was rebuilt against that source with digest `03098e39d264cc629e864f50131fc54e5324fa51c76cd51bf860cde1a7bb68d3`, the same actor and all five known-credential checks passing again.

The parent artifact reached [the authenticated preview](https://blocknations-web-preview.johannes-gaebler.workers.dev/) as worker version `6182cfa5-5275-4905-b513-479a960ed424`. Authenticated inspection verified its exact data/Wasm filenames, restored the learned match after reload and completed another AI turn into round three without console errors. The refreshed shell has one Multiplayer menu entry and only Account in the footer. Anonymous game files and multiplayer APIs remain 403; the public PWA/install shell and sign-in lobby remain accessible. At that earlier check the public site still offered Normal/Rider Focus/Hard. The later direct publication request resolved the review question and produced the best-actor public release recorded above. No access-store, multiplayer-store or DNS changes were part of the parent preview. Owned local test browsers and the synthetic server were closed; training continued independently.

### Historical initial candidate, 2026-10-09

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
