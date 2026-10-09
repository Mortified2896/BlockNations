using System;
using System.Collections;
using System.IO;
using System.Linq;
using BlockNations.Simulation;
using UnityEngine;

// An optional process: closing it has no learner/worker lifecycle side effect.
[DefaultExecutionOrder(-1100)]
public sealed class TrainingViewer : MonoBehaviour, ITrainingSpectator
{
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private TrainingArena trainingArena;
    [SerializeField] private TrainingSeatAgent[] seats;
    [SerializeField] private Camera boardCamera;
    [SerializeField] private TrainingHumanPresentation humanPresentation;
    [SerializeField] private bool viewerMode;
    [SerializeField] private string runDirectory;
    private readonly TrainingOverlay overlay = new TrainingOverlay();
    private TrainingTraceReader traces;
    private TrainingArena.ArenaStatus totals, worker;
    private bool ready, showingLive;
    private int selectedWorker, width, height;
    private double nextPoll;
    private Rect originalRect;
    private Color originalBackground;
    private int originalCullingMask, originalFrameRate;
    private bool originalBackgroundExecution;
    private string readError;
    [Serializable] private sealed class Manifest { public string owner; public int schema, boardSize; }
    [Serializable] private sealed class Clock { public int version, boardSize; public string runId; public double totalSeconds; public bool estimated; }
    [Serializable] private sealed class Control { public bool paused; }
    [Serializable] private sealed class Watch { public int worker; public bool live; }
    [Serializable] private sealed class RatingSession { public string session; }

    public TrainingReplayHistory Replay { get; } = new TrainingReplayHistory { FollowRecent = true };
    public TrainingProgressHistory Progress { get; private set; } = new TrainingProgressHistory();
    public TrainingEloHistory EloHistory { get; private set; }
    public TrainingStorageStatus StorageStatus { get; private set; }
    public TrainingPlaytestBridge Playtest { get; } = new TrainingPlaytestBridge();
    public double TotalTrainingSeconds { get; private set; } = -1;
    public bool TrainingTimeEstimated { get; private set; }
    public int BoardSize { get; private set; } = 11;
    public string TrainingRunDirectory => runDirectory;
    public bool IsTraining => true;
    public bool IsHumanPlaytest => false;
    public bool IsRatingCheck => false;
    public bool CanContinue => totals != null && totals.trainerConnected && string.IsNullOrEmpty(totals.failure);
    public bool CanEndHumanTurn => false;
    public bool CanStartNewMatch => false;
    public bool CanReturnToTraining => false;
    public int HumanSeat => -1;
    public string HumanPolicyVersion => null;
    public bool Paused
    {
        get => totals?.paused ?? false;
        set
        {
            if (!CanContinue) return;
            Write("training-control.json", JsonUtility.ToJson(new Control { paused = value }));
        }
    }
    public int Games => totals?.games ?? 0;
    public int Captures => totals?.captures ?? 0;
    public int Interruptions => totals?.interruptions ?? 0;
    public long Actions => totals?.actions ?? 0;
    public long Decisions => totals?.decisions ?? 0;
    public double Elapsed => totals?.elapsedSeconds ?? 0;
    public int Round => worker?.round ?? 0;
    public int RoundLimit => worker?.roundLimit ?? 100;
    public bool FullOpening => worker?.fullOpening ?? true;
    public int CurriculumDistance => worker?.curriculumDistance ?? 2;
    public int StartingDistance => BoardSize - 3;
    public string LastAction => worker?.lastAction ?? "Waiting for simulation telemetry";
    public string Failure => !string.IsNullOrEmpty(totals?.failure) ? totals.failure : readError;
    public Color SpectatorBackgroundColor => boardCamera.backgroundColor;
    public int WorkerCount => Math.Max(1, totals?.workerCount ?? 1);
    public int SelectedWorker
    {
        get => selectedWorker;
        set { selectedWorker = Mathf.Clamp(value, 0, WorkerCount - 1); nextPoll = 0; Replay.SetLiveFrames(null, null); }
    }
    public bool ShowingLive => showingLive;
    public int GoldForSeat(int seat) => seat == 0 ? worker?.gold0 ?? 0 : worker?.gold1 ?? 0;
    public void EndHumanTurn() { }
    public void NewHumanMatch() { }
    public void ReturnToTraining() { }
    public void WatchLive() { showingLive = true; Replay.BackToLive(); if (ready) Poll(); else nextPoll = 0; }
    public void WatchRecent()
    {
        showingLive = false; Replay.FollowRecent = true;
        if (Replay.CanInspect) Replay.Inspect(Time.realtimeSinceStartupAsDouble);
        nextPoll = 0;
    }
    public void StopAndSave() { if (CanContinue) Write("stop.request", "Stop and save requested from the viewer.\n"); }

    private void Awake()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--training-viewer") viewerMode = bool.Parse(args[++i]);
            else if (args[i] == "--viewer-run") runDirectory = args[++i];
        }
        if (!viewerMode) { enabled = false; return; }
        if (turnManager == null || trainingArena == null || seats == null || boardCamera == null || humanPresentation == null)
            throw new InvalidOperationException("Viewer needs its authored scene references.");
        Manifest manifest = Read<Manifest>(Path.Combine(runDirectory, "run.json"));
        if (manifest == null || manifest.owner != "BlockNations.LocalTraining.v1" || manifest.schema != 2 ||
            !BlockNations.AI.LearnedActionSchema.SupportsBoard(manifest.boardSize)) throw new ArgumentException("Choose an owned compatible training run.");
        BoardSize = manifest.boardSize;
        turnManager.gridManager.width = turnManager.gridManager.height = BoardSize;
        trainingArena.enabled = false;
        foreach (TrainingSeatAgent agent in seats) agent.gameObject.SetActive(false);
        humanPresentation.SetHumanMode(false);
        originalBackgroundExecution = Application.runInBackground;
        Application.runInBackground = true;
        originalFrameRate = Application.targetFrameRate; Application.targetFrameRate = 30;
    }

    private IEnumerator Start()
    {
        while (!turnManager.ExternalMatchReady) yield return null;
        originalRect = boardCamera.rect; originalBackground = boardCamera.backgroundColor; originalCullingMask = boardCamera.cullingMask;
        float grey = originalBackground.grayscale;
        boardCamera.backgroundColor = Color.Lerp(originalBackground, new Color(grey, grey, grey, originalBackground.a), .15f);
        var opening = new SceneSimulationAdapter(turnManager).State;
        traces = new TrainingTraceReader(new SimulationReplayProjector(turnManager, opening), opening.Roster);
        boardCamera.cullingMask = 0;
        ready = true;
        Poll();
    }

    private void Update()
    {
        if (!ready) return;
        if (width != Screen.width || height != Screen.height)
        {
            width = Screen.width; height = Screen.height;
            float left = TrainingOverlay.ReservedWidth / Math.Max(1, width);
            boardCamera.rect = new Rect(left, 0, 1 - left, 1);
        }
        Replay.Tick(Time.realtimeSinceStartupAsDouble);
        Playtest.Poll(this);
        if (Time.realtimeSinceStartupAsDouble >= nextPoll) Poll();
    }

    private void Poll()
    {
        nextPoll = Time.realtimeSinceStartupAsDouble + .5;
        try
        {
            totals = Read<TrainingArena.ArenaStatus>(Path.Combine(runDirectory, "arena-status.json")) ?? totals;
            if (totals != null && (totals.schema != 2 || totals.boardSize != BoardSize || totals.workerCount < 1 || totals.workerCount > 16 ||
                totals.simulationBackend != "standalone-dotnet" || totals.simulationVersion != SimulationRules.Version))
                throw new ArgumentException("Viewer and training backend are incompatible.");
            if (totals != null && DateTime.UtcNow - File.GetLastWriteTimeUtc(Path.Combine(runDirectory, "arena-status.json")) > TimeSpan.FromSeconds(10))
                totals.trainerConnected = false;
            selectedWorker = Mathf.Clamp(selectedWorker, 0, WorkerCount - 1);
            worker = Read<TrainingArena.ArenaStatus>(Path.Combine(runDirectory, "workers", selectedWorker.ToString(), "arena-status.json")) ?? worker;
            string runId = new DirectoryInfo(runDirectory).Name;
            var progress = Read<TrainingProgressHistory>(Path.Combine(runDirectory, $"board-{BoardSize}-progress.json"));
            if (progress != null && progress.IsValid()) Progress = progress;
            var elo = Read<TrainingEloHistory>(Path.Combine(runDirectory, "match-elo.json"));
            if (elo != null && elo.IsValid(BoardSize, runId)) EloHistory = elo;
            var storage = Read<TrainingStorageStatus>(Path.Combine(runDirectory, "supervisor-status.json"));
            if (storage != null && storage.IsValid(runId)) StorageStatus = storage;
            var clock = Read<Clock>(Path.Combine(runDirectory, "training-time.json"));
            if (clock != null && clock.version == 1 && clock.boardSize == BoardSize && clock.runId == runId && clock.totalSeconds >= 0 &&
                !double.IsNaN(clock.totalSeconds) && !double.IsInfinity(clock.totalSeconds))
            { TotalTrainingSeconds = clock.totalSeconds; TrainingTimeEstimated = clock.estimated; }
            Replay.SetCompletedGames(traces.Recent(runDirectory, WorkerCount));
            if (!showingLive && !Replay.Inspecting && Replay.CanInspect) Replay.Inspect(Time.realtimeSinceStartupAsDouble);
            Write("viewer-watch.json", JsonUtility.ToJson(new Watch { worker = selectedWorker, live = showingLive }));
            if (showingLive)
            {
                string path = Path.Combine(runDirectory, "workers", selectedWorker.ToString(), "live.json");
                var session = Read<RatingSession>(Path.Combine(runDirectory, "rating-session.json"));
                var live = File.Exists(path) && (Paused || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromSeconds(10)) ? traces.Read(path) : null;
                if (live != null && session != null && live.sourceKey.StartsWith(session.session + ":" + selectedWorker + ":", StringComparison.Ordinal))
                    Replay.SetLiveFrames(live.frames[0], live.frames[live.frames.Count - 1]);
                else Replay.SetLiveFrames(null, null);
            }
            readError = null;
        }
        catch (Exception error) when (error is IOException || error is ArgumentException || error is UnauthorizedAccessException)
        { readError = "Viewer telemetry: " + error.Message; }
    }

    private static T Read<T>(string path) where T : class =>
        File.Exists(path) && new FileInfo(path).Length < 2_000_000 ? JsonUtility.FromJson<T>(File.ReadAllText(path)) : null;
    private void Write(string name, string text)
    {
        string path = Path.Combine(runDirectory, name);
        File.WriteAllText(path + ".tmp", text);
        if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
    }
    private void OnGUI() { if (ready) overlay.Draw(this); }
    private void OnDestroy()
    {
        if (!viewerMode) return;
        Application.runInBackground = originalBackgroundExecution; Application.targetFrameRate = originalFrameRate;
        if (!ready || boardCamera == null) return;
        boardCamera.rect = originalRect; boardCamera.backgroundColor = originalBackground; boardCamera.cullingMask = originalCullingMask;
    }
}
