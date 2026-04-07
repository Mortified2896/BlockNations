using System;
using System.Collections.Generic;
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

    public enum StopReason
    {
        None = 0,
        CertaintyReached_A_Better = 1,
        CertaintyReached_A_Worse = 2,
        TimeBudgetReached = 3,
        EmergencySafetyFuseTriggered = 4,
        InvalidStatsDetected = 5
    }

    public enum RunCompletionKind
    {
        None = 0,
        NormalCompleted = 1,
        AbnormalAborted = 2
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
    }

    public readonly struct ActiveRunSnapshot
    {
        public readonly int completedGames;
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

        public ActiveRunSnapshot(
            int completedGames,
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
            float bayesianSideABetterProbability)
        {
            this.completedGames = completedGames;
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
        }
    }

    private const float QuickExplorationTimeBudgetSeconds = 120f;
    private const float StandardComparisonTimeBudgetSeconds = 300f;
    private const float StrictComparisonTimeBudgetSeconds = 900f;
    private const float MinCertaintyThreshold = 0.5f;
    private const float MaxCertaintyThreshold = 0.9999f;
    private const float MinTimeBudgetSeconds = 1f;
    private const int MinPositiveValue = 1;
    private const int BayesianNormalApproximationThreshold = 200;

    private static SimulationSettings pendingSimulationSettings = GetDefaultSimulationSettings();
    private static bool hasPendingSimulationSettings;
    private static ActiveRun activeRun;

    private sealed class ActiveRun
    {
        public sealed class CompletedMatchRecord
        {
            public bool hasTrackedPerspective;
            public int trackedSeatIndex;
            public double trackedScore;
            public double player1Score;
            public double player2Score;
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
        public TurnManager.AIDebugProfile baseSideAProfile;
        public TurnManager.AIDebugProfile baseSideBProfile;
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

    public static bool HasActiveRun => activeRun != null;

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
                    emergencyHardMaxGames = 5000
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
                    emergencyHardMaxGames = 25000
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
                    emergencyHardMaxGames = 10000
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
            settings.emergencyHardMaxGames <= 0;

        if (missingAllNumericSettings)
        {
            return fallback;
        }

        settings.preset = sanitizedPreset;
        settings.evaluationMethod = EvaluationMethod.Bayesian;
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

    public static void BeginNewRun(
        SimulationSettings settings,
        TurnManager.AIRecruitVariant baseSideARecruitVariant,
        TurnManager.AIRecruitVariant baseSideBRecruitVariant,
        TurnManager.AIDebugProfile baseSideAProfile,
        TurnManager.AIDebugProfile baseSideBProfile)
    {
        activeRun = new ActiveRun
        {
            runId = Guid.NewGuid().ToString("N"),
            settings = SanitizeSimulationSettings(settings),
            startedAtUtc = DateTime.UtcNow,
            baseSideARecruitVariant = baseSideARecruitVariant,
            baseSideBRecruitVariant = baseSideBRecruitVariant,
            baseSideAProfile = baseSideAProfile,
            baseSideBProfile = baseSideBProfile
        };
    }

    public static bool TryGetUpcomingMatchSettings(
        out TurnManager.AIRecruitVariant sideARecruitVariant,
        out TurnManager.AIRecruitVariant sideBRecruitVariant,
        out TurnManager.AIDebugProfile sideAProfile,
        out TurnManager.AIDebugProfile sideBProfile)
    {
        sideARecruitVariant = TurnManager.AIRecruitVariant.Default;
        sideBRecruitVariant = TurnManager.AIRecruitVariant.Default;
        sideAProfile = TurnManager.AIDebugProfile.Baseline;
        sideBProfile = TurnManager.AIDebugProfile.Baseline;

        if (activeRun == null)
        {
            return false;
        }

        bool shouldSwapSeats = ((activeRun.completedMatchCount + 1) % 2) == 0;
        if (shouldSwapSeats)
        {
            sideARecruitVariant = activeRun.baseSideBRecruitVariant;
            sideBRecruitVariant = activeRun.baseSideARecruitVariant;
            sideAProfile = activeRun.baseSideBProfile;
            sideBProfile = activeRun.baseSideAProfile;
            return true;
        }

        sideARecruitVariant = activeRun.baseSideARecruitVariant;
        sideBRecruitVariant = activeRun.baseSideBRecruitVariant;
        sideAProfile = activeRun.baseSideAProfile;
        sideBProfile = activeRun.baseSideBProfile;
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

        int decisiveGames = Math.Max(0, activeRun.sideAWins + activeRun.sideBWins);
        float elapsedSeconds = Mathf.Max(0f, (float)(DateTime.UtcNow - activeRun.startedAtUtc).TotalSeconds);
        float safeElapsedSeconds = Mathf.Max(0.001f, elapsedSeconds);
        float probability = Mathf.Clamp01((float)ComputeSideABetterProbability(
            activeRun.settings.evaluationMethod,
            activeRun.sideAWins,
            activeRun.sideBWins));
        snapshot = new ActiveRunSnapshot(
            activeRun.completedMatchCount,
            activeRun.sideAWins,
            activeRun.sideBWins,
            activeRun.trueDraws,
            activeRun.aborts,
            decisiveGames,
            activeRun.settings.batchSize,
            elapsedSeconds,
            activeRun.settings.timeBudgetSeconds,
            Mathf.Max(0f, activeRun.settings.timeBudgetSeconds - elapsedSeconds),
            activeRun.completedMatchCount / safeElapsedSeconds,
            probability);
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

        if (string.Equals(matchResult.winner, "Abort", StringComparison.Ordinal))
        {
            activeRun.aborts++;
        }
        else if (!string.Equals(matchResult.winner, "SideA", StringComparison.Ordinal) &&
                 !string.Equals(matchResult.winner, "SideB", StringComparison.Ordinal))
        {
            activeRun.trueDraws++;
        }

        activeRun.completedMatches.Add(BuildCompletedMatchRecord(matchResult));

        matchResult.runId = activeRun.runId;
        matchResult.matchIndexInRun = activeRun.completedMatchCount;
        matchResult.runEmergencyHardMaxGames = activeRun.settings.emergencyHardMaxGames;

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
        float sideAScoreRate = (float)((run.sideAWins + (0.5d * run.drawsOrAborts)) / completedMatches);

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
            baseSideAProfile = run.baseSideAProfile,
            baseSideBProfile = run.baseSideBProfile,
            matchCount = run.completedMatchCount,
            sideAWins = run.sideAWins,
            sideBWins = run.sideBWins,
            drawsOrAborts = run.drawsOrAborts,
            trueDraws = run.trueDraws,
            aborts = run.aborts,
            elapsedSeconds = elapsedSeconds,
            turnsPerSecond = run.totalTurnCount / elapsedSeconds,
            sideAWinRate = run.sideAWins / (float)completedMatches,
            sideAScoreRate = sideAScoreRate,
            sideAEffectSize = sideAScoreRate - 0.5f,
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
            stopReason = decision.stopReason.ToString()
        };

        PopulateComparisonStats(run, summary);
        return summary;
    }

    private static ActiveRun.CompletedMatchRecord BuildCompletedMatchRecord(AIVsAIMatchCsvLogger.MatchResult matchResult)
    {
        ActiveRun.CompletedMatchRecord record = new ActiveRun.CompletedMatchRecord();
        if (matchResult == null)
        {
            return record;
        }

        bool sideAIsCalculus = string.Equals(matchResult.sideAProfile, TurnManager.AIDebugProfile.Calculus.ToString(), StringComparison.Ordinal);
        bool sideBIsCalculus = string.Equals(matchResult.sideBProfile, TurnManager.AIDebugProfile.Calculus.ToString(), StringComparison.Ordinal);
        bool sideAIsBaseline = string.Equals(matchResult.sideAProfile, TurnManager.AIDebugProfile.Baseline.ToString(), StringComparison.Ordinal);
        bool sideBIsBaseline = string.Equals(matchResult.sideBProfile, TurnManager.AIDebugProfile.Baseline.ToString(), StringComparison.Ordinal);
        if ((sideAIsCalculus && sideBIsBaseline) || (sideBIsCalculus && sideAIsBaseline))
        {
            record.hasTrackedPerspective = true;
            record.trackedSeatIndex = sideAIsCalculus ? 0 : 1;
            record.trackedScore = GetSeatScore(matchResult.winner, record.trackedSeatIndex);
        }

        record.isAbort = string.Equals(matchResult.winner, "Abort", StringComparison.Ordinal);
        record.isDraw = !record.isAbort &&
                        !string.Equals(matchResult.winner, "SideA", StringComparison.Ordinal) &&
                        !string.Equals(matchResult.winner, "SideB", StringComparison.Ordinal);
        record.player1Score = GetSeatScore(matchResult.winner, 0);
        record.player2Score = GetSeatScore(matchResult.winner, 1);
        return record;
    }

    private static RunStopDecision EvaluateRunStopDecision(ActiveRun run)
    {
        if (run == null)
        {
            return new RunStopDecision(false, RunCompletionKind.None, StopReason.None, 0.5d);
        }

        double sideABetterProbability = ComputeSideABetterProbability(
            run.settings.evaluationMethod,
            run.sideAWins,
            run.sideBWins);

        if (!HasValidRecordedCounts(run) ||
            double.IsNaN(sideABetterProbability) ||
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

        if (run.completedMatchCount >= run.settings.emergencyHardMaxGames)
        {
            return new RunStopDecision(
                shouldStop: true,
                completionKind: RunCompletionKind.AbnormalAborted,
                stopReason: StopReason.EmergencySafetyFuseTriggered,
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

        return true;
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

        bool isCalculusVsBaseline =
            run.baseSideAProfile != run.baseSideBProfile &&
            ((run.baseSideAProfile == TurnManager.AIDebugProfile.Calculus && run.baseSideBProfile == TurnManager.AIDebugProfile.Baseline) ||
             (run.baseSideBProfile == TurnManager.AIDebugProfile.Calculus && run.baseSideAProfile == TurnManager.AIDebugProfile.Baseline));
        bool isSameProfileControl = run.baseSideAProfile == run.baseSideBProfile;

        if (!isCalculusVsBaseline && !isSameProfileControl)
        {
            summary.comparisonMode = "not_applicable";
            summary.pairedThreshold = "n/a";
            return;
        }

        summary.pairedStatsApplicable = true;
        summary.comparisonMode = isCalculusVsBaseline ? "profile_comparison" : "seat_bias_control";
        summary.trackedEntityLabel = isCalculusVsBaseline ? TurnManager.AIDebugProfile.Calculus.ToString() : "Player 1";
        summary.seat1Label = isCalculusVsBaseline ? "Calculus as Player 1" : "Player 1";
        summary.seat2Label = isCalculusVsBaseline ? "Calculus as Player 2" : "Player 2";

        List<double> pairScoreRates = new List<double>();
        int seat1Wins = 0;
        int seat1Draws = 0;
        int seat1Losses = 0;
        int seat2Wins = 0;
        int seat2Draws = 0;
        int seat2Losses = 0;

        for (int i = 0; i < run.completedMatches.Count; i++)
        {
            ActiveRun.CompletedMatchRecord record = run.completedMatches[i];
            if (isCalculusVsBaseline && !record.hasTrackedPerspective)
            {
                continue;
            }

            if (isCalculusVsBaseline)
            {
                if (record.trackedSeatIndex == 0)
                {
                    summary.seat1GameCount++;
                    if (record.trackedScore >= 0.999d)
                    {
                        seat1Wins++;
                    }
                    else if (record.trackedScore <= 0.001d)
                    {
                        seat1Losses++;
                    }
                    else
                    {
                        seat1Draws++;
                    }
                }
                else
                {
                    summary.seat2GameCount++;
                    if (record.trackedScore >= 0.999d)
                    {
                        seat2Wins++;
                    }
                    else if (record.trackedScore <= 0.001d)
                    {
                        seat2Losses++;
                    }
                    else
                    {
                        seat2Draws++;
                    }
                }
                continue;
            }

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
            if (isCalculusVsBaseline)
            {
                if (!first.hasTrackedPerspective || !second.hasTrackedPerspective)
                {
                    continue;
                }

                if (first.trackedSeatIndex == second.trackedSeatIndex)
                {
                    continue;
                }

                pairScoreRates.Add((first.trackedScore + second.trackedScore) * 0.5d);
            }
            else
            {
                pairScoreRates.Add((first.player1Score + second.player1Score) * 0.5d);
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
}
