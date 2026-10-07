using System;
using UnityEngine;
using Object = UnityEngine.Object;

public partial class TurnManager
{
    [Header("External match driver (isolated development scenes)")]
    [SerializeField] private bool externallyDrivenMatch;
    [SerializeField] private int externalHumanSeatIndex = -1;
    public bool IsExternallyDrivenMatch => externallyDrivenMatch;
    public bool ExternalMatchReady { get; private set; }
    public int ExternalWinnerSeatIndex { get; private set; } = -1;

    public void ResetExternalMatch(int initialGold, int firstSeat)
    {
        if (!externallyDrivenMatch || !ExternalMatchReady || gridManager == null)
            throw new InvalidOperationException("An explicitly configured external arena must be ready before reset.");
        if (firstSeat < 0 || firstSeat > 1) throw new ArgumentOutOfRangeException(nameof(firstSeat));
        StopAllCoroutines();
        hardAIRuntime.ResetKnowledge();
        foreach (Unit unit in Object.FindObjectsByType<Unit>())
        {
            if (unit.gameObject.scene != gameObject.scene) continue;
            unit.gameObject.SetActive(false);
            Destroy(unit.gameObject);
        }
        gridManager.RebuildGrid(11, 11);
        currentMode = GameMode.VsAI;
        gameOver = false;
        ExternalWinnerSeatIndex = -1;
        turnNumber = 1;
        SetSeatGoldFromLegacyBridges(2, initialGold, initialGold);
        SetCurrentTurnSeatIndexForRuntime(firstSeat, 2);
        BeginSeatTurn(firstSeat, playTurnStartSound: false);
    }

    public bool TryAdvanceExternalMatchTurn(int completedSeat)
    {
        if (!externallyDrivenMatch || !ExternalMatchReady || gameOver || !IsTurnOwnedBySeat(completedSeat)) return false;
        AdvanceVsAITurnAfterSeat(completedSeat);
        BeginSeatTurn(currentTurnSeatIndex, playTurnStartSound: false);
        return true;
    }

    private void RefreshExternalSpectatorVisibility()
    {
        // Presentation has no effect on the per-seat seen set or policy observations.
        foreach (TileVisibility tile in gridManager.GetAllTiles())
        {
            if (tile.fogRenderer != null) tile.fogRenderer.enabled = false;
            if (tile.exploredRenderer != null) tile.exploredRenderer.enabled = false;
        }
        foreach (Unit unit in Object.FindObjectsByType<Unit>())
            unit.SetFogVisibility(true, unit.ownerSeatIndex == currentTurnSeatIndex);
    }
}
