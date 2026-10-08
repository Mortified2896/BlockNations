using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

// A narrow command inbox lets automation use compiled Unity APIs in the open
// licensed Editor. It accepts only these development operations, never code.
[InitializeOnLoad]
public static class TrainingEditorCommands
{
    private const string Inbox = "UserSettings/LocalTraining-command.json";
    private const string Result = "UserSettings/LocalTraining-command-result.json";
    private const string TestResultKey = "BlockNations.ML.TestResult";
    private static double nextCheck;
    static TrainingEditorCommands()
    {
        EditorApplication.update += Poll;
        TestRunnerApi.RegisterTestCallback(new Results());
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextCheck || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Inbox)) return;
        Command request = JsonUtility.FromJson<Command>(File.ReadAllText(Inbox));
        File.Delete(Inbox);
        try
        {
            switch (request.command)
            {
                case "create": TrainingSceneBuilder.Create(); break;
                case "openControls": LocalTrainingWindow.Open(); break;
                case "start": LocalTrainingWindow.Launch(request.runId, request.resume, request.hours, request.budgetGB,
                    request.seed, request.curriculum, request.maxSteps, request.checkpointInterval, request.playerPath); break;
                case "stop": LocalTrainingWindow.RequestStop(); break;
                case "exitPlay": EditorApplication.isPlaying = false; break;
                case "playModel": LocalTrainingWindow.PlayModel(request.modelPath, request.human); break;
                case "editTests": RunTests(TestMode.EditMode); break;
                case "playTests": RunTests(TestMode.PlayMode); break;
                default: throw new ArgumentException("Unknown training development command.");
            }
            File.WriteAllText(Result, JsonUtility.ToJson(new Reply { command = request.command, ok = true, message = "Dispatched using Unity Editor APIs." }, true));
        }
        catch (Exception error)
        {
            File.WriteAllText(Result, JsonUtility.ToJson(new Reply { command = request.command, ok = false, message = error.ToString() }, true));
            Debug.LogException(error);
        }
    }

    private static void RunTests(TestMode mode)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Leave Play Mode before validation.");
        string directory = Path.GetFullPath("Logs/Validation/MLTraining/" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ"));
        Directory.CreateDirectory(directory);
        SessionState.SetString(TestResultKey, Path.Combine(directory, mode + "-results.xml"));
        string[] groups = mode == TestMode.EditMode ? new[] { "LearnedActionSchemaTests", "TrainingProgressHistoryTests", "HardTacticianPolicyTests",
            "LocalAIOpponentCompatibilityTests", "AIVsAIMatchHandoffTests" } : new[] { "ExternalTrainingMatchTests", "AdjacentEmptyEnemyCityCaptureTests" };
        TestRunnerApi api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.Execute(new ExecutionSettings(new Filter { testMode = mode, groupNames = groups }));
    }

    [Serializable] private sealed class Command
    {
        public string command, runId, modelPath, playerPath;
        public bool resume, human, curriculum = true;
        public double hours = 8, budgetGB = 20;
        public int seed = 42, maxSteps = 1_000_000, checkpointInterval = 5000;
    }
    [Serializable] private sealed class Reply { public string command, message; public bool ok; }
    private sealed class Results : ICallbacks
    {
        public void RunStarted(ITestAdaptor tests) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor test) { }
        public void RunFinished(ITestResultAdaptor test)
        {
            string path = SessionState.GetString(TestResultKey, "");
            if (string.IsNullOrEmpty(path)) return;
            TestRunnerApi.SaveResultToFile(test, path);
            SessionState.EraseString(TestResultKey);
            Debug.Log($"[ML validation] {test.TestStatus}: {test.PassCount} passed, {test.FailCount} failed; {path}");
        }
    }
}
