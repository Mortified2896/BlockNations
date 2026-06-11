using System.Collections.Generic;
using UnityEngine;

public static class LegalActionService
{
    public static List<LegalTurnAction> GetLegalActionsForSeat(
        TurnManager turnManager,
        int seatIndex,
        LegalActionVisibilityMode visibilityMode = LegalActionVisibilityMode.CurrentViewerVisible)
    {
        return GetLegalUnitActionsForSeat(turnManager, seatIndex, visibilityMode);
    }

    public static List<LegalTurnAction> GetLegalUnitActionsForSeat(
        TurnManager turnManager,
        int seatIndex,
        LegalActionVisibilityMode visibilityMode = LegalActionVisibilityMode.CurrentViewerVisible)
    {
        List<LegalTurnAction> actions = new List<LegalTurnAction>();
        if (!CanQuerySeat(turnManager, seatIndex))
        {
            return actions;
        }

        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            if (unit == null || unit.ownerSeatIndex != seatIndex)
            {
                continue;
            }

            actions.AddRange(GetLegalActionsForUnit(turnManager, unit, seatIndex, visibilityMode));
        }

        return actions;
    }

    public static List<LegalTurnAction> GetLegalActionsForUnit(
        TurnManager turnManager,
        Unit unit,
        int seatIndex,
        LegalActionVisibilityMode visibilityMode = LegalActionVisibilityMode.CurrentViewerVisible)
    {
        List<LegalTurnAction> actions = new List<LegalTurnAction>();
        if (!CanQuerySeat(turnManager, seatIndex) ||
            unit == null ||
            unit.ownerSeatIndex != seatIndex ||
            turnManager.gridManager == null ||
            !turnManager.gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility originTile) ||
            originTile == null)
        {
            return actions;
        }

        AddLegalMoveActions(turnManager.gridManager, unit, seatIndex, originTile, visibilityMode, actions);
        AddLegalAttackActions(turnManager.gridManager, unit, seatIndex, originTile, visibilityMode, actions);
        return actions;
    }

    private static bool CanQuerySeat(TurnManager turnManager, int seatIndex)
    {
        return turnManager != null &&
               seatIndex >= 0 &&
               turnManager.IsTurnOwnedBySeat(seatIndex);
    }

    private static void AddLegalMoveActions(
        GridManager grid,
        Unit unit,
        int seatIndex,
        TileVisibility originTile,
        LegalActionVisibilityMode visibilityMode,
        List<LegalTurnAction> actions)
    {
        if (grid == null || unit == null || originTile == null || actions == null || !unit.CanMoveThisTurn())
        {
            return;
        }

        int remainingMoves = unit.GetRemainingMoveRangeThisTurn();
        if (remainingMoves <= 0)
        {
            return;
        }

        Dictionary<TileVisibility, List<TileVisibility>> reachablePaths = UnitActionRules.BuildReachablePathMap(
            grid,
            originTile,
            remainingMoves,
            nextTile =>
            {
                Unit occupant = GridUtils.GetUnitAtPosition(nextTile.transform.position, unit);
                return occupant != null && IsVisibleForLegalAction(nextTile, visibilityMode);
            });

        foreach (KeyValuePair<TileVisibility, List<TileVisibility>> entry in reachablePaths)
        {
            TileVisibility targetTile = entry.Key;
            if (targetTile == null)
            {
                continue;
            }

            Unit occupant = GridUtils.GetUnitAtPosition(targetTile.transform.position, unit);
            bool hasVisibleOccupant = occupant != null && IsVisibleForLegalAction(targetTile, visibilityMode);
            if (hasVisibleOccupant)
            {
                continue;
            }

            actions.Add(new LegalTurnAction(
                LegalActionType.UnitMove,
                seatIndex,
                unit,
                originTile,
                targetTile,
                targetUnit: null,
                path: entry.Value));
        }
    }

    private static void AddLegalAttackActions(
        GridManager grid,
        Unit unit,
        int seatIndex,
        TileVisibility originTile,
        LegalActionVisibilityMode visibilityMode,
        List<LegalTurnAction> actions)
    {
        if (grid == null || unit == null || originTile == null || actions == null || !unit.CanAttackThisTurn())
        {
            return;
        }

        foreach (TileVisibility targetTile in grid.GetAllTiles())
        {
            if (targetTile == null)
            {
                continue;
            }

            int stepDistance = UnitActionRules.GetChebyshevDistance(originTile, targetTile);
            if (!unit.IsTargetInAttackRange(stepDistance))
            {
                continue;
            }

            Unit targetUnit = GridUtils.GetUnitAtPosition(targetTile.transform.position, unit);
            if (targetUnit == null ||
                targetUnit.ownerSeatIndex == unit.ownerSeatIndex ||
                !IsVisibleForLegalAction(targetTile, visibilityMode))
            {
                continue;
            }

            actions.Add(new LegalTurnAction(
                LegalActionType.UnitAttack,
                seatIndex,
                unit,
                originTile,
                targetTile,
                targetUnit,
                path: null));
        }
    }

    private static bool IsVisibleForLegalAction(
        TileVisibility tile,
        LegalActionVisibilityMode visibilityMode)
    {
        return visibilityMode == LegalActionVisibilityMode.CurrentViewerVisible &&
               tile != null &&
               tile.isVisibleNow;
    }
}
