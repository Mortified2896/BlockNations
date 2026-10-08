using BlockNations.Simulation;
using System.Collections.Generic;

public partial class TurnManager
{
    // Legacy opponents keep their candidate-selection policies; execution is shared.
    private bool TryApplyLegacyAttack(Unit unit, Unit target, HashSet<TileVisibility> visible)
    {
        foreach (LegalTurnAction action in GetLegalAIUnitActions(unit, visible))
            if (action.ActionType == LegalActionType.UnitAttack && action.TargetUnit == target)
                return new SceneSimulationAdapter(this).TryApply(action);
        return false;
    }

    // Keep existing serialized gold/legacy bridges at the Unity/save boundary.
    internal void ApplySimulationGold(MatchState state)
    {
        EnsureSeatGoldCapacity(GetRuntimeSeatCount());
        for (int seat = 0; seat < state.SeatCount; seat++) seatGold[seat] = state.GoldForSeat(seat);
        SyncLegacyGoldBridge();
    }
}
