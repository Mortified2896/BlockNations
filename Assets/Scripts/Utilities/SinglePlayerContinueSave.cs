using System;
using System.IO;
using UnityEngine;

/// <summary>Shared eligibility and slot selection for the menu's Continue action.</summary>
public static class SinglePlayerContinueSave
{
    private const string PrimaryFileName = "save_sp.json";
    private const string LegacyFileName = "save.json";

    [Serializable]
    private sealed class Header
    {
        public string mode;
        public bool gameOver;
    }

    public static bool TryGetPath(string persistentRoot, out string savePath)
    {
        savePath = null;
        if (string.IsNullOrWhiteSpace(persistentRoot)) return false;

        string candidate = Path.Combine(persistentRoot, PrimaryFileName);
        // A primary slot supersedes the legacy slot, even when the primary match is finished.
        if (!File.Exists(candidate)) candidate = Path.Combine(persistentRoot, LegacyFileName);
        if (!File.Exists(candidate)) return false;

        try
        {
            Header header = JsonUtility.FromJson<Header>(File.ReadAllText(candidate));
            if (header == null || header.gameOver) return false;
            // Older single-player saves can omit mode. PBp stays in its own menu flow.
            if (!string.IsNullOrEmpty(header.mode) &&
                !string.Equals(header.mode, TurnManager.GameMode.VsAI.ToString(), StringComparison.Ordinal))
                return false;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }

        savePath = candidate;
        return true;
    }
}
