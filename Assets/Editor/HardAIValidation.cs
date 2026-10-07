using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

// Run regression checks in the already-open, licensed Editor. This never starts AI matches.
[InitializeOnLoad]
public static class HardAIValidation
{
    private const string ResultPathKey = "BlockNations.HardAI.ValidationResultPath";
    static HardAIValidation() => TestRunnerApi.RegisterTestCallback(new Results());

    [MenuItem("Tools/Block Nations/Validate Hard AI/Edit Mode")]
    public static void RunEditMode() => Run(TestMode.EditMode,
        new[] { "HardTacticianPolicyTests", "MultiplayerScrollViewTests", "UITKResponsiveSizeTierControllerTests" });

    [MenuItem("Tools/Block Nations/Validate Hard AI/Play Mode")]
    public static void RunPlayMode() => Run(TestMode.PlayMode, new[] { "AdjacentEmptyEnemyCityCaptureTests" });

    private static void Run(TestMode mode, string[] groups)
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Run validation while the Editor is idle and compilation is complete.");
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/Validation/HardAI", DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ")));
        Directory.CreateDirectory(directory);
        SessionState.SetString(ResultPathKey, Path.Combine(directory, mode + "-results.xml"));
        TestRunnerApi api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.Execute(new ExecutionSettings(new Filter { testMode = mode, groupNames = groups }));
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
            Debug.Log($"[Hard AI validation] {result.TestStatus}: passed={result.PassCount}, failed={result.FailCount}; results={path}");
        }
    }
}
