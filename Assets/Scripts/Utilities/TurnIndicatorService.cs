using System;
using System.IO;
using UnityEngine;

public static class TurnIndicatorService
{
    [Serializable]
    private class MinimalPlayByPostState
    {
        public string gameId;
        public string mode;
        public bool isPlayerTurn;
        public int turnNumber;
    }

    public static bool TryGetIsMyTurn(string gameId, out bool isMyTurn, out string debugReason)
    {
        isMyTurn = false;
        debugReason = null;

        if (string.IsNullOrWhiteSpace(gameId))
        {
            debugReason = "GAME_ID_MISSING";
            return false;
        }

        if (!LocalPlayerSeatStore.TryGetSeat(gameId, out int seatOrPlayerIndex))
        {
            debugReason = "SEAT_UNKNOWN";
            return false;
        }

        if (!TryReadLatestState(gameId, out MinimalPlayByPostState state, out string stateReason))
        {
            debugReason = stateReason;
            return false;
        }

        bool amPlayer1 = seatOrPlayerIndex == 0;
        isMyTurn = state.isPlayerTurn == amPlayer1;
        debugReason = "OK";
        return true;
    }

    private static bool TryReadLatestState(string gameId, out MinimalPlayByPostState state, out string reason)
    {
        if (TryReadLatestTurnFileState(gameId, out state))
        {
            reason = "LATEST_TURN_FILE";
            return true;
        }

        string savePath = Path.Combine(Application.persistentDataPath, "save.json");
        if (TryReadStateFromPath(savePath, gameId, out state))
        {
            reason = "SAVE_JSON";
            return true;
        }

        string importedPath = Path.Combine(Application.persistentDataPath, "imported.json");
        if (TryReadStateFromPath(importedPath, gameId, out state))
        {
            reason = "IMPORTED_JSON";
            return true;
        }

        state = null;
        reason = "STATE_MISSING";
        return false;
    }

    private static bool TryReadLatestTurnFileState(string gameId, out MinimalPlayByPostState state)
    {
        state = null;
        string gameFolder = Path.Combine(
            Application.persistentDataPath,
            "PlayByPost",
            "Turns",
            Hash128.Compute(gameId).ToString());

        if (!Directory.Exists(gameFolder))
        {
            return false;
        }

        int bestTurn = 0;
        string bestPath = null;
        try
        {
            foreach (string file in Directory.EnumerateFiles(gameFolder, "turn_*.json", SearchOption.TopDirectoryOnly))
            {
                if (!TryParseTurnNumberFromPath(file, out int turn))
                {
                    continue;
                }

                if (turn > bestTurn)
                {
                    bestTurn = turn;
                    bestPath = file;
                }
            }
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrEmpty(bestPath))
        {
            return false;
        }

        return TryReadStateFromPath(bestPath, gameId, out state);
    }

    private static bool TryReadStateFromPath(string path, string expectedGameId, out MinimalPlayByPostState state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(path);
            MinimalPlayByPostState parsed = JsonUtility.FromJson<MinimalPlayByPostState>(json);
            if (parsed == null)
            {
                return false;
            }

            if (!string.Equals(parsed.mode, TurnManager.GameMode.PlayByPost.ToString(), StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(parsed.gameId, expectedGameId, StringComparison.Ordinal))
            {
                return false;
            }

            state = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseTurnNumberFromPath(string path, out int turnNumber)
    {
        turnNumber = 0;
        string name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        const string prefix = "turn_";
        const string suffix = ".json";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string middle = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
        return int.TryParse(middle, out turnNumber) && turnNumber > 0;
    }
}
