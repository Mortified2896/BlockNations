using System;
using System.Collections.Generic;
using BlockNations.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

// Unity/save compatibility boundary. Capture once per command/query, run pure rules,
// then project the result. Direct training keeps MatchState without this scene bridge.
public sealed class SceneSimulationAdapter
{
    public MatchState State { get; }
    public readonly Dictionary<int, Unit> Units = new Dictionary<int, Unit>();
    public readonly Dictionary<int, City> Cities = new Dictionary<int, City>();
    private readonly TurnManager manager;

    public SceneSimulationAdapter(TurnManager manager)
    {
        this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        GridManager grid = manager.gridManager;
        var tiles = new bool[grid.width * grid.height];
        foreach (TileVisibility tile in grid.GetAllTiles()) tiles[grid.width * tile.gridY + tile.gridX] = true;
        var roster = manager.GetRecruitableOfficialUnitDefinitions();
        roster.RemoveAll(definition => manager.GetUnitPrefabForType(definition.TypeId) == null);
        State = new MatchState(grid.width, grid.height, manager.RuntimeSeatCount, roster,
            manager.currentTurnSeatIndex, manager.turnNumber, manager.visibilityRadius, manager.goldPerCity, tiles,
            firstSeat: manager.IsExternallyDrivenMatch ? manager.ExternalFirstSeatIndex : 0);
        State.RestoreOutcome(manager.gameOver, manager.ExternalWinnerSeatIndex);
        for (int seat = 0; seat < State.SeatCount; seat++) State.SetGold(seat, manager.GetGoldForSeat(seat));
        foreach (City city in Object.FindObjectsByType<City>(FindObjectsSortMode.None))
        {
            if (city.gameObject.scene != manager.gameObject.scene || !grid.TryGetTile(city.x, city.y, out _)) continue;
            Cities.Add(city.GetInstanceID(), city);
            State.AddCity(new SimulationCity(city.GetInstanceID(), city.ownerSeatIndex, State.Position(city.x, city.y), city.hasRecruitedThisTurn));
        }
        foreach (Unit unit in Object.FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            if (unit.gameObject.scene != manager.gameObject.scene || !unit.gameObject.activeInHierarchy || unit.currentHealthUnits <= 0 ||
                !grid.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile)) continue;
            Units.Add(unit.GetInstanceID(), unit);
            State.AddUnit(new SimulationUnit(unit.GetInstanceID(), unit.ownerSeatIndex, State.Position(tile.gridX, tile.gridY),
                DefinitionForUnit(unit), unit.currentHealthUnits, unit.movesUsedThisTurn, unit.attacksUsedThisTurn));
        }
    }

    public static UnitDefinition DefinitionForUnit(Unit unit)
    {
        UnitDefinition official = UnitRegistry.GetDefinitionOrDefault(unit.UnitTypeId);
        return new UnitDefinition(unit.UnitTypeId, unit.DisplayName, official.RecruitCost, unit.VisionRange, official.PrefabTypeId,
            unit.maxHealthUnits, unit.attackUnits, unit.AttackRange, unit.CanAttackAfterMoving, unit.defenseUnits,
            unit.maxMovesPerTurn, unit.maxAttacksPerTurn, official.UsesCommittedMoveAction, unit.AttackEndsMovement);
    }

    public bool[] VisibilityMask(Func<TileVisibility, bool> predicate)
    {
        var visible = new bool[State.Tiles.Length];
        foreach (TileVisibility tile in manager.gridManager.GetAllTiles()) visible[State.Position(tile.gridX, tile.gridY)] = predicate(tile);
        return visible;
    }
    public HashSet<TileVisibility> VisibilityForSeat(int seat)
    {
        bool[] visible = MatchEngine.Visibility(State, seat);
        var result = new HashSet<TileVisibility>();
        foreach (TileVisibility tile in manager.gridManager.GetAllTiles()) if (visible[State.Position(tile.gridX, tile.gridY)]) result.Add(tile);
        return result;
    }

    public LegalTurnAction SceneAction(MatchLegalAction action)
    {
        MatchCommand command = action.Command;
        Units.TryGetValue(command.ActorId, out Unit unit); Units.TryGetValue(command.TargetId, out Unit target);
        City city = null;
        if (command.Kind == MatchActionKind.Recruit) { Cities.TryGetValue(command.ActorId, out city); unit = null; }
        TileVisibility origin = null, destination = null;
        if (unit != null) manager.gridManager.TryGetTileAtWorldPosition(unit.transform.position, out origin);
        if (State.Contains(command.Destination)) manager.gridManager.TryGetTile(command.Destination % State.Width, command.Destination / State.Width, out destination);
        if (city != null) origin = destination;
        var path = new List<TileVisibility>(action.Path.Length);
        foreach (int position in action.Path)
            if (manager.gridManager.TryGetTile(position % State.Width, position / State.Width, out TileVisibility tile)) path.Add(tile);
        return new LegalTurnAction((LegalActionType)command.Kind, command.Seat, unit, origin, destination, target,
            command.Kind == MatchActionKind.Move ? path : null, city, command.RecruitType,
            State.RecruitType(command.RecruitType)?.RecruitCost ?? 0);
    }

    public MatchCommand Command(LegalTurnAction action) => new MatchCommand((MatchActionKind)action.ActionType, action.SeatIndex,
        action.ActionType == LegalActionType.CityRecruit ? action.City != null ? action.City.GetInstanceID() : 0 : action.Unit != null ? action.Unit.GetInstanceID() : 0,
        action.TargetTile != null ? State.Position(action.TargetTile.gridX, action.TargetTile.gridY) : -1,
        action.TargetUnit != null ? action.TargetUnit.GetInstanceID() : 0, action.RecruitUnitTypeId);

    public bool TryApply(LegalTurnAction action)
    {
        if (action.ActionType == LegalActionType.EndTurn || !manager.IsTurnOwnedBySeat(action.SeatIndex)) return false;
        GameObject recruitPrefab = action.ActionType == LegalActionType.CityRecruit ? manager.GetUnitPrefabForType(action.RecruitUnitTypeId) : null;
        if (action.ActionType == LegalActionType.CityRecruit && (recruitPrefab == null || recruitPrefab.GetComponent<Unit>() == null)) return false;
        MatchCommand command = Command(action);
        MatchTransition transition = MatchEngine.Apply(State, command);
        if (!transition.Applied) return false;
        if (transition.RecruitedUnitId != 0)
        {
            SimulationUnit recruited = State.GetUnit(transition.RecruitedUnitId);
            City city = Cities[action.City.GetInstanceID()];
            GameObject spawned = manager.InstantiateConfiguredUnit(recruited.Definition.TypeId, recruitPrefab, city.transform.position,
                recruited.Seat, city, resetTurnState: true);
            if (spawned == null) return false; // Prefab/component preflight keeps the live state untouched on failure.
            Units.Add(recruited.Id, spawned.GetComponent<Unit>());
        }
        ProjectResourcesAndPieces(transition.CapturedCityId);
        if (action.ActionType == LegalActionType.UnitAttack && SoundManager.Instance != null && !manager.ShouldSuppressAIVsAIAudio())
            SoundManager.Instance.PlayAttack();
        if (transition.MovedSteps > 0 && SoundManager.Instance != null && !manager.ShouldSuppressAIVsAIAudio()) SoundManager.Instance.PlayMove();
        if (transition.CapturedCityId != 0) manager.OnCityCaptured(command.Seat, Cities[transition.CapturedCityId]);
        return true;
    }

    public void BeginTurn(int seat)
    {
        MatchEngine.BeginTurn(State, seat);
        ProjectResourcesAndPieces(0);
    }
    public void CollectIncome(int seat)
    {
        MatchEngine.CollectIncome(State, seat);
        manager.ApplySimulationGold(State);
    }
    private void ProjectResourcesAndPieces(int capturedCity)
    {
        manager.ApplySimulationGold(State);
        foreach (KeyValuePair<int, Unit> pair in Units)
        {
            Unit live = pair.Value;
            SimulationUnit unit = State.GetUnit(pair.Key);
            if (unit == null) { live.SetCurrentHealthUnits(0); live.Die(); continue; }
            if (live.currentHealthUnits != unit.Health) live.SetCurrentHealthUnits(unit.Health);
            live.movesUsedThisTurn = unit.MovesUsed; live.attacksUsedThisTurn = unit.AttacksUsed;
            manager.gridManager.TryGetTile(unit.Position % State.Width, unit.Position / State.Width, out TileVisibility tile);
            Vector3 position = tile.transform.position; position.z = live.transform.position.z; live.transform.position = position;
            live.currentCity = null;
        }
        foreach (KeyValuePair<int, City> pair in Cities)
        {
            SimulationCity city = State.GetCity(pair.Key);
            pair.Value.hasRecruitedThisTurn = city.Recruited;
            if (city.Id != capturedCity && pair.Value.ownerSeatIndex != city.Seat) pair.Value.SetOwnerSeatIndex(city.Seat);
            SimulationUnit occupant = State.UnitAt(city.Position);
            pair.Value.stationedUnit = occupant != null ? Units[occupant.Id].gameObject : null;
            if (occupant != null) Units[occupant.Id].currentCity = pair.Value;
        }
    }
}
