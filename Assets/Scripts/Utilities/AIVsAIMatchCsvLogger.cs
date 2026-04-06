using System;
using System.IO;
using System.Text;
using UnityEngine;

public static class AIVsAIMatchCsvLogger
{
    private const string RootFolderName = "DevMatchResults";
    private const string FileName = "ai_vs_ai_results.csv";
    private const string Header =
        "timestampUtc,appVersion,mapSizePreset,boardWidth,boardHeight,gameMode,sideAAIConfig,sideBAIConfig,winner,totalTurnCount,sideAFinalCityCount,sideBFinalCityCount,sideAFinalUnitCount,sideBFinalUnitCount";

    public sealed class MatchResult
    {
        public string timestampUtc;
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

    public static string GetResultsFilePath()
    {
        return Path.Combine(Application.persistentDataPath, RootFolderName, FileName);
    }

    public static bool TryAppendResult(MatchResult result)
    {
        if (result == null)
        {
            return false;
        }

        try
        {
            string path = GetResultsFilePath();
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            bool writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
            using StreamWriter writer = new StreamWriter(path, append: true, Encoding.UTF8);
            if (writeHeader)
            {
                writer.WriteLine(Header);
            }

            writer.WriteLine(BuildCsvRow(result));
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

    private static string BuildCsvRow(MatchResult result)
    {
        return string.Join(",",
            Escape(result.timestampUtc),
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
