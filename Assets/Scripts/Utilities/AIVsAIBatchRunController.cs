using System;
using System.Collections.Generic;
using UnityEngine;

public static class AIVsAIBatchRunController
{
    private const int DefaultRequestedMatchCount = 1;

    private static int pendingRequestedMatchCount = DefaultRequestedMatchCount;
    private static bool hasPendingRequestedMatchCount;
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
        public int plannedMatchCount;
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

    public static bool HasActiveRun => activeRun != null;

    public static void SetPendingRequestedMatchCount(int requestedMatchCount)
    {
        pendingRequestedMatchCount = Math.Max(DefaultRequestedMatchCount, requestedMatchCount);
        hasPendingRequestedMatchCount = true;
    }

    public static bool TryConsumePendingRequestedMatchCount(out int requestedMatchCount)
    {
        requestedMatchCount = Math.Max(DefaultRequestedMatchCount, pendingRequestedMatchCount);
        pendingRequestedMatchCount = DefaultRequestedMatchCount;
        bool hadPending = hasPendingRequestedMatchCount;
        hasPendingRequestedMatchCount = false;
        return hadPending;
    }

    public static void BeginNewRun(
        int requestedMatchCount,
        TurnManager.AIRecruitVariant baseSideARecruitVariant,
        TurnManager.AIRecruitVariant baseSideBRecruitVariant,
        TurnManager.AIDebugProfile baseSideAProfile,
        TurnManager.AIDebugProfile baseSideBProfile)
    {
        activeRun = new ActiveRun
        {
            runId = Guid.NewGuid().ToString("N"),
            plannedMatchCount = Math.Max(DefaultRequestedMatchCount, requestedMatchCount),
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
        pendingRequestedMatchCount = DefaultRequestedMatchCount;
        hasPendingRequestedMatchCount = false;
        activeRun = null;
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
        activeRun.sideAAIConfig = matchResult.sideAAIConfig;
        activeRun.sideBAIConfig = matchResult.sideBAIConfig;

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
        matchResult.plannedMatchCountInRun = activeRun.plannedMatchCount;

        isRunComplete = activeRun.completedMatchCount >= activeRun.plannedMatchCount;
        if (!isRunComplete)
        {
            return true;
        }

        int completedMatches = Math.Max(1, activeRun.completedMatchCount);
        float elapsedSeconds = Mathf.Max(0.001f, (float)(DateTime.UtcNow - activeRun.startedAtUtc).TotalSeconds);
        summary = new AIVsAIMatchCsvLogger.RunSummary
        {
            timestampUtc = DateTime.UtcNow.ToString("o"),
            runId = activeRun.runId,
            appVersion = activeRun.appVersion,
            mapSizePreset = activeRun.mapSizePreset,
            boardWidth = activeRun.boardWidth,
            boardHeight = activeRun.boardHeight,
            gameMode = activeRun.gameMode,
            sideAAIConfig = activeRun.sideAAIConfig,
            sideBAIConfig = activeRun.sideBAIConfig,
            baseSideARecruitVariant = activeRun.baseSideARecruitVariant,
            baseSideBRecruitVariant = activeRun.baseSideBRecruitVariant,
            baseSideAProfile = activeRun.baseSideAProfile,
            baseSideBProfile = activeRun.baseSideBProfile,
            matchCount = activeRun.completedMatchCount,
            sideAWins = activeRun.sideAWins,
            sideBWins = activeRun.sideBWins,
            drawsOrAborts = activeRun.drawsOrAborts,
            trueDraws = activeRun.trueDraws,
            aborts = activeRun.aborts,
            elapsedSeconds = elapsedSeconds,
            turnsPerSecond = activeRun.totalTurnCount / elapsedSeconds,
            sideAWinRate = activeRun.sideAWins / (float)completedMatches,
            averageTotalTurnCount = activeRun.totalTurnCount / (float)completedMatches
        };
        PopulateComparisonStats(activeRun, summary);

        activeRun = null;
        return true;
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
}
