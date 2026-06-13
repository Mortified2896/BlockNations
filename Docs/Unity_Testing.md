# Unity Test Runner Notes

Block Nations uses Unity 6000.4.0f1. Local command-line test runs should use the Unity Editor binary installed by Unity Hub.

## Hermes/macOS CLI licensing caveat

When running Unity batchmode from Hermes on Jo's macOS machine, set `HOME=/Users/Jo` so Unity can see the normal Unity Hub / Personal license state.

Hermes profile shells may otherwise use a profile-local home such as:

```text
/Users/Jo/.hermes/profiles/block-nations/home
```

That profile-local home can cause Unity CLI licensing failures even when Unity Hub and the Editor are activated normally.

## PlayMode test command

Do not pass `-quit` with `-runTests` in this setup; Unity exits after the test run. Including `-quit` can make Unity quit after import before writing test results.

Example filtered PlayMode run:

```bash
cd /Users/Jo/GitHub/BlockNations

UNITY_BIN="/Applications/Unity/Hub/Editor/6000.4.0f1/Unity.app/Contents/MacOS/Unity"
OUT_DIR="/Users/Jo/GitHub/BlockNations/Logs/HermesValidation"
mkdir -p "$OUT_DIR"
rm -f "$OUT_DIR/playmode-results.xml" "$OUT_DIR/unity-test.log"

HOME=/Users/Jo "$UNITY_BIN" -batchmode -nographics \
  -projectPath "/Users/Jo/GitHub/BlockNations" \
  -runTests \
  -testPlatform PlayMode \
  -testResults "$OUT_DIR/playmode-results.xml" \
  -testFilter "AdjacentEmptyEnemyCityCaptureTests" \
  -logFile "$OUT_DIR/unity-test.log"
```

Expected success indicators:

- Unity exits with code `0`.
- `Logs/HermesValidation/playmode-results.xml` is created.
- The XML root reports `result="Passed"` with `failed="0"`.

## Notes

- `Logs/HermesValidation/` is for local validation output and should remain untracked.
- If `dotnet build --no-restore` reports a missing `Temp/obj/.../project.assets.json`, run `dotnet restore` for the relevant generated Unity `.csproj` first.
