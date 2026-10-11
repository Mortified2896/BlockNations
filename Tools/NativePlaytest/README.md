# Private Mac policy playtest

Build a separate arm64 Mac game with the existing Easy, Medium and Hard setup controls. It uses the explicitly frozen release in `Assets/Resources/LearnedAI`, the shared core-three rules and Unity Inference Engine's CPU backend. No trainer, Python process, Codex connection or remote inference service is needed to play. Main training continues independently.

```sh
python3 Tools/NativePlaytest/build.py \
  --snapshot /Users/Jo/.local/share/blocknations-private-policy/project \
  --output "$PWD/Build/PrivatePolicyPlaytest.app" \
  --cpu-fixtures /Users/Jo/GitHub/BlockNations/Logs/Validation/CoreThreeBestWeb-20261011/cpu-fixtures.json
```

Use a new output name for every build. The wrapper reuses the credential-free web snapshot preparation, removes ML-Agents and training assets/code, pins the model bytes and source digest, builds with the matching Unity Editor and audits the native bundle. It writes an intent receipt before starting Unity and a release receipt only after validation. Both, together with any failed build's app and log, are preserved. All native components must include arm64; Unity's universal launcher and frameworks are valid. The dedicated application identity and company/product settings isolate its preferences and saves. The source checkout's project settings are not changed. Public deployment is a separate operation; the script never deploys or promotes live training weights.

The existing single-player capability define hides multiplayer and development simulations. The public fixed-opponent define is omitted, so local presets remain selectable. All three use the same frozen weights with different legal-action sampling. Reference results establish their relative ordering, not human ratings or varied strategies. Current Hard still heavily prefers Riders. Each preset receives the same fair observations, and new games use 7x7 with the 2/3 opening. This ordinary-game route currently starts the human as Blue, moving first, including Play Again. The training playtest route has separate randomized roles; use balanced automated evaluation to compare both roles until this ordinary route supports that too.

These ordinary-game matches do not use the training arena's human demonstration recorder. They do not currently contribute demonstrations to learning. Existing private human data remains preserved; adding a reviewed bridge is follow-up work.

An optional CPU fixture file verifies Unity probabilities against frozen reference vectors during import. Build success and a credential scan do not prove the whole interactive flow; validate menu selection, recruitment, local AI turns, game-over explanation, Play Again and menu return in the actual app. Actual iPhone/Android performance remains a separate requirement.
