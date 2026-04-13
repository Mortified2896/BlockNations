using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ResetCurrentDevClientDataMenu
{
    private const string MenuPath = "Tools/Block Nations/Reset Current Dev Client Data";

    private const string ProfilePlayerIdKeyRaw = "profile_player_id";
    private const string ProfileUsernameKeyRaw = "profile_username";
    private const string ProfileTypedDisplayNameKeyRaw = "profile_typed_display_name";
    private const string PlayByPostGameIdKeyRaw = "pbp_gameId";
    private const string PlayByPostForceNewKeyRaw = "pbp_forceNew";
    private const string PlayByPostPendingNewGameIdKeyRaw = "pbp_pendingNewGameId";
    private const string PendingCreateShareReadyGameIdKeyRaw = "ui_pbp_createShareReadyGameId";
    private const string ReturnToMultiplayerPaneKeyRaw = "ui_returnToMultiplayerPane";

    [Serializable]
    private sealed class MinimalSaveHeader
    {
        public string gameId;
        public string mode;
    }

    [MenuItem(MenuPath)]
    private static void ResetCurrentDevClientData()
    {
        ResetCurrentDevClientDataInternal(showConfirmation: true);
    }

    public static void ResetCurrentDevClientDataFromCommandLine()
    {
        ResetCurrentDevClientDataInternal(showConfirmation: false);
    }

    private static void ResetCurrentDevClientDataInternal(bool showConfirmation)
    {
        string storageNamespace = DevClientInstanceScope.StorageNamespace;
        string persistentRoot = DevClientInstanceScope.GetScopedPersistentDataPath();

        if (showConfirmation)
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Reset Current Dev Client Data",
                $"Delete local profile/PBp test data for the current dev client namespace?\n\nNamespace: {storageNamespace}\nPath: {persistentRoot}\n\nThis cannot be undone.",
                "Reset Current Namespace",
                "Cancel");

            if (!confirmed)
            {
                return;
            }
        }

        HashSet<string> playByPostGameIds = CollectKnownPlayByPostGameIds(persistentRoot);

        int deletedPrefs = DeleteScopedPlayerPrefsKeys();
        int clearedSeats = ClearKnownSeats(playByPostGameIds);
        PlayerPrefs.Save();

        int deletedFiles = 0;
        int deletedDirectories = 0;
        deletedFiles += DeleteFileIfExists(Path.Combine(persistentRoot, "index.json"));
        deletedFiles += DeleteFileIfExists(Path.Combine(persistentRoot, "index.json.tmp"));
        deletedFiles += DeleteFileIfExists(Path.Combine(persistentRoot, "index.json.bak"));
        deletedFiles += DeletePlayByPostSaveFileIfPresent(Path.Combine(persistentRoot, "save.json"));
        deletedFiles += DeletePlayByPostSaveFileIfPresent(Path.Combine(persistentRoot, "imported.json"));
        deletedDirectories += DeleteDirectoryIfExists(Path.Combine(persistentRoot, "pbp"));
        deletedDirectories += DeleteDirectoryIfExists(Path.Combine(persistentRoot, "PlayByPost"));

        Debug.Log(
            $"[EditorReset] Reset current dev client data namespace={storageNamespace} path={persistentRoot} " +
            $"deletedPrefs={deletedPrefs} clearedSeats={clearedSeats} deletedFiles={deletedFiles} deletedDirectories={deletedDirectories}");

        if (showConfirmation)
        {
            EditorUtility.DisplayDialog(
                "Reset Complete",
                $"Namespace '{storageNamespace}' reset.\n\nDeleted prefs: {deletedPrefs}\nCleared seats: {clearedSeats}\nDeleted files: {deletedFiles}\nDeleted folders: {deletedDirectories}",
                "OK");
        }
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateResetCurrentDevClientData()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static HashSet<string> CollectKnownPlayByPostGameIds(string persistentRoot)
    {
        HashSet<string> gameIds = new HashSet<string>(StringComparer.Ordinal);
        List<SaveManifestService.ManifestGameSummary> summaries = SaveManifestService.GetActivePlayByPostGames();
        for (int i = 0; i < summaries.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(summaries[i].gameId))
            {
                gameIds.Add(summaries[i].gameId);
            }
        }

        TryAddPlayByPostGameIdFromSave(Path.Combine(persistentRoot, "save.json"), gameIds);
        TryAddPlayByPostGameIdFromSave(Path.Combine(persistentRoot, "imported.json"), gameIds);

        string pbpSnapshotFolder = Path.Combine(persistentRoot, "pbp");
        if (Directory.Exists(pbpSnapshotFolder))
        {
            foreach (string path in Directory.EnumerateFiles(pbpSnapshotFolder, "*.json", SearchOption.TopDirectoryOnly))
            {
                TryAddPlayByPostGameIdFromSave(path, gameIds);
            }
        }

        return gameIds;
    }

    private static int DeleteScopedPlayerPrefsKeys()
    {
        string[] rawKeys =
        {
            ProfilePlayerIdKeyRaw,
            ProfileUsernameKeyRaw,
            ProfileTypedDisplayNameKeyRaw,
            PlayByPostGameIdKeyRaw,
            PlayByPostForceNewKeyRaw,
            PlayByPostPendingNewGameIdKeyRaw,
            PendingCreateShareReadyGameIdKeyRaw,
            ReturnToMultiplayerPaneKeyRaw
        };

        int deleted = 0;
        for (int i = 0; i < rawKeys.Length; i++)
        {
            string scopedKey = DevClientInstanceScope.ScopePlayerPrefsKey(rawKeys[i]);
            if (!PlayerPrefs.HasKey(scopedKey))
            {
                continue;
            }

            PlayerPrefs.DeleteKey(scopedKey);
            deleted++;
        }

        return deleted;
    }

    private static int ClearKnownSeats(HashSet<string> playByPostGameIds)
    {
        int cleared = 0;
        foreach (string gameId in playByPostGameIds)
        {
            if (LocalPlayerSeatStore.ClearSeat(gameId))
            {
                cleared++;
            }
        }

        return cleared;
    }

    private static void TryAddPlayByPostGameIdFromSave(string path, HashSet<string> gameIds)
    {
        MinimalSaveHeader header = TryReadHeader(path);
        if (header == null)
        {
            return;
        }

        if (!string.Equals(header.mode, TurnManager.GameMode.PlayByPost.ToString(), StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(header.gameId))
        {
            gameIds.Add(header.gameId);
        }
    }

    private static MinimalSaveHeader TryReadHeader(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<MinimalSaveHeader>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static int DeletePlayByPostSaveFileIfPresent(string path)
    {
        MinimalSaveHeader header = TryReadHeader(path);
        if (header == null)
        {
            return 0;
        }

        if (!string.Equals(header.mode, TurnManager.GameMode.PlayByPost.ToString(), StringComparison.Ordinal))
        {
            return 0;
        }

        return DeleteFileIfExists(path);
    }

    private static int DeleteFileIfExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return 0;
        }

        try
        {
            File.Delete(path);
            return 1;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[EditorReset] Failed deleting file: {path} ({ex.Message})");
            return 0;
        }
    }

    private static int DeleteDirectoryIfExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return 0;
        }

        try
        {
            Directory.Delete(path, true);
            return 1;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[EditorReset] Failed deleting directory: {path} ({ex.Message})");
            return 0;
        }
    }
}
