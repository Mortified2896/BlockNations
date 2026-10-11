using System;
using BlockNations.Simulation;
using UnityEngine;

public partial class TurnManager
{
    [Header("Advanced match (explicit scene wiring)")]
    [SerializeField] private AdvancedMatchController advancedMatchController;
    public AdvancedMatchController AdvancedController => advancedMatchController;
    public bool IsAdvancedMatch => advancedMatchController != null;

    public void ConfigureAdvancedDriver(Action restart)
    {
        if (!externallyDrivenMatch || advancedMatchController == null || gridManager == null ||
            gridManager.width != 7 || gridManager.height != 7)
            throw new InvalidOperationException("Advanced mode requires its explicitly wired 7x7 scene.");
        ConfigureExternalHumanSeat(AdvancedMatchController.HumanSeat, restart);
        ConfigureExperimentalRules(MatchRuleProfile.CoreThree);
    }

    // The advanced wrapper owns turn settlement. This only mirrors its state
    // into the established input, board presentation and HUD boundary.
    internal void ProjectAdvancedTurn(MatchState state)
    {
        turnNumber = state.Round;
        SetCurrentTurnSeatIndexForRuntime(state.CurrentTurnSeat, state.SeatCount);
        ApplySimulationGold(state);
        RefreshEndTurnButtonInteractable(force: true);
    }
}
