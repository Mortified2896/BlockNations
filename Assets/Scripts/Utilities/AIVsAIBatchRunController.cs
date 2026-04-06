using System;

public static class AIVsAIBatchRunController
{
    private const int DefaultRequestedMatchCount = 1;

    private static int pendingRequestedMatchCount = DefaultRequestedMatchCount;
    private static bool hasPendingRequestedMatchCount;
    private static ActiveRun activeRun;

    private sealed class ActiveRun
    {
        public string runId;
        public int plannedMatchCount;
        public int completedMatchCount;
        public int sideAWins;
        public int sideBWins;
        public int drawsOrAborts;
        public int totalTurnCount;
        public string appVersion;
        public string mapSizePreset;
        public int boardWidth;
        public int boardHeight;
        public string gameMode;
        public string sideAAIConfig;
        public string sideBAIConfig;
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

    public static void BeginNewRun(int requestedMatchCount)
    {
        activeRun = new ActiveRun
        {
            runId = Guid.NewGuid().ToString("N"),
            plannedMatchCount = Math.Max(DefaultRequestedMatchCount, requestedMatchCount)
        };
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

        matchResult.runId = activeRun.runId;
        matchResult.matchIndexInRun = activeRun.completedMatchCount;
        matchResult.plannedMatchCountInRun = activeRun.plannedMatchCount;

        isRunComplete = activeRun.completedMatchCount >= activeRun.plannedMatchCount;
        if (!isRunComplete)
        {
            return true;
        }

        int completedMatches = Math.Max(1, activeRun.completedMatchCount);
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
            matchCount = activeRun.completedMatchCount,
            sideAWins = activeRun.sideAWins,
            sideBWins = activeRun.sideBWins,
            drawsOrAborts = activeRun.drawsOrAborts,
            sideAWinRate = activeRun.sideAWins / (float)completedMatches,
            averageTotalTurnCount = activeRun.totalTurnCount / (float)completedMatches
        };

        activeRun = null;
        return true;
    }
}
