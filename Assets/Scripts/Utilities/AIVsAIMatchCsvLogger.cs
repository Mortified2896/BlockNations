using System;
using System.IO;
using System.Text;
using UnityEngine;

public static class AIVsAIMatchCsvLogger
{
    private const string RootFolderName = "DevMatchResults";
    private const string MatchResultsFileName = "ai_vs_ai_match_results.csv";
    private const string RunSummaryFileName = "ai_vs_ai_run_summaries.csv";
    private const string MatchHeader =
        "timestampUtc,runId,matchIndexInRun,plannedMatchCountInRun,appVersion,mapSizePreset,boardWidth,boardHeight,gameMode,sideAAIConfig,sideBAIConfig,winner,totalTurnCount,sideAFinalCityCount,sideBFinalCityCount,sideAFinalUnitCount,sideBFinalUnitCount";
    private const string RunSummaryHeader =
        "timestampUtc,runId,appVersion,mapSizePreset,boardWidth,boardHeight,gameMode,sideAAIConfig,sideBAIConfig,matchCount,sideAWins,sideBWins,drawsOrAborts,sideAWinRate,averageTotalTurnCount";

    public sealed class MatchResult
    {
        public string timestampUtc;
        public string runId;
        public int matchIndexInRun;
        public int plannedMatchCountInRun;
        public string appVersion;
        public string mapSizePreset;
        public int boardWidth;
        public int boardHeight;
        public string gameMode;
        public string sideAAIConfig;
        public string sideBAIConfig;
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
        public int matchCount;
        public int sideAWins;
        public int sideBWins;
        public int drawsOrAborts;
        public float sideAWinRate;
        public float averageTotalTurnCount;
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
            result.plannedMatchCountInRun.ToString(),
            Escape(result.appVersion),
            Escape(result.mapSizePreset),
            result.boardWidth.ToString(),
            result.boardHeight.ToString(),
            Escape(result.gameMode),
            Escape(result.sideAAIConfig),
            Escape(result.sideBAIConfig),
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
            summary.sideAWinRate.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
            summary.averageTotalTurnCount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
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
}
