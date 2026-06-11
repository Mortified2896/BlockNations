using System;
using System.Collections.Generic;
using UnityEngine;

public static class LegalActionService
{
    public static List<LegalTurnAction> GetLegalActionsForSeat(
        TurnManager turnManager,
        int seatIndex,
        LegalActionVisibilityMode visibilityMode = LegalActionVisibilityMode.CurrentViewerVisible)
    {
        List<LegalTurnAction> actions = GetLegalUnitActionsForSeat(turnManager, seatIndex, visibilityMode);
        AddLegalCityRecruitActions(turnManager, seatIndex, actions);
        AddLegalEndTurnAction(turnManager, seatIndex, actions);
        return actions;
    }

    public static List<LegalTurnAction> GetLegalActionsForSeat(
        TurnManager turnManager,
        int seatIndex,
        ISet<TileVisibility> visibleTiles)
    {
        List<LegalTurnAction> actions = GetLegalUnitActionsForSeat(turnManager, seatIndex, visibleTiles);
        AddLegalCityRecruitActions(turnManager, seatIndex, actions);
        AddLegalEndTurnAction(turnManager, seatIndex, actions);
        return actions;
    }

    public static List<LegalTurnAction> GetLegalUnitActionsForSeat(
        TurnManager turnManager,
        int seatIndex,
        LegalActionVisibilityMode visibilityMode = LegalActionVisibilityMode.CurrentViewerVisible)
    {
        return GetLegalUnitActionsForSeat(turnManager, seatIndex, BuildVisibilityPredicate(visibilityMode));
    }

    public static List<LegalTurnAction> GetLegalUnitActionsForSeat(
        TurnManager turnManager,
        int seatIndex,
        ISet<TileVisibility> visibleTiles)
    {
        return GetLegalUnitActionsForSeat(turnManager, seatIndex, BuildVisibilityPredicate(visibleTiles));
    }

    private static List<LegalTurnAction> GetLegalUnitActionsForSeat(
        TurnManager turnManager,
        int seatIndex,
        Func<TileVisibility, bool> isTileVisible)
    {
        List<LegalTurnAction> actions = new List<LegalTurnAction>();
        if (!CanQuerySeat(turnManager, seatIndex))
        {
            return actions;
        }

        Unit[] units = UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            if (unit == null || unit.ownerSeatIndex != seatIndex)
            {
                continue;
            }

            actions.AddRange(GetLegalActionsForUnit(turnManager, unit, seatIndex, isTileVisible));
        }

        return actions;
    }

    public static List<LegalTurnAction> GetLegalActionsForUnit(
        TurnManager turnManager,
        Unit unit,
        int seatIndex,
        LegalActionVisibilityMode visibilityMode = LegalActionVisibilityMode.CurrentViewerVisible)
    {
        return GetLegalActionsForUnit(turnManager, unit, seatIndex, BuildVisibilityPredicate(visibilityMode));
    }

    public static List<LegalTurnAction> GetLegalActionsForUnit(
        TurnManager turnManager,
        Unit unit,
        int seatIndex,
        ISet<TileVisibility> visibleTiles)
    {
        return GetLegalActionsForUnit(turnManager, unit, seatIndex, BuildVisibilityPredicate(visibleTiles));
    }

    private static List<LegalTurnAction> GetLegalActionsForUnit(
        TurnManager turnManager,
        Unit unit,
        int seatIndex,
        Func<TileVisibility, bool> isTileVisible)
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

        AddLegalMoveActions(turnManager.gridManager, unit, seatIndex, originTile, isTileVisible, actions);
        AddLegalAttackActions(turnManager.gridManager, unit, seatIndex, originTile, isTileVisible, actions);
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
        Func<TileVisibility, bool> isTileVisible,
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
                return occupant != null && IsVisibleForLegalAction(nextTile, isTileVisible);
            });

        foreach (KeyValuePair<TileVisibility, List<TileVisibility>> entry in reachablePaths)
        {
            TileVisibility targetTile = entry.Key;
            if (targetTile == null)
            {
                continue;
            }

            Unit occupant = GridUtils.GetUnitAtPosition(targetTile.transform.position, unit);
            bool hasVisibleOccupant = occupant != null && IsVisibleForLegalAction(targetTile, isTileVisible);
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
        Func<TileVisibility, bool> isTileVisible,
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
                !IsVisibleForLegalAction(targetTile, isTileVisible))
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

    private static void AddLegalCityRecruitActions(
        TurnManager turnManager,
        int seatIndex,
        List<LegalTurnAction> actions)
    {
        if (turnManager == null || actions == null || turnManager.gameOver || !turnManager.IsTurnOwnedBySeat(seatIndex))
        {
            return;
        }

        City[] cities = UnityEngine.Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        List<UnitDefinition> recruitableUnits = turnManager.GetRecruitableOfficialUnitDefinitions();
        int availableGold = turnManager.GetGoldForSeat(seatIndex);
        foreach (City city in cities)
        {
            if (city == null ||
                city.ownerSeatIndex != seatIndex ||
                city.stationedUnit != null ||
                city.hasRecruitedThisTurn ||
                GridUtils.IsTileOccupied(city.transform.position, null))
            {
                continue;
            }

            TileVisibility spawnTile = null;
            if (turnManager.gridManager != null)
            {
                turnManager.gridManager.TryGetTileAtWorldPosition(city.transform.position, out spawnTile);
            }

            for (int i = 0; i < recruitableUnits.Count; i++)
            {
                UnitDefinition unitDefinition = recruitableUnits[i];
                if (unitDefinition == null ||
                    availableGold < unitDefinition.RecruitCost ||
                    turnManager.GetUnitPrefabForType(unitDefinition.TypeId) == null)
                {
                    continue;
                }

                actions.Add(new LegalTurnAction(
                    LegalActionType.CityRecruit,
                    seatIndex,
                    unit: null,
                    originTile: spawnTile,
                    targetTile: spawnTile,
                    targetUnit: null,
                    path: null,
                    city: city,
                    recruitUnitTypeId: unitDefinition.TypeId,
                    recruitCost: unitDefinition.RecruitCost));
            }
        }
    }

    private static void AddLegalEndTurnAction(
        TurnManager turnManager,
        int seatIndex,
        List<LegalTurnAction> actions)
    {
        if (turnManager == null || actions == null || turnManager.gameOver || !turnManager.IsTurnOwnedBySeat(seatIndex))
        {
            return;
        }

        actions.Add(new LegalTurnAction(
            LegalActionType.EndTurn,
            seatIndex,
            unit: null,
            originTile: null,
            targetTile: null,
            targetUnit: null,
            path: null));
    }

    private static Func<TileVisibility, bool> BuildVisibilityPredicate(LegalActionVisibilityMode visibilityMode)
    {
        return tile => visibilityMode == LegalActionVisibilityMode.CurrentViewerVisible &&
                       tile != null &&
                       tile.isVisibleNow;
    }

    private static Func<TileVisibility, bool> BuildVisibilityPredicate(ISet<TileVisibility> visibleTiles)
    {
        return tile => tile != null && visibleTiles != null && visibleTiles.Contains(tile);
    }

    private static bool IsVisibleForLegalAction(
        TileVisibility tile,
        Func<TileVisibility, bool> isTileVisible)
    {
        return isTileVisible != null && isTileVisible(tile);
    }
}
