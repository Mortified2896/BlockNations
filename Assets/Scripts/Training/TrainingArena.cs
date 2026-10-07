using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BlockNations.AI;
using Unity.MLAgents;
using UnityEngine;
using Object = UnityEngine.Object;

// One visible arena. Policy state is seat-specific; presentation is a separate spectator.
[DefaultExecutionOrder(-1000)]
public sealed class TrainingArena : MonoBehaviour
{
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private TrainingSeatAgent[] seats;
    [SerializeField] private Camera boardCamera;
    [SerializeField] private int seed = 42;
    [SerializeField] private int maxRounds = 100;
    [SerializeField] private int decisionsPerFrame = 2;
    [SerializeField] private bool requireTrainer = true;
    [SerializeField] private bool useCurriculum = true;
    [SerializeField] private int initialCurriculumDistance = 2;
    [SerializeField] private int humanSeatIndex = -1;
    [SerializeField] private string statusPath;
    private readonly SeatAIObservationSource observationSource = new SeatAIObservationSource();
    private System.Random random;
    private bool ready, needsReset, ending;
    private bool previousBackground, previousAutomaticStepping;
    private bool previousCommunicatorEnabled, registeredTrainerFactory, trainerStopping;
    private ICommunicator trainerCommunicator;
    private int matchDecisions, turnActions, recentCaptures, recentGames, curriculumDistance = 2, matchRoundLimit;
    private double nextStatusTime, startedAt;
    private readonly Queue<bool> recentResults = new Queue<bool>();
    public bool Paused { get; set; }
    public long Decisions { get; private set; }
    public long Actions { get; private set; }
    public int Games { get; private set; }
    public int Captures { get; private set; }
    public int Interruptions { get; private set; }
    public int Rejections { get; private set; }
    public int TrainerResets { get; private set; }
    public string LastAction { get; private set; } = "Waiting for trainer";
    public string Failure { get; private set; }
    public int Round => turnManager != null ? turnManager.turnNumber : 0;
    public int ActingSeat => turnManager != null ? turnManager.currentTurnSeatIndex : -1;
    public int CurriculumDistance => curriculumDistance;
    public int NextRandom(int exclusiveMaximum) => random.Next(exclusiveMaximum);

    private void Awake()
    {
        // Install the SDK's public quit notification before Agent.OnEnable initializes
        // Academy. A quit response contains no decision and must never become a move.
        previousCommunicatorEnabled = CommunicatorFactory.Enabled;
        CommunicatorFactory.Enabled = requireTrainer;
#if UNITY_EDITOR || UNITY_STANDALONE
        if (requireTrainer)
        {
            registeredTrainerFactory = true;
            CommunicatorFactory.Register<ICommunicator>(() =>
            {
                trainerCommunicator = RpcCommunicator.Create();
                trainerCommunicator.QuitCommandReceived += TrainerClosed;
                return trainerCommunicator;
            });
        }
#endif
    }

    private void TrainerClosed()
    {
        trainerStopping = true;
        Paused = true;
        LastAction = "Trainer closed; this arena is stopping";
        WriteStatusIfDue(force: true);
    }

    private IEnumerator Start()
    {
        curriculumDistance = initialCurriculumDistance >= 2 && initialCurriculumDistance <= 8 && initialCurriculumDistance % 2 == 0
            ? initialCurriculumDistance : 2;
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < arguments.Length; i++)
        {
            if (arguments[i] == "--training-status") statusPath = arguments[++i];
            else if (arguments[i] == "--training-seed" && int.TryParse(arguments[++i], out int configuredSeed)) seed = configuredSeed;
            else if (arguments[i] == "--training-curriculum" && bool.TryParse(arguments[++i], out bool configuredCurriculum)) useCurriculum = configuredCurriculum;
            else if (arguments[i] == "--training-curriculum-distance" && int.TryParse(arguments[++i], out int configuredDistance) &&
                configuredDistance >= 2 && configuredDistance <= 8 && configuredDistance % 2 == 0) curriculumDistance = configuredDistance;
        }
        if (turnManager == null || !turnManager.IsExternallyDrivenMatch || seats == null || seats.Length != 2 ||
            seats[0] == null || seats[1] == null || seats[0].SeatIndex != 0 || seats[1].SeatIndex != 1 || boardCamera == null)
            throw new InvalidOperationException("Training arena needs explicitly wired two-seat agents, external TurnManager, and camera.");
        random = new System.Random(seed);
        previousBackground = Application.runInBackground;
        Application.runInBackground = true;
        previousAutomaticStepping = Academy.Instance.AutomaticSteppingEnabled;
        Academy.Instance.AutomaticSteppingEnabled = false;
        Academy.Instance.OnEnvironmentReset += TrainerReset;
        if (requireTrainer && !Academy.Instance.IsCommunicatorOn)
        {
            Fail("Python trainer is not connected. Start from the Local ML Training window.");
            yield break;
        }
        // Complete the SDK's initial reset before preparing a first observation.
        Academy.Instance.EnvironmentStep();
        if (trainerStopping) yield break;
        while (!turnManager.ExternalMatchReady) yield return null;
        startedAt = Time.realtimeSinceStartupAsDouble;
        ready = true;
        ResetMatch();
    }

    private void Update()
    {
        if (!ready || Paused || !string.IsNullOrEmpty(Failure)) { WriteStatusIfDue(); return; }
        if (needsReset)
        {
            if (humanSeatIndex >= 0) { Paused = true; WriteStatusIfDue(); return; }
            ResetMatch();
        }
        for (int decision = 0; decision < decisionsPerFrame && !needsReset && !Paused; decision++)
        {
            if (turnManager.gameOver) { FinishMatch(false); break; }
            if (turnManager.turnNumber > matchRoundLimit || matchDecisions >= 20000) { FinishMatch(true); break; }
            int seat = turnManager.currentTurnSeatIndex;
            if (seat == humanSeatIndex) break;
            TrainingSeatAgent agent = seats[seat];
            agent.PrepareDecision(observationSource.Observe(turnManager, seat));
            Academy.Instance.EnvironmentStep();
        }
        WriteStatusIfDue();
    }

    public bool CanAct(int seat) => ready && !ending && !needsReset && !Paused && !trainerStopping &&
        SeatAIActionExecutor.CanAct(turnManager, seat);

    public void RecordDecision() { Decisions++; matchDecisions++; }

    private void TrainerReset()
    {
        if (!ready) return; // The SDK's initial handshake precedes the first match.
        TrainerResets++;
        // A self-play team switch discards the in-flight partial match. The SDK
        // resets its agents after this callback; start a fresh board next frame.
        // Do not issue EndEpisode/reward RPCs from inside the reset exchange.
        needsReset = true;
        LastAction = "Trainer reset; preparing a fresh match";
        Academy.Instance.StatsRecorder.Add("Match/TrainerReset", 1);
    }

    public bool Execute(LegalTurnAction action)
    {
        RecordDecision();
        bool executed = action.ActionType == LegalActionType.EndTurn
            ? turnManager.TryAdvanceExternalMatchTurn(action.SeatIndex)
            : SeatAIActionExecutor.TryExecute(turnManager, action);
        if (!executed) return false;
        Actions++;
        turnActions = action.ActionType == LegalActionType.EndTurn ? 0 : turnActions + 1;
        LastAction = action.ActionType + " by seat " + action.SeatIndex;
        turnManager.RecalculatePlayerVisibility();
        if (turnManager.gameOver) FinishMatch(false);
        else if (turnActions > 4096) FinishMatch(true);
        return true;
    }

    public void RejectAction(string reason)
    {
        Rejections++;
        // A schema/rules integration fault must not become silently corrupted experience.
        Fail(reason);
    }

    private void FinishMatch(bool interrupted)
    {
        ending = true;
        int winner = turnManager.ExternalWinnerSeatIndex;
        if (!interrupted && (winner < 0 || winner > 1)) { Fail("Terminal match has no capture winner."); return; }
        foreach (TrainingSeatAgent agent in seats)
        {
            agent.PrepareTerminalObservation(observationSource.Observe(turnManager, agent.SeatIndex).Observation);
            if (interrupted) agent.EpisodeInterrupted();
            else { agent.AddReward(agent.SeatIndex == winner ? 1f : -1f); agent.EndEpisode(); }
        }
        Games++;
        if (interrupted) Interruptions++; else Captures++;
        LastAction = interrupted ? "Match interrupted at its limit" : "Seat " + winner + " won by city capture";
        recentResults.Enqueue(!interrupted);
        if (!interrupted) recentCaptures++;
        recentGames++;
        if (recentResults.Count > 100) { if (recentResults.Dequeue()) recentCaptures--; recentGames--; }
        if (useCurriculum && recentGames == 100 && recentCaptures >= 65 && curriculumDistance < 8)
        {
            curriculumDistance += 2;
            recentResults.Clear(); recentCaptures = recentGames = 0;
        }
        Academy.Instance.StatsRecorder.Add("Match/Capture", interrupted ? 0 : 1);
        Academy.Instance.StatsRecorder.Add("Match/Interrupted", interrupted ? 1 : 0);
        Academy.Instance.StatsRecorder.Add("Match/Rounds", Round);
        Academy.Instance.StatsRecorder.Add("Match/CurriculumDistance", useCurriculum ? curriculumDistance : 8);
        needsReset = true;
        ending = false;
    }

    private void ResetMatch()
    {
        needsReset = false;
        matchDecisions = turnActions = 0;
        observationSource.ResetKnowledge();
        foreach (TrainingSeatAgent agent in seats) agent.OnEpisodeBegin();
        bool fullOpening = humanSeatIndex >= 0 || !useCurriculum || random.Next(5) == 0;
        matchRoundLimit = fullOpening ? maxRounds : Math.Min(maxRounds, 30);
        turnManager.ResetExternalMatch(fullOpening ? 2 : 4, humanSeatIndex >= 0 ? humanSeatIndex : random.Next(2));
        if (!fullOpening) ConfigureCurriculumOpening();
        turnManager.RecalculatePlayerVisibility();
        boardCamera.transform.position = new Vector3(0, 0, boardCamera.transform.position.z);
        boardCamera.orthographicSize = Mathf.Max(6.5f, 6.5f / Mathf.Max(0.1f, boardCamera.aspect));
        LastAction = "Match " + (Games + 1) + (fullOpening ? " — full opening" : " — curriculum distance " + curriculumDistance);
    }

    private void ConfigureCurriculumOpening()
    {
        GridManager grid = turnManager.gridManager;
        int distance = random.Next(4) == 0 ? Math.Max(2, curriculumDistance - 2) : curriculumDistance;
        int low = (10 - distance) / 2, high = low + distance;
        bool mirror = random.Next(2) == 0;
        List<UnitDefinition> roster = turnManager.GetRecruitableOfficialUnitDefinitions();
        roster.Sort((a, b) => string.CompareOrdinal(a.TypeId, b.TypeId));
        foreach (City city in Object.FindObjectsByType<City>())
        {
            int coordinate = (city.ownerSeatIndex == 0) != mirror ? low : high;
            city.x = city.y = coordinate;
            grid.TryGetTile(city.x, city.y, out TileVisibility tile);
            city.transform.position = tile.transform.position;
            // Generic roster sampling exercises each capability rather than named matchup rules.
            UnitDefinition type = roster[random.Next(roster.Count)];
            GameObject unit = turnManager.InstantiateConfiguredUnit(type.TypeId, turnManager.GetUnitPrefabForType(type.TypeId),
                city.transform.position, city.ownerSeatIndex, city, resetTurnState: true);
            city.stationedUnit = unit;
        }
    }

    private void Fail(string message)
    {
        Failure = message; Paused = true;
        Debug.LogError("[ML Training] " + message);
        WriteStatusIfDue(force: true);
    }

    [Serializable] public sealed class ArenaStatus
    {
        public long decisions, actions;
        public int games, captures, interruptions, rejections, trainerResets, round, seat, curriculumDistance, seed, roundLimit;
        public double elapsedSeconds, decisionsPerSecond;
        public bool paused, trainerConnected;
        public string lastAction, failure;
    }

    private void WriteStatusIfDue(bool force = false)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if ((!force && now < nextStatusTime) || string.IsNullOrEmpty(statusPath)) return;
        nextStatusTime = now + 2;
        double elapsed = Math.Max(0, now - startedAt);
        var state = new ArenaStatus { decisions = Decisions, actions = Actions, games = Games, captures = Captures,
            interruptions = Interruptions, rejections = Rejections, round = Round, seat = ActingSeat,
            trainerResets = TrainerResets,
            curriculumDistance = curriculumDistance, seed = seed, elapsedSeconds = elapsed,
            roundLimit = matchRoundLimit,
            decisionsPerSecond = elapsed > 0 ? Decisions / elapsed : 0, paused = Paused,
            trainerConnected = !trainerStopping && Academy.IsInitialized && Academy.Instance.IsCommunicatorOn, lastAction = LastAction, failure = Failure };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(statusPath));
            File.WriteAllText(statusPath + ".tmp", JsonUtility.ToJson(state, true));
            if (File.Exists(statusPath)) File.Replace(statusPath + ".tmp", statusPath, null);
            else File.Move(statusPath + ".tmp", statusPath);
        }
        catch (IOException error) { Failure = "Cannot save arena status: " + error.Message; Paused = true; }
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12, 12, 410, 180), GUI.skin.box);
        GUILayout.Label(humanSeatIndex >= 0 ? "Local learned policy playtest" : requireTrainer ? "Live local self-play training" : "Local policy inference check");
        GUILayout.Label($"Match {Games + 1} | round {Round}/{matchRoundLimit} | seat {ActingSeat}");
        GUILayout.Label($"Captures {Captures} | interruptions {Interruptions} | actions {Actions}");
        GUILayout.Label(LastAction);
        if (!string.IsNullOrEmpty(Failure)) GUILayout.Label(Failure);
        if (GUILayout.Button(Paused ? "Continue" : "Pause live run")) Paused = !Paused;
        if (humanSeatIndex >= 0 && turnManager != null && turnManager.IsTurnOwnedBySeat(humanSeatIndex) && !turnManager.gameOver)
            if (GUILayout.Button("End your turn")) turnManager.TryAdvanceExternalMatchTurn(humanSeatIndex);
        if (humanSeatIndex >= 0 && needsReset && GUILayout.Button("New playtest match")) { Paused = false; ResetMatch(); }
        GUILayout.EndArea();
    }

    private void OnDestroy()
    {
        if (trainerCommunicator != null) trainerCommunicator.QuitCommandReceived -= TrainerClosed;
        if (registeredTrainerFactory) CommunicatorFactory.ClearCreator();
        CommunicatorFactory.Enabled = previousCommunicatorEnabled;
        Application.runInBackground = previousBackground;
        if (Academy.IsInitialized)
        {
            Academy.Instance.OnEnvironmentReset -= TrainerReset;
            Academy.Instance.AutomaticSteppingEnabled = previousAutomaticStepping;
        }
    }
}
