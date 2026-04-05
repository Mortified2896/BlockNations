using UnityEngine;

/// <summary>
/// Holds the chosen map size while switching scenes.
/// </summary>
public static class MapSizeSelection
{
    private static TurnManager.MapSizePreset pendingPreset = TurnManager.MapSizePreset.Unspecified;

    public static void SetPending(TurnManager.MapSizePreset preset)
    {
        pendingPreset = preset;
    }

    public static bool TryConsume(out TurnManager.MapSizePreset preset)
    {
        preset = pendingPreset;
        pendingPreset = TurnManager.MapSizePreset.Unspecified;
        return preset != TurnManager.MapSizePreset.Unspecified;
    }
}
