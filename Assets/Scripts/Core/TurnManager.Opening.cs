using BlockNations.Simulation;

public partial class TurnManager
{
    private void InitializeSeatGoldForNewGame(int seatCount)
    {
        EnsureSeatGoldCapacity(seatCount);
        for (int seat = 0; seat < seatGold.Count; seat++)
            seatGold[seat] = currentMode == GameMode.VsAI
                ? MatchOpening.GoldBeforeIncome(seat, firstSeat: 0)
                : startingGold;
        SyncLegacyGoldBridge();
    }

    // Player identity is stable even when a development match starts with another seat.
    internal bool TryGetLocalOpeningSeatForUi(out int seat, out int firstSeat, out int seatCount)
    {
        seat = -1;
        firstSeat = externallyDrivenMatch ? ExternalFirstSeatIndex : 0;
        seatCount = GetRuntimeSeatCount();
        if (currentMode == GameMode.PlayByPost)
        {
            if (!TryGetLocalSeatIndexForPbp(currentGameId, out seat)) return false;
        }
        else if (currentMode == GameMode.VsAI)
        {
            if (externallyDrivenMatch) seat = externalHumanSeatIndex;
            else if (!IsAIVsAIDebugModeActive()) seat = 0;
        }
        return seat >= 0 && seat < seatCount;
    }
}
