using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

[InitializeOnLoad]
public sealed class LocalTrainingWindow : EditorWindow
{
    private const string ActiveRunKey = "BlockNations.ML.ActiveRun";
    private const string RootKey = "BlockNations.ML.Root";
    private const string PythonKey = "BlockNations.ML.Python";
    private const string AwaitPlayKey = "BlockNations.ML.AwaitPlay";
    private const string AwaitStartedKey = "BlockNations.ML.AwaitStarted";
    private const string TrainingPlayKey = "BlockNations.ML.TrainingPlay";
    private static double nextPoll;
    private static string launchError;
    private static Process supervisor;
    private double hours = 8, budgetGB = 20;
    private int seed = 42;
    private bool curriculum = true;
    private bool useStandalone;
    private string playerPath = "";
    private string runId = "", modelPath = "";
    private Vector2 scroll;
    public static string RootPath => EditorPrefs.GetString(RootKey,
        Application.platform == RuntimePlatform.OSXEditor
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/BlockNations/Training")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockNations", "Training"));
    public static string PythonPath => EditorPrefs.GetString(PythonKey,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/blocknations-ml/venv/bin/python"));
    public static string ActiveRun => SessionState.GetString(ActiveRunKey, "");
    public static string RunDirectory => string.IsNullOrEmpty(ActiveRun) ? "" : Path.Combine(RootPath, "runs", ActiveRun);

    static LocalTrainingWindow()
    {
        EditorApplication.update += Poll;
        EditorApplication.quitting += RequestStop;
    }

    [MenuItem("Tools/Block Nations/Local ML Training/Open Controls")]
    public static void Open() => GetWindow<LocalTrainingWindow>("Local ML Training");

    private void OnEnable()
    {
        minSize = new Vector2(560, 580);
        if (string.IsNullOrEmpty(runId)) runId = ActiveRun;
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        bool active = IsRunActive();
        EditorGUILayout.LabelField("Local self-play", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Start runs successive matches automatically and shows the live board. Capture wins/losses train the policy. Round limits are recorded as interruptions. Comparison tournaments are separate.", MessageType.Info);
        using (new EditorGUI.DisabledScope(active))
        {
            string root = EditorGUILayout.TextField("Training storage", RootPath);
            string python = EditorGUILayout.TextField("Trainer Python", PythonPath);
            if (root != RootPath) EditorPrefs.SetString(RootKey, root);
            if (python != PythonPath) EditorPrefs.SetString(PythonKey, python);
            useStandalone = EditorGUILayout.Toggle("Use standalone training player", useStandalone);
            if (useStandalone) playerPath = EditorGUILayout.TextField("Training player (.app)", string.IsNullOrEmpty(playerPath) ? Path.GetFullPath("Build/LocalTraining.app") : playerPath);
        }
        hours = EditorGUILayout.DoubleField("Maximum hours", hours);
        budgetGB = EditorGUILayout.DoubleField("Total storage limit (GB)", budgetGB);
        seed = EditorGUILayout.IntField("Seed", seed);
        curriculum = EditorGUILayout.Toggle("Tactical curriculum + full games", curriculum);
        runId = EditorGUILayout.TextField("Run id (blank = new)", runId);
        EditorGUILayout.HelpBox("Default: 20 GB shared across all runs, 512 MB checkpoint reserve, 20 GB free disk guard. Five recent checkpoints per active behavior, pinned models protected, bounded trainer logs, videos/replays off. Plug in and keep the lid open; screen locking is supported.", MessageType.None);
        using (new EditorGUI.DisabledScope(active || EditorApplication.isPlaying || EditorApplication.isCompiling))
        {
            if (GUILayout.Button("Start Training")) Launch(runId, false, hours, budgetGB, seed, curriculum, player: useStandalone ? playerPath : null);
            if (GUILayout.Button("Resume Saved Run")) Launch(runId, true, hours, budgetGB, seed, curriculum, player: useStandalone ? playerPath : null);
        }
        using (new EditorGUI.DisabledScope(!active))
            if (GUILayout.Button("Stop and Save Checkpoint")) RequestStop();
        if (!string.IsNullOrEmpty(launchError)) EditorGUILayout.HelpBox(launchError, MessageType.Error);
        if (!string.IsNullOrEmpty(RunDirectory))
        {
            EditorGUILayout.LabelField("Current run", ActiveRun);
            SupervisorStatus status = Read<SupervisorStatus>(Path.Combine(RunDirectory, "supervisor-status.json"));
            if (status != null)
            {
                EditorGUILayout.LabelField("Trainer", status.state + (string.IsNullOrEmpty(status.stopReason) ? "" : " / " + status.stopReason));
                EditorGUILayout.LabelField("Time", TimeSpan.FromSeconds(status.elapsedSeconds).ToString(@"hh\:mm\:ss"));
                EditorGUILayout.LabelField("Storage", $"{status.usedBytes / 1e9:F3} / {status.budgetBytes / 1e9:F1} GB; free {status.freeBytes / 1e9:F1} GB");
                EditorGUILayout.LabelField("Saved artifacts", $"Checkpoints {status.checkpointCount}, exports {status.exportCount}");
                if (status.peakResidentBytes > 0) EditorGUILayout.LabelField("Peak trainer + player RAM", $"{status.peakResidentBytes / 1e9:F2} GB resident");
            }
            TrainingArena.ArenaStatus arena = Read<TrainingArena.ArenaStatus>(Path.Combine(RunDirectory, "arena-status.json"));
            if (arena != null)
            {
                EditorGUILayout.LabelField("Live matches", $"{arena.games} completed; captures {arena.captures}, interruptions {arena.interruptions}");
                EditorGUILayout.LabelField("Decisions", $"{arena.decisions} / {arena.decisionsPerSecond:F1} per second; actions {arena.actions}; rejected {arena.rejections}");
                EditorGUILayout.LabelField("Board", $"Round {arena.round}/{arena.roundLimit}, seat {arena.seat}, curriculum distance {arena.curriculumDistance}");
                if (!string.IsNullOrEmpty(arena.failure)) EditorGUILayout.HelpBox(arena.failure, MessageType.Error);
            }
            if (GUILayout.Button("Show Run Files")) EditorUtility.RevealInFinder(RunDirectory);
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Play a saved policy locally", EditorStyles.boldLabel);
        modelPath = EditorGUILayout.TextField("ONNX checkpoint", modelPath);
        if (GUILayout.Button("Choose ONNX")) modelPath = EditorUtility.OpenFilePanel("Choose a trained policy", RootPath, "onnx");
        using (new EditorGUI.DisabledScope(active || EditorApplication.isPlaying || string.IsNullOrEmpty(modelPath)))
            if (GUILayout.Button("Play Against This Model")) PlayModel(modelPath, true);
        EditorGUILayout.EndScrollView();
    }

    public static void Launch(string id, bool resume, double durationHours = 8, double storageGB = 20,
        int runSeed = 42, bool useCurriculum = true, int maxSteps = 1_000_000, int checkpointInterval = 5000, string player = null)
    {
        if (IsRunActive() || EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Finish the active run and leave Play Mode before starting another.");
        if (!File.Exists(PythonPath)) throw new FileNotFoundException("Install the scoped trainer environment described in Docs/ML_Training_MVP.md.", PythonPath);
        if (resume && string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Enter the saved run id to resume.");
        id = string.IsNullOrWhiteSpace(id) ? "mac-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ") : id.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^[A-Za-z0-9][A-Za-z0-9_-]{0,80}$"))
            throw new ArgumentException("Use letters, numbers, dash, or underscore for the run id.");
        if (durationHours <= 0 || storageGB <= 0.512) throw new ArgumentException("Use a positive duration and a budget above the checkpoint reserve.");
        string directory = Path.Combine(RootPath, "runs", id);
        int savedDistance = 2;
        if (resume)
        {
            RunManifest manifest = Read<RunManifest>(Path.Combine(directory, "run.json"));
            if (manifest == null || manifest.owner != "BlockNations.LocalTraining.v1" || manifest.schema != BlockNations.AI.LearnedActionSchema.Version)
                throw new InvalidOperationException("Resume requires an owned, compatible training run.");
            runSeed = manifest.seed;
            useCurriculum = manifest.curriculum;
            TrainingArena.ArenaStatus previous = Read<TrainingArena.ArenaStatus>(Path.Combine(directory, "arena-status.json"));
            if (previous != null && previous.curriculumDistance >= 2 && previous.curriculumDistance <= 8 && previous.curriculumDistance % 2 == 0)
                savedDistance = previous.curriculumDistance;
        }
        if (string.IsNullOrEmpty(player))
        {
            TrainingArena arena = TrainingSceneBuilder.Prepare(Path.Combine(directory, "arena-status.json"), runSeed, useCurriculum, requireTrainer: true);
            TrainingSceneBuilder.Set(arena, "initialCurriculumDistance", savedDistance);
        }
        else if (!Directory.Exists(player)) throw new DirectoryNotFoundException("Build the Mac training player first.");
        string script = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/Training/supervisor.py"));
        string[] arguments = { script, "--root", RootPath, "--run-id", id, "--hours", durationHours.ToString(CultureInfo.InvariantCulture),
            "--budget-gb", storageGB.ToString(CultureInfo.InvariantCulture), "--seed", runSeed.ToString(),
            "--max-steps", maxSteps.ToString(), "--checkpoint-interval", checkpointInterval.ToString() };
        string command = string.Join(" ", arguments.Select(Quote)) + (resume ? " --resume" : "");
        if (!useCurriculum) command += " --full-openings";
        if (!string.IsNullOrEmpty(player)) command += " --env " + Quote(Path.GetFullPath(player));
        launchError = null;
        supervisor = new Process { StartInfo = new ProcessStartInfo(PythonPath, command)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true } };
        supervisor.ErrorDataReceived += (_, data) => { if (!string.IsNullOrEmpty(data.Data)) launchError = data.Data; };
        supervisor.OutputDataReceived += (_, data) => { if (!string.IsNullOrEmpty(data.Data)) launchError = data.Data; };
        supervisor.Start(); supervisor.BeginErrorReadLine(); supervisor.BeginOutputReadLine();
        SessionState.SetString(ActiveRunKey, id);
        SessionState.SetBool(AwaitPlayKey, string.IsNullOrEmpty(player));
        SessionState.SetBool(TrainingPlayKey, string.IsNullOrEmpty(player));
        SessionState.SetString(AwaitStartedKey, DateTime.UtcNow.ToString("o"));
        LocalTrainingWindow window = GetWindow<LocalTrainingWindow>("Local ML Training");
        window.runId = id; window.hours = durationHours; window.budgetGB = storageGB;
        window.seed = runSeed; window.curriculum = useCurriculum;
        window.useStandalone = !string.IsNullOrEmpty(player); window.playerPath = player ?? "";
        Debug.Log("[ML Training] Starting supervised local run " + id);
    }

    public static void RequestStop()
    {
        if (string.IsNullOrEmpty(RunDirectory) || !Directory.Exists(RunDirectory)) return;
        File.WriteAllText(Path.Combine(RunDirectory, "stop.request"), "Stop requested from Unity.\n");
        SessionState.SetBool(AwaitPlayKey, false);
    }

    public static bool IsRunActive()
    {
        if (supervisor != null && !supervisor.HasExited) return true;
        if (string.IsNullOrEmpty(RunDirectory)) return false;
        SupervisorStatus status = Read<SupervisorStatus>(Path.Combine(RunDirectory, "supervisor-status.json"));
        return status != null && (status.state == "running" || status.state == "starting" || status.state == "saving") && ProcessExists(status.supervisorPid);
    }

    private static bool ProcessExists(int pid)
    {
        if (pid <= 0) return false;
        try { using (Process process = Process.GetProcessById(pid)) return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (string.IsNullOrEmpty(ActiveRun))
        {
            ActiveRunInfo previous = Read<ActiveRunInfo>(Path.Combine(RootPath, "active.json"));
            if (previous != null && !string.IsNullOrEmpty(previous.runId) &&
                System.Text.RegularExpressions.Regex.IsMatch(previous.runId, @"^[A-Za-z0-9][A-Za-z0-9_-]{0,80}$"))
                SessionState.SetString(ActiveRunKey, previous.runId);
        }
        if (SessionState.GetBool(AwaitPlayKey, false))
        {
            SupervisorStatus readiness = Read<SupervisorStatus>(Path.Combine(RunDirectory, "supervisor-status.json"));
            bool listening = readiness != null && readiness.trainerReady &&
                (supervisor == null || readiness.supervisorPid == supervisor.Id) && ProcessExists(readiness.supervisorPid);
            if (listening && !EditorApplication.isPlaying && !EditorApplication.isCompiling)
            {
                SessionState.SetBool(AwaitPlayKey, false);
                TrainingSceneBuilder.ShowBoard();
                EditorApplication.isPlaying = true;
            }
            else if ((supervisor != null && supervisor.HasExited) ||
                (DateTime.TryParse(SessionState.GetString(AwaitStartedKey, ""), out DateTime start) && (DateTime.UtcNow - start.ToUniversalTime()).TotalSeconds > 180))
            {
                SessionState.SetBool(AwaitPlayKey, false);
                launchError = launchError ?? "Trainer did not become ready; inspect trainer.log.";
                RequestStop();
                Debug.LogError("[ML Training] " + launchError);
            }
        }
        SupervisorStatus state = string.IsNullOrEmpty(RunDirectory) ? null : Read<SupervisorStatus>(Path.Combine(RunDirectory, "supervisor-status.json"));
        if (SessionState.GetBool(TrainingPlayKey, false) && state != null &&
            (state.state == "stopped" || state.state == "failed") && EditorApplication.isPlaying &&
            UnityEngine.Object.FindAnyObjectByType<TrainingArena>() != null)
        {
            SessionState.SetBool(TrainingPlayKey, false);
            EditorApplication.isPlaying = false;
        }
        foreach (LocalTrainingWindow window in Resources.FindObjectsOfTypeAll<LocalTrainingWindow>()) window.Repaint();
    }

    public static ModelAsset ImportModel(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path) || Path.GetExtension(path) != ".onnx") throw new ArgumentException("Choose a saved ONNX model.");
        DirectoryInfo directory = new FileInfo(path).Directory;
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "run.json"))) directory = directory.Parent;
        RunManifest manifest = directory == null ? null : Read<RunManifest>(Path.Combine(directory.FullName, "run.json"));
        if (manifest == null || manifest.owner != "BlockNations.LocalTraining.v1" || manifest.schema != BlockNations.AI.LearnedActionSchema.Version ||
            manifest.observationSize != BlockNations.AI.LearnedActionSchema.ObservationSize || manifest.actionCount != BlockNations.AI.LearnedActionSchema.ActionCount)
            throw new InvalidOperationException("This model lacks a compatible Block Nations training contract.");
        string pinsPath = Path.Combine(directory.FullName, "pins.json");
        Pins pins = Read<Pins>(pinsPath) ?? new Pins();
        // ML-Agents also performs its own checkpoint retention. Archive selected
        // weights outside its results directory so that retention cannot remove them.
        string archive = Path.Combine(directory.FullName, "pinned");
        Directory.CreateDirectory(archive);
        string digest;
        using (SHA256 sha = SHA256.Create())
        using (FileStream input = File.OpenRead(path))
            digest = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant().Substring(0, 16);
        string pinnedModel = Path.Combine(archive, "Policy-" + digest + ".onnx");
        if (!string.Equals(path, pinnedModel, StringComparison.Ordinal)) File.Copy(path, pinnedModel, true);
        string checkpoint = Path.ChangeExtension(path, ".pt");
        if (!File.Exists(checkpoint))
        {
            string behaviorDirectory = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
            if (Directory.Exists(behaviorDirectory)) checkpoint = Directory.GetFiles(behaviorDirectory, "*.pt")
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }
        if (!string.IsNullOrEmpty(checkpoint) && File.Exists(checkpoint))
        {
            string pinnedCheckpoint = Path.ChangeExtension(pinnedModel, ".pt");
            if (!string.Equals(checkpoint, pinnedCheckpoint, StringComparison.Ordinal)) File.Copy(checkpoint, pinnedCheckpoint, true);
        }
        pins.files = pins.files.Concat(new[] { Path.GetRelativePath(directory.FullName, pinnedModel) }).Distinct().ToArray();
        File.WriteAllText(pinsPath, JsonUtility.ToJson(pins, true));
        string assetDirectory = "Assets/TrainingModels";
        Directory.CreateDirectory(assetDirectory);
        string target = assetDirectory + "/LocalPolicy.onnx";
        File.Copy(pinnedModel, target, true);
        AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
        ModelAsset model = AssetDatabase.LoadAssetAtPath<ModelAsset>(target);
        if (model == null) throw new InvalidOperationException("Unity could not import this policy.");
        return model;
    }

    public static void PlayModel(string path, bool human)
    {
        if (IsRunActive() || EditorApplication.isPlaying) throw new InvalidOperationException("Stop and save training before a local model playtest.");
        ModelAsset model = ImportModel(path);
        SessionState.SetBool(TrainingPlayKey, false);
        TrainingSceneBuilder.Prepare(Path.GetFullPath("Logs/Validation/MLTraining/local-inference-status.json"),
            42, curriculum: false, requireTrainer: false, model: model, human: human);
        TrainingSceneBuilder.ShowBoard();
        EditorApplication.isPlaying = true;
        Debug.Log("[ML Training] Local CPU inference playtest: " + path);
    }

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    public static T Read<T>(string path) where T : class
    {
        try { return File.Exists(path) ? JsonUtility.FromJson<T>(File.ReadAllText(path)) : null; }
        catch (Exception error) when (error is IOException || error is ArgumentException) { return null; }
    }
    [Serializable] public sealed class SupervisorStatus
    {
        public string runId, state, stopReason;
        public int supervisorPid, trainerPid, checkpointCount, exportCount, exitCode;
        public long usedBytes, freeBytes, budgetBytes, peakResidentBytes;
        public double elapsedSeconds;
        public bool trainerReady;
    }
    [Serializable] private sealed class RunManifest { public string owner; public int schema, observationSize, actionCount, seed; public bool curriculum = true; }
    [Serializable] private sealed class ActiveRunInfo { public string runId; }
    [Serializable] private sealed class Pins { public string[] files = Array.Empty<string>(); }
}
