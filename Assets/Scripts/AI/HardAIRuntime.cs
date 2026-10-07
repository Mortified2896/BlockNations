using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using BlockNations.AI;
using UnityEngine;
using Object = UnityEngine.Object;

// Unity adapter owns observation, incremental scheduling, and authoritative execution.
// Policy/simulation code cannot inspect the scene or use human-viewer visibility.
public sealed class HardAIRuntime
{
    private readonly IAIActionPolicy policy = new HardTacticianPolicy();
    private readonly Dictionary<int, Dictionary<int, AICityState>> cityMemory = new Dictionary<int, Dictionary<int, AICityState>>();
    private readonly HashSet<string> failedActions = new HashSet<string>(StringComparer.Ordinal);
    private IAIDecision activeDecision;
    public int KnowledgeGeneration { get; private set; }
    public void ResetKnowledge()
    {
        KnowledgeGeneration++;
        activeDecision?.Cancel();
        activeDecision = null;
        cityMemory.Clear();
        failedActions.Clear();
    }

    public sealed class DecisionContext
    {
        public AIObservation Observation;
        public Unit[] Units;
        public City[] Cities;
        public HashSet<TileVisibility> Visible;
        public readonly Dictionary<string, LegalTurnAction> RuntimeActions = new Dictionary<string, LegalTurnAction>(StringComparer.Ordinal);
    }

    public IEnumerator RunTurn(TurnManager manager, int seatIndex) => RunTurnWithPolicy(manager, seatIndex, policy);

    public IEnumerator RunTurnWithPolicy(TurnManager manager, int seatIndex, IAIActionPolicy actionPolicy)
    {
        if (!CanRun(manager, seatIndex)) yield break;
        int generation = KnowledgeGeneration;
        failedActions.Clear();
        foreach (Unit unit in Object.FindObjectsByType<Unit>())
            if (unit.ownerSeatIndex == seatIndex) unit.ResetMovementForTurn();
        int remainingWork = HardTacticianPolicy.TurnWorkBudget;
        // Safety bound scales with current assets; each successful action consumes a resource.
        int actionLimit = 32 + Object.FindObjectsByType<Unit>().Length * 8;
        for (int actionNumber = 0; actionNumber < actionLimit && CanRun(manager, seatIndex) && generation == KnowledgeGeneration; actionNumber++)
        {
            DecisionContext context = Observe(manager, seatIndex);
            IAIDecision search = actionPolicy.BeginDecision(context.Observation, Math.Min(HardTacticianPolicy.DecisionWorkBudget, remainingWork));
            activeDecision = search;
            Stopwatch elapsed = Stopwatch.StartNew();
            HardAIDiagnostics.Begin(context.Observation, search, actionPolicy.Version, manager.turnNumber);
            while (!search.Complete && CanRun(manager, seatIndex) && generation == KnowledgeGeneration)
            {
                while (manager.IsAIExecutionPaused && CanRun(manager, seatIndex) && generation == KnowledgeGeneration) yield return null;
                if (!CanRun(manager, seatIndex) || generation != KnowledgeGeneration) yield break;
                Stopwatch slice = Stopwatch.StartNew();
                do { search.AdvanceOnce(); }
                while (!search.Complete && slice.Elapsed.TotalMilliseconds < 4);
                HardAIDiagnostics.ElapsedSeconds = elapsed.Elapsed.TotalSeconds;
                if (!search.Complete) yield return null;
            }
            if (!CanRun(manager, seatIndex) || generation != KnowledgeGeneration) yield break;
            remainingWork = Math.Max(0, remainingWork - search.WorkCompleted);
            AICandidatePlan best = search.Best;
            if (best == null || best.Actions.Length == 0 || best.Actions[0].Kind == AIActionKind.EndTurn)
            {
                if (HardAIExperienceRecorder.Enabled) HardAIExperienceRecorder.Append(AIExperienceRecord.Create(
                    context.Observation, search, actionPolicy.Version, manager.turnNumber, AIExecutionResult.EndTurn));
                yield break;
            }
            while ((HardAIDiagnostics.ShouldWait || manager.IsAIExecutionPaused) && CanRun(manager, seatIndex) && generation == KnowledgeGeneration) yield return null;
            if (!CanRun(manager, seatIndex) || generation != KnowledgeGeneration) yield break;
            HardAIDiagnostics.ConsumeStep();
            AIAction selected = best.Actions[0];
            bool executed = context.RuntimeActions.TryGetValue(selected.Key, out LegalTurnAction action) && TryExecute(manager, action);
            if (!executed)
                failedActions.Add(selected.Key);
            else failedActions.Clear();
            HardAIDiagnostics.LastAction = (executed ? "" : "Rejected: ") + Describe(context.Observation, selected);
            manager.RecalculatePlayerVisibility();
            if (HardAIExperienceRecorder.Enabled && generation == KnowledgeGeneration && manager.gridManager != null)
                HardAIExperienceRecorder.Append(AIExperienceRecord.Create(context.Observation, search, actionPolicy.Version,
                    manager.turnNumber, executed ? AIExecutionResult.Executed : AIExecutionResult.Rejected,
                    Observe(manager, seatIndex).Observation, manager.gameOver));
            // Rebuild visibility and the root mask after EVERY action, including recruitment.
            yield return null;
        }
    }

    private static bool CanRun(TurnManager manager, int seat) => manager != null && !manager.gameOver &&
        manager.gridManager != null &&
        manager.currentMode == TurnManager.GameMode.VsAI && manager.currentTurnSeatIndex == seat && manager.IsTurnOwnedBySeat(seat);

    public DecisionContext Observe(TurnManager manager, int seat)
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

        DecisionContext context = new DecisionContext { Observation = observation, Units = units.ToArray(), Cities = cities, Visible = visible };
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
                        AIActionRules.Damage(legal.Unit.attackUnits, legal.TargetUnit.defenseUnits) <= 0) continue;
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
            if (failedActions.Contains(action.Key)) continue;
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

    internal static bool TryExecute(TurnManager manager, LegalTurnAction proposed)
    {
        if (!CanRun(manager, proposed.SeatIndex)) return false;
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

    public static string Describe(AIObservation observation, AIAction action)
    {
        string destination = $"({action.Destination % observation.Width},{action.Destination / observation.Width})";
        if (action.Kind == AIActionKind.EndTurn) return "End turn";
        if (action.Kind == AIActionKind.Recruit) return $"Recruit {observation.RecruitTypes[action.RecruitType].Type} at {destination}";
        string actor = action.Actor < observation.Units.Length ? observation.Units[action.Actor].Type : "new recruit";
        return $"{actor} #{action.Actor}: {action.Kind} {destination}";
    }
}

public static class HardAIDiagnostics
{
    public static AIObservation Observation { get; private set; }
    public static IAIDecision Search { get; private set; }
    public static string PolicyVersion { get; private set; }
    public static int Turn { get; private set; }
    public static double ElapsedSeconds;
    public static string LastAction;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static bool PauseBeforeAction;
    private static bool stepRequested;
    public static bool ShouldWait => PauseBeforeAction && !stepRequested;
    public static void RequestStep() => stepRequested = true;
    public static void ConsumeStep() => stepRequested = false;
#else
    public static bool ShouldWait => false;
    public static void ConsumeStep() { }
#endif
    public static void Begin(AIObservation observation, IAIDecision search, string version, int turn)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Observation = observation;
        Search = search;
        PolicyVersion = version;
        Turn = turn;
        ElapsedSeconds = 0;
#endif
    }
}
