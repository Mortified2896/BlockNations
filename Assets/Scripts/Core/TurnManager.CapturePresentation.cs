using UnityEngine;

public partial class TurnManager
{
    // The winning move is public once the local match ends. Keep this display
    // override separate from per-seat visibility, observations and saved state.
    private City terminalCapturedCity;

    private bool TryConfigureLocalCapturePresentation(int winnerSeat, City capturedCity)
    {
        int humanSeat = externallyDrivenMatch ? externalHumanSeatIndex : 0;
        if (currentMode != GameMode.VsAI || humanSeat < 0 ||
            (!externallyDrivenMatch && IsAIVsAIDebugModeActive())) return false;

        terminalCapturedCity = capturedCity;
        bool humanWon = winnerSeat == humanSeat;
        Unit captor = capturedCity != null && capturedCity.stationedUnit != null
            ? capturedCity.stationedUnit.GetComponent<Unit>() : null;
        string message = humanWon ? "You captured the enemy city" : "The AI captured your city";
        if (captor != null) message += humanWon ? $" with your {captor.DisplayName}" : $" with its {captor.DisplayName}";
        SetGameOverUiTitle(humanWon ? "Victory" : "Defeat");
        SetGameOverUiMessage(message + ".");
        return true;
    }

    private void ApplyTerminalCapturePresentation()
    {
        if (!gameOver || terminalCapturedCity == null || gridManager == null) return;
        if (gridManager.TryGetTile(terminalCapturedCity.x, terminalCapturedCity.y, out TileVisibility tile))
        {
            // Do not mark the tile visible/seen for an agent. The final board
            // only needs to display the capture that has already ended play.
            if (tile.fogRenderer != null) tile.fogRenderer.enabled = false;
            if (tile.exploredRenderer != null) tile.exploredRenderer.enabled = false;
        }
        if (terminalCapturedCity.stationedUnit != null)
        {
            Unit captor = terminalCapturedCity.stationedUnit.GetComponent<Unit>();
            if (captor != null) captor.SetFogVisibility(true, captor.ownerSeatIndex == GetViewerSeatIndexForRuntime());
        }
    }
}
