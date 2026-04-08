using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class MatchSnapshotHistorySettings
{
    private const string EnabledKeyPrefix = "debug_snapshot_history_enabled_";

    public static void SetEnabled(string gameId, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return;
        }

        string key = BuildEnabledKey(gameId);
        if (enabled)
        {
            PlayerPrefs.SetInt(key, 1);
        }
        else
        {
            PlayerPrefs.DeleteKey(key);
        }

        PlayerPrefs.Save();
    }

    public static bool IsEnabled(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        return PlayerPrefs.GetInt(BuildEnabledKey(gameId), 0) == 1;
    }

    private static string BuildEnabledKey(string gameId)
    {
        return EnabledKeyPrefix + Hash128.Compute(gameId.Trim()).ToString();
    }
}

public static class MatchSnapshotHistoryStore
{
    private const string RootFolderName = "DebugSnapshotHistory";
    private const string ManifestFileName = "manifest.json";

    [Serializable]
    public sealed class SnapshotHistoryGridCoord
    {
        public int x;
        public int y;
    }

    [Serializable]
    public sealed class SnapshotHistoryVisibleUnitSummary
    {
        public string unitTypeId;
        public bool isPlayerOwned;
        public int x;
        public int y;
        public int currentHealthUnits;
        public int movesUsedThisTurn;
        public int attacksUsedThisTurn;
    }

    [Serializable]
    public sealed class SnapshotHistoryCityThreatSummary
    {
        public int x;
        public int y;
        public int visibleEnemyCountWithinThreatRadius;
        public int nearestVisibleEnemyDistance;
    }

    [Serializable]
    public sealed class SnapshotHistoryDecisionContext
    {
        public bool actingSideIsPlayerOwned;
        public bool viewerIsPlayerOwned;
        public string aiProfile;
        public int visibleTileCount;
        public List<SnapshotHistoryGridCoord> visibleTiles = new List<SnapshotHistoryGridCoord>();
        public List<SnapshotHistoryVisibleUnitSummary> visibleEnemyUnits = new List<SnapshotHistoryVisibleUnitSummary>();
        public List<SnapshotHistoryCityThreatSummary> threatenedFriendlyCities = new List<SnapshotHistoryCityThreatSummary>();
        public SnapshotHistoryAIReasoning aiReasoning;
    }

    [Serializable]
    public sealed class SnapshotHistoryAIReasoning
    {
        public string aiDifficulty;
        public string aiRecruitVariant;
        public bool aiHasPerfectInfo;
        public bool calculusUsesApproximateRuleModel;
        public string calculusApproximationSummary;
        public string calculusApproximationLocations;
        public bool hasKeyCity;
        public int keyCityX;
        public int keyCityY;
        public int currentGold;
        public int visibleEnemyUnitCount;
        public int visibleFriendlyUnitCount;
        public int estimatedEnemyGoldMin;
        public int estimatedEnemyGoldMax;
        public int estimatedPossibleEnemyRecruitCountMin;
        public int estimatedPossibleEnemyRecruitCountMax;
        public bool canWinThisTurn;
        public bool couldLoseKeyCityNextTurn;
        public bool keyCityThreatSearchUsedFallback;
        public string keyCityThreatSearchSummary;
        public bool canDefendKeyCityThisTurn;
        public int unsafeTilesNearKeyCityCount;
        public int immediateThreatSourceCountNearKeyCity;
        public int visibleFastEnemyCountNearKeyCity;
        public int defenderCountNearKeyCity;
        public int defenderDeficitNearKeyCity;
        public int candidateActionCount;
        public int chosenActionScore;
        public int secondBestActionScore;
        public string chosenActionReason;
        public string chosenRecruitReason;
        public string chosenDefensePlanSummary;
        public string chosenActionSummary;
    }

    [Serializable]
    private sealed class SnapshotHistoryManifest
    {
        public int formatVersion = 2;
        public string gameId;
        public string mode;
        public int nextOrdinal = 1;
        public List<SnapshotHistoryEntry> entries = new List<SnapshotHistoryEntry>();
    }

    [Serializable]
    private sealed class SnapshotHistoryEntry
    {
        public int ordinal;
        public string fileName;
        public string source;
        public string capturedUtc;
        public string canonicalPath;
        public int roundTurn;
        public bool isPlayerTurn;
        public int transportSeq;
        public SnapshotHistoryDecisionContext decisionContext;
    }

    public static void TryCaptureSnapshot(
        string gameId,
        TurnManager.GameMode mode,
        string source,
        string json,
        int roundTurn,
        bool isPlayerTurn,
        string canonicalPath = null,
        SnapshotHistoryDecisionContext decisionContext = null)
    {
        if (string.IsNullOrWhiteSpace(gameId) ||
            string.IsNullOrWhiteSpace(json) ||
            !MatchSnapshotHistorySettings.IsEnabled(gameId))
        {
            return;
        }

        try
        {
            string matchFolderPath = GetMatchFolderPath(gameId);
            Directory.CreateDirectory(matchFolderPath);

            string manifestPath = Path.Combine(matchFolderPath, ManifestFileName);
            SnapshotHistoryManifest manifest = LoadManifest(manifestPath, gameId, mode);

            int ordinal = manifest.nextOrdinal > 0 ? manifest.nextOrdinal : manifest.entries.Count + 1;
            string safeSource = SanitizeForFileName(string.IsNullOrWhiteSpace(source) ? "snapshot" : source);
            string sideLabel = isPlayerTurn ? "p1" : "p2";
            string fileName = $"{ordinal:0000}_{safeSource}_r{Mathf.Max(0, roundTurn):0000}_{sideLabel}.json";
            string snapshotPath = Path.Combine(matchFolderPath, fileName);
            File.WriteAllText(snapshotPath, json);

            manifest.gameId = gameId;
            manifest.mode = mode.ToString();
            manifest.nextOrdinal = ordinal + 1;
            manifest.entries.Add(new SnapshotHistoryEntry
            {
                ordinal = ordinal,
                fileName = fileName,
                source = source,
                capturedUtc = DateTime.UtcNow.ToString("o"),
                canonicalPath = canonicalPath,
                roundTurn = roundTurn,
                isPlayerTurn = isPlayerTurn,
                transportSeq = ComputeTransportSeq(roundTurn, isPlayerTurn),
                decisionContext = decisionContext
            });

            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"[SnapshotHistory] Failed capturing history for gameId={gameId}: {ex.Message}");
#endif
        }
    }

    private static SnapshotHistoryManifest LoadManifest(string manifestPath, string gameId, TurnManager.GameMode mode)
    {
        if (File.Exists(manifestPath))
        {
            try
            {
                SnapshotHistoryManifest existing = JsonUtility.FromJson<SnapshotHistoryManifest>(File.ReadAllText(manifestPath));
                if (existing != null)
                {
                    if (existing.formatVersion <= 0)
                    {
                        existing.formatVersion = 2;
                    }
                    existing.entries ??= new List<SnapshotHistoryEntry>();
                    if (existing.nextOrdinal <= 0)
                    {
                        existing.nextOrdinal = existing.entries.Count + 1;
                    }
                    if (string.IsNullOrWhiteSpace(existing.gameId))
                    {
                        existing.gameId = gameId;
                    }
                    if (string.IsNullOrWhiteSpace(existing.mode))
                    {
                        existing.mode = mode.ToString();
                    }
                    return existing;
                }
            }
            catch
            {
                // Fall back to a fresh manifest.
            }
        }

        return new SnapshotHistoryManifest
        {
            gameId = gameId,
            mode = mode.ToString(),
            nextOrdinal = 1,
            entries = new List<SnapshotHistoryEntry>()
        };
    }

    private static string GetMatchFolderPath(string gameId)
    {
        string safeGameId = SanitizeForFileName(gameId);
        string gameHash = Hash128.Compute(gameId).ToString();
        string folderName = $"{safeGameId}__{gameHash.Substring(0, 8)}";
        return Path.Combine(Application.persistentDataPath, RootFolderName, folderName);
    }

    private static string SanitizeForFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "match";
        }

        char[] chars = value.Trim().ToCharArray();
        char[] invalidChars = Path.GetInvalidFileNameChars();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalidChars, chars[i]) >= 0 || char.IsWhiteSpace(chars[i]))
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private static int ComputeTransportSeq(int roundTurn, bool isPlayerTurn)
    {
        int clampedRoundTurn = Math.Max(0, roundTurn);
        return clampedRoundTurn * 2 + (isPlayerTurn ? 0 : 1);
    }
}
