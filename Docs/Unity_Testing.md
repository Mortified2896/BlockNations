# Unity Test Runner Notes

Use the Unity Hub Editor matching `ProjectSettings/ProjectVersion.txt` (currently 6000.4.0f1). PlayMode gameplay tests and UITK Editor tests have different graphics requirements.

Last reviewed: 2026-10-07.

## Project and licensing

Do not start a batch run against this checkout while another Unity Editor instance is using it. Finish the run before opening the project interactively.

Run under the normal licensed macOS user environment. A Hermes profile shell may resolve its user home to `/Users/Jo/.hermes/profiles/block-nations/home`, which can prevent Unity from finding Jo's normal Hub/Personal license state. Check the execution user/environment if licensing fails rather than changing project files or resetting licenses.

Do not pass `-quit` with `-runTests` in this setup: the test runner exits when complete, and the extra flag can quit after import before results are written.

## PlayMode city capture test

This targeted gameplay test supports `-nographics`:

```bash
cd /Users/Jo/GitHub/BlockNations
BN_UNITY_BIN="/Applications/Unity/Hub/Editor/6000.4.0f1/Unity.app/Contents/MacOS/Unity"
BN_CHECK_OUT="$PWD/Logs/Validation/$(date +%Y%m%dT%H%M%S)"
mkdir -p "$BN_CHECK_OUT"

"$BN_UNITY_BIN" -batchmode -nographics \
  -projectPath "$PWD" \
  -runTests \
  -testPlatform PlayMode \
  -testFilter "AdjacentEmptyEnemyCityCaptureTests" \
  -testResults "$BN_CHECK_OUT/playmode-results.xml" \
  -logFile "$BN_CHECK_OUT/playmode.log"
```

The test exercises both acting seats and adjacent positions for capturing an empty enemy city. It is a regression check, not complete AI correctness/balance coverage.

## EditMode tests with graphics

`MultiplayerScrollViewTests` opens an EditorWindow for UITK layout. It requires a graphics device even for the parameterized helper cases because they share the same setup fixture. Do not use `-nographics` for this suite.

```bash
cd /Users/Jo/GitHub/BlockNations
BN_UNITY_BIN="/Applications/Unity/Hub/Editor/6000.4.0f1/Unity.app/Contents/MacOS/Unity"
BN_CHECK_OUT="$PWD/Logs/Validation/$(date +%Y%m%dT%H%M%S)"
mkdir -p "$BN_CHECK_OUT"

"$BN_UNITY_BIN" -batchmode \
  -projectPath "$PWD" \
  -runTests \
  -testPlatform EditMode \
  -testResults "$BN_CHECK_OUT/editmode-results.xml" \
  -logFile "$BN_CHECK_OUT/editmode.log"
```

The current 11 EditMode cases cover responsive size tiers and multiplayer scroll behavior. If graphical batch execution is unavailable, run them through the Editor Test Runner; failures during graphics initialization do not establish a gameplay regression.

## Interpret results

Successful validation requires exit code 0 and a fresh XML result with `result="Passed"` and `failed="0"`. Inspect the XML/log on failure; do not rely on a stale result file.

On 2026-10-07 at source revision `0d510b3`, the filtered PlayMode test passed and all 11 EditMode tests passed with graphics enabled. An earlier EditMode attempt with `-nographics` failed in fixture setup with "No graphic device is available to initialize the view." Local results are in ignored `Logs/RestartAudit/`.

`Logs/` and generated Unity project files are local validation artifacts and should remain untracked. A generated `.csproj`/dotnet build can help diagnose compilation but does not replace Unity import/runtime tests. If `dotnet build --no-restore` reports missing generated project assets, restore the relevant generated project first.

## Validation scope

- Documentation-only changes: check source claims, links, commands, and diff scope; do not rerun Unity solely for prose edits.
- AI changes: targeted tactical/legal-action/visibility checks plus existing evaluation tooling and device responsiveness.
- PBp changes: supported seats, claims, progression/resignation/elimination, compatibility, and independent POV/turn/transport state.
- Scene/prefab/UI behavior changes: add a focused manual checklist for the affected flow/device layouts; report which checks were actually executed.
- Menu screenshots: use `Assets/Editor/TakeScreenshotMenu.cs` in Play Mode with the Game view active/visible. Settle after switching panes, capture one pane into `Screenshots/`, and verify it before moving on.
