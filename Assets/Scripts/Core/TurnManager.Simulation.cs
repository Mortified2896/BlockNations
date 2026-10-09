using BlockNations.Simulation;
using System.Collections.Generic;

public partial class TurnManager
{
    // Optional observers receive command boundaries. They never choose or modify
    // actions; development recordings subscribe explicitly to this manager.
    public event System.Action<MatchState, MatchCommand> SimulationCommandPreparing;
    public event System.Action<MatchState, MatchCommand, MatchTransition> SimulationCommandCompleted;
    internal bool HasSimulationCommandObservers => SimulationCommandPreparing != null || SimulationCommandCompleted != null;
    internal void NotifySimulationCommandPreparing(MatchState state, MatchCommand command) => SimulationCommandPreparing?.Invoke(state, command);
    internal void NotifySimulationCommandCompleted(MatchState state, MatchCommand command, MatchTransition transition) =>
        SimulationCommandCompleted?.Invoke(state, command, transition);

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
