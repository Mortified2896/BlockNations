public static class PlayByPostSeatCountSelection
{
    private static bool hasPendingSelection;
    private static int pendingSeatCount = PlayByPostSeatUtility.MinSeatCount;

    public static void SetPending(int seatCount)
    {
        hasPendingSelection = true;
        pendingSeatCount = PlayByPostSeatUtility.NormalizeSeatCount(seatCount);
    }

    public static bool TryPeek(out int seatCount)
    {
        seatCount = PlayByPostSeatUtility.NormalizeSeatCount(pendingSeatCount);
        return hasPendingSelection;
    }

    public static bool TryConsume(out int seatCount)
    {
        seatCount = PlayByPostSeatUtility.NormalizeSeatCount(pendingSeatCount);
        bool hadPending = hasPendingSelection;
        hasPendingSelection = false;
        pendingSeatCount = PlayByPostSeatUtility.MinSeatCount;
        return hadPending;
    }
}
