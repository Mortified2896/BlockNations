using System;
using System.Collections.Generic;
using BlockNations.AI;
using UnityEngine;
using Object = UnityEngine.Object;

// Scene-to-policy boundary. Memory belongs to one match and one observer seat.
public sealed class SeatAIObservationSource
{
    private readonly Dictionary<int, Dictionary<int, AICityState>> cityMemory = new Dictionary<int, Dictionary<int, AICityState>>();
    public void ResetKnowledge() => cityMemory.Clear();

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
        GridManager grid = manager.gridManager;
        HashSet<TileVisibility> visible = manager.ComputeVisibilityForSeat(seat);
        foreach (TileVisibility tile in visible) tile.RecordSeenBySeat(seat);
        int size = grid.width * grid.height;
        AIObservation observation = new AIObservation { Width = grid.width, Height = grid.height, Seat = seat,
            Gold = manager.GetGoldForSeat(seat), CityVision = manager.visibilityRadius,
            Round = manager.turnNumber, IncomePerCity = manager.goldPerCity,
            Tiles = new bool[size], Seen = new bool[size], Visible = new bool[size] };
        foreach (TileVisibility tile in grid.GetAllTiles())
        {
            if (tile == null) continue;
            int p = tile.gridY * grid.width + tile.gridX;
            observation.Tiles[p] = true;
            observation.Seen[p] = tile.HasBeenSeenBySeat(seat);
            observation.Visible[p] = visible.Contains(tile);
        }
        List<Unit> units = new List<Unit>();
        foreach (Unit unit in Object.FindObjectsByType<Unit>())
            if (unit != null && unit.gameObject.activeInHierarchy && unit.currentHealthUnits > 0 &&
                grid.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile) &&
                (unit.ownerSeatIndex == seat || visible.Contains(tile))) units.Add(unit);
        units.Sort((a, b) =>
        {
            int order = a.ownerSeatIndex.CompareTo(b.ownerSeatIndex);
            grid.TryGetTileAtWorldPosition(a.transform.position, out TileVisibility at);
            grid.TryGetTileAtWorldPosition(b.transform.position, out TileVisibility bt);
            if (order == 0) order = at.gridY.CompareTo(bt.gridY);
            if (order == 0) order = at.gridX.CompareTo(bt.gridX);
            return order != 0 ? order : string.CompareOrdinal(a.UnitTypeId, b.UnitTypeId);
        });
        observation.Units = new AIUnitState[units.Count];
        for (int i = 0; i < units.Count; i++)
        {
            Unit unit = units[i];
            grid.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile);
            observation.Units[i] = UnitState(unit, tile);
        }

        if (!cityMemory.TryGetValue(seat, out Dictionary<int, AICityState> remembered))
            cityMemory[seat] = remembered = new Dictionary<int, AICityState>();
        List<int> memoryPositions = new List<int>(remembered.Keys);
        foreach (int p in memoryPositions)
        {
            AICityState city = remembered[p];
            city.CurrentlyVisible = false;
            remembered[p] = city;
        }
        City[] allCities = Object.FindObjectsByType<City>();
        Dictionary<int, City> liveKnownCities = new Dictionary<int, City>();
        foreach (City city in allCities)
        {
            if (!grid.TryGetTile(city.x, city.y, out TileVisibility tile) ||
                (city.ownerSeatIndex != seat && !visible.Contains(tile))) continue;
            int position = city.y * grid.width + city.x;
            remembered[position] = new AICityState { Seat = city.ownerSeatIndex, X = city.x, Y = city.y,
                Recruited = city.hasRecruitedThisTurn, CurrentlyVisible = true };
            liveKnownCities[position] = city;
        }
        memoryPositions = new List<int>(remembered.Keys);
        memoryPositions.Sort();
        observation.Cities = new AICityState[memoryPositions.Count];
        City[] cities = new City[memoryPositions.Count];
        for (int i = 0; i < memoryPositions.Count; i++)
        {
            observation.Cities[i] = remembered[memoryPositions[i]];
            liveKnownCities.TryGetValue(memoryPositions[i], out cities[i]);
        }
        // Explicit relationship boundary preserves today's free-for-all rules.
        // Future allies must be classified here, before entering the policy.
        List<int> hostileSeats = new List<int>();
        for (int other = 0; other < manager.RuntimeSeatCount; other++)
            if (other != seat) hostileSeats.Add(other);
        observation.HostileSeats = hostileSeats.ToArray();
        List<UnitDefinition> definitions = manager.GetRecruitableOfficialUnitDefinitions();
        definitions.Sort((a, b) => string.CompareOrdinal(a.TypeId, b.TypeId));
        observation.RecruitTypes = new AIUnitState[definitions.Count];
        for (int i = 0; i < definitions.Count; i++) observation.RecruitTypes[i] = RecruitState(definitions[i], seat);

        Context context = new Context { Observation = observation, Units = units.ToArray(), Cities = cities, Visible = visible };
        List<AIAction> roots = new List<AIAction>();
        foreach (LegalTurnAction legal in LegalActionService.GetLegalActionsForSeat(manager, seat, visible))
        {
            AIAction action = new AIAction { Actor = -1, Target = -1 };
            switch (legal.ActionType)
            {
                case LegalActionType.UnitMove:
                    action.Kind = AIActionKind.Move;
                    action.Actor = units.IndexOf(legal.Unit);
                    action.Destination = legal.TargetTile.gridY * grid.width + legal.TargetTile.gridX;
                    action.MoveCost = legal.Path.Count;
                    break;
                case LegalActionType.UnitAttack:
                    action.Kind = AIActionKind.Attack;
                    action.Actor = units.IndexOf(legal.Unit);
                    action.Target = units.IndexOf(legal.TargetUnit);
                    if (action.Target < 0 || !observation.IsHostileSeat(legal.TargetUnit.ownerSeatIndex) ||
                        (!includeZeroDamageAttacks && AIActionRules.Damage(legal.Unit.attackUnits, legal.TargetUnit.defenseUnits) <= 0)) continue;
                    action.Destination = legal.TargetTile.gridY * grid.width + legal.TargetTile.gridX;
                    break;
                case LegalActionType.CityRecruit:
                    action.Kind = AIActionKind.Recruit;
                    action.Actor = Array.IndexOf(cities, legal.City);
                    action.RecruitType = definitions.FindIndex(d => d.TypeId == legal.RecruitUnitTypeId);
                    if (action.RecruitType < 0) continue;
                    action.Destination = legal.City.y * grid.width + legal.City.x;
                    break;
                default: action.Kind = AIActionKind.EndTurn; break;
            }
            if (action.Kind != AIActionKind.EndTurn && action.Actor < 0) continue;
            if (excludedActions != null && excludedActions.Contains(action.Key)) continue;
            roots.Add(action);
            context.RuntimeActions[action.Key] = legal;
        }
        roots.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        observation.LegalActions = roots.ToArray();
        return context;
    }

    private static AIUnitState UnitState(Unit unit, TileVisibility tile) => new AIUnitState {
        Seat = unit.ownerSeatIndex, Type = unit.UnitTypeId, X = tile.gridX, Y = tile.gridY,
        Health = unit.currentHealthUnits, MaxHealth = unit.maxHealthUnits, Attack = unit.attackUnits,
        Defense = unit.defenseUnits, Range = unit.AttackRange, Vision = unit.VisionRange,
        MaxMoves = unit.maxMovesPerTurn, MovesUsed = unit.movesUsedThisTurn,
        MaxAttacks = unit.maxAttacksPerTurn, AttacksUsed = unit.attacksUsedThisTurn,
        AttackAfterMoving = unit.CanAttackAfterMoving,
        CommittedMove = UnitActionRules.UsesCommittedMoveActionThisTurn(unit.UnitTypeId),
        Cost = UnitRegistry.GetDefinitionOrDefault(unit.UnitTypeId).RecruitCost };

    private static AIUnitState RecruitState(UnitDefinition definition, int seat) => new AIUnitState {
        Seat = seat, Type = definition.TypeId, Health = definition.MaxHealthUnits, MaxHealth = definition.MaxHealthUnits,
        Attack = definition.AttackUnits, Defense = definition.DefenseUnits, Range = definition.AttackRange,
        Vision = definition.VisionRange, MaxMoves = definition.MaxMovesPerTurn, MaxAttacks = definition.MaxAttacksPerTurn,
        AttackAfterMoving = definition.CanAttackAfterMoving,
        CommittedMove = UnitActionRules.UsesCommittedMoveActionThisTurn(definition.TypeId), Cost = definition.RecruitCost };

}
