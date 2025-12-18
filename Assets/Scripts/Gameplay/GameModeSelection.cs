using UnityEngine;

/// <summary>
/// Holds the chosen game mode while switching scenes (e.g., from a main menu into gameplay).
/// </summary>
public static class GameModeSelection
{
    private static TurnManager.GameMode pendingMode = TurnManager.GameMode.None;

    public static void SetPendingMode(TurnManager.GameMode mode)
    {
        pendingMode = mode;
    }

    public static bool TryConsume(out TurnManager.GameMode mode)
    {
        mode = pendingMode;
        pendingMode = TurnManager.GameMode.None;
        return mode != TurnManager.GameMode.None;
    }
}
