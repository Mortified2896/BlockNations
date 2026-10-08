// Policies propose commands; the shared engine revalidates them before mutation.
public static class SeatAIActionExecutor
{
    public static bool CanAct(TurnManager manager, int seat) => manager != null && !manager.gameOver &&
        manager.gridManager != null && manager.currentMode == TurnManager.GameMode.VsAI &&
        manager.currentTurnSeatIndex == seat && manager.IsTurnOwnedBySeat(seat);

    public static bool TryExecute(TurnManager manager, LegalTurnAction proposed)
    {
        if (!CanAct(manager, proposed.SeatIndex)) return false;
        if (proposed.ActionType == LegalActionType.EndTurn) return true;
        if (proposed.ActionType == LegalActionType.CityRecruit)
            return proposed.City != null && proposed.City.ownerSeatIndex == proposed.SeatIndex && proposed.City.TrySpawnUnit(proposed.RecruitUnitTypeId);
        return new SceneSimulationAdapter(manager).TryApply(proposed);
    }
}
