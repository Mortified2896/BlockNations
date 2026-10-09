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
    [SerializeField] private double hours = 8, budgetGB = 20;
    [SerializeField] private int seed = 42, boardSize = 11;
    [SerializeField] private bool curriculum = true;
    [SerializeField] private bool useStandalone;
    [SerializeField] private bool decoupled = true;
    [SerializeField] private int parallelGames = 4;
    [SerializeField] private string playerPath = "";
    [SerializeField] private string runId = "", modelPath = "";
    private string adoptedRun;
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
        // Native Editor state is unavailable during ScriptableObject construction.
        EditorApplication.delayCall += InitializeControls;
    }

    private static void InitializeControls()
    {
        // Validation/build Editors share macOS EditorPrefs with the live Editor.
        // They must not adopt or stop that Editor's independent training run.
        if (Application.isBatchMode) return;
        EditorApplication.update += Poll;
        EditorApplication.quitting += () => {
            SupervisorStatus status = Read<SupervisorStatus>(Path.Combine(RunDirectory ?? "", "supervisor-status.json"));
            if (status == null || status.backend != "standalone-dotnet") RequestStop();
        };
    }

    [MenuItem("Tools/Block Nations/Local ML Training/Open Controls")]
    public static void Open() => GetWindow<LocalTrainingWindow>("Local ML Training");

    private void OnEnable()
    {
        minSize = new Vector2(560, 580);
        if (string.IsNullOrEmpty(runId)) runId = ActiveRun;
    }

    private void AdoptRunSettings()
    {
        if (string.IsNullOrEmpty(ActiveRun) || adoptedRun == ActiveRun) return;
        RunManifest manifest = Read<RunManifest>(Path.Combine(RunDirectory, "run.json"));
        SupervisorStatus status = Read<SupervisorStatus>(Path.Combine(RunDirectory, "supervisor-status.json"));
        if (manifest == null || status == null) return;
        adoptedRun = ActiveRun; runId = ActiveRun;
        boardSize = manifest.boardSize; seed = manifest.seed; curriculum = manifest.curriculum;
        hours = status.durationSeconds / 3600; budgetGB = status.budgetBytes / 1e9;
        if (status.backend == "standalone-dotnet")
        { decoupled = true; parallelGames = status.workerCount; }
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        AdoptRunSettings();
        bool active = IsRunActive();
        EditorGUILayout.LabelField("Local self-play", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Self-play runs automatically. Decoupled C# training runs outside Unity; its optional viewer plays recent games by default. Capture results train the policy; turn limits interrupt episodes. Comparison tournaments are separate.", MessageType.Info);
        using (new EditorGUI.DisabledScope(active))
        {
            string root = EditorGUILayout.TextField("Training storage", RootPath);
            string python = EditorGUILayout.TextField("Trainer Python", PythonPath);
            if (root != RootPath) EditorPrefs.SetString(RootKey, root);
            if (python != PythonPath) EditorPrefs.SetString(PythonKey, python);
            decoupled = EditorGUILayout.Toggle("Decoupled C# training", decoupled);
            if (decoupled)
            {
                parallelGames = EditorGUILayout.IntPopup("Parallel games", parallelGames, new[] { "1", "2", "4" }, new[] { 1, 2, 4 });
                EditorGUILayout.LabelField("Simulation worker", TrainingSimulationWorkerBuild.WorkerPath);
                playerPath = EditorGUILayout.TextField("Viewer/playtest player (.app)", string.IsNullOrEmpty(playerPath) ? Path.GetFullPath("Build/LocalTrainingV2.app") : playerPath);
            }
            else
            {
                useStandalone = EditorGUILayout.Toggle("Use standalone training player", useStandalone);
                if (useStandalone) playerPath = EditorGUILayout.TextField("Training player (.app)", string.IsNullOrEmpty(playerPath) ? Path.GetFullPath("Build/LocalTrainingV2.app") : playerPath);
            }
        }
        hours = EditorGUILayout.DoubleField(new GUIContent("Maximum hours", "0 enables continuing learning with no time limit."), hours);
        EditorGUILayout.HelpBox(hours == 0 ?
            "No time limit. Continuing learning stays enabled for this run; positive hours can still limit later sessions. Stop and Save Checkpoint or the storage/free-disk safeguards end the session. Retention means the storage allowance may never fill." :
            "Set hours to 0 for continuing learning without a timer. Storage limits and checkpoint retention still apply.", MessageType.None);
        budgetGB = EditorGUILayout.DoubleField("Total storage limit (GB)", budgetGB);
        seed = EditorGUILayout.IntField("Seed", seed);
        boardSize = EditorGUILayout.IntPopup("Board size", boardSize, new[] { "5 × 5", "6 × 6", "7 × 7", "9 × 9", "11 × 11" }, new[] { 5, 6, 7, 9, 11 });
        if (boardSize != 11) curriculum = false;
        using (new EditorGUI.DisabledScope(boardSize != 11))
            curriculum = EditorGUILayout.Toggle("Tactical curriculum + full games", curriculum);
        EditorGUILayout.LabelField("New run", $"Automatic ID · {boardSize} × {boardSize} · fresh weights");
        runId = EditorGUILayout.TextField("Saved run ID", runId);
        EditorGUILayout.HelpBox("Start New Training creates a unique run ID for the selected board. Resume Saved Run uses the saved ID above and restores that run's board and weights.", MessageType.None);
        EditorGUILayout.HelpBox("Default: 20 GB shared across all runs, 512 MB checkpoint reserve, 20 GB free disk guard. Recent checkpoints, logs and small replay records are bounded; pinned models are protected. Plug in and keep the lid open; screen locking is supported.", MessageType.None);
        using (new EditorGUI.DisabledScope(active || EditorApplication.isPlaying || EditorApplication.isCompiling))
        {
            if (GUILayout.Button("Start New Training")) LaunchFromControls(false);
            if (GUILayout.Button("Resume Saved Run")) LaunchFromControls(true);
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
                if (status.continuousTraining) EditorGUILayout.LabelField("Trainer plan", "Continuing learning · no step limit");
                else if (status.trainingStepLimit > 0) EditorGUILayout.LabelField("Trainer plan", $"Up to {status.trainingStepLimit:N0} cumulative learning steps");
                TimeSpan elapsed = TimeSpan.FromSeconds(status.elapsedSeconds);
                EditorGUILayout.LabelField("Session time", $"{(long)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}");
                if (status.durationSeconds > 0) EditorGUILayout.LabelField("Session time limit", $"{status.durationSeconds / 3600:G4} hours");
                else if (status.continuousTraining) EditorGUILayout.LabelField("Session time limit", "None");
                EditorGUILayout.LabelField("Storage", $"{status.usedBytes / 1e9:F3} / {status.budgetBytes / 1e9:F1} GB; free {status.freeBytes / 1e9:F1} GB");
                EditorGUILayout.LabelField("Saved artifacts", $"Checkpoints {status.checkpointCount}, exports {status.exportCount}");
                if (status.peakResidentBytes > 0) EditorGUILayout.LabelField(status.backend == "standalone-dotnet" ? "Peak learner + worker RAM" : "Peak trainer + player RAM", $"{status.peakResidentBytes / 1e9:F2} GB resident");
            }
            TrainingArena.ArenaStatus arena = Read<TrainingArena.ArenaStatus>(Path.Combine(RunDirectory, "arena-status.json"));
            if (arena != null)
            {
                EditorGUILayout.LabelField("Live matches", $"{arena.games} completed; captures {arena.captures}, interruptions {arena.interruptions}");
                EditorGUILayout.LabelField("Decisions", $"{arena.decisions} / {arena.decisionsPerSecond:F1} per second; actions {arena.actions}; rejected {arena.rejections}");
                EditorGUILayout.LabelField("Board", $"Round {arena.round}/{arena.roundLimit}, seat {arena.seat}, {(arena.fullOpening ? "full opening" : "curriculum distance " + arena.startingDistance)}; trainer resets {arena.trainerResets}");
                if (!string.IsNullOrEmpty(arena.failure)) EditorGUILayout.HelpBox(arena.failure, MessageType.Error);
            }
            if (GUILayout.Button("Show Run Files")) EditorUtility.RevealInFinder(RunDirectory);
            if (active && status != null && status.viewerAvailable && GUILayout.Button("Show Training Viewer"))
                File.WriteAllText(Path.Combine(RunDirectory, "viewer.request"), "Open viewer.\n");
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Play a saved policy locally", EditorStyles.boldLabel);
        modelPath = EditorGUILayout.TextField("ONNX checkpoint", modelPath);
        if (GUILayout.Button("Choose ONNX")) modelPath = EditorUtility.OpenFilePanel("Choose a trained policy", RootPath, "onnx");
        using (new EditorGUI.DisabledScope(active || EditorApplication.isPlaying || string.IsNullOrEmpty(modelPath)))
            if (GUILayout.Button("Play Against This Model")) PlayModel(modelPath, true);
        EditorGUILayout.EndScrollView();
    }

    private void LaunchFromControls(bool resume)
    {
        try
        {
            Launch(resume ? runId : null, resume, hours, budgetGB, seed, curriculum,
                player: decoupled || useStandalone ? playerPath : null, boardSize: boardSize,
                decoupled: decoupled, parallelGames: parallelGames);
        }
        catch (Exception error) when (!(error is ExitGUIException)) { launchError = error.Message; }
    }

    public static void Launch(string id, bool resume, double durationHours = 8, double storageGB = 20,
        int runSeed = 42, bool useCurriculum = true, int maxSteps = 1_000_000, int checkpointInterval = 5000, string player = null, int boardSize = 11,
        bool decoupled = false, int parallelGames = 2)
    {
        if (!BlockNations.AI.LearnedActionSchema.SupportsBoard(boardSize)) throw new ArgumentException("Choose a supported training board size.");
        if (IsRunActive() || EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Finish the active run and leave Play Mode before starting another.");
        if (!File.Exists(PythonPath)) throw new FileNotFoundException("Install the scoped trainer environment described in Docs/ML_Training_MVP.md.", PythonPath);
        if (decoupled)
        {
            if (!File.Exists(TrainingSimulationWorkerBuild.WorkerPath)) throw new FileNotFoundException("Build the Mac training player and C# worker first.", TrainingSimulationWorkerBuild.WorkerPath);
            if (parallelGames != 1 && parallelGames != 2 && parallelGames != 4) throw new ArgumentException("Choose 1, 2 or 4 parallel games.");
            if (string.IsNullOrEmpty(player)) player = Path.GetFullPath("Build/LocalTrainingV2.app");
        }
        id = TrainingRunSelection.ResolveId(RootPath, id, resume, boardSize, DateTime.UtcNow);
        ValidateRunLimits(durationHours, storageGB);
        string directory = Path.Combine(RootPath, "runs", id);
        int savedDistance = 2;
        if (resume)
        {
            RunManifest manifest = Read<RunManifest>(Path.Combine(directory, "run.json"));
            if (manifest == null || manifest.owner != "BlockNations.LocalTraining.v1" || manifest.schema != BlockNations.AI.LearnedActionSchema.Version)
                throw new InvalidOperationException("Resume requires a compatible schema v2 run; v1 checkpoints remain preserved separately.");
            runSeed = manifest.seed;
            useCurriculum = manifest.curriculum;
            TrainingArena.ArenaStatus previous = Read<TrainingArena.ArenaStatus>(Path.Combine(directory, "arena-status.json"));
            if (previous != null && previous.curriculumDistance >= 2 && previous.curriculumDistance <= 8 && previous.curriculumDistance % 2 == 0)
                savedDistance = previous.curriculumDistance;
            boardSize = manifest.boardSize;
        }
        if (boardSize != 11) useCurriculum = false;
        if (string.IsNullOrEmpty(player))
        {
            TrainingArena arena = TrainingSceneBuilder.Prepare(Path.Combine(directory, "arena-status.json"), runSeed, useCurriculum, requireTrainer: true);
            TrainingSceneBuilder.Set(arena, "initialCurriculumDistance", savedDistance);
            TrainingSceneBuilder.Set(arena, "boardSize", boardSize);
        }
        else if (!Directory.Exists(player)) throw new DirectoryNotFoundException("Build the Mac training player first.");
        string script = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/Training/supervisor.py"));
        string[] arguments = { script, "--root", RootPath, "--run-id", id, "--hours", durationHours.ToString(CultureInfo.InvariantCulture),
            "--budget-gb", storageGB.ToString(CultureInfo.InvariantCulture), "--seed", runSeed.ToString(),
            "--board-size", boardSize.ToString(), "--max-steps", maxSteps.ToString(), "--checkpoint-interval", checkpointInterval.ToString() };
        string command = string.Join(" ", arguments.Select(Quote)) + (resume ? " --resume" : "");
        if (!useCurriculum) command += " --full-openings";
        if (!string.IsNullOrEmpty(player)) command += " --env " + Quote(Path.GetFullPath(player));
        if (decoupled) command += " --backend dotnet --parallel-games " + parallelGames + " --worker " + Quote(TrainingSimulationWorkerBuild.WorkerPath);
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
        window.seed = runSeed; window.curriculum = useCurriculum; window.boardSize = boardSize;
        window.useStandalone = !string.IsNullOrEmpty(player); window.playerPath = player ?? "";
        window.decoupled = decoupled; window.parallelGames = parallelGames;
        Debug.Log("[ML Training] Starting supervised local run " + id);
    }

    internal static void ValidateRunLimits(double durationHours, double storageGB)
    {
        if (double.IsNaN(durationHours) || double.IsInfinity(durationHours) || durationHours < 0 ||
            double.IsNaN(storageGB) || double.IsInfinity(storageGB) || storageGB <= 0.512)
            throw new ArgumentException("Use nonnegative hours (0 = no time limit) and a finite storage budget above the checkpoint reserve.");
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
        TrainingArena arena = TrainingSceneBuilder.Prepare(Path.GetFullPath("Logs/Validation/MLTraining/local-inference-status.json"),
            42, curriculum: false, requireTrainer: false, model: model, human: human);
        DirectoryInfo source = new FileInfo(Path.GetFullPath(path)).Directory;
        while (source != null && !File.Exists(Path.Combine(source.FullName, "run.json"))) source = source.Parent;
        RunManifest manifest = Read<RunManifest>(Path.Combine(source.FullName, "run.json"));
        TrainingSceneBuilder.Set(arena, "boardSize", manifest.boardSize);
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
        public string runId, state, stopReason, backend;
        public int supervisorPid, trainerPid, checkpointCount, exportCount, exitCode, workerCount = 1;
        public long usedBytes, freeBytes, budgetBytes, peakResidentBytes;
        public long trainingStepLimit;
        public double elapsedSeconds, durationSeconds;
        public bool trainerReady, continuousTraining, viewerAvailable;
    }
    [Serializable] private sealed class RunManifest { public string owner; public int schema, observationSize, actionCount, seed, boardSize = 11; public bool curriculum = true; }
    [Serializable] private sealed class ActiveRunInfo { public string runId; }
    [Serializable] private sealed class Pins { public string[] files = Array.Empty<string>(); }
}
