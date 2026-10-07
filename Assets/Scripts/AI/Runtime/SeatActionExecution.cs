using System.Collections.Generic;
using UnityEngine;

// Revalidate immediately before execution; policies never mutate scene objects.
public static class SeatAIActionExecutor
{
    public static bool CanAct(TurnManager manager, int seat) => manager != null && !manager.gameOver &&
        manager.gridManager != null && manager.currentMode == TurnManager.GameMode.VsAI &&
        manager.currentTurnSeatIndex == seat && manager.IsTurnOwnedBySeat(seat);

    public static bool TryExecute(TurnManager manager, LegalTurnAction proposed)
    {
        if (!CanAct(manager, proposed.SeatIndex)) return false;
        HashSet<TileVisibility> visible = manager.ComputeVisibilityForSeat(proposed.SeatIndex);
        LegalTurnAction? current = null;
        foreach (LegalTurnAction legal in LegalActionService.GetLegalActionsForSeat(manager, proposed.SeatIndex, visible))
            if (legal.ActionType == proposed.ActionType && legal.Unit == proposed.Unit && legal.TargetTile == proposed.TargetTile &&
                legal.TargetUnit == proposed.TargetUnit && legal.City == proposed.City && legal.RecruitUnitTypeId == proposed.RecruitUnitTypeId)
            { current = legal; break; }
        if (!current.HasValue) return false;
        LegalTurnAction action = current.Value;
        if (action.ActionType == LegalActionType.CityRecruit) return action.City.TrySpawnUnit(action.RecruitUnitTypeId);
        if (action.ActionType == LegalActionType.EndTurn) return true;
        Unit unit = action.Unit;
        if (unit == null || unit.ownerSeatIndex != proposed.SeatIndex) return false;
        if (action.ActionType == LegalActionType.UnitMove)
        {
            // Follow the authoritative path; never teleport through hidden occupants.
            int steps = 0;
            foreach (TileVisibility tile in action.Path)
            {
                if (GridUtils.GetUnitAtPosition(tile.transform.position, unit) != null)
                {
                    // Match the human path rule: a hidden blocker consumes the attempted step
                    // on a multi-step move. A blocked one-step action remains invalid.
                    if (action.Path.Count == 1 || visible.Contains(tile)) return false;
                    steps++;
                    break;
                }
                ClearCityLink(unit);
                Vector3 position = tile.transform.position;
                position.z = unit.transform.position.z;
                unit.transform.position = position;
                steps++;
            }
            unit.RegisterMove(steps);
            PlayMove(manager);
            City city = GridUtils.GetCityAtPosition(unit.transform.position);
            if (city != null && city.ownerSeatIndex != proposed.SeatIndex) manager.OnCityCaptured(proposed.SeatIndex, city);
            return steps > 0;
        }
        Unit target = action.TargetUnit;
        Vector3 targetPosition = target.transform.position;
        targetPosition.z = unit.transform.position.z;
        unit.RegisterAttack();
        if (unit.Attack(target) && unit.AdvancesIntoDefenderTileOnKill)
        {
            ClearCityLink(unit);
            unit.transform.position = targetPosition;
            PlayMove(manager);
        }
        City captured = GridUtils.GetCityAtPosition(unit.transform.position);
        if (captured != null && captured.ownerSeatIndex != proposed.SeatIndex) manager.OnCityCaptured(proposed.SeatIndex, captured);
        return true;
    }

    private static void ClearCityLink(Unit unit)
    {
        if (unit.currentCity == null) return;
        if (unit.currentCity.stationedUnit == unit.gameObject) unit.currentCity.stationedUnit = null;
        unit.currentCity = null;
    }
    private static void PlayMove(TurnManager manager)
    {
        if (SoundManager.Instance != null && !manager.ShouldSuppressAIVsAIAudio()) SoundManager.Instance.PlayMove();
    }

}
