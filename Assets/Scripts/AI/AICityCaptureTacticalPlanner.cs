using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

public static class AICityCaptureTacticalPlanner
{
    public enum StepType
    {
        Move,
        Attack
    }

    public readonly struct PlanStep
    {
        public PlanStep(StepType stepType, Unit unit, TileVisibility targetTile, Unit targetUnit)
        {
            StepType = stepType;
            Unit = unit;
            TargetTile = targetTile;
            TargetUnit = targetUnit;
        }

        public StepType StepType { get; }
        public Unit Unit { get; }
        public TileVisibility TargetTile { get; }
        public Unit TargetUnit { get; }
    }

    public sealed class Plan
    {
        public Plan(City targetCity, IReadOnlyList<PlanStep> steps, string summary)
        {
            TargetCity = targetCity;
            Steps = steps;
            Summary = summary;
        }

        public City TargetCity { get; }
        public IReadOnlyList<PlanStep> Steps { get; }
        public string Summary { get; }
    }

    private enum SimActionType
    {
        Move,
        Attack
    }

    private struct SimAction
    {
        public SimActionType Type;
        public int UnitIndex;
        public int TargetUnitIndex;
        public TileVisibility TargetTile;
        public int MoveCost;
        public int Score;
    }

    private sealed class SimUnit
    {
        public Unit RuntimeUnit;
        public int OwnerSeatIndex;
        public int X;
        public int Y;
        public int CurrentHealthUnits;
        public int AttackUnits;
        public int DefenseUnits;
        public int AttackRange;
        public bool CanAttackAfterMoving;
        public int MaxMovesPerTurn;
        public int MovesUsedThisTurn;
        public int MaxAttacksPerTurn;
        public int AttacksUsedThisTurn;
        public bool AdvancesIntoDefenderTileOnKill;
        public bool Alive = true;

        public SimUnit Clone()
        {
            return (SimUnit)MemberwiseClone();
        }
    }

    private sealed class SimState
    {
        public List<SimUnit> Units;
        public List<PlanStep> Steps;

        public SimState Clone()
        {
            List<SimUnit> clonedUnits = new List<SimUnit>(Units.Count);
            for (int i = 0; i < Units.Count; i++)
            {
                clonedUnits.Add(Units[i].Clone());
            }

            return new SimState
            {
                Units = clonedUnits,
                Steps = new List<PlanStep>(Steps)
            };
        }
    }

    private const int CandidateUnitLimitPerCity = 6;
    private const int MaxPlanDepth = 6;
    private const int MaxBranchesPerState = 18;
    private const int MaxVisitedStatesPerCity = 450;
    private const int MaxSearchMilliseconds = 45;
    private const int UsefulTileSlack = 2;

    public static bool TryFindPlan(
        TurnManager turnManager,
        GridManager gridManager,
        int actingSeatIndex,
        City[] allCities,
        Unit[] allUnits,
        HashSet<TileVisibility> visibleTiles,
        out Plan plan)
    {
        plan = null;
        if (turnManager == null ||
            gridManager == null ||
            allCities == null ||
            allUnits == null ||
            !turnManager.IsTurnOwnedBySeat(actingSeatIndex))
        {
            return false;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < allCities.Length; i++)
        {
            City city = allCities[i];
            if (city == null || city.ownerSeatIndex == actingSeatIndex)
            {
                continue;
            }

            if (!TryGetVisibleTile(gridManager, city.x, city.y, visibleTiles, out TileVisibility cityTile))
            {
                continue;
            }

            List<Unit> candidateUnits = BuildCandidateUnits(gridManager, actingSeatIndex, city, allUnits);
            if (candidateUnits.Count == 0)
            {
                continue;
            }

            SimState initialState = BuildInitialState(gridManager, candidateUnits, allUnits, visibleTiles);
            if (SearchCityPlan(
                    gridManager,
                    actingSeatIndex,
                    city,
                    cityTile,
                    visibleTiles,
                    initialState,
                    stopwatch,
                    out plan))
            {
                return true;
            }

            if (stopwatch.ElapsedMilliseconds >= MaxSearchMilliseconds)
            {
                break;
            }
        }

        return false;
    }

    private static List<Unit> BuildCandidateUnits(
        GridManager gridManager,
        int actingSeatIndex,
        City city,
        Unit[] allUnits)
    {
        List<Unit> candidates = new List<Unit>();
        for (int i = 0; i < allUnits.Length; i++)
        {
            Unit unit = allUnits[i];
            if (unit == null ||
                unit.ownerSeatIndex != actingSeatIndex ||
                !unit.gameObject.activeInHierarchy ||
                !gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility unitTile) ||
                unitTile == null)
            {
                continue;
            }

            candidates.Add(unit);
        }

        candidates.Sort((a, b) =>
        {
            gridManager.TryGetTileAtWorldPosition(a.transform.position, out TileVisibility aTile);
            gridManager.TryGetTileAtWorldPosition(b.transform.position, out TileVisibility bTile);
            int aDistance = UnitActionRules.GetChebyshevDistance(aTile.gridX, aTile.gridY, city.x, city.y);
            int bDistance = UnitActionRules.GetChebyshevDistance(bTile.gridX, bTile.gridY, city.x, city.y);
            return aDistance.CompareTo(bDistance);
        });

        if (candidates.Count > CandidateUnitLimitPerCity)
        {
            candidates.RemoveRange(CandidateUnitLimitPerCity, candidates.Count - CandidateUnitLimitPerCity);
        }

        return candidates;
    }

    private static SimState BuildInitialState(
        GridManager gridManager,
        List<Unit> candidateUnits,
        Unit[] allUnits,
        HashSet<TileVisibility> visibleTiles)
    {
        SimState state = new SimState
        {
            Units = new List<SimUnit>(),
            Steps = new List<PlanStep>()
        };

        for (int i = 0; i < candidateUnits.Count; i++)
        {
            AddSimUnit(gridManager, state.Units, candidateUnits[i]);
        }

        for (int i = 0; i < allUnits.Length; i++)
        {
            Unit unit = allUnits[i];
            if (unit == null || !unit.gameObject.activeInHierarchy || candidateUnits.Contains(unit))
            {
                continue;
            }

            if (!gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile) ||
                tile == null ||
                !IsVisible(tile, visibleTiles))
            {
                continue;
            }

            AddSimUnit(gridManager, state.Units, unit);
        }

        return state;
    }

    private static void AddSimUnit(GridManager gridManager, List<SimUnit> units, Unit unit)
    {
        if (unit == null ||
            !gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile) ||
            tile == null)
        {
            return;
        }

        units.Add(new SimUnit
        {
            RuntimeUnit = unit,
            OwnerSeatIndex = unit.ownerSeatIndex,
            X = tile.gridX,
            Y = tile.gridY,
            CurrentHealthUnits = unit.currentHealthUnits,
            AttackUnits = unit.attackUnits,
            DefenseUnits = unit.defenseUnits,
            AttackRange = unit.AttackRange,
            CanAttackAfterMoving = unit.CanAttackAfterMoving,
            MaxMovesPerTurn = unit.maxMovesPerTurn,
            MovesUsedThisTurn = unit.movesUsedThisTurn,
            MaxAttacksPerTurn = unit.maxAttacksPerTurn,
            AttacksUsedThisTurn = unit.attacksUsedThisTurn,
            AdvancesIntoDefenderTileOnKill = unit.AdvancesIntoDefenderTileOnKill,
            Alive = unit.currentHealthUnits > 0
        });
    }

    private static bool SearchCityPlan(
        GridManager gridManager,
        int actingSeatIndex,
        City city,
        TileVisibility cityTile,
        HashSet<TileVisibility> visibleTiles,
        SimState initialState,
        Stopwatch stopwatch,
        out Plan plan)
    {
        plan = null;
        Queue<SimState> frontier = new Queue<SimState>();
        HashSet<string> visited = new HashSet<string>();
        frontier.Enqueue(initialState);

        int visitedCount = 0;
        while (frontier.Count > 0 &&
               visitedCount < MaxVisitedStatesPerCity &&
               stopwatch.ElapsedMilliseconds < MaxSearchMilliseconds)
        {
            SimState state = frontier.Dequeue();
            string signature = BuildStateSignature(state);
            if (!visited.Add(signature))
            {
                continue;
            }

            visitedCount++;
            if (HasCapturedCity(state, actingSeatIndex, city))
            {
                plan = new Plan(city, state.Steps, BuildSummary(city, state.Steps));
                return true;
            }

            if (state.Steps.Count >= MaxPlanDepth)
            {
                continue;
            }

            List<SimAction> actions = GenerateUsefulActions(
                gridManager,
                actingSeatIndex,
                city,
                cityTile,
                visibleTiles,
                state);
            for (int i = 0; i < actions.Count; i++)
            {
                SimState nextState = ApplyAction(gridManager, state, actions[i]);
                if (nextState != null)
                {
                    frontier.Enqueue(nextState);
                }
            }
        }

        return false;
    }

    private static List<SimAction> GenerateUsefulActions(
        GridManager gridManager,
        int actingSeatIndex,
        City city,
        TileVisibility cityTile,
        HashSet<TileVisibility> visibleTiles,
        SimState state)
    {
        List<SimAction> actions = new List<SimAction>();
        for (int unitIndex = 0; unitIndex < state.Units.Count; unitIndex++)
        {
            SimUnit unit = state.Units[unitIndex];
            if (!IsActingUnitReady(unit, actingSeatIndex))
            {
                continue;
            }

            AddAttackActions(actingSeatIndex, city, state, unitIndex, actions);
            AddMoveActions(gridManager, city, cityTile, visibleTiles, state, unitIndex, actions);
        }

        actions.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (actions.Count > MaxBranchesPerState)
        {
            actions.RemoveRange(MaxBranchesPerState, actions.Count - MaxBranchesPerState);
        }

        return actions;
    }

    private static void AddAttackActions(
        int actingSeatIndex,
        City city,
        SimState state,
        int attackerIndex,
        List<SimAction> actions)
    {
        SimUnit attacker = state.Units[attackerIndex];
        if (!CanAttackThisTurn(attacker))
        {
            return;
        }

        for (int targetIndex = 0; targetIndex < state.Units.Count; targetIndex++)
        {
            SimUnit target = state.Units[targetIndex];
            if (!target.Alive || target.OwnerSeatIndex == actingSeatIndex)
            {
                continue;
            }

            int distance = UnitActionRules.GetChebyshevDistance(attacker.X, attacker.Y, target.X, target.Y);
            if (!UnitActionRules.IsTargetInAttackRange(attacker.AttackRange, distance))
            {
                continue;
            }

            int damage = UnitActionRules.ComputeMitigatedDamage(attacker.AttackUnits, target.DefenseUnits);
            if (damage <= 0)
            {
                continue;
            }

            bool lethal = damage >= target.CurrentHealthUnits;
            bool targetOnCity = target.X == city.x && target.Y == city.y;
            int score = damage * 10;
            if (targetOnCity)
            {
                score += 1000;
            }

            if (lethal)
            {
                score += targetOnCity && attacker.AdvancesIntoDefenderTileOnKill ? 3000 : 600;
            }

            actions.Add(new SimAction
            {
                Type = SimActionType.Attack,
                UnitIndex = attackerIndex,
                TargetUnitIndex = targetIndex,
                Score = score
            });
        }
    }

    private static void AddMoveActions(
        GridManager gridManager,
        City city,
        TileVisibility cityTile,
        HashSet<TileVisibility> visibleTiles,
        SimState state,
        int unitIndex,
        List<SimAction> actions)
    {
        SimUnit unit = state.Units[unitIndex];
        int remainingMoves = GetRemainingMoveRangeThisTurn(unit);
        if (remainingMoves <= 0)
        {
            return;
        }

        int currentDistanceToCity = UnitActionRules.GetChebyshevDistance(unit.X, unit.Y, city.x, city.y);
        Dictionary<TileVisibility, List<TileVisibility>> reachable = UnitActionRules.BuildReachablePathMap(
            gridManager,
            GetTile(gridManager, unit.X, unit.Y),
            remainingMoves,
            nextTile => IsTileBlockedForMove(state, unitIndex, nextTile, visibleTiles));

        foreach (KeyValuePair<TileVisibility, List<TileVisibility>> entry in reachable)
        {
            TileVisibility targetTile = entry.Key;
            if (targetTile == null || IsTileOccupiedByAliveUnit(state, unitIndex, targetTile.gridX, targetTile.gridY))
            {
                continue;
            }

            int newDistanceToCity = UnitActionRules.GetChebyshevDistance(targetTile.gridX, targetTile.gridY, city.x, city.y);
            bool canAttackCityOccupantFromTarget = CanAttackCityOccupantFromTile(state, unit, targetTile, city);
            bool isCityTile = targetTile == cityTile;
            if (!isCityTile &&
                !canAttackCityOccupantFromTarget &&
                newDistanceToCity > currentDistanceToCity + UsefulTileSlack)
            {
                continue;
            }

            int score = Mathf.Max(0, currentDistanceToCity - newDistanceToCity) * 20;
            if (canAttackCityOccupantFromTarget)
            {
                score += 800;
            }

            if (isCityTile)
            {
                score += 1500;
            }

            actions.Add(new SimAction
            {
                Type = SimActionType.Move,
                UnitIndex = unitIndex,
                TargetTile = targetTile,
                MoveCost = entry.Value != null ? Mathf.Max(1, entry.Value.Count) : 1,
                Score = score
            });
        }
    }

    private static SimState ApplyAction(GridManager gridManager, SimState state, SimAction action)
    {
        SimState next = state.Clone();
        SimUnit unit = next.Units[action.UnitIndex];
        if (!unit.Alive)
        {
            return null;
        }

        if (action.Type == SimActionType.Move)
        {
            if (action.TargetTile == null)
            {
                return null;
            }

            int remainingMoves = GetRemainingMoveRangeThisTurn(unit);
            int moveCost = Mathf.Max(1, action.MoveCost);
            if (moveCost > remainingMoves)
            {
                return null;
            }

            unit.X = action.TargetTile.gridX;
            unit.Y = action.TargetTile.gridY;
            unit.MovesUsedThisTurn = UnitActionRules.RegisterMove(unit.MovesUsedThisTurn, unit.MaxMovesPerTurn, moveCost);
            next.Steps.Add(new PlanStep(StepType.Move, unit.RuntimeUnit, action.TargetTile, null));
            return next;
        }

        if (action.Type == SimActionType.Attack)
        {
            SimUnit target = next.Units[action.TargetUnitIndex];
            if (!CanAttackThisTurn(unit) || !target.Alive)
            {
                return null;
            }

            int distance = UnitActionRules.GetChebyshevDistance(unit.X, unit.Y, target.X, target.Y);
            if (!UnitActionRules.IsTargetInAttackRange(unit.AttackRange, distance))
            {
                return null;
            }

            int damage = UnitActionRules.ComputeMitigatedDamage(unit.AttackUnits, target.DefenseUnits);
            if (damage <= 0)
            {
                return null;
            }

            unit.AttacksUsedThisTurn = UnitActionRules.RegisterAttack(unit.AttacksUsedThisTurn, unit.MaxAttacksPerTurn);
            target.CurrentHealthUnits = Mathf.Max(0, target.CurrentHealthUnits - damage);
            if (target.CurrentHealthUnits <= 0)
            {
                target.Alive = false;
                if (unit.AdvancesIntoDefenderTileOnKill)
                {
                    unit.X = target.X;
                    unit.Y = target.Y;
                }
            }

            next.Steps.Add(new PlanStep(StepType.Attack, unit.RuntimeUnit, null, target.RuntimeUnit));
            return next;
        }

        return null;
    }

    private static bool IsActingUnitReady(SimUnit unit, int actingSeatIndex)
    {
        return unit != null &&
               unit.Alive &&
               unit.OwnerSeatIndex == actingSeatIndex &&
               (GetRemainingMoveRangeThisTurn(unit) > 0 || CanAttackThisTurn(unit));
    }

    private static bool CanAttackThisTurn(SimUnit unit)
    {
        return unit != null &&
               UnitActionRules.CanAttackThisTurn(
                   unit.CanAttackAfterMoving,
                   unit.MaxAttacksPerTurn,
                   unit.AttacksUsedThisTurn,
                   unit.MovesUsedThisTurn);
    }

    private static int GetRemainingMoveRangeThisTurn(SimUnit unit)
    {
        if (unit == null || unit.AttacksUsedThisTurn > 0)
        {
            return 0;
        }

        return UnitActionRules.GetRemainingMoveRangeThisTurn(
            unit.RuntimeUnit != null ? unit.RuntimeUnit.UnitTypeId : null,
            unit.MaxMovesPerTurn,
            unit.MovesUsedThisTurn);
    }

    private static bool CanAttackCityOccupantFromTile(SimState state, SimUnit unit, TileVisibility tile, City city)
    {
        if (state == null || unit == null || tile == null || city == null || !CanAttackThisTurn(unit))
        {
            return false;
        }

        SimUnit occupant = GetAliveUnitAt(state, city.x, city.y);
        if (occupant == null || occupant.OwnerSeatIndex == unit.OwnerSeatIndex)
        {
            return false;
        }

        int distance = UnitActionRules.GetChebyshevDistance(tile.gridX, tile.gridY, city.x, city.y);
        return UnitActionRules.IsTargetInAttackRange(unit.AttackRange, distance);
    }

    private static bool HasCapturedCity(SimState state, int actingSeatIndex, City city)
    {
        for (int i = 0; i < state.Units.Count; i++)
        {
            SimUnit unit = state.Units[i];
            if (unit.Alive &&
                unit.OwnerSeatIndex == actingSeatIndex &&
                unit.X == city.x &&
                unit.Y == city.y)
            {
                SimUnit other = GetAliveUnitAt(state, city.x, city.y, i);
                return other == null;
            }
        }

        return false;
    }

    private static bool IsTileBlockedForMove(
        SimState state,
        int movingUnitIndex,
        TileVisibility tile,
        HashSet<TileVisibility> visibleTiles)
    {
        if (tile == null)
        {
            return true;
        }

        SimUnit occupant = GetAliveUnitAt(state, tile.gridX, tile.gridY, movingUnitIndex);
        return occupant != null && IsVisible(tile, visibleTiles);
    }

    private static bool IsTileOccupiedByAliveUnit(SimState state, int ignoredUnitIndex, int x, int y)
    {
        return GetAliveUnitAt(state, x, y, ignoredUnitIndex) != null;
    }

    private static SimUnit GetAliveUnitAt(SimState state, int x, int y, int ignoredUnitIndex = -1)
    {
        for (int i = 0; i < state.Units.Count; i++)
        {
            if (i == ignoredUnitIndex)
            {
                continue;
            }

            SimUnit unit = state.Units[i];
            if (unit.Alive && unit.X == x && unit.Y == y)
            {
                return unit;
            }
        }

        return null;
    }

    private static TileVisibility GetTile(GridManager gridManager, int x, int y)
    {
        return gridManager != null && gridManager.TryGetTile(x, y, out TileVisibility tile) ? tile : null;
    }

    private static bool TryGetVisibleTile(
        GridManager gridManager,
        int x,
        int y,
        HashSet<TileVisibility> visibleTiles,
        out TileVisibility tile)
    {
        tile = null;
        return gridManager != null &&
               gridManager.TryGetTile(x, y, out tile) &&
               tile != null &&
               IsVisible(tile, visibleTiles);
    }

    private static bool IsVisible(TileVisibility tile, HashSet<TileVisibility> visibleTiles)
    {
        return tile != null && visibleTiles != null && visibleTiles.Contains(tile);
    }

    private static string BuildStateSignature(SimState state)
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < state.Units.Count; i++)
        {
            SimUnit unit = state.Units[i];
            builder.Append(i);
            builder.Append(':');
            builder.Append(unit.Alive ? 1 : 0);
            builder.Append(':');
            builder.Append(unit.X);
            builder.Append(',');
            builder.Append(unit.Y);
            builder.Append(':');
            builder.Append(unit.CurrentHealthUnits);
            builder.Append(':');
            builder.Append(unit.MovesUsedThisTurn);
            builder.Append(':');
            builder.Append(unit.AttacksUsedThisTurn);
            builder.Append('|');
        }

        return builder.ToString();
    }

    private static string BuildSummary(City city, IReadOnlyList<PlanStep> steps)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("city=(");
        builder.Append(city.x);
        builder.Append(',');
        builder.Append(city.y);
        builder.Append(") steps=");
        for (int i = 0; i < steps.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(" -> ");
            }

            PlanStep step = steps[i];
            builder.Append(step.Unit != null ? step.Unit.DisplayName : "<unit>");
            builder.Append(step.StepType == StepType.Move ? " move" : " attack");
            if (step.TargetTile != null)
            {
                builder.Append(" (");
                builder.Append(step.TargetTile.gridX);
                builder.Append(',');
                builder.Append(step.TargetTile.gridY);
                builder.Append(')');
            }
            else if (step.TargetUnit != null)
            {
                builder.Append(' ');
                builder.Append(step.TargetUnit.DisplayName);
            }
        }

        return builder.ToString();
    }
}
