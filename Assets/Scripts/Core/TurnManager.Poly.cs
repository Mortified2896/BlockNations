using System;
using BlockNations.Simulation;
using UnityEngine;

public partial class TurnManager
{
    [Header("Poly match (explicit scene wiring)")]
    [SerializeField] private PolyGameController polyController;
    public PolyGameController PolyController => polyController;
    public bool IsPolyMatch => polyController != null;
    internal void ConfigurePolyDriver(Action restart)
    {
        if (!externallyDrivenMatch || polyController == null || gridManager == null || gridManager.width != 11 || gridManager.height != 11)
            throw new InvalidOperationException("Poly requires its explicitly wired native 11x11 scene.");
        ConfigureExternalHumanSeat(0, restart);
        currentMode = GameMode.VsAI;
    }
    internal void ProjectPolyTurn(MatchState state)
    {
        turnNumber = state.Round;
        gameOver = state.GameOver;
        ExternalWinnerSeatIndex = state.WinnerSeat;
        SetCurrentTurnSeatIndexForRuntime(state.CurrentTurnSeat, state.SeatCount);
        ApplySimulationGold(state);
        RefreshEndTurnButtonInteractable(force: true);
    }
}
