using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class AIVsAIMatchCsvLogger
{
    private const string RootFolderName = "DevMatchResults";
    private const string MatchResultsFileName = "ai_vs_ai_match_results_v4.csv";
    private const string RunSummaryFileName = "ai_vs_ai_run_summaries_v4.csv";
    private const string MatchHeader =
        "timestampUtc,runId,matchIndexInRun,runEmergencyHardMaxGames,appVersion,mapSizePreset,boardWidth,boardHeight,gameMode,sideAAIConfig,sideBAIConfig,sideAProfile,sideBProfile,winner,totalTurnCount,sideAFinalCityCount,sideBFinalCityCount,sideAFinalUnitCount,sideBFinalUnitCount";
    private const string RunSummaryHeader =
        "timestampUtc,runId,appVersion,mapSizePreset,boardWidth,boardHeight,gameMode,sideAAIConfig,sideBAIConfig,matchCount,sideAWins,sideBWins,drawsOrAborts,trueDraws,aborts,elapsedSeconds,turnsPerSecond,sideAWinRate,sideAScoreRate,sideAEffectSize,averageTotalTurnCount,simulationPreset,simulationSettingsLabel,evaluationMethod,evaluationMethodLabel,certaintyThreshold,minimumGames,batchSize,timeBudgetSeconds,emergencyHardMaxGames,bayesianDecisiveGames,bayesianSideABetterProbability,runEndedNormally,stopReason,comparisonMode,trackedEntityLabel,seat1Label,seat2Label,pairedStatsApplicable,completePairCount,unmatchedIgnoredGameCount,pairedAFavoredCount,pairedSplitCount,pairedBFavoredCount,pairedMeanScoreRate,pairedEffectSize,pairedPValue,pairedThreshold,seatEffectSize,seat1GameCount,seat1Wins,seat1Draws,seat1Losses,seat1ScoreRate,seat1EffectSize,seat2GameCount,seat2Wins,seat2Draws,seat2Losses,seat2ScoreRate,seat2EffectSize";

    public sealed class MatchResult
    {
        public string timestampUtc;
        public string runId;
        public int matchIndexInRun;
        public int runEmergencyHardMaxGames;
        public string appVersion;
        public string mapSizePreset;
        public int boardWidth;
        public int boardHeight;
        public string gameMode;
        public string sideAAIConfig;
        public string sideBAIConfig;
        public string sideAProfile;
        public string sideBProfile;
        public string winner;
        public int totalTurnCount;
        public int sideAFinalCityCount;
        public int sideBFinalCityCount;
        public int sideAFinalUnitCount;
        public int sideBFinalUnitCount;
    }

    public sealed class RunSummary
    {
        public string timestampUtc;
        public string runId;
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
        public int matchCount;
        public int sideAWins;
        public int sideBWins;
        public int drawsOrAborts;
        public int trueDraws;
        public int aborts;
        public float elapsedSeconds;
        public float turnsPerSecond;
        public float sideAWinRate;
        public float sideAScoreRate;
        public float sideAEffectSize;
        public float averageTotalTurnCount;
        public AIVsAIBatchRunController.SimulationPreset simulationPreset;
        public string simulationSettingsLabel;
        public AIVsAIBatchRunController.EvaluationMethod evaluationMethod;
        public string evaluationMethodLabel;
        public float certaintyThreshold;
        public int minimumGames;
        public int batchSize;
        public float timeBudgetSeconds;
        public int emergencyHardMaxGames;
        public int bayesianDecisiveGames;
        public float bayesianSideABetterProbability;
        public bool runEndedNormally;
        public string stopReason;
        public string comparisonMode;
        public string trackedEntityLabel;
        public string seat1Label;
        public string seat2Label;
        public bool pairedStatsApplicable;
        public int completePairCount;
        public int unmatchedIgnoredGameCount;
        public int pairedAFavoredCount;
        public int pairedSplitCount;
        public int pairedBFavoredCount;
        public float pairedMeanScoreRate;
        public float pairedEffectSize;
        public float pairedPValue = -1f;
        public string pairedThreshold;
        public float seatEffectSize;
        public int seat1GameCount;
        public int seat1Wins;
        public int seat1Draws;
        public int seat1Losses;
        public float seat1ScoreRate;
        public float seat1EffectSize;
        public int seat2GameCount;
        public int seat2Wins;
        public int seat2Draws;
        public int seat2Losses;
        public float seat2ScoreRate;
        public float seat2EffectSize;
    }

    public static string GetResultsFilePath()
    {
        return Path.Combine(Application.persistentDataPath, RootFolderName, MatchResultsFileName);
    }

    public static string GetRunSummaryFilePath()
    {
        return Path.Combine(Application.persistentDataPath, RootFolderName, RunSummaryFileName);
    }

    public static bool TryAppendResult(MatchResult result)
    {
        if (result == null)
        {
            return false;
        }

        try
        {
            AppendLine(GetResultsFilePath(), MatchHeader, BuildCsvRow(result));
            return true;
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"[AIVsAICsv] Failed writing CSV row: {ex.Message}");
#endif
            return false;
        }
    }

    public static bool TryAppendRunSummary(RunSummary summary)
    {
        if (summary == null)
        {
            return false;
        }

        try
        {
            AppendLine(GetRunSummaryFilePath(), RunSummaryHeader, BuildSummaryCsvRow(summary));
            return true;
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"[AIVsAICsv] Failed writing summary row: {ex.Message}");
#endif
            return false;
        }
    }

    private static string BuildCsvRow(MatchResult result)
    {
        return string.Join(",",
            Escape(result.timestampUtc),
            Escape(result.runId),
            result.matchIndexInRun.ToString(),
            result.runEmergencyHardMaxGames.ToString(),
            Escape(result.appVersion),
            Escape(result.mapSizePreset),
            result.boardWidth.ToString(),
            result.boardHeight.ToString(),
            Escape(result.gameMode),
            Escape(result.sideAAIConfig),
            Escape(result.sideBAIConfig),
            Escape(result.sideAProfile),
            Escape(result.sideBProfile),
            Escape(result.winner),
            result.totalTurnCount.ToString(),
            result.sideAFinalCityCount.ToString(),
            result.sideBFinalCityCount.ToString(),
            result.sideAFinalUnitCount.ToString(),
            result.sideBFinalUnitCount.ToString());
    }

    private static string BuildSummaryCsvRow(RunSummary summary)
    {
        return string.Join(",",
            Escape(summary.timestampUtc),
            Escape(summary.runId),
            Escape(summary.appVersion),
            Escape(summary.mapSizePreset),
            summary.boardWidth.ToString(),
            summary.boardHeight.ToString(),
            Escape(summary.gameMode),
            Escape(summary.sideAAIConfig),
            Escape(summary.sideBAIConfig),
            summary.matchCount.ToString(),
            summary.sideAWins.ToString(),
            summary.sideBWins.ToString(),
            summary.drawsOrAborts.ToString(),
            summary.trueDraws.ToString(),
            summary.aborts.ToString(),
            FormatFloat(summary.elapsedSeconds, 2),
            FormatFloat(summary.turnsPerSecond, 2),
            FormatFloat(summary.sideAWinRate, 4),
            FormatFloat(summary.sideAScoreRate, 4),
            FormatFloat(summary.sideAEffectSize, 4),
            FormatFloat(summary.averageTotalTurnCount, 2),
            Escape(summary.simulationPreset.ToString()),
            Escape(summary.simulationSettingsLabel),
            Escape(summary.evaluationMethod.ToString()),
            Escape(summary.evaluationMethodLabel),
            FormatFloat(summary.certaintyThreshold, 4),
            summary.minimumGames.ToString(),
            summary.batchSize.ToString(),
            FormatFloat(summary.timeBudgetSeconds, 2),
            summary.emergencyHardMaxGames.ToString(),
            summary.bayesianDecisiveGames.ToString(),
            FormatFloat(summary.bayesianSideABetterProbability, 4),
            summary.runEndedNormally ? "true" : "false",
            Escape(summary.stopReason),
            Escape(summary.comparisonMode),
            Escape(summary.trackedEntityLabel),
            Escape(summary.seat1Label),
            Escape(summary.seat2Label),
            summary.pairedStatsApplicable ? "true" : "false",
            summary.completePairCount.ToString(),
            summary.unmatchedIgnoredGameCount.ToString(),
            summary.pairedAFavoredCount.ToString(),
            summary.pairedSplitCount.ToString(),
            summary.pairedBFavoredCount.ToString(),
            FormatFloat(summary.pairedMeanScoreRate, 4),
            FormatFloat(summary.pairedEffectSize, 4),
            summary.pairedPValue >= 0f ? FormatFloat(summary.pairedPValue, 4) : string.Empty,
            Escape(summary.pairedThreshold),
            FormatFloat(summary.seatEffectSize, 4),
            summary.seat1GameCount.ToString(),
            summary.seat1Wins.ToString(),
            summary.seat1Draws.ToString(),
            summary.seat1Losses.ToString(),
            FormatFloat(summary.seat1ScoreRate, 4),
            FormatFloat(summary.seat1EffectSize, 4),
            summary.seat2GameCount.ToString(),
            summary.seat2Wins.ToString(),
            summary.seat2Draws.ToString(),
            summary.seat2Losses.ToString(),
            FormatFloat(summary.seat2ScoreRate, 4),
            FormatFloat(summary.seat2EffectSize, 4));
    }

    private static void AppendLine(string path, string header, string line)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        bool writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
        using StreamWriter writer = new StreamWriter(path, append: true, Encoding.UTF8);
        if (writeHeader)
        {
            writer.WriteLine(header);
        }

        writer.WriteLine(line);
    }

    private static string Escape(string value)
    {
        string safeValue = value ?? string.Empty;
        if (safeValue.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
        {
            return safeValue;
        }

        return "\"" + safeValue.Replace("\"", "\"\"") + "\"";
    }

    private static string FormatFloat(float value, int decimals)
    {
        string format = decimals <= 2 ? "0.00" : "0.0000";
        return value.ToString(format, CultureInfo.InvariantCulture);
    }
}
