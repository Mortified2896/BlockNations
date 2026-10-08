using System;
using System.Collections.Generic;
using BlockNations.Simulation;

// Unity object mapping only; the shared C# engine owns action legality.
public static class LegalActionService
{
    public static List<LegalTurnAction> GetLegalActionsForSeat(TurnManager manager, int seat,
        LegalActionVisibilityMode mode = LegalActionVisibilityMode.CurrentViewerVisible) =>
        Query(manager, seat, null, tile => tile != null && tile.isVisibleNow, false);
    public static List<LegalTurnAction> GetLegalActionsForSeat(TurnManager manager, int seat, ISet<TileVisibility> visible) =>
        Query(manager, seat, null, tile => visible != null && visible.Contains(tile), false);
    public static List<LegalTurnAction> GetLegalUnitActionsForSeat(TurnManager manager, int seat,
        LegalActionVisibilityMode mode = LegalActionVisibilityMode.CurrentViewerVisible) =>
        Query(manager, seat, null, tile => tile != null && tile.isVisibleNow, true);
    public static List<LegalTurnAction> GetLegalUnitActionsForSeat(TurnManager manager, int seat, ISet<TileVisibility> visible) =>
        Query(manager, seat, null, tile => visible != null && visible.Contains(tile), true);
    public static List<LegalTurnAction> GetLegalActionsForUnit(TurnManager manager, Unit unit, int seat,
        LegalActionVisibilityMode mode = LegalActionVisibilityMode.CurrentViewerVisible) =>
        unit == null ? new List<LegalTurnAction>() : Query(manager, seat, unit, tile => tile != null && tile.isVisibleNow, true);
    public static List<LegalTurnAction> GetLegalActionsForUnit(TurnManager manager, Unit unit, int seat, ISet<TileVisibility> visible) =>
        unit == null ? new List<LegalTurnAction>() : Query(manager, seat, unit, tile => visible != null && visible.Contains(tile), true);

    private static List<LegalTurnAction> Query(TurnManager manager, int seat, Unit unit, Func<TileVisibility, bool> visible, bool unitsOnly)
    {
        var result = new List<LegalTurnAction>();
        if (manager == null || manager.gridManager == null || manager.gameOver || !manager.IsTurnOwnedBySeat(seat)) return result;
        var adapter = new SceneSimulationAdapter(manager);
        bool[] mask = adapter.VisibilityMask(visible);
        List<MatchLegalAction> actions = unit != null ? MatchEngine.LegalUnitActions(adapter.State, seat, unit.GetInstanceID(), mask) :
            MatchEngine.LegalActions(adapter.State, seat, mask);
        foreach (MatchLegalAction action in actions)
        {
            if (unitsOnly && action.Command.Kind != MatchActionKind.Move && action.Command.Kind != MatchActionKind.Attack) continue;
            result.Add(adapter.SceneAction(action));
        }
        return result;
    }
}
