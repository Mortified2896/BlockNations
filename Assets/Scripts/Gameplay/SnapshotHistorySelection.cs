/// <summary>
/// Holds the chosen snapshot-history debug option while switching scenes.
/// </summary>
public static class SnapshotHistorySelection
{
    private static bool hasPendingSelection;
    private static bool pendingEnabled;

    public static void SetPending(bool enabled)
    {
        hasPendingSelection = true;
        pendingEnabled = enabled;
    }

    public static bool TryConsume(out bool enabled)
    {
        enabled = pendingEnabled;
        bool hadPending = hasPendingSelection;
        hasPendingSelection = false;
        pendingEnabled = false;
        return hadPending;
    }
}
