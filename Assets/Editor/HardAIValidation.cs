using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

// Run checks in the already-open, licensed Editor. No tournaments are started.
[InitializeOnLoad]
public static class HardAIValidation
{
    private const string ResultPathKey = "BlockNations.HardAI.ValidationResultPath";
    static HardAIValidation() => TestRunnerApi.RegisterTestCallback(new Results());

    [MenuItem("Tools/Block Nations/Validate Hard AI/Edit Mode")]
    public static void RunEditMode() => Run(TestMode.EditMode,
        new[] { "HardTacticianPolicyTests", "AIExternalActionDecisionTests", "LocalAIPlaytestSetupTests", "AIVsAIMatchHandoffTests", "MultiplayerScrollViewTests", "UITKResponsiveSizeTierControllerTests" });

    [MenuItem("Tools/Block Nations/Validate Hard AI/Play Mode")]
    public static void RunPlayMode() => Run(TestMode.PlayMode, new[] { "AdjacentEmptyEnemyCityCaptureTests" });

    [MenuItem("Tools/Block Nations/Luna Bridge/Verify one real Luna action")]
    public static void RunLiveLunaCheck()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Run the live check while the Editor is idle.");
        SessionState.SetBool("BlockNations.Luna.LiveCheck", true);
        Run(TestMode.PlayMode, null, new[] { "AdjacentEmptyEnemyCityCaptureTests.LiveLunaChoosesAndExecutesAWinningLegalAction" });
    }

    private static void Run(TestMode mode, string[] groups, string[] names = null)
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Run validation while the Editor is idle and compilation is complete.");
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/Validation/HardAI", DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ")));
        Directory.CreateDirectory(directory);
        SessionState.SetString(ResultPathKey, Path.Combine(directory, mode + "-results.xml"));
        TestRunnerApi api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.Execute(new ExecutionSettings(new Filter { testMode = mode, groupNames = groups, testNames = names }));
    }

    private sealed class Results : ICallbacks
    {
        public void RunStarted(ITestAdaptor tests) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            string path = SessionState.GetString(ResultPathKey, string.Empty);
            if (string.IsNullOrEmpty(path)) return;
            TestRunnerApi.SaveResultToFile(result, path);
            SessionState.EraseString(ResultPathKey);
            SessionState.EraseBool("BlockNations.Luna.LiveCheck");
            Debug.Log($"[Hard AI validation] {result.TestStatus}: passed={result.PassCount}, failed={result.FailCount}; results={path}");
        }
    }
}
