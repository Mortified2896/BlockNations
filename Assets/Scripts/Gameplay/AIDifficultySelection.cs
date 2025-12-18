using UnityEngine;

/// <summary>
/// Holds the chosen AI difficulty while switching scenes
/// (e.g., from a main menu into gameplay).
/// </summary>
public static class AIDifficultySelection
{
    private static TurnManager.AIDifficulty pendingDifficulty = TurnManager.AIDifficulty.None;

    public static void SetPending(TurnManager.AIDifficulty difficulty)
    {
        pendingDifficulty = difficulty;
    }

    public static bool TryConsume(out TurnManager.AIDifficulty difficulty)
    {
        difficulty = pendingDifficulty;
        pendingDifficulty = TurnManager.AIDifficulty.None;
        return difficulty != TurnManager.AIDifficulty.None;
    }
}

