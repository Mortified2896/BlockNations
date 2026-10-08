using System;
using System.Collections.Generic;
using BlockNations.AI;
using BlockNations.Simulation;

// Scene compatibility adapter around the same filtered projection used by training.
public sealed class SeatAIObservationSource
{
    private readonly SimulationObservationSource source = new SimulationObservationSource();
    public void ResetKnowledge() => source.ResetKnowledge();
    public void SetPublicStartingCities(AICityState[] cities) => source.SetPublicStartingCities(cities);
    public sealed class Context
    {
        public AIObservation Observation;
        public Unit[] Units;
        public City[] Cities;
        public HashSet<TileVisibility> Visible;
        public readonly Dictionary<string, LegalTurnAction> RuntimeActions = new Dictionary<string, LegalTurnAction>(StringComparer.Ordinal);
    }
    public Context Observe(TurnManager manager, int seat, bool includeZeroDamageAttacks = true, ISet<string> excludedActions = null)
    {
        var adapter = new SceneSimulationAdapter(manager);
        bool[] seen = new bool[adapter.State.Tiles.Length];
        foreach (TileVisibility tile in manager.gridManager.GetAllTiles()) seen[adapter.State.Position(tile.gridX, tile.gridY)] = tile.HasBeenSeenBySeat(seat);
        SimulationObservationSource.Context snapshot = source.Observe(adapter.State, seat, includeZeroDamageAttacks, excludedActions, seen);
        var context = new Context { Observation = snapshot.Observation, Units = new Unit[snapshot.UnitIds.Length],
            Cities = new City[snapshot.CityIds.Length], Visible = new HashSet<TileVisibility>() };
        foreach (TileVisibility tile in manager.gridManager.GetAllTiles())
            if (snapshot.Observation.Visible[adapter.State.Position(tile.gridX, tile.gridY)]) { tile.RecordSeenBySeat(seat); context.Visible.Add(tile); }
        for (int i = 0; i < context.Units.Length; i++) adapter.Units.TryGetValue(snapshot.UnitIds[i], out context.Units[i]);
        for (int i = 0; i < context.Cities.Length; i++) adapter.Cities.TryGetValue(snapshot.CityIds[i], out context.Cities[i]);
        var actions = new Dictionary<MatchCommand, MatchLegalAction>();
        foreach (MatchLegalAction legal in MatchEngine.LegalActions(adapter.State, seat, snapshot.Observation.Visible)) actions[legal.Command] = legal;
        foreach (KeyValuePair<string, MatchCommand> entry in snapshot.Commands) context.RuntimeActions.Add(entry.Key, adapter.SceneAction(actions[entry.Value]));
        return context;
    }
}
