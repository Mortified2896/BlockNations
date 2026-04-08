using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class AIVsAIBatchRunController
{
    public enum EvaluationMethod
    {
        Bayesian = 0
    }

    public enum SimulationPreset
    {
        Manual = 0,
        QuickExploration = 1,
        StandardComparison = 2,
        StrictComparison = 3
    }

    public enum SimulationMode
    {
        HeadToHead = 0,
        Tournament = 1
    }

    public enum TournamentType
    {
        RoundRobin = 0
    }

    public enum StopReason
    {
        None = 0,
        CertaintyReached_A_Better = 1,
        CertaintyReached_A_Worse = 2,
        TimeBudgetReached = 3,
        EmergencySafetyFuseTriggered = 4,
        InvalidStatsDetected = 5,
        TournamentScheduleCompleted = 6
    }

    public enum RunCompletionKind
    {
        None = 0,
        NormalCompleted = 1,
        AbnormalAborted = 2
    }

    [Serializable]
    public struct AIVariant
    {
        public TurnManager.AIRecruitVariant baseModel;
        public AILocalDecisionFeatures localFeatures;

        public AIVariant(TurnManager.AIRecruitVariant baseModel, AILocalDecisionFeatures localFeatures)
        {
            this.baseModel = baseModel;
            this.localFeatures = localFeatures;
        }
    }

    [Serializable]
    public struct TournamentEstimate
    {
        public int participantCount;
        public int totalPairings;
        public int totalGames;
        public float gamesPerSecondUsed;
        public float estimatedRuntimeSeconds;
    }

    [Serializable]
    public struct SimulationSettings
    {
        public SimulationPreset preset;
        public EvaluationMethod evaluationMethod;
        public float certaintyThreshold;
        public int minimumGames;
        public float timeBudgetSeconds;
        public int batchSize;
        public int emergencyHardMaxGames;
        public SimulationMode mode;
        public TournamentType tournamentType;
        public int tournamentParticipantMask;
        public int tournamentGamesPerPairing;
        public bool tournamentSeatSwap;
        public bool tournamentRunContinuously;
    }

    public readonly struct ActiveRunSnapshot
    {
        public readonly SimulationMode simulationMode;
        public readonly int completedGames;
        public readonly int scheduledGames;
        public readonly int scheduledPairings;
        public readonly int participantCount;
        public readonly int sideAWins;
        public readonly int sideBWins;
        public readonly int trueDraws;
        public readonly int aborts;
        public readonly int decisiveGames;
        public readonly int batchSize;
        public readonly float elapsedSeconds;
        public readonly float timeBudgetSeconds;
        public readonly float remainingTimeSeconds;
        public readonly float gamesPerSecond;
        public readonly float bayesianSideABetterProbability;
        public readonly float certaintyThreshold;
        public readonly float decisiveEdgePoints;
        public readonly bool isWaitingForBatchAfterThreshold;
        public readonly string tournamentStandingsPreview;

        public ActiveRunSnapshot(
            SimulationMode simulationMode,
            int completedGames,
            int scheduledGames,
            int scheduledPairings,
            int participantCount,
            int sideAWins,
            int sideBWins,
            int trueDraws,
            int aborts,
            int decisiveGames,
            int batchSize,
            float elapsedSeconds,
            float timeBudgetSeconds,
            float remainingTimeSeconds,
            float gamesPerSecond,
            float bayesianSideABetterProbability,
            float certaintyThreshold,
            float decisiveEdgePoints,
            bool isWaitingForBatchAfterThreshold,
            string tournamentStandingsPreview)
        {
            this.simulationMode = simulationMode;
            this.completedGames = completedGames;
            this.scheduledGames = scheduledGames;
            this.scheduledPairings = scheduledPairings;
            this.participantCount = participantCount;
            this.sideAWins = sideAWins;
            this.sideBWins = sideBWins;
            this.trueDraws = trueDraws;
            this.aborts = aborts;
            this.decisiveGames = decisiveGames;
            this.batchSize = batchSize;
            this.elapsedSeconds = elapsedSeconds;
            this.timeBudgetSeconds = timeBudgetSeconds;
            this.remainingTimeSeconds = remainingTimeSeconds;
            this.gamesPerSecond = gamesPerSecond;
            this.bayesianSideABetterProbability = bayesianSideABetterProbability;
            this.certaintyThreshold = certaintyThreshold;
            this.decisiveEdgePoints = decisiveEdgePoints;
            this.isWaitingForBatchAfterThreshold = isWaitingForBatchAfterThreshold;
            this.tournamentStandingsPreview = tournamentStandingsPreview ?? string.Empty;
        }
    }

    private const float QuickExplorationTimeBudgetSeconds = 120f;
    private const float StandardComparisonTimeBudgetSeconds = 300f;
    private const float StrictComparisonTimeBudgetSeconds = 900f;
    private const float MinCertaintyThreshold = 0.5f;
    private const float MaxCertaintyThreshold = 0.9999f;
    private const float MinTimeBudgetSeconds = 1f;
    private const int MinPositiveValue = 1;
    private const int MinTournamentParticipantCount = 2;
    private const int DefaultTournamentGamesPerPairing = 1;
    private const int GeneratedVariantPoolCount = 16;
    private const int AllGeneratedVariantsMask = (1 << GeneratedVariantPoolCount) - 1;
    private const int BayesianNormalApproximationThreshold = 200;
    private const float DefaultNormalGamesPerSecond = 0.15f;
    private const float DefaultFastGamesPerSecond = 0.50f;
    private const float DefaultVeryFastGamesPerSecond = 1.25f;
    private const float DefaultUltraFastGamesPerSecond = 2.50f;

    private static readonly AIVariant[] GeneratedVariantPool = BuildGeneratedVariantPool();
    private static SimulationSettings pendingSimulationSettings = GetDefaultSimulationSettings();
    private static bool hasPendingSimulationSettings;
    private static ActiveRun activeRun;
    private static float lastObservedGamesPerSecond = -1f;

    private sealed class ActiveRun
    {
        public sealed class CompletedMatchRecord
        {
            public int pairingIndex;
            public int pairingGameIndex;
            public bool seatsWereSwapped;
            public int logicalVariantAIndex;
            public int logicalVariantBIndex;
            public string logicalVariantALabel;
            public string logicalVariantBLabel;
            public double logicalVariantAScore;
            public double logicalVariantBScore;
            public double player1Score;
            public double player2Score;
            public double baseSideAScore;
            public bool isDraw;
            public bool isAbort;
        }

        public string runId;
        public SimulationSettings settings;
        public int completedMatchCount;
        public int sideAWins;
        public int sideBWins;
        public int drawsOrAborts;
        public int trueDraws;
        public int aborts;
        public int totalTurnCount;
        public DateTime startedAtUtc;
        public string appVersion;
        public string mapSizePreset;
        public int boardWidth;
        public int boardHeight;
        public string gameMode;
        public string sideAAIConfig;
        public string sideBAIConfig;
        public TurnManager.AIRecruitVariant baseSideARecruitVariant;
        public TurnManager.AIRecruitVariant baseSideBRecruitVariant;
        public AILocalDecisionFeatures baseSideAFeatures;
        public AILocalDecisionFeatures baseSideBFeatures;
        public TurnManager.AIDebugProfile baseSideAProfile;
        public TurnManager.AIDebugProfile baseSideBProfile;
        public readonly List<AIVariant> tournamentParticipants = new List<AIVariant>();
        public readonly List<ScheduledMatch> tournamentSchedule = new List<ScheduledMatch>();
        public readonly List<CompletedMatchRecord> completedMatches = new List<CompletedMatchRecord>();
    }

    private readonly struct RunStopDecision
    {
        public readonly bool shouldStop;
        public readonly RunCompletionKind completionKind;
        public readonly StopReason stopReason;
        public readonly double sideABetterProbability;

        public RunStopDecision(
            bool shouldStop,
            RunCompletionKind completionKind,
            StopReason stopReason,
            double sideABetterProbability)
        {
            this.shouldStop = shouldStop;
            this.completionKind = completionKind;
            this.stopReason = stopReason;
            this.sideABetterProbability = sideABetterProbability;
        }
    }

    private readonly struct ScheduledMatch
    {
        public readonly int pairingIndex;
        public readonly int pairingGameIndex;
        public readonly int logicalVariantAIndex;
        public readonly int logicalVariantBIndex;
        public readonly bool seatsWereSwapped;

        public ScheduledMatch(
            int pairingIndex,
            int pairingGameIndex,
            int logicalVariantAIndex,
            int logicalVariantBIndex,
            bool seatsWereSwapped)
        {
            this.pairingIndex = pairingIndex;
            this.pairingGameIndex = pairingGameIndex;
            this.logicalVariantAIndex = logicalVariantAIndex;
            this.logicalVariantBIndex = logicalVariantBIndex;
            this.seatsWereSwapped = seatsWereSwapped;
        }

        public int RuntimeSideAIndex => seatsWereSwapped ? logicalVariantBIndex : logicalVariantAIndex;
        public int RuntimeSideBIndex => seatsWereSwapped ? logicalVariantAIndex : logicalVariantBIndex;
    }

    private readonly struct MatchContext
    {
        public readonly bool isValid;
        public readonly bool seatsWereSwapped;
        public readonly int pairingIndex;
        public readonly int pairingGameIndex;
        public readonly int logicalVariantAIndex;
        public readonly int logicalVariantBIndex;
        public readonly AIVariant logicalVariantA;
        public readonly AIVariant logicalVariantB;
        public readonly AIVariant runtimeSideA;
        public readonly AIVariant runtimeSideB;
        public readonly string logicalVariantALabel;
        public readonly string logicalVariantBLabel;
        public readonly string runtimeSideALabel;
        public readonly string runtimeSideBLabel;
        public readonly string pairingLabel;

        public MatchContext(
            bool isValid,
            bool seatsWereSwapped,
            int pairingIndex,
            int pairingGameIndex,
            int logicalVariantAIndex,
            int logicalVariantBIndex,
            AIVariant logicalVariantA,
            AIVariant logicalVariantB,
            AIVariant runtimeSideA,
            AIVariant runtimeSideB,
            string logicalVariantALabel,
            string logicalVariantBLabel)
        {
            this.isValid = isValid;
            this.seatsWereSwapped = seatsWereSwapped;
            this.pairingIndex = pairingIndex;
            this.pairingGameIndex = pairingGameIndex;
            this.logicalVariantAIndex = logicalVariantAIndex;
            this.logicalVariantBIndex = logicalVariantBIndex;
            this.logicalVariantA = logicalVariantA;
            this.logicalVariantB = logicalVariantB;
            this.runtimeSideA = runtimeSideA;
            this.runtimeSideB = runtimeSideB;
            this.logicalVariantALabel = logicalVariantALabel;
            this.logicalVariantBLabel = logicalVariantBLabel;
            runtimeSideALabel = GetVariantLabel(runtimeSideA);
            runtimeSideBLabel = GetVariantLabel(runtimeSideB);
            pairingLabel = $"{logicalVariantALabel} vs {logicalVariantBLabel}";
        }
    }

    private sealed class TournamentStanding
    {
        public string label;
        public int wins;
        public int losses;
        public int draws;
        public int aborts;
        public int games;
        public double scoreSum;
    }

    private sealed class TournamentPairingAggregate
    {
        public string pairingLabel;
        public string logicalVariantALabel;
        public string logicalVariantBLabel;
        public int logicalVariantAWins;
        public int logicalVariantBWins;
        public int draws;
        public int aborts;
        public int games;
        public int swappedGames;
        public double logicalVariantAScoreSum;
    }

    public static bool HasActiveRun => activeRun != null;

    public static bool TryGetActiveSimulationSettings(out SimulationSettings settings)
    {
        if (activeRun == null)
        {
            settings = default;
            return false;
        }

        settings = activeRun.settings;
        return true;
    }

    public static int GetGeneratedVariantCount()
    {
        return GeneratedVariantPool.Length;
    }

    public static AIVariant GetGeneratedVariant(int index)
    {
        if (index < 0 || index >= GeneratedVariantPool.Length)
        {
            return GeneratedVariantPool[0];
        }

        return GeneratedVariantPool[index];
    }

    public static string GetGeneratedVariantLabel(int index)
    {
        return GetVariantLabel(GetGeneratedVariant(index));
    }

    public static string GetVariantLabel(AIVariant variant)
    {
        string modelLabel = variant.baseModel == TurnManager.AIRecruitVariant.RiderFocus
            ? "Rider Focus"
            : "Baseline";
        string featureLabel = GetFeatureMaskLabel(variant.localFeatures);
        return $"{modelLabel} [{featureLabel}]";
    }

    public static int GetDefaultTournamentParticipantMask()
    {
        return AllGeneratedVariantsMask;
    }

    public static int CountTournamentParticipants(int participantMask)
    {
        int sanitizedMask = SanitizeTournamentParticipantMask(participantMask);
        int count = 0;
        for (int i = 0; i < GeneratedVariantPool.Length; i++)
        {
            if ((sanitizedMask & (1 << i)) != 0)
            {
                count++;
            }
        }

        return count;
    }

    public static TournamentEstimate EstimateTournament(
        SimulationSettings settings,
        TurnManager.AIVsAIBatchSpeedPreset speedPreset)
    {
        settings = SanitizeSimulationSettings(settings);
        List<AIVariant> participants = BuildTournamentParticipants(settings.tournamentParticipantMask);
        int pairings = GetRoundRobinPairingCount(participants.Count);
        int gamesPerPairing = Mathf.Max(MinPositiveValue, settings.tournamentGamesPerPairing);
        int seatMultiplier = settings.tournamentSeatSwap ? 2 : 1;
        int totalGames = pairings * gamesPerPairing * seatMultiplier;
        float gamesPerSecond = GetEstimatedGamesPerSecond(speedPreset);
        float estimatedRuntimeSeconds = gamesPerSecond > 0.0001f
            ? totalGames / gamesPerSecond
            : 0f;

        return new TournamentEstimate
        {
            participantCount = participants.Count,
            totalPairings = pairings,
            totalGames = totalGames,
            gamesPerSecondUsed = gamesPerSecond,
            estimatedRuntimeSeconds = estimatedRuntimeSeconds
        };
    }

    public static SimulationSettings GetDefaultSimulationSettings()
    {
        return GetPresetSettings(SimulationPreset.StandardComparison);
    }

    public static SimulationSettings GetPresetSettings(SimulationPreset preset)
    {
        switch (preset)
        {
            case SimulationPreset.QuickExploration:
                return new SimulationSettings
                {
                    preset = SimulationPreset.QuickExploration,
                    evaluationMethod = EvaluationMethod.Bayesian,
                    certaintyThreshold = 0.90f,
                    minimumGames = 50,
                    timeBudgetSeconds = QuickExplorationTimeBudgetSeconds,
                    batchSize = 25,
                    emergencyHardMaxGames = 5000,
                    mode = SimulationMode.HeadToHead,
                    tournamentType = TournamentType.RoundRobin,
                    tournamentParticipantMask = GetDefaultTournamentParticipantMask(),
                    tournamentGamesPerPairing = DefaultTournamentGamesPerPairing,
                    tournamentSeatSwap = true,
                    tournamentRunContinuously = false
                };

            case SimulationPreset.StrictComparison:
                return new SimulationSettings
                {
                    preset = SimulationPreset.StrictComparison,
                    evaluationMethod = EvaluationMethod.Bayesian,
                    certaintyThreshold = 0.99f,
                    minimumGames = 200,
                    timeBudgetSeconds = StrictComparisonTimeBudgetSeconds,
                    batchSize = 50,
                    emergencyHardMaxGames = 25000,
                    mode = SimulationMode.HeadToHead,
                    tournamentType = TournamentType.RoundRobin,
                    tournamentParticipantMask = GetDefaultTournamentParticipantMask(),
                    tournamentGamesPerPairing = DefaultTournamentGamesPerPairing,
                    tournamentSeatSwap = true,
                    tournamentRunContinuously = false
                };

            case SimulationPreset.StandardComparison:
            default:
                return new SimulationSettings
                {
                    preset = SimulationPreset.StandardComparison,
                    evaluationMethod = EvaluationMethod.Bayesian,
                    certaintyThreshold = 0.95f,
                    minimumGames = 100,
                    timeBudgetSeconds = StandardComparisonTimeBudgetSeconds,
                    batchSize = 50,
                    emergencyHardMaxGames = 10000,
                    mode = SimulationMode.HeadToHead,
                    tournamentType = TournamentType.RoundRobin,
                    tournamentParticipantMask = GetDefaultTournamentParticipantMask(),
                    tournamentGamesPerPairing = DefaultTournamentGamesPerPairing,
                    tournamentSeatSwap = true,
                    tournamentRunContinuously = false
                };
        }
    }

    public static SimulationSettings SanitizeSimulationSettings(SimulationSettings settings)
    {
        SimulationPreset sanitizedPreset = Enum.IsDefined(typeof(SimulationPreset), settings.preset)
            ? settings.preset
            : SimulationPreset.Manual;
        SimulationSettings fallback = sanitizedPreset == SimulationPreset.Manual
            ? GetDefaultSimulationSettings()
            : GetPresetSettings(sanitizedPreset);
        bool missingAllNumericSettings =
            settings.certaintyThreshold <= 0f &&
            settings.minimumGames <= 0 &&
            settings.timeBudgetSeconds <= 0f &&
            settings.batchSize <= 0 &&
            settings.emergencyHardMaxGames <= 0 &&
            settings.tournamentGamesPerPairing <= 0;

        if (missingAllNumericSettings)
        {
            fallback.mode = Enum.IsDefined(typeof(SimulationMode), settings.mode)
                ? settings.mode
                : fallback.mode;
            fallback.tournamentParticipantMask = SanitizeTournamentParticipantMask(settings.tournamentParticipantMask);
            fallback.tournamentGamesPerPairing = Math.Max(MinPositiveValue, settings.tournamentGamesPerPairing <= 0
                ? fallback.tournamentGamesPerPairing
                : settings.tournamentGamesPerPairing);
            fallback.tournamentSeatSwap = settings.tournamentSeatSwap;
            fallback.tournamentRunContinuously = settings.tournamentRunContinuously;
            return fallback;
        }

        settings.preset = sanitizedPreset;
        settings.evaluationMethod = EvaluationMethod.Bayesian;
        settings.mode = Enum.IsDefined(typeof(SimulationMode), settings.mode)
            ? settings.mode
            : SimulationMode.HeadToHead;
        settings.tournamentType = Enum.IsDefined(typeof(TournamentType), settings.tournamentType)
            ? settings.tournamentType
            : TournamentType.RoundRobin;
        settings.certaintyThreshold = SanitizeFloat(
            settings.certaintyThreshold,
            fallback.certaintyThreshold,
            MinCertaintyThreshold,
            MaxCertaintyThreshold);
        settings.minimumGames = SanitizeInt(
            settings.minimumGames,
            MinPositiveValue);
        settings.timeBudgetSeconds = SanitizeFloat(
            settings.timeBudgetSeconds,
            fallback.timeBudgetSeconds,
            MinTimeBudgetSeconds,
            float.MaxValue);
        settings.batchSize = SanitizeInt(
            settings.batchSize,
            MinPositiveValue);
        settings.emergencyHardMaxGames = SanitizeInt(
            settings.emergencyHardMaxGames,
            MinPositiveValue);
        settings.batchSize = EnsureEvenPairSize(settings.batchSize);
        settings.tournamentParticipantMask = SanitizeTournamentParticipantMask(settings.tournamentParticipantMask);
        settings.tournamentGamesPerPairing = SanitizeInt(
            settings.tournamentGamesPerPairing,
            MinPositiveValue);
        return settings;
    }

    public static string GetPresetDisplayName(SimulationPreset preset)
    {
        switch (preset)
        {
            case SimulationPreset.QuickExploration:
                return "Quick Exploration";

            case SimulationPreset.StandardComparison:
                return "Standard Comparison";

            case SimulationPreset.StrictComparison:
                return "Strict Comparison";

            case SimulationPreset.Manual:
            default:
                return "Manual";
        }
    }

    public static string GetEvaluationMethodDisplayName(EvaluationMethod evaluationMethod)
    {
        switch (evaluationMethod)
        {
            case EvaluationMethod.Bayesian:
            default:
                return "Bayesian";
        }
    }

    public static string GetSimulationModeDisplayName(SimulationMode mode)
    {
        switch (mode)
        {
            case SimulationMode.Tournament:
                return "Tournament";

            case SimulationMode.HeadToHead:
            default:
                return "Head-to-Head";
        }
    }

    public static string GetTournamentTypeDisplayName(TournamentType tournamentType)
    {
        switch (tournamentType)
        {
            case TournamentType.RoundRobin:
            default:
                return "Round Robin";
        }
    }

    public static void SetPendingSimulationSettings(SimulationSettings settings)
    {
        pendingSimulationSettings = SanitizeSimulationSettings(settings);
        hasPendingSimulationSettings = true;
    }

    public static bool TryConsumePendingSimulationSettings(out SimulationSettings settings)
    {
        settings = SanitizeSimulationSettings(pendingSimulationSettings);
        pendingSimulationSettings = GetDefaultSimulationSettings();
        bool hadPending = hasPendingSimulationSettings;
        hasPendingSimulationSettings = false;
        return hadPending;
    }

    public static bool TryGetInitialTournamentMatchSettings(
        SimulationSettings settings,
        out TurnManager.AIRecruitVariant sideARecruitVariant,
        out TurnManager.AIRecruitVariant sideBRecruitVariant,
        out AILocalDecisionFeatures sideAFeatures,
        out AILocalDecisionFeatures sideBFeatures,
        out TurnManager.AIDebugProfile sideAProfile,
        out TurnManager.AIDebugProfile sideBProfile)
    {
        sideARecruitVariant = TurnManager.AIRecruitVariant.Default;
        sideBRecruitVariant = TurnManager.AIRecruitVariant.Default;
        sideAFeatures = AILocalDecisionFeatures.None;
        sideBFeatures = AILocalDecisionFeatures.None;
        sideAProfile = TurnManager.AIDebugProfile.Baseline;
        sideBProfile = TurnManager.AIDebugProfile.Baseline;

        settings = SanitizeSimulationSettings(settings);
        if (settings.mode != SimulationMode.Tournament)
        {
            return false;
        }

        List<AIVariant> participants = BuildTournamentParticipants(settings.tournamentParticipantMask);
        List<ScheduledMatch> schedule = BuildTournamentSchedule(settings, participants);
        if (schedule.Count <= 0)
        {
            return false;
        }

        ScheduledMatch firstMatch = schedule[0];
        AIVariant runtimeSideA = participants[firstMatch.RuntimeSideAIndex];
        AIVariant runtimeSideB = participants[firstMatch.RuntimeSideBIndex];
        sideARecruitVariant = runtimeSideA.baseModel;
        sideBRecruitVariant = runtimeSideB.baseModel;
        sideAFeatures = runtimeSideA.localFeatures;
        sideBFeatures = runtimeSideB.localFeatures;
        return true;
    }

    public static void BeginNewRun(
        SimulationSettings settings,
        TurnManager.AIRecruitVariant baseSideARecruitVariant,
        TurnManager.AIRecruitVariant baseSideBRecruitVariant,
        AILocalDecisionFeatures baseSideAFeatures,
        AILocalDecisionFeatures baseSideBFeatures,
        TurnManager.AIDebugProfile baseSideAProfile,
        TurnManager.AIDebugProfile baseSideBProfile)
    {
        SimulationSettings sanitizedSettings = SanitizeSimulationSettings(settings);
        activeRun = new ActiveRun
        {
            runId = Guid.NewGuid().ToString("N"),
            settings = sanitizedSettings,
            startedAtUtc = DateTime.UtcNow,
            baseSideARecruitVariant = baseSideARecruitVariant,
            baseSideBRecruitVariant = baseSideBRecruitVariant,
            baseSideAFeatures = baseSideAFeatures,
            baseSideBFeatures = baseSideBFeatures,
            baseSideAProfile = baseSideAProfile,
            baseSideBProfile = baseSideBProfile
        };

        if (sanitizedSettings.mode != SimulationMode.Tournament)
        {
            return;
        }

        activeRun.tournamentParticipants.AddRange(BuildTournamentParticipants(sanitizedSettings.tournamentParticipantMask));
        activeRun.tournamentSchedule.AddRange(BuildTournamentSchedule(sanitizedSettings, activeRun.tournamentParticipants));
        if (activeRun.tournamentParticipants.Count >= 2)
        {
            activeRun.baseSideARecruitVariant = activeRun.tournamentParticipants[0].baseModel;
            activeRun.baseSideAFeatures = activeRun.tournamentParticipants[0].localFeatures;
            activeRun.baseSideAProfile = TurnManager.AIDebugProfile.Baseline;
            activeRun.baseSideBRecruitVariant = activeRun.tournamentParticipants[1].baseModel;
            activeRun.baseSideBFeatures = activeRun.tournamentParticipants[1].localFeatures;
            activeRun.baseSideBProfile = TurnManager.AIDebugProfile.Baseline;
        }
    }

    public static bool TryGetUpcomingMatchSettings(
        out TurnManager.AIRecruitVariant sideARecruitVariant,
        out TurnManager.AIRecruitVariant sideBRecruitVariant,
        out AILocalDecisionFeatures sideAFeatures,
        out AILocalDecisionFeatures sideBFeatures,
        out TurnManager.AIDebugProfile sideAProfile,
        out TurnManager.AIDebugProfile sideBProfile)
    {
        sideARecruitVariant = TurnManager.AIRecruitVariant.Default;
        sideBRecruitVariant = TurnManager.AIRecruitVariant.Default;
        sideAFeatures = AILocalDecisionFeatures.None;
        sideBFeatures = AILocalDecisionFeatures.None;
        sideAProfile = TurnManager.AIDebugProfile.Baseline;
        sideBProfile = TurnManager.AIDebugProfile.Baseline;

        if (activeRun == null)
        {
            return false;
        }

        MatchContext context = BuildMatchContext(activeRun, activeRun.completedMatchCount);
        if (!context.isValid)
        {
            return false;
        }

        sideARecruitVariant = context.runtimeSideA.baseModel;
        sideBRecruitVariant = context.runtimeSideB.baseModel;
        sideAFeatures = context.runtimeSideA.localFeatures;
        sideBFeatures = context.runtimeSideB.localFeatures;
        return true;
    }

    public static void ClearAll()
    {
        pendingSimulationSettings = GetDefaultSimulationSettings();
        hasPendingSimulationSettings = false;
        activeRun = null;
    }

    public static bool TryGetActiveRunSnapshot(out ActiveRunSnapshot snapshot)
    {
        snapshot = default;
        if (activeRun == null)
        {
            return false;
        }

        float elapsedSeconds = Mathf.Max(0f, (float)(DateTime.UtcNow - activeRun.startedAtUtc).TotalSeconds);
        float safeElapsedSeconds = Mathf.Max(0.001f, elapsedSeconds);
        float gamesPerSecond = activeRun.completedMatchCount / safeElapsedSeconds;
        UpdateObservedGamesPerSecond(gamesPerSecond);

        if (activeRun.settings.mode == SimulationMode.Tournament)
        {
            int scheduledGames = activeRun.tournamentSchedule.Count;
            float remainingGames = Mathf.Max(0, scheduledGames - activeRun.completedMatchCount);
            float remainingTime = gamesPerSecond > 0.0001f ? remainingGames / gamesPerSecond : 0f;
            float estimatedTotal = elapsedSeconds + remainingTime;
            snapshot = new ActiveRunSnapshot(
                simulationMode: activeRun.settings.mode,
                completedGames: activeRun.completedMatchCount,
                scheduledGames: scheduledGames,
                scheduledPairings: GetRoundRobinPairingCount(activeRun.tournamentParticipants.Count),
                participantCount: activeRun.tournamentParticipants.Count,
                sideAWins: activeRun.sideAWins,
                sideBWins: activeRun.sideBWins,
                trueDraws: activeRun.trueDraws,
                aborts: activeRun.aborts,
                decisiveGames: Math.Max(0, activeRun.sideAWins + activeRun.sideBWins),
                batchSize: Math.Max(1, activeRun.settings.tournamentGamesPerPairing * (activeRun.settings.tournamentSeatSwap ? 2 : 1)),
                elapsedSeconds: elapsedSeconds,
                timeBudgetSeconds: estimatedTotal,
                remainingTimeSeconds: remainingTime,
                gamesPerSecond: gamesPerSecond,
                bayesianSideABetterProbability: 0.5f,
                certaintyThreshold: 0f,
                decisiveEdgePoints: 0f,
                isWaitingForBatchAfterThreshold: false,
                tournamentStandingsPreview: BuildTournamentStandingsPreview(activeRun, 2));
            return true;
        }

        int decisiveGames = Math.Max(0, activeRun.sideAWins + activeRun.sideBWins);
        float probability = Mathf.Clamp01((float)ComputeSideABetterProbability(
            activeRun.settings.evaluationMethod,
            activeRun.sideAWins,
            activeRun.sideBWins));
        float decisiveEdgePoints = 0f;
        if (decisiveGames > 0)
        {
            float winRateA = activeRun.sideAWins / (float)decisiveGames;
            decisiveEdgePoints = (winRateA - 0.5f) * 100f;
        }

        bool thresholdReached =
            activeRun.completedMatchCount >= activeRun.settings.minimumGames &&
            (probability >= activeRun.settings.certaintyThreshold ||
             probability <= (1f - activeRun.settings.certaintyThreshold));
        bool reachedBatchBoundary =
            IsPairBoundary(activeRun.completedMatchCount) &&
            (activeRun.completedMatchCount % Math.Max(MinPositiveValue, activeRun.settings.batchSize)) == 0;
        snapshot = new ActiveRunSnapshot(
            simulationMode: activeRun.settings.mode,
            completedGames: activeRun.completedMatchCount,
            scheduledGames: 0,
            scheduledPairings: 0,
            participantCount: 2,
            sideAWins: activeRun.sideAWins,
            sideBWins: activeRun.sideBWins,
            trueDraws: activeRun.trueDraws,
            aborts: activeRun.aborts,
            decisiveGames: decisiveGames,
            batchSize: activeRun.settings.batchSize,
            elapsedSeconds: elapsedSeconds,
            timeBudgetSeconds: activeRun.settings.timeBudgetSeconds,
            remainingTimeSeconds: Mathf.Max(0f, activeRun.settings.timeBudgetSeconds - elapsedSeconds),
            gamesPerSecond: gamesPerSecond,
            bayesianSideABetterProbability: probability,
            certaintyThreshold: activeRun.settings.certaintyThreshold,
            decisiveEdgePoints: decisiveEdgePoints,
            isWaitingForBatchAfterThreshold: thresholdReached && !reachedBatchBoundary,
            tournamentStandingsPreview: string.Empty);
        return true;
    }

    public static bool TryGetHudSnapshot(out ActiveRunSnapshot snapshot)
    {
        if (TryGetActiveRunSnapshot(out snapshot))
        {
            return true;
        }

        if (!hasPendingSimulationSettings)
        {
            snapshot = default;
            return false;
        }

        SimulationSettings settings = SanitizeSimulationSettings(pendingSimulationSettings);
        if (settings.mode == SimulationMode.Tournament)
        {
            TournamentEstimate estimate = EstimateTournament(settings, TurnManager.AIVsAIBatchSpeedPreset.UltraFast);
            snapshot = new ActiveRunSnapshot(
                simulationMode: settings.mode,
                completedGames: 0,
                scheduledGames: estimate.totalGames,
                scheduledPairings: estimate.totalPairings,
                participantCount: estimate.participantCount,
                sideAWins: 0,
                sideBWins: 0,
                trueDraws: 0,
                aborts: 0,
                decisiveGames: 0,
                batchSize: Math.Max(1, settings.tournamentGamesPerPairing * (settings.tournamentSeatSwap ? 2 : 1)),
                elapsedSeconds: 0f,
                timeBudgetSeconds: estimate.estimatedRuntimeSeconds,
                remainingTimeSeconds: estimate.estimatedRuntimeSeconds,
                gamesPerSecond: estimate.gamesPerSecondUsed,
                bayesianSideABetterProbability: 0.5f,
                certaintyThreshold: 0f,
                decisiveEdgePoints: 0f,
                isWaitingForBatchAfterThreshold: false,
                tournamentStandingsPreview: "Standings pending");
            return true;
        }

        snapshot = new ActiveRunSnapshot(
            simulationMode: settings.mode,
            completedGames: 0,
            scheduledGames: 0,
            scheduledPairings: 0,
            participantCount: 2,
            sideAWins: 0,
            sideBWins: 0,
            trueDraws: 0,
            aborts: 0,
            decisiveGames: 0,
            batchSize: settings.batchSize,
            elapsedSeconds: 0f,
            timeBudgetSeconds: settings.timeBudgetSeconds,
            remainingTimeSeconds: settings.timeBudgetSeconds,
            gamesPerSecond: 0f,
            bayesianSideABetterProbability: 0.5f,
            certaintyThreshold: settings.certaintyThreshold,
            decisiveEdgePoints: 0f,
            isWaitingForBatchAfterThreshold: false,
            tournamentStandingsPreview: string.Empty);
        return true;
    }

    public static bool TryRecordMatch(
        AIVsAIMatchCsvLogger.MatchResult matchResult,
        out bool isRunComplete,
        out AIVsAIMatchCsvLogger.RunSummary summary)
    {
        isRunComplete = false;
        summary = null;

        if (activeRun == null || matchResult == null)
        {
            return false;
        }

        MatchContext context = BuildMatchContext(activeRun, activeRun.completedMatchCount);
        if (!context.isValid)
        {
            return false;
        }

        activeRun.completedMatchCount++;
        activeRun.totalTurnCount += Math.Max(0, matchResult.totalTurnCount);
        activeRun.appVersion = matchResult.appVersion;
        activeRun.mapSizePreset = matchResult.mapSizePreset;
        activeRun.boardWidth = matchResult.boardWidth;
        activeRun.boardHeight = matchResult.boardHeight;
        activeRun.gameMode = matchResult.gameMode;
        if (string.IsNullOrWhiteSpace(activeRun.sideAAIConfig))
        {
            activeRun.sideAAIConfig = matchResult.sideAAIConfig;
        }

        if (string.IsNullOrWhiteSpace(activeRun.sideBAIConfig))
        {
            activeRun.sideBAIConfig = matchResult.sideBAIConfig;
        }

        if (activeRun.settings.mode == SimulationMode.Tournament)
        {
            switch (matchResult.winner)
            {
                case "SideA":
                    activeRun.sideAWins++;
                    break;

                case "SideB":
                    activeRun.sideBWins++;
                    break;

                default:
                    activeRun.drawsOrAborts++;
                    break;
            }
        }
        else
        {
            switch (MapWinnerToBaseVariant(matchResult.winner, context.seatsWereSwapped))
            {
                case "SideA":
                    activeRun.sideAWins++;
                    break;

                case "SideB":
                    activeRun.sideBWins++;
                    break;

                default:
                    activeRun.drawsOrAborts++;
                    break;
            }
        }

        if (string.Equals(matchResult.winner, "Abort", StringComparison.Ordinal))
        {
            activeRun.aborts++;
        }
        else if (!string.Equals(matchResult.winner, "SideA", StringComparison.Ordinal) &&
                 !string.Equals(matchResult.winner, "SideB", StringComparison.Ordinal))
        {
            activeRun.trueDraws++;
        }

        activeRun.completedMatches.Add(BuildCompletedMatchRecord(matchResult, context));

        matchResult.runId = activeRun.runId;
        matchResult.matchIndexInRun = activeRun.completedMatchCount;
        matchResult.runEmergencyHardMaxGames = activeRun.settings.emergencyHardMaxGames;
        matchResult.simulationMode = activeRun.settings.mode.ToString();
        matchResult.sideAVariantLabel = context.runtimeSideALabel;
        matchResult.sideBVariantLabel = context.runtimeSideBLabel;
        matchResult.pairingLabel = context.pairingLabel;
        matchResult.pairingIndex = context.pairingIndex;
        matchResult.pairingGameIndex = context.pairingGameIndex;
        matchResult.seatsSwapped = context.seatsWereSwapped;
        matchResult.participantCount = activeRun.settings.mode == SimulationMode.Tournament
            ? activeRun.tournamentParticipants.Count
            : 2;

        RunStopDecision decision = EvaluateRunStopDecision(activeRun);
        isRunComplete = decision.shouldStop;
        if (!isRunComplete)
        {
            return true;
        }

        summary = BuildRunSummary(activeRun, decision);
        activeRun = null;
        return true;
    }

    private static AIVsAIMatchCsvLogger.RunSummary BuildRunSummary(ActiveRun run, RunStopDecision decision)
    {
        if (run == null)
        {
            return null;
        }

        int completedMatches = Math.Max(1, run.completedMatchCount);
        int decisiveGames = Math.Max(0, run.sideAWins + run.sideBWins);
        float elapsedSeconds = Mathf.Max(0.001f, (float)(DateTime.UtcNow - run.startedAtUtc).TotalSeconds);
        float turnsPerSecond = run.totalTurnCount / elapsedSeconds;
        UpdateObservedGamesPerSecond(run.completedMatchCount / elapsedSeconds);

        AIVsAIMatchCsvLogger.RunSummary summary = new AIVsAIMatchCsvLogger.RunSummary
        {
            timestampUtc = DateTime.UtcNow.ToString("o"),
            runId = run.runId,
            appVersion = run.appVersion,
            mapSizePreset = run.mapSizePreset,
            boardWidth = run.boardWidth,
            boardHeight = run.boardHeight,
            gameMode = run.gameMode,
            sideAAIConfig = run.sideAAIConfig,
            sideBAIConfig = run.sideBAIConfig,
            baseSideARecruitVariant = run.baseSideARecruitVariant,
            baseSideBRecruitVariant = run.baseSideBRecruitVariant,
            baseSideAFeatures = run.baseSideAFeatures,
            baseSideBFeatures = run.baseSideBFeatures,
            baseSideAProfile = run.baseSideAProfile,
            baseSideBProfile = run.baseSideBProfile,
            matchCount = run.completedMatchCount,
            sideAWins = run.sideAWins,
            sideBWins = run.sideBWins,
            drawsOrAborts = run.drawsOrAborts,
            trueDraws = run.trueDraws,
            aborts = run.aborts,
            elapsedSeconds = elapsedSeconds,
            turnsPerSecond = turnsPerSecond,
            averageTotalTurnCount = run.totalTurnCount / (float)completedMatches,
            simulationPreset = run.settings.preset,
            simulationSettingsLabel = GetPresetDisplayName(run.settings.preset),
            evaluationMethod = run.settings.evaluationMethod,
            evaluationMethodLabel = GetEvaluationMethodDisplayName(run.settings.evaluationMethod),
            certaintyThreshold = run.settings.certaintyThreshold,
            minimumGames = run.settings.minimumGames,
            batchSize = run.settings.batchSize,
            timeBudgetSeconds = run.settings.timeBudgetSeconds,
            emergencyHardMaxGames = run.settings.emergencyHardMaxGames,
            bayesianDecisiveGames = decisiveGames,
            bayesianSideABetterProbability = Mathf.Clamp01((float)decision.sideABetterProbability),
            runEndedNormally = decision.completionKind == RunCompletionKind.NormalCompleted,
            stopReason = decision.stopReason.ToString(),
            simulationMode = run.settings.mode,
            tournamentType = run.settings.tournamentType,
            tournamentParticipantCount = run.tournamentParticipants.Count,
            tournamentScheduledPairingCount = GetRoundRobinPairingCount(run.tournamentParticipants.Count),
            tournamentScheduledGameCount = run.tournamentSchedule.Count,
            tournamentGamesPerPairing = run.settings.tournamentGamesPerPairing,
            tournamentSeatSwapEnabled = run.settings.tournamentSeatSwap
        };

        if (run.settings.mode == SimulationMode.Tournament)
        {
            summary.simulationSettingsLabel = GetTournamentTypeDisplayName(run.settings.tournamentType);
            summary.evaluationMethodLabel = "Fixed Schedule";
            PopulateTournamentStats(run, summary);
            return summary;
        }

        float sideAScoreRate = (float)((run.sideAWins + (0.5d * run.drawsOrAborts)) / completedMatches);
        summary.sideAWinRate = run.sideAWins / (float)completedMatches;
        summary.sideAScoreRate = sideAScoreRate;
        summary.sideAEffectSize = sideAScoreRate - 0.5f;
        PopulateComparisonStats(run, summary);
        return summary;
    }

    private static ActiveRun.CompletedMatchRecord BuildCompletedMatchRecord(
        AIVsAIMatchCsvLogger.MatchResult matchResult,
        MatchContext context)
    {
        ActiveRun.CompletedMatchRecord record = new ActiveRun.CompletedMatchRecord
        {
            pairingIndex = context.pairingIndex,
            pairingGameIndex = context.pairingGameIndex,
            seatsWereSwapped = context.seatsWereSwapped,
            logicalVariantAIndex = context.logicalVariantAIndex,
            logicalVariantBIndex = context.logicalVariantBIndex,
            logicalVariantALabel = context.logicalVariantALabel,
            logicalVariantBLabel = context.logicalVariantBLabel
        };

        if (matchResult == null)
        {
            return record;
        }

        record.isAbort = string.Equals(matchResult.winner, "Abort", StringComparison.Ordinal);
        record.isDraw = !record.isAbort &&
                        !string.Equals(matchResult.winner, "SideA", StringComparison.Ordinal) &&
                        !string.Equals(matchResult.winner, "SideB", StringComparison.Ordinal);
        record.player1Score = GetSeatScore(matchResult.winner, 0);
        record.player2Score = GetSeatScore(matchResult.winner, 1);
        record.baseSideAScore = GetBaseSideAScore(matchResult.winner, context.seatsWereSwapped);
        record.logicalVariantAScore = GetLogicalVariantScore(matchResult.winner, context.seatsWereSwapped, logicalVariantA: true);
        record.logicalVariantBScore = GetLogicalVariantScore(matchResult.winner, context.seatsWereSwapped, logicalVariantA: false);
        return record;
    }

    private static MatchContext BuildMatchContext(ActiveRun run, int matchIndex)
    {
        if (run == null)
        {
            return default;
        }

        if (run.settings.mode == SimulationMode.Tournament)
        {
            if (matchIndex < 0 || matchIndex >= run.tournamentSchedule.Count)
            {
                return default;
            }

            ScheduledMatch scheduledMatch = run.tournamentSchedule[matchIndex];
            if (scheduledMatch.logicalVariantAIndex < 0 ||
                scheduledMatch.logicalVariantAIndex >= run.tournamentParticipants.Count ||
                scheduledMatch.logicalVariantBIndex < 0 ||
                scheduledMatch.logicalVariantBIndex >= run.tournamentParticipants.Count)
            {
                return default;
            }

            AIVariant logicalVariantA = run.tournamentParticipants[scheduledMatch.logicalVariantAIndex];
            AIVariant logicalVariantB = run.tournamentParticipants[scheduledMatch.logicalVariantBIndex];
            AIVariant runtimeSideA = run.tournamentParticipants[scheduledMatch.RuntimeSideAIndex];
            AIVariant runtimeSideB = run.tournamentParticipants[scheduledMatch.RuntimeSideBIndex];
            return new MatchContext(
                isValid: true,
                seatsWereSwapped: scheduledMatch.seatsWereSwapped,
                pairingIndex: scheduledMatch.pairingIndex,
                pairingGameIndex: scheduledMatch.pairingGameIndex,
                logicalVariantAIndex: scheduledMatch.logicalVariantAIndex,
                logicalVariantBIndex: scheduledMatch.logicalVariantBIndex,
                logicalVariantA: logicalVariantA,
                logicalVariantB: logicalVariantB,
                runtimeSideA: runtimeSideA,
                runtimeSideB: runtimeSideB,
                logicalVariantALabel: GetVariantLabel(logicalVariantA),
                logicalVariantBLabel: GetVariantLabel(logicalVariantB));
        }

        bool seatsWereSwapped = IsSeatSwappedForMatchIndex(matchIndex + 1);
        AIVariant logicalHeadToHeadA = new AIVariant(run.baseSideARecruitVariant, run.baseSideAFeatures);
        AIVariant logicalHeadToHeadB = new AIVariant(run.baseSideBRecruitVariant, run.baseSideBFeatures);
        AIVariant runtimeHeadToHeadA = seatsWereSwapped ? logicalHeadToHeadB : logicalHeadToHeadA;
        AIVariant runtimeHeadToHeadB = seatsWereSwapped ? logicalHeadToHeadA : logicalHeadToHeadB;
        return new MatchContext(
            isValid: true,
            seatsWereSwapped: seatsWereSwapped,
            pairingIndex: (matchIndex / 2) + 1,
            pairingGameIndex: seatsWereSwapped ? 2 : 1,
            logicalVariantAIndex: 0,
            logicalVariantBIndex: 1,
            logicalVariantA: logicalHeadToHeadA,
            logicalVariantB: logicalHeadToHeadB,
            runtimeSideA: runtimeHeadToHeadA,
            runtimeSideB: runtimeHeadToHeadB,
            logicalVariantALabel: GetVariantLabel(logicalHeadToHeadA),
            logicalVariantBLabel: GetVariantLabel(logicalHeadToHeadB));
    }

    private static bool IsSeatSwappedForMatchIndex(int matchIndexInRun)
    {
        return matchIndexInRun > 0 && (matchIndexInRun % 2) == 0;
    }

    public static bool IsPairBoundary(int completedMatches)
    {
        return completedMatches > 0 && (completedMatches % 2) == 0;
    }

    public static int GetCompletedPairs(int completedMatches)
    {
        return Math.Max(0, completedMatches / 2);
    }

    private static string MapWinnerToBaseVariant(string runtimeWinner, bool seatsWereSwapped)
    {
        if (!seatsWereSwapped)
        {
            return runtimeWinner;
        }

        if (string.Equals(runtimeWinner, "SideA", StringComparison.Ordinal))
        {
            return "SideB";
        }

        if (string.Equals(runtimeWinner, "SideB", StringComparison.Ordinal))
        {
            return "SideA";
        }

        return runtimeWinner;
    }

    private static double GetBaseSideAScore(string runtimeWinner, bool seatsWereSwapped)
    {
        if (string.Equals(runtimeWinner, "Abort", StringComparison.Ordinal))
        {
            return 0.5d;
        }

        string baseVariantWinner = MapWinnerToBaseVariant(runtimeWinner, seatsWereSwapped);
        if (string.Equals(baseVariantWinner, "SideA", StringComparison.Ordinal))
        {
            return 1d;
        }

        if (string.Equals(baseVariantWinner, "SideB", StringComparison.Ordinal))
        {
            return 0d;
        }

        return 0.5d;
    }

    private static double GetLogicalVariantScore(string runtimeWinner, bool seatsWereSwapped, bool logicalVariantA)
    {
        if (string.Equals(runtimeWinner, "Abort", StringComparison.Ordinal))
        {
            return 0.5d;
        }

        string logicalWinner = MapWinnerToBaseVariant(runtimeWinner, seatsWereSwapped);
        if (string.Equals(logicalWinner, "SideA", StringComparison.Ordinal))
        {
            return logicalVariantA ? 1d : 0d;
        }

        if (string.Equals(logicalWinner, "SideB", StringComparison.Ordinal))
        {
            return logicalVariantA ? 0d : 1d;
        }

        return 0.5d;
    }

    private static RunStopDecision EvaluateRunStopDecision(ActiveRun run)
    {
        if (run == null)
        {
            return new RunStopDecision(false, RunCompletionKind.None, StopReason.None, 0.5d);
        }

        if (!HasValidRecordedCounts(run))
        {
            return new RunStopDecision(
                shouldStop: true,
                completionKind: RunCompletionKind.AbnormalAborted,
                stopReason: StopReason.InvalidStatsDetected,
                sideABetterProbability: 0.5d);
        }

        if (run.settings.mode == SimulationMode.Tournament)
        {
            if (run.completedMatchCount >= run.tournamentSchedule.Count)
            {
                return new RunStopDecision(
                    shouldStop: true,
                    completionKind: RunCompletionKind.NormalCompleted,
                    stopReason: StopReason.TournamentScheduleCompleted,
                    sideABetterProbability: 0.5d);
            }

            return new RunStopDecision(
                shouldStop: false,
                completionKind: RunCompletionKind.None,
                stopReason: StopReason.None,
                sideABetterProbability: 0.5d);
        }

        double sideABetterProbability = ComputeSideABetterProbability(
            run.settings.evaluationMethod,
            run.sideAWins,
            run.sideBWins);

        if (double.IsNaN(sideABetterProbability) ||
            double.IsInfinity(sideABetterProbability) ||
            sideABetterProbability < 0d ||
            sideABetterProbability > 1d)
        {
            return new RunStopDecision(
                shouldStop: true,
                completionKind: RunCompletionKind.AbnormalAborted,
                stopReason: StopReason.InvalidStatsDetected,
                sideABetterProbability: sideABetterProbability);
        }

        bool reachedPairBoundary = IsPairBoundary(run.completedMatchCount);
        if (run.completedMatchCount >= run.settings.emergencyHardMaxGames && reachedPairBoundary)
        {
            return new RunStopDecision(
                shouldStop: true,
                completionKind: RunCompletionKind.AbnormalAborted,
                stopReason: StopReason.EmergencySafetyFuseTriggered,
                sideABetterProbability: sideABetterProbability);
        }

        if (!reachedPairBoundary)
        {
            return new RunStopDecision(
                shouldStop: false,
                completionKind: RunCompletionKind.None,
                stopReason: StopReason.None,
                sideABetterProbability: sideABetterProbability);
        }

        bool reachedBatchBoundary = (run.completedMatchCount % Math.Max(MinPositiveValue, run.settings.batchSize)) == 0;
        if (reachedBatchBoundary && run.completedMatchCount >= run.settings.minimumGames)
        {
            if (sideABetterProbability >= run.settings.certaintyThreshold)
            {
                return new RunStopDecision(
                    shouldStop: true,
                    completionKind: RunCompletionKind.NormalCompleted,
                    stopReason: StopReason.CertaintyReached_A_Better,
                    sideABetterProbability: sideABetterProbability);
            }

            if (sideABetterProbability <= (1d - run.settings.certaintyThreshold))
            {
                return new RunStopDecision(
                    shouldStop: true,
                    completionKind: RunCompletionKind.NormalCompleted,
                    stopReason: StopReason.CertaintyReached_A_Worse,
                    sideABetterProbability: sideABetterProbability);
            }
        }

        float elapsedSeconds = Mathf.Max(0f, (float)(DateTime.UtcNow - run.startedAtUtc).TotalSeconds);
        if (elapsedSeconds >= run.settings.timeBudgetSeconds)
        {
            return new RunStopDecision(
                shouldStop: true,
                completionKind: RunCompletionKind.NormalCompleted,
                stopReason: StopReason.TimeBudgetReached,
                sideABetterProbability: sideABetterProbability);
        }

        return new RunStopDecision(
            shouldStop: false,
            completionKind: RunCompletionKind.None,
            stopReason: StopReason.None,
            sideABetterProbability: sideABetterProbability);
    }

    private static bool HasValidRecordedCounts(ActiveRun run)
    {
        if (run == null)
        {
            return false;
        }

        if (run.completedMatchCount < 0 ||
            run.sideAWins < 0 ||
            run.sideBWins < 0 ||
            run.drawsOrAborts < 0 ||
            run.trueDraws < 0 ||
            run.aborts < 0 ||
            run.totalTurnCount < 0)
        {
            return false;
        }

        if (run.trueDraws + run.aborts != run.drawsOrAborts)
        {
            return false;
        }

        if (run.sideAWins + run.sideBWins + run.drawsOrAborts != run.completedMatchCount)
        {
            return false;
        }

        if (run.completedMatches.Count != run.completedMatchCount)
        {
            return false;
        }

        if (run.settings.mode == SimulationMode.Tournament)
        {
            return run.tournamentParticipants.Count >= MinTournamentParticipantCount &&
                   run.tournamentSchedule.Count > 0;
        }

        return true;
    }

    private static int EnsureEvenPairSize(int value)
    {
        int sanitizedValue = Math.Max(MinPositiveValue, value);
        return (sanitizedValue % 2) == 0
            ? sanitizedValue
            : sanitizedValue + 1;
    }

    private static double ComputeSideABetterProbability(
        EvaluationMethod evaluationMethod,
        int sideAWins,
        int sideBWins)
    {
        switch (evaluationMethod)
        {
            case EvaluationMethod.Bayesian:
            default:
                double alpha = 1d + Math.Max(0, sideAWins);
                double beta = 1d + Math.Max(0, sideBWins);
                int decisiveGames = Math.Max(0, sideAWins + sideBWins);
                if (decisiveGames >= BayesianNormalApproximationThreshold)
                {
                    double mean = alpha / (alpha + beta);
                    double variance = (alpha * beta) / (((alpha + beta) * (alpha + beta)) * (alpha + beta + 1d));
                    if (variance <= 0d)
                    {
                        return mean > 0.5d ? 1d : mean < 0.5d ? 0d : 0.5d;
                    }

                    double standardDeviation = Math.Sqrt(variance);
                    return Math.Max(0d, Math.Min(1d, NormalCdf((mean - 0.5d) / standardDeviation)));
                }

                double probability = 1d - RegularizedIncompleteBeta(alpha, beta, 0.5d);
                return Math.Max(0d, Math.Min(1d, probability));
        }
    }

    private static double GetSeatScore(string winner, int trackedSeatIndex)
    {
        if (string.Equals(winner, "Abort", StringComparison.Ordinal))
        {
            return 0.5d;
        }

        if (string.Equals(winner, "SideA", StringComparison.Ordinal))
        {
            return trackedSeatIndex == 0 ? 1d : 0d;
        }

        if (string.Equals(winner, "SideB", StringComparison.Ordinal))
        {
            return trackedSeatIndex == 1 ? 1d : 0d;
        }

        return 0.5d;
    }

    private static void PopulateComparisonStats(ActiveRun run, AIVsAIMatchCsvLogger.RunSummary summary)
    {
        if (run == null || summary == null)
        {
            return;
        }

        List<double> pairScoreRates = new List<double>();
        int seat1Wins = 0;
        int seat1Draws = 0;
        int seat1Losses = 0;
        int seat2Wins = 0;
        int seat2Draws = 0;
        int seat2Losses = 0;
        bool isSeatBiasControl =
            run.baseSideAProfile == run.baseSideBProfile &&
            run.baseSideARecruitVariant == run.baseSideBRecruitVariant &&
            run.baseSideAFeatures == run.baseSideBFeatures;

        summary.pairedStatsApplicable = true;
        summary.comparisonMode = isSeatBiasControl ? "seat_bias_control" : "paired_head_to_head";
        summary.trackedEntityLabel = isSeatBiasControl ? "Runtime Side A" : "Side A Variant";
        summary.seat1Label = "Runtime Side A";
        summary.seat2Label = "Runtime Side B";

        for (int i = 0; i < run.completedMatches.Count; i++)
        {
            ActiveRun.CompletedMatchRecord record = run.completedMatches[i];
            summary.seat1GameCount++;
            summary.seat2GameCount++;
            if (record.player1Score >= 0.999d)
            {
                seat1Wins++;
            }
            else if (record.player1Score <= 0.001d)
            {
                seat1Losses++;
            }
            else
            {
                seat1Draws++;
            }

            if (record.player2Score >= 0.999d)
            {
                seat2Wins++;
            }
            else if (record.player2Score <= 0.001d)
            {
                seat2Losses++;
            }
            else
            {
                seat2Draws++;
            }
        }

        summary.seat1Wins = seat1Wins;
        summary.seat1Draws = seat1Draws;
        summary.seat1Losses = seat1Losses;
        summary.seat2Wins = seat2Wins;
        summary.seat2Draws = seat2Draws;
        summary.seat2Losses = seat2Losses;

        if (summary.seat1GameCount > 0)
        {
            summary.seat1ScoreRate = (float)((seat1Wins + 0.5d * seat1Draws) / summary.seat1GameCount);
            summary.seat1EffectSize = summary.seat1ScoreRate - 0.5f;
        }

        if (summary.seat2GameCount > 0)
        {
            summary.seat2ScoreRate = (float)((seat2Wins + 0.5d * seat2Draws) / summary.seat2GameCount);
            summary.seat2EffectSize = summary.seat2ScoreRate - 0.5f;
        }

        summary.seatEffectSize = summary.seat1ScoreRate - summary.seat2ScoreRate;

        for (int pairStart = 0; pairStart + 1 < run.completedMatches.Count; pairStart += 2)
        {
            ActiveRun.CompletedMatchRecord first = run.completedMatches[pairStart];
            ActiveRun.CompletedMatchRecord second = run.completedMatches[pairStart + 1];
            double firstTrackedScore = isSeatBiasControl ? first.player1Score : first.baseSideAScore;
            double secondTrackedScore = isSeatBiasControl ? second.player1Score : second.baseSideAScore;
            double pairScoreRate = (firstTrackedScore + secondTrackedScore) * 0.5d;
            pairScoreRates.Add(pairScoreRate);

            if (pairScoreRate >= 0.999d)
            {
                summary.pairedAFavoredCount++;
            }
            else if (pairScoreRate <= 0.001d)
            {
                summary.pairedBFavoredCount++;
            }
            else
            {
                summary.pairedSplitCount++;
            }
        }

        summary.completePairCount = pairScoreRates.Count;
        summary.unmatchedIgnoredGameCount = Math.Max(0, run.completedMatches.Count - pairScoreRates.Count * 2);

        if (pairScoreRates.Count == 0)
        {
            summary.pairedThreshold = "insufficient";
            return;
        }

        double pairMean = ComputeMean(pairScoreRates);
        summary.pairedMeanScoreRate = (float)pairMean;
        summary.pairedEffectSize = (float)(pairMean - 0.5d);

        if (pairScoreRates.Count < 2)
        {
            summary.pairedThreshold = "insufficient";
            return;
        }

        summary.pairedPValue = (float)ComputeTwoSidedOneSampleTTestPValue(pairScoreRates, 0.5d);
        summary.pairedThreshold = GetThresholdLabel(summary.pairedPValue);
    }

    private static void PopulateTournamentStats(ActiveRun run, AIVsAIMatchCsvLogger.RunSummary summary)
    {
        if (run == null || summary == null)
        {
            return;
        }

        summary.comparisonMode = "round_robin_tournament";
        summary.trackedEntityLabel = "Tournament Variant";
        summary.seat1Label = "Runtime Side A";
        summary.seat2Label = "Runtime Side B";
        summary.pairedStatsApplicable = false;

        List<TournamentStanding> standings = BuildSortedTournamentStandings(run);

        Dictionary<int, TournamentPairingAggregate> pairings = new Dictionary<int, TournamentPairingAggregate>();
        int seat1Wins = 0;
        int seat1Draws = 0;
        int seat1Losses = 0;
        int seat2Wins = 0;
        int seat2Draws = 0;
        int seat2Losses = 0;

        for (int i = 0; i < run.completedMatches.Count; i++)
        {
            ActiveRun.CompletedMatchRecord record = run.completedMatches[i];
            summary.seat1GameCount++;
            summary.seat2GameCount++;
            if (record.player1Score >= 0.999d)
            {
                seat1Wins++;
            }
            else if (record.player1Score <= 0.001d)
            {
                seat1Losses++;
            }
            else
            {
                seat1Draws++;
            }

            if (record.player2Score >= 0.999d)
            {
                seat2Wins++;
            }
            else if (record.player2Score <= 0.001d)
            {
                seat2Losses++;
            }
            else
            {
                seat2Draws++;
            }

            if (!pairings.TryGetValue(record.pairingIndex, out TournamentPairingAggregate aggregate))
            {
                aggregate = new TournamentPairingAggregate
                {
                    pairingLabel = $"{record.logicalVariantALabel} vs {record.logicalVariantBLabel}",
                    logicalVariantALabel = record.logicalVariantALabel,
                    logicalVariantBLabel = record.logicalVariantBLabel
                };
                pairings.Add(record.pairingIndex, aggregate);
            }

            aggregate.games++;
            aggregate.logicalVariantAScoreSum += record.logicalVariantAScore;
            if (record.seatsWereSwapped)
            {
                aggregate.swappedGames++;
            }

            if (record.isAbort)
            {
                aggregate.aborts++;
            }
            else if (record.isDraw)
            {
                aggregate.draws++;
            }
            else if (record.logicalVariantAScore > record.logicalVariantBScore)
            {
                aggregate.logicalVariantAWins++;
            }
            else
            {
                aggregate.logicalVariantBWins++;
            }
        }

        summary.seat1Wins = seat1Wins;
        summary.seat1Draws = seat1Draws;
        summary.seat1Losses = seat1Losses;
        summary.seat2Wins = seat2Wins;
        summary.seat2Draws = seat2Draws;
        summary.seat2Losses = seat2Losses;
        if (summary.seat1GameCount > 0)
        {
            summary.seat1ScoreRate = (float)((seat1Wins + 0.5d * seat1Draws) / summary.seat1GameCount);
            summary.seat1EffectSize = summary.seat1ScoreRate - 0.5f;
        }

        if (summary.seat2GameCount > 0)
        {
            summary.seat2ScoreRate = (float)((seat2Wins + 0.5d * seat2Draws) / summary.seat2GameCount);
            summary.seat2EffectSize = summary.seat2ScoreRate - 0.5f;
        }

        summary.seatEffectSize = summary.seat1ScoreRate - summary.seat2ScoreRate;

        if (standings.Count > 0)
        {
            TournamentStanding winner = standings[0];
            summary.tournamentWinnerLabel = winner.label;
            summary.sideAWinRate = winner.games > 0 ? winner.wins / (float)winner.games : 0f;
            summary.sideAScoreRate = winner.games > 0 ? (float)(winner.scoreSum / winner.games) : 0f;
            summary.sideAEffectSize = summary.sideAScoreRate - 0.5f;
        }

        StringBuilder standingsBuilder = new StringBuilder();
        for (int i = 0; i < standings.Count; i++)
        {
            TournamentStanding standing = standings[i];
            double scoreRate = standing.games > 0 ? standing.scoreSum / standing.games : 0d;
            if (i > 0)
            {
                standingsBuilder.Append('\n');
            }

            standingsBuilder.Append(i + 1);
            standingsBuilder.Append(". ");
            standingsBuilder.Append(standing.label);
            standingsBuilder.Append(" | W");
            standingsBuilder.Append(standing.wins);
            standingsBuilder.Append(" L");
            standingsBuilder.Append(standing.losses);
            standingsBuilder.Append(" D");
            standingsBuilder.Append(standing.draws);
            if (standing.aborts > 0)
            {
                standingsBuilder.Append(" A");
                standingsBuilder.Append(standing.aborts);
            }
            standingsBuilder.Append(" | Score ");
            standingsBuilder.Append(scoreRate.ToString("P1"));
        }

        summary.tournamentStandingsSummary = standingsBuilder.ToString();

        List<int> pairingKeys = new List<int>(pairings.Keys);
        pairingKeys.Sort();
        StringBuilder pairingsBuilder = new StringBuilder();
        for (int i = 0; i < pairingKeys.Count; i++)
        {
            TournamentPairingAggregate aggregate = pairings[pairingKeys[i]];
            if (i > 0)
            {
                pairingsBuilder.Append('\n');
            }

            double scoreRate = aggregate.games > 0 ? aggregate.logicalVariantAScoreSum / aggregate.games : 0d;
            pairingsBuilder.Append(aggregate.pairingLabel);
            pairingsBuilder.Append(": ");
            pairingsBuilder.Append(aggregate.logicalVariantALabel);
            pairingsBuilder.Append(' ');
            pairingsBuilder.Append(aggregate.logicalVariantAWins);
            pairingsBuilder.Append(" | ");
            pairingsBuilder.Append(aggregate.logicalVariantBLabel);
            pairingsBuilder.Append(' ');
            pairingsBuilder.Append(aggregate.logicalVariantBWins);
            pairingsBuilder.Append(" | D");
            pairingsBuilder.Append(aggregate.draws);
            if (aggregate.aborts > 0)
            {
                pairingsBuilder.Append(" | A");
                pairingsBuilder.Append(aggregate.aborts);
            }
            pairingsBuilder.Append(" | Score ");
            pairingsBuilder.Append(scoreRate.ToString("P1"));
            if (summary.tournamentSeatSwapEnabled)
            {
                pairingsBuilder.Append(" | Swapped ");
                pairingsBuilder.Append(aggregate.swappedGames);
            }
        }

        summary.tournamentPairingSummary = pairingsBuilder.ToString();

        StringBuilder participantsBuilder = new StringBuilder();
        for (int i = 0; i < run.tournamentParticipants.Count; i++)
        {
            if (i > 0)
            {
                participantsBuilder.Append(" | ");
            }

            participantsBuilder.Append(GetVariantLabel(run.tournamentParticipants[i]));
        }

        summary.tournamentParticipantsSummary = participantsBuilder.ToString();
    }

    private static List<TournamentStanding> BuildSortedTournamentStandings(ActiveRun run)
    {
        List<TournamentStanding> standings = new List<TournamentStanding>();
        if (run == null)
        {
            return standings;
        }

        standings = new List<TournamentStanding>(run.tournamentParticipants.Count);
        for (int i = 0; i < run.tournamentParticipants.Count; i++)
        {
            standings.Add(new TournamentStanding
            {
                label = GetVariantLabel(run.tournamentParticipants[i])
            });
        }

        for (int i = 0; i < run.completedMatches.Count; i++)
        {
            ActiveRun.CompletedMatchRecord record = run.completedMatches[i];
            TournamentStanding standingA = standings[record.logicalVariantAIndex];
            TournamentStanding standingB = standings[record.logicalVariantBIndex];
            standingA.games++;
            standingB.games++;
            standingA.scoreSum += record.logicalVariantAScore;
            standingB.scoreSum += record.logicalVariantBScore;

            if (record.isAbort)
            {
                standingA.aborts++;
                standingB.aborts++;
            }
            else if (record.isDraw)
            {
                standingA.draws++;
                standingB.draws++;
            }
            else if (record.logicalVariantAScore > record.logicalVariantBScore)
            {
                standingA.wins++;
                standingB.losses++;
            }
            else
            {
                standingA.losses++;
                standingB.wins++;
            }
        }

        standings.Sort((left, right) =>
        {
            double leftScoreRate = left.games > 0 ? left.scoreSum / left.games : 0d;
            double rightScoreRate = right.games > 0 ? right.scoreSum / right.games : 0d;
            int comparison = rightScoreRate.CompareTo(leftScoreRate);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = right.wins.CompareTo(left.wins);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.losses.CompareTo(right.losses);
            if (comparison != 0)
            {
                return comparison;
            }

            return string.Compare(left.label, right.label, StringComparison.Ordinal);
        });

        return standings;
    }

    private static string BuildTournamentStandingsPreview(ActiveRun run, int maxEntries)
    {
        List<TournamentStanding> standings = BuildSortedTournamentStandings(run);
        if (standings.Count <= 0)
        {
            return "Standings pending";
        }

        int previewCount = Math.Max(1, Math.Min(maxEntries, standings.Count));
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < previewCount; i++)
        {
            TournamentStanding standing = standings[i];
            double scoreRate = standing.games > 0 ? standing.scoreSum / standing.games : 0d;
            if (i > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(i + 1);
            builder.Append(". ");
            builder.Append(standing.label);
            builder.Append(' ');
            builder.Append(scoreRate.ToString("P0"));
            builder.Append(" W");
            builder.Append(standing.wins);
        }

        return builder.ToString();
    }

    private static double ComputeMean(List<double> values)
    {
        if (values == null || values.Count == 0)
        {
            return 0d;
        }

        double sum = 0d;
        for (int i = 0; i < values.Count; i++)
        {
            sum += values[i];
        }

        return sum / values.Count;
    }

    private static double ComputeTwoSidedOneSampleTTestPValue(List<double> values, double nullMean)
    {
        int sampleCount = values != null ? values.Count : 0;
        if (sampleCount < 2)
        {
            return 1d;
        }

        double mean = ComputeMean(values);
        double varianceSum = 0d;
        for (int i = 0; i < values.Count; i++)
        {
            double delta = values[i] - mean;
            varianceSum += delta * delta;
        }

        double sampleVariance = varianceSum / (sampleCount - 1);
        if (sampleVariance <= 0d)
        {
            return Math.Abs(mean - nullMean) <= 1e-9d ? 1d : 0d;
        }

        double standardError = Math.Sqrt(sampleVariance / sampleCount);
        if (standardError <= 0d)
        {
            return Math.Abs(mean - nullMean) <= 1e-9d ? 1d : 0d;
        }

        double tStatistic = (mean - nullMean) / standardError;
        double cumulativeProbability = StudentTCdf(tStatistic, sampleCount - 1);
        double tailProbability = Math.Min(cumulativeProbability, 1d - cumulativeProbability);
        return Math.Max(0d, Math.Min(1d, tailProbability * 2d));
    }

    private static string GetThresholdLabel(float pValue)
    {
        if (pValue <= 0.01f)
        {
            return "99%";
        }

        if (pValue <= 0.05f)
        {
            return "95%";
        }

        if (pValue <= 0.10f)
        {
            return "90%";
        }

        return "none";
    }

    private static double StudentTCdf(double tStatistic, int degreesOfFreedom)
    {
        if (degreesOfFreedom <= 0)
        {
            return 0.5d;
        }

        if (Math.Abs(tStatistic) <= double.Epsilon)
        {
            return 0.5d;
        }

        double x = degreesOfFreedom / (degreesOfFreedom + (tStatistic * tStatistic));
        double regularizedBeta = RegularizedIncompleteBeta(0.5d * degreesOfFreedom, 0.5d, x);
        return tStatistic > 0d
            ? 1d - (0.5d * regularizedBeta)
            : 0.5d * regularizedBeta;
    }

    private static double RegularizedIncompleteBeta(double a, double b, double x)
    {
        if (x <= 0d)
        {
            return 0d;
        }

        if (x >= 1d)
        {
            return 1d;
        }

        double betaTerm = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) + (a * Math.Log(x)) + (b * Math.Log(1d - x)));
        if (x < (a + 1d) / (a + b + 2d))
        {
            return betaTerm * BetaContinuedFraction(a, b, x) / a;
        }

        return 1d - (betaTerm * BetaContinuedFraction(b, a, 1d - x) / b);
    }

    private static double BetaContinuedFraction(double a, double b, double x)
    {
        const int maxIterations = 200;
        const double epsilon = 3e-7d;
        const double minValue = 1e-30d;

        double qab = a + b;
        double qap = a + 1d;
        double qam = a - 1d;
        double c = 1d;
        double d = 1d - (qab * x / qap);
        if (Math.Abs(d) < minValue)
        {
            d = minValue;
        }

        d = 1d / d;
        double h = d;
        for (int iteration = 1; iteration <= maxIterations; iteration++)
        {
            int m2 = 2 * iteration;
            double aa = iteration * (b - iteration) * x / ((qam + m2) * (a + m2));
            d = 1d + (aa * d);
            if (Math.Abs(d) < minValue)
            {
                d = minValue;
            }

            c = 1d + (aa / c);
            if (Math.Abs(c) < minValue)
            {
                c = minValue;
            }

            d = 1d / d;
            h *= d * c;

            aa = -(a + iteration) * (qab + iteration) * x / ((a + m2) * (qap + m2));
            d = 1d + (aa * d);
            if (Math.Abs(d) < minValue)
            {
                d = minValue;
            }

            c = 1d + (aa / c);
            if (Math.Abs(c) < minValue)
            {
                c = minValue;
            }

            d = 1d / d;
            double delta = d * c;
            h *= delta;
            if (Math.Abs(delta - 1d) < epsilon)
            {
                break;
            }
        }

        return h;
    }

    private static double NormalCdf(double zScore)
    {
        return 0.5d * (1d + Erf(zScore / Math.Sqrt(2d)));
    }

    private static double Erf(double x)
    {
        double sign = x < 0d ? -1d : 1d;
        double absoluteX = Math.Abs(x);
        double t = 1d / (1d + (0.3275911d * absoluteX));
        double polynomial =
            (((((1.061405429d * t) - 1.453152027d) * t) + 1.421413741d) * t - 0.284496736d) * t + 0.254829592d;
        double approximation = 1d - (polynomial * t * Math.Exp(-(absoluteX * absoluteX)));
        return sign * approximation;
    }

    private static double LogGamma(double value)
    {
        double[] coefficients =
        {
            76.18009172947146d,
            -86.50532032941677d,
            24.01409824083091d,
            -1.231739572450155d,
            0.001208650973866179d,
            -0.000005395239384953d
        };

        double x = value;
        double y = value;
        double tmp = x + 5.5d;
        tmp -= (x + 0.5d) * Math.Log(tmp);
        double series = 1.000000000190015d;

        for (int i = 0; i < coefficients.Length; i++)
        {
            y += 1d;
            series += coefficients[i] / y;
        }

        return -tmp + Math.Log(2.5066282746310005d * series / x);
    }

    private static float SanitizeFloat(float value, float fallback, float minValue, float maxValue)
    {
        float sanitized = float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        return Mathf.Clamp(sanitized, minValue, maxValue);
    }

    private static int SanitizeInt(int value, int minValue)
    {
        int sanitized = value <= 0 ? minValue : value;
        return Math.Max(minValue, sanitized);
    }

    private static int SanitizeTournamentParticipantMask(int participantMask)
    {
        return participantMask & AllGeneratedVariantsMask;
    }

    private static int CountEnabledVariants(int participantMask)
    {
        int count = 0;
        for (int i = 0; i < GeneratedVariantPool.Length; i++)
        {
            if ((participantMask & (1 << i)) != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static float GetEstimatedGamesPerSecond(TurnManager.AIVsAIBatchSpeedPreset speedPreset)
    {
        if (lastObservedGamesPerSecond > 0.0001f)
        {
            return lastObservedGamesPerSecond;
        }

        switch (speedPreset)
        {
            case TurnManager.AIVsAIBatchSpeedPreset.Normal:
                return DefaultNormalGamesPerSecond;

            case TurnManager.AIVsAIBatchSpeedPreset.Fast:
                return DefaultFastGamesPerSecond;

            case TurnManager.AIVsAIBatchSpeedPreset.VeryFast:
                return DefaultVeryFastGamesPerSecond;

            case TurnManager.AIVsAIBatchSpeedPreset.UltraFast:
            default:
                return DefaultUltraFastGamesPerSecond;
        }
    }

    private static void UpdateObservedGamesPerSecond(float gamesPerSecond)
    {
        if (gamesPerSecond > 0.0001f && !float.IsNaN(gamesPerSecond) && !float.IsInfinity(gamesPerSecond))
        {
            lastObservedGamesPerSecond = gamesPerSecond;
        }
    }

    private static List<AIVariant> BuildTournamentParticipants(int participantMask)
    {
        int sanitizedMask = SanitizeTournamentParticipantMask(participantMask);
        List<AIVariant> participants = new List<AIVariant>(GeneratedVariantPool.Length);
        for (int i = 0; i < GeneratedVariantPool.Length; i++)
        {
            if ((sanitizedMask & (1 << i)) == 0)
            {
                continue;
            }

            participants.Add(GeneratedVariantPool[i]);
        }

        return participants;
    }

    private static List<ScheduledMatch> BuildTournamentSchedule(SimulationSettings settings, List<AIVariant> participants)
    {
        List<ScheduledMatch> schedule = new List<ScheduledMatch>();
        if (participants == null || participants.Count < MinTournamentParticipantCount)
        {
            return schedule;
        }

        int pairingIndex = 0;
        int gamesPerPairing = Math.Max(MinPositiveValue, settings.tournamentGamesPerPairing);
        bool seatSwap = settings.tournamentSeatSwap;

        for (int variantAIndex = 0; variantAIndex < participants.Count; variantAIndex++)
        {
            for (int variantBIndex = variantAIndex + 1; variantBIndex < participants.Count; variantBIndex++)
            {
                pairingIndex++;
                for (int gameIndex = 1; gameIndex <= gamesPerPairing; gameIndex++)
                {
                    schedule.Add(new ScheduledMatch(
                        pairingIndex,
                        gameIndex,
                        variantAIndex,
                        variantBIndex,
                        seatsWereSwapped: false));
                    if (seatSwap)
                    {
                        schedule.Add(new ScheduledMatch(
                            pairingIndex,
                            gameIndex,
                            variantAIndex,
                            variantBIndex,
                            seatsWereSwapped: true));
                    }
                }
            }
        }

        return schedule;
    }

    private static int GetRoundRobinPairingCount(int participantCount)
    {
        participantCount = Math.Max(0, participantCount);
        return participantCount < 2
            ? 0
            : (participantCount * (participantCount - 1)) / 2;
    }

    private static AIVariant[] BuildGeneratedVariantPool()
    {
        List<AIVariant> variants = new List<AIVariant>(GeneratedVariantPoolCount);
        TurnManager.AIRecruitVariant[] baseModels =
        {
            TurnManager.AIRecruitVariant.Default,
            TurnManager.AIRecruitVariant.RiderFocus
        };

        for (int modelIndex = 0; modelIndex < baseModels.Length; modelIndex++)
        {
            for (int featureMask = 0; featureMask < 8; featureMask++)
            {
                AILocalDecisionFeatures features = AILocalDecisionFeatures.None;
                if ((featureMask & 1) != 0)
                {
                    features |= AILocalDecisionFeatures.OffensiveObviousWin;
                }

                if ((featureMask & 2) != 0)
                {
                    features |= AILocalDecisionFeatures.ExchangeScoring;
                }

                if ((featureMask & 4) != 0)
                {
                    features |= AILocalDecisionFeatures.DefensiveVeto;
                }

                variants.Add(new AIVariant(baseModels[modelIndex], features));
            }
        }

        return variants.ToArray();
    }

    private static string GetFeatureMaskLabel(AILocalDecisionFeatures features)
    {
        if (features == AILocalDecisionFeatures.None)
        {
            return "None";
        }

        List<string> labels = new List<string>(3);
        if ((features & AILocalDecisionFeatures.OffensiveObviousWin) != 0)
        {
            labels.Add("Offense");
        }

        if ((features & AILocalDecisionFeatures.ExchangeScoring) != 0)
        {
            labels.Add("Exchange");
        }

        if ((features & AILocalDecisionFeatures.DefensiveVeto) != 0)
        {
            labels.Add("Defense");
        }

        return labels.Count > 0
            ? string.Join(" + ", labels)
            : "None";
    }
}
