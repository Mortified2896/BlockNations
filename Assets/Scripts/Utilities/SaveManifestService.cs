using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class SaveManifestService
{
    private const string ManifestFileName = "index.json";
    private const string ManifestVersion = "1";
    private const string DefaultLocalGameId = "local_default_slot";
    private const string EntryKeyLocalPrefix = "local:";
    private const string EntryKeyPbpFilePrefix = "pbp-file:";

    private static readonly object Sync = new object();
    private static SaveManifest cachedManifest;
    private static bool loadedOnce;

    [Serializable]
    private class SaveManifest
    {
        public string version = ManifestVersion;
        public List<SaveEntry> entries = new List<SaveEntry>();
    }

    [Serializable]
    private class SaveEntry
    {
        public string entryKey;
        public string gameId;
        public string displayName;
        public string mode;
        public string slotType;
        public string savePath;
        public string folderPath;
        public string createdUtc;
        public string lastPlayedUtc;
        public bool isFinished;
        public string transportType;
        public string opponentLabel;
        public bool hasLastKnownTurnState;
        public int lastKnownRoundTurn;
        public bool lastKnownIsPlayerTurn;
        public int lastKnownCurrentTurnSeatIndex;
        public int lastKnownSeatCount = PlayByPostSeatUtility.MinSeatCount;
        public int lastKnownTransportSeq;
    }

    public struct ManifestGameSummary
    {
        public string entryKey;
        public string gameId;
        public string displayName;
        public string mode;
        public string slotType;
        public string lastPlayedUtc;
        public bool isFinished;
        public string transportType;
        public bool hasLastKnownTurnState;
        public int lastKnownRoundTurn;
        public bool lastKnownIsPlayerTurn;
        public int lastKnownCurrentTurnSeatIndex;
        public int lastKnownSeatCount;
        public int lastKnownTransportSeq;
    }

    [Serializable]
    private class MinimalSaveHeader
    {
        public string gameId;
        public string mode;
        public string mapSizePreset;
        public int boardWidth;
        public int boardHeight;
        public bool isPlayerTurn;
        public int turnNumber;
        public bool gameOver;
        public int seatCount = PlayByPostSeatUtility.MinSeatCount;
        public int currentTurnSeatIndex;
        public int transportSeq;
        public List<PlayByPostSeatMetadata> seats = new List<PlayByPostSeatMetadata>();
    }

    public static void RecordLocalSave(
        string gameId,
        TurnManager.GameMode mode,
        string savePath,
        bool isFinished,
        int? lastKnownRoundTurn = null,
        bool? lastKnownIsPlayerTurn = null,
        int? lastKnownCurrentTurnSeatIndex = null,
        int? lastKnownTransportSeq = null,
        int? lastKnownSeatCount = null)
    {
        string entryKey = mode == TurnManager.GameMode.PlayByPost
            ? BuildPbpGameEntryKey(gameId)
            : BuildLocalEntryKey(savePath);
        RecordSave(
            entryKey,
            gameId,
            mode,
            savePath,
            isFinished,
            transportType: null,
            folderPath: null,
            allowCreateWithoutEntryKey: mode == TurnManager.GameMode.PlayByPost,
            lastKnownRoundTurn: lastKnownRoundTurn,
            lastKnownIsPlayerTurn: lastKnownIsPlayerTurn,
            lastKnownCurrentTurnSeatIndex: lastKnownCurrentTurnSeatIndex,
            lastKnownTransportSeq: lastKnownTransportSeq,
            lastKnownSeatCount: lastKnownSeatCount);
    }

    public static void RecordImportedSave(string gameId, string mode, bool isFinished, string savePath)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return;

        TurnManager.GameMode parsedMode = ParseMode(mode);
        string resolvedGameId = string.IsNullOrWhiteSpace(gameId) ? DefaultLocalGameId : gameId;
        string entryKey = BuildLocalEntryKey(savePath);
        RecordSave(
            entryKey,
            resolvedGameId,
            parsedMode,
            savePath,
            isFinished,
            transportType: null,
            folderPath: null,
            allowCreateWithoutEntryKey: false,
            lastKnownRoundTurn: null,
            lastKnownIsPlayerTurn: null,
            lastKnownCurrentTurnSeatIndex: null,
            lastKnownTransportSeq: null,
            lastKnownSeatCount: null);
    }

    public static void RecordPlayByPostExport(
        string gameId,
        string transportType,
        int? lastKnownRoundTurn = null,
        bool? lastKnownIsPlayerTurn = null,
        int? lastKnownCurrentTurnSeatIndex = null,
        int? lastKnownTransportSeq = null,
        int? lastKnownSeatCount = null)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return;

        RecordSave(entryKey: null, gameId, TurnManager.GameMode.PlayByPost, savePath: null, isFinished: false,
            transportType: transportType, folderPath: null, allowCreateWithoutEntryKey: true,
            lastKnownRoundTurn: lastKnownRoundTurn, lastKnownIsPlayerTurn: lastKnownIsPlayerTurn,
            lastKnownCurrentTurnSeatIndex: lastKnownCurrentTurnSeatIndex,
            lastKnownTransportSeq: lastKnownTransportSeq,
            lastKnownSeatCount: lastKnownSeatCount);
    }

    public static void RecordLoadApplied(
        string gameId,
        TurnManager.GameMode mode,
        bool isFinished,
        int? lastKnownRoundTurn = null,
        bool? lastKnownIsPlayerTurn = null,
        int? lastKnownCurrentTurnSeatIndex = null,
        int? lastKnownTransportSeq = null,
        int? lastKnownSeatCount = null)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            gameId = DefaultLocalGameId;

        RecordSave(entryKey: null, gameId, mode, savePath: null, isFinished: isFinished,
            transportType: null, folderPath: null, allowCreateWithoutEntryKey: true,
            lastKnownRoundTurn: lastKnownRoundTurn, lastKnownIsPlayerTurn: lastKnownIsPlayerTurn,
            lastKnownCurrentTurnSeatIndex: lastKnownCurrentTurnSeatIndex,
            lastKnownTransportSeq: lastKnownTransportSeq,
            lastKnownSeatCount: lastKnownSeatCount);
    }

    public static void EnsurePlayByPostEntry(string gameId, string transportType)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return;

        RecordSave(entryKey: null, gameId, TurnManager.GameMode.PlayByPost, savePath: null, isFinished: false,
            transportType: transportType, folderPath: null, allowCreateWithoutEntryKey: true,
            lastKnownRoundTurn: null, lastKnownIsPlayerTurn: null,
            lastKnownCurrentTurnSeatIndex: null,
            lastKnownTransportSeq: null,
            lastKnownSeatCount: null);
    }

    public static void DumpManifestToLog()
    {
#if DEVELOPMENT_BUILD
        lock (Sync)
        {
            EnsureLoaded();
            int count = cachedManifest != null && cachedManifest.entries != null ? cachedManifest.entries.Count : 0;
            Debug.Log($"[SaveManifest] entries={count}");
            if (cachedManifest == null || cachedManifest.entries == null)
                return;

            for (int i = 0; i < cachedManifest.entries.Count; i++)
            {
                SaveEntry entry = cachedManifest.entries[i];
                if (entry == null)
                    continue;

                Debug.Log(
                    $"[SaveManifest] entryKey={entry.entryKey} mode={entry.mode} savePath={entry.savePath} folderPath={entry.folderPath} lastPlayedUtc={entry.lastPlayedUtc} isFinished={entry.isFinished} " +
                    $"lastKnownRoundTurn={entry.lastKnownRoundTurn} lastKnownIsPlayerTurn={entry.lastKnownIsPlayerTurn} lastKnownCurrentTurnSeatIndex={entry.lastKnownCurrentTurnSeatIndex} lastKnownSeatCount={entry.lastKnownSeatCount} lastKnownTransportSeq={entry.lastKnownTransportSeq} hasLastKnownTurnState={entry.hasLastKnownTurnState}");
            }
        }
#endif
    }

    public static void RecordPlayByPostFileFolder(string folderPath, string gameId)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        string folderRel = ToRelativePersistentPath(folderPath);
        string entryKey = BuildPbpFileEntryKey(folderRel);
        RecordSave(entryKey, gameId, TurnManager.GameMode.PlayByPost, savePath: null, isFinished: false,
            transportType: "File", folderPath: folderRel, allowCreateWithoutEntryKey: false,
            lastKnownRoundTurn: null, lastKnownIsPlayerTurn: null,
            lastKnownCurrentTurnSeatIndex: null,
            lastKnownTransportSeq: null,
            lastKnownSeatCount: null);
    }

    public static List<ManifestGameSummary> GetActivePlayByPostGames()
    {
        lock (Sync)
        {
            EnsureLoaded();
            List<ManifestGameSummary> results = new List<ManifestGameSummary>();
            if (cachedManifest == null || cachedManifest.entries == null)
                return results;

            for (int i = 0; i < cachedManifest.entries.Count; i++)
            {
                SaveEntry entry = cachedManifest.entries[i];
                if (entry == null)
                    continue;

                if (!string.Equals(entry.slotType, "PlayByPost", StringComparison.Ordinal))
                    continue;

                if (entry.isFinished)
                    continue;

                results.Add(new ManifestGameSummary
                {
                    entryKey = entry.entryKey,
                    gameId = entry.gameId,
                    displayName = entry.displayName,
                    mode = entry.mode,
                    slotType = entry.slotType,
                    lastPlayedUtc = entry.lastPlayedUtc,
                    isFinished = entry.isFinished,
                    transportType = entry.transportType,
                    hasLastKnownTurnState = entry.hasLastKnownTurnState,
                    lastKnownRoundTurn = entry.lastKnownRoundTurn,
                    lastKnownIsPlayerTurn = entry.lastKnownIsPlayerTurn,
                    lastKnownCurrentTurnSeatIndex = entry.lastKnownCurrentTurnSeatIndex,
                    lastKnownSeatCount = entry.lastKnownSeatCount > 0
                        ? entry.lastKnownSeatCount
                        : PlayByPostSeatUtility.MinSeatCount,
                    lastKnownTransportSeq = entry.lastKnownTransportSeq
                });
            }

            results.Sort((a, b) => string.CompareOrdinal(b.lastPlayedUtc ?? string.Empty, a.lastPlayedUtc ?? string.Empty));
            return results;
        }
    }

    public static bool MarkPlayByPostGameFinished(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return false;

        lock (Sync)
        {
            EnsureLoaded();
            if (cachedManifest == null || cachedManifest.entries == null)
                return false;

            bool updated = false;
            string nowUtc = UtcNowIso();
            for (int i = 0; i < cachedManifest.entries.Count; i++)
            {
                SaveEntry entry = cachedManifest.entries[i];
                if (entry == null)
                    continue;

                if (!string.Equals(entry.slotType, "PlayByPost", StringComparison.Ordinal))
                    continue;

                if (!string.Equals(entry.gameId, gameId, StringComparison.Ordinal))
                    continue;

                if (entry.isFinished)
                    continue;

                entry.isFinished = true;
                entry.lastPlayedUtc = nowUtc;
                updated = true;
            }

            if (!updated)
                return false;

            WriteManifest(cachedManifest);
            DumpManifestToLog();
            return true;
        }
    }

    public static bool TryDeleteMatchingPlayByPostSaveFile(string path, string gameId)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(gameId) || !File.Exists(path))
            return false;

        try
        {
            string json = File.ReadAllText(path);
            MinimalSaveHeader header = JsonUtility.FromJson<MinimalSaveHeader>(json);
            if (header == null)
                return false;

            if (!string.Equals(header.mode, TurnManager.GameMode.PlayByPost.ToString(), StringComparison.Ordinal))
                return false;

            if (!string.Equals(header.gameId, gameId, StringComparison.Ordinal))
                return false;

            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RecordSave(
        string entryKey,
        string gameId,
        TurnManager.GameMode mode,
        string savePath,
        bool isFinished,
        string transportType,
        string folderPath,
        bool allowCreateWithoutEntryKey,
        int? lastKnownRoundTurn,
        bool? lastKnownIsPlayerTurn,
        int? lastKnownCurrentTurnSeatIndex,
        int? lastKnownTransportSeq,
        int? lastKnownSeatCount)
    {
        lock (Sync)
        {
            EnsureLoaded();
            if (mode == TurnManager.GameMode.PlayByPost)
            {
                MigrateLocalPlayByPostEntry(gameId);
            }
            if (string.IsNullOrWhiteSpace(entryKey) &&
                mode == TurnManager.GameMode.PlayByPost &&
                !string.IsNullOrWhiteSpace(gameId) &&
                string.IsNullOrWhiteSpace(folderPath))
            {
                entryKey = BuildPbpGameEntryKey(gameId);
            }

            string normalizedEntryKey = NormalizeEntryKey(entryKey);
            SaveEntry entry = FindEntry(cachedManifest, normalizedEntryKey, gameId, folderPath);
            if (entry == null)
            {
                if (string.IsNullOrWhiteSpace(normalizedEntryKey) && !allowCreateWithoutEntryKey)
                {
                    return;
                }

                entry = new SaveEntry
                {
                    entryKey = normalizedEntryKey,
                    gameId = gameId,
                    createdUtc = UtcNowIso()
                };
                cachedManifest.entries.Add(entry);
            }

            if (!string.IsNullOrWhiteSpace(normalizedEntryKey) &&
                (string.IsNullOrWhiteSpace(entry.entryKey) || entry.entryKey == normalizedEntryKey))
            {
                entry.entryKey = normalizedEntryKey;
            }

            if (!string.IsNullOrWhiteSpace(gameId))
            {
                entry.gameId = gameId;
            }

            if (mode == TurnManager.GameMode.PlayByPost &&
                string.IsNullOrWhiteSpace(entry.displayName))
            {
                entry.displayName = PbpGameDisplayNameGenerator.BuildForGameId(entry.gameId);
            }

            entry.mode = mode.ToString();
            entry.slotType = SlotTypeFromMode(mode);
            entry.lastPlayedUtc = UtcNowIso();
            entry.isFinished = isFinished;

            if (!string.IsNullOrWhiteSpace(savePath))
            {
                entry.savePath = ToRelativePersistentPath(savePath);
            }

            if (!string.IsNullOrWhiteSpace(folderPath))
            {
                entry.folderPath = ToRelativePersistentPath(folderPath);
            }

            if (!string.IsNullOrWhiteSpace(transportType))
            {
                entry.transportType = transportType;
            }

            if (mode == TurnManager.GameMode.PlayByPost &&
                lastKnownRoundTurn.HasValue &&
                lastKnownIsPlayerTurn.HasValue)
            {
                int clampedRoundTurn = Math.Max(0, lastKnownRoundTurn.Value);
                bool knownIsPlayerTurn = lastKnownIsPlayerTurn.Value;
                int normalizedSeatCount = PlayByPostSeatUtility.NormalizeSeatCount(
                    lastKnownSeatCount ?? PlayByPostSeatUtility.MinSeatCount);
                int currentTurnSeatIndex = lastKnownCurrentTurnSeatIndex ?? (knownIsPlayerTurn ? 0 : 1);
                entry.hasLastKnownTurnState = true;
                entry.lastKnownRoundTurn = clampedRoundTurn;
                entry.lastKnownIsPlayerTurn = knownIsPlayerTurn;
                entry.lastKnownCurrentTurnSeatIndex = PlayByPostSeatUtility.NormalizeSeatIndex(currentTurnSeatIndex, normalizedSeatCount);
                entry.lastKnownSeatCount = normalizedSeatCount;
                entry.lastKnownTransportSeq = lastKnownTransportSeq.GetValueOrDefault(
                    ComputePlayByPostTransportSeq(clampedRoundTurn, knownIsPlayerTurn));
            }

            WriteManifest(cachedManifest);
            DumpManifestToLog();
        }
    }

    private static SaveEntry FindEntry(SaveManifest manifest, string entryKey, string gameId, string folderPath)
    {
        if (manifest == null)
            return null;

        if (!string.IsNullOrWhiteSpace(entryKey))
        {
            for (int i = 0; i < manifest.entries.Count; i++)
            {
                SaveEntry entry = manifest.entries[i];
                if (entry != null && NormalizeEntryKey(entry.entryKey) == entryKey)
                {
                    return entry;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            string rel = ToRelativePersistentPath(folderPath);
            for (int i = 0; i < manifest.entries.Count; i++)
            {
                SaveEntry entry = manifest.entries[i];
                if (entry != null && NormalizePathSeparators(entry.folderPath) == rel)
                {
                    return entry;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(gameId))
        {
            for (int i = 0; i < manifest.entries.Count; i++)
            {
                SaveEntry entry = manifest.entries[i];
                if (entry != null && entry.gameId == gameId)
                {
                    return entry;
                }
            }
        }

        return null;
    }

    private static void EnsureLoaded()
    {
        if (loadedOnce)
            return;

        loadedOnce = true;
        cachedManifest = LoadManifest();
    }

    private static SaveManifest LoadManifest()
    {
        string path = GetManifestPath();
        if (!File.Exists(path))
        {
            return BuildInitialManifest();
        }

        try
        {
            string json = File.ReadAllText(path);
            SaveManifest loaded = JsonUtility.FromJson<SaveManifest>(json);
            if (loaded == null)
                return BuildInitialManifest();

            loaded.entries ??= new List<SaveEntry>();
            return loaded;
        }
        catch
        {
            return BuildInitialManifest();
        }
    }

    private static SaveManifest BuildInitialManifest()
    {
        SaveManifest manifest = new SaveManifest();

        string persistentRoot = Application.persistentDataPath;
        if (string.IsNullOrWhiteSpace(persistentRoot))
            return manifest;

        string defaultSavePath = Path.Combine(persistentRoot, "save.json");
        TryAddSaveFileEntry(manifest, defaultSavePath);

        string importedPath = Path.Combine(persistentRoot, "imported.json");
        TryAddSaveFileEntry(manifest, importedPath);

        string pbpRoot = Path.Combine(persistentRoot, "PlayByPost", "Turns");
        TryAddPlayByPostEntries(manifest, pbpRoot);

        WriteManifest(manifest);
        return manifest;
    }

    private static void TryAddSaveFileEntry(SaveManifest manifest, string path)
    {
        if (!File.Exists(path))
            return;

        MinimalSaveHeader header = TryReadHeader(path);
        if (header == null || string.IsNullOrWhiteSpace(header.mode))
            return;

        TurnManager.GameMode mode = ParseMode(header.mode);
        string gameId = string.IsNullOrWhiteSpace(header.gameId) ? DefaultLocalGameId : header.gameId;
        SaveEntry entry = new SaveEntry
        {
            entryKey = BuildLocalEntryKey(path),
            gameId = gameId,
            displayName = mode == TurnManager.GameMode.PlayByPost
                ? PbpGameDisplayNameGenerator.BuildForGameId(gameId)
                : null,
            mode = mode.ToString(),
            slotType = SlotTypeFromMode(mode),
            savePath = ToRelativePersistentPath(path),
            createdUtc = UtcNowIso(),
            lastPlayedUtc = UtcNowIso(),
            isFinished = header.gameOver
        };
        if (mode == TurnManager.GameMode.PlayByPost)
        {
            int clampedRoundTurn = Math.Max(0, header.turnNumber);
            entry.hasLastKnownTurnState = true;
            entry.lastKnownRoundTurn = clampedRoundTurn;
            entry.lastKnownIsPlayerTurn = header.isPlayerTurn;
            entry.lastKnownCurrentTurnSeatIndex = PlayByPostSeatUtility.NormalizeSeatIndex(
                header.currentTurnSeatIndex,
                header.seatCount);
            entry.lastKnownSeatCount = PlayByPostSeatUtility.NormalizeSeatCount(header.seatCount);
            entry.lastKnownTransportSeq = header.transportSeq > 0
                ? header.transportSeq
                : ComputePlayByPostTransportSeq(clampedRoundTurn, header.isPlayerTurn);
        }
        manifest.entries.Add(entry);
    }

    private static void TryAddPlayByPostEntries(SaveManifest manifest, string pbpRoot)
    {
        if (!Directory.Exists(pbpRoot))
            return;

        foreach (string folder in Directory.EnumerateDirectories(pbpRoot))
        {
            int bestTurn = 0;
            string bestPath = null;
            foreach (string file in Directory.EnumerateFiles(folder, "turn_*.json", SearchOption.TopDirectoryOnly))
            {
                if (!TryParseTurnNumberFromPath(file, out int turn))
                    continue;

                if (turn > bestTurn)
                {
                    bestTurn = turn;
                    bestPath = file;
                }
            }

            MinimalSaveHeader header = bestPath != null ? TryReadHeader(bestPath) : null;
            string gameId = header != null && !string.IsNullOrWhiteSpace(header.gameId)
                ? header.gameId
                : "pbp_folder_" + Path.GetFileName(folder);

            SaveEntry entry = new SaveEntry
            {
                entryKey = BuildPbpFileEntryKey(folder),
                gameId = gameId,
                displayName = PbpGameDisplayNameGenerator.BuildForGameId(gameId),
                mode = TurnManager.GameMode.PlayByPost.ToString(),
                slotType = SlotTypeFromMode(TurnManager.GameMode.PlayByPost),
                folderPath = ToRelativePersistentPath(folder),
                createdUtc = UtcNowIso(),
                lastPlayedUtc = UtcNowIso(),
                isFinished = false,
                transportType = "File"
            };

            if (header != null && !string.IsNullOrWhiteSpace(header.mode))
            {
                entry.mode = header.mode;
            }
            if (header != null)
            {
                int clampedRoundTurn = Math.Max(0, header.turnNumber);
                entry.hasLastKnownTurnState = true;
                entry.lastKnownRoundTurn = clampedRoundTurn;
                entry.lastKnownIsPlayerTurn = header.isPlayerTurn;
                entry.lastKnownCurrentTurnSeatIndex = PlayByPostSeatUtility.NormalizeSeatIndex(
                    header.currentTurnSeatIndex,
                    header.seatCount);
                entry.lastKnownSeatCount = PlayByPostSeatUtility.NormalizeSeatCount(header.seatCount);
                entry.lastKnownTransportSeq = header.transportSeq > 0
                    ? header.transportSeq
                    : ComputePlayByPostTransportSeq(clampedRoundTurn, header.isPlayerTurn);
            }

            manifest.entries.Add(entry);
        }
    }

    private static MinimalSaveHeader TryReadHeader(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<MinimalSaveHeader>(json);
        }
        catch
        {
            return null;
        }
    }

    private static TurnManager.GameMode ParseMode(string mode)
    {
        if (Enum.TryParse(mode, out TurnManager.GameMode parsed))
            return parsed;

        return TurnManager.GameMode.None;
    }

    private static string SlotTypeFromMode(TurnManager.GameMode mode)
    {
        switch (mode)
        {
            case TurnManager.GameMode.PlayByPost:
                return "PlayByPost";
            default:
                return "SinglePlayer";
        }
    }

    private static string ToRelativePersistentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        string root = Application.persistentDataPath;
        if (string.IsNullOrWhiteSpace(root))
            return path;

        try
        {
            string fullPath = Path.GetFullPath(path);
            string fullRoot = Path.GetFullPath(root);
            if (fullPath.StartsWith(fullRoot, StringComparison.Ordinal))
            {
                string rel = fullPath.Substring(fullRoot.Length);
                if (rel.StartsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                    rel.StartsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                {
                    rel = rel.Substring(1);
                }
                return NormalizePathSeparators(rel);
            }
        }
        catch
        {
        }

        return NormalizePathSeparators(path);
    }

    private static string BuildLocalEntryKey(string path)
    {
        string rel = ToRelativePersistentPath(path);
        if (string.IsNullOrWhiteSpace(rel))
            return null;

        return EntryKeyLocalPrefix + rel;
    }

    private static string BuildPbpGameEntryKey(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return null;

        return "pbp:" + gameId;
    }

    private static string BuildPbpFileEntryKey(string folderPath)
    {
        string rel = ToRelativePersistentPath(folderPath);
        if (string.IsNullOrWhiteSpace(rel))
            return null;

        return EntryKeyPbpFilePrefix + rel;
    }

    private static string NormalizeEntryKey(string entryKey)
    {
        if (string.IsNullOrWhiteSpace(entryKey))
            return entryKey;

        return entryKey.Replace('\\', '/');
    }

    private static string NormalizePathSeparators(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        return path.Replace('\\', '/');
    }

    private static void WriteManifest(SaveManifest manifest)
    {
        if (manifest == null)
            return;

        string path = GetManifestPath();
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonUtility.ToJson(manifest, true);
        string tmpPath = path + ".tmp";
        string bakPath = path + ".bak";

        try
        {
            File.WriteAllText(tmpPath, json);
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmpPath, path, bakPath, true);
                    return;
                }
                catch
                {
                    try
                    {
                        File.Copy(path, bakPath, true);
                    }
                    catch
                    {
                    }

                    try
                    {
                        File.Copy(tmpPath, path, true);
                        File.Delete(tmpPath);
                        return;
                    }
                    catch
                    {
                    }
                }
            }

            File.Move(tmpPath, path);
        }
        catch
        {
            try
            {
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
            }
            catch
            {
            }
        }
    }

    private static string GetManifestPath()
    {
        return Path.Combine(Application.persistentDataPath, ManifestFileName);
    }

    private static string UtcNowIso()
    {
        return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
    }

    public static int ComputePlayByPostTransportSeq(int roundTurn, bool isPlayerTurn)
    {
        int clampedRoundTurn = Math.Max(0, roundTurn);
        return clampedRoundTurn * 2 + (isPlayerTurn ? 0 : 1);
    }

    private static void MigrateLocalPlayByPostEntry(string gameId)
    {
        if (cachedManifest == null || cachedManifest.entries == null)
            return;
        if (string.IsNullOrWhiteSpace(gameId))
            return;

        string localKey = NormalizeEntryKey(EntryKeyLocalPrefix + "save.json");
        for (int i = 0; i < cachedManifest.entries.Count; i++)
        {
            SaveEntry entry = cachedManifest.entries[i];
            if (entry == null)
                continue;

            if (NormalizeEntryKey(entry.entryKey) != localKey)
                continue;

            if (entry.mode != TurnManager.GameMode.PlayByPost.ToString())
                continue;

            if (!string.Equals(entry.gameId, gameId, StringComparison.Ordinal))
                continue;

            entry.entryKey = BuildPbpGameEntryKey(gameId);
            entry.slotType = SlotTypeFromMode(TurnManager.GameMode.PlayByPost);
            entry.mode = TurnManager.GameMode.PlayByPost.ToString();
            return;
        }
    }

    private static bool TryParseTurnNumberFromPath(string path, out int turnNumber)
    {
        turnNumber = 0;
        string name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name))
            return false;

        const string prefix = "turn_";
        const string suffix = ".json";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        string middle = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
        return int.TryParse(middle, out turnNumber) && turnNumber > 0;
    }
}
