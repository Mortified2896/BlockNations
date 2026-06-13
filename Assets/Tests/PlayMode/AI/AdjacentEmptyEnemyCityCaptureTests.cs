using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using NUnit.Framework;
using UnityEngine.TestTools;

public class AdjacentEmptyEnemyCityCaptureTests
{
    private object _turnManager, _gridManager, _city, _cityTile;
    private Type _tileVisibilityType, _cityType, _unitType, _turnManagerType;
    private Type _gridManagerType, _legalTurnActionType, _legalActionServiceType;
    private Type _planStepType, _planType, _stepTypeEnum, _gameModeEnum;
    private Type _gridUtilsType;
    private object _moveStepValue;

    private readonly List<GameObject> _sceneObjects = new List<GameObject>();
    private readonly List<GameObject> _caseObjects = new List<GameObject>();

    private static readonly Vector2Int[] Offsets =
    {
        new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
        new Vector2Int(-1, 0),                         new Vector2Int(1, 0),
        new Vector2Int(-1, 1),  new Vector2Int(0, 1),  new Vector2Int(1, 1)
    };

    private static Type FindType(string name)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(name);
            if (t != null) return t;
        }
        Assert.Fail($"Type '{name}' not found");
        return null;
    }

    private static void SetMember(object obj, string name, object value)
    {
        var type = obj.GetType();
        var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null) { f.SetValue(obj, value); return; }
        var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null) { p.SetValue(obj, value); return; }
        Assert.Fail($"Field/property '{name}' not found on {type.Name}");
    }

    private static object GetMember(object obj, string name)
    {
        var type = obj.GetType();
        var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null) return f.GetValue(obj);
        var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null) return p.GetValue(obj);
        Assert.Fail($"Field/property '{name}' not found on {type.Name}");
        return null;
    }

    private static object InvokeMethod(object obj, string name, object[] args)
    {
        foreach (var m in obj.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (m.Name == name && m.GetParameters().Length == args.Length)
                return m.Invoke(obj, args);
        }
        Assert.Fail($"Method '{name}' with {args.Length} params not found on {obj.GetType().Name}");
        return null;
    }

    private static object InvokeStaticMethodCompatible(Type type, string name, object[] args)
    {
        foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (m.Name != name) continue;
            var pars = m.GetParameters();
            if (pars.Length != args.Length) continue;
            bool match = true;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != null && !pars[i].ParameterType.IsAssignableFrom(args[i].GetType()))
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return m.Invoke(null, args);
        }
        Assert.Fail($"Static method '{name}' with {args.Length} params not found on {type.Name}");
        return null;
    }

    private static Vector2Int GetTileCoords(object tile)
    {
        if (tile == null) return new Vector2Int(int.MinValue, int.MinValue);
        int x = (int)GetMember(tile, "gridX");
        int y = (int)GetMember(tile, "gridY");
        return new Vector2Int(x, y);
    }

    private static string FormatTile(object tile)
    {
        if (tile == null) return "<null>";
        var c = GetTileCoords(tile);
        return $"({c.x},{c.y})";
    }

    private static string DescribeLegalActions(IEnumerable actions, object expectedUnit, object expectedTargetTile)
    {
        if (actions == null) return "  (no actions enumerable)";
        var sb = new StringBuilder();
        int index = 0;
        bool truncated = false;
        const int maxItems = 12;
        foreach (var action in actions)
        {
            if (action == null) continue;
            if (index >= maxItems)
            {
                truncated = true;
                break;
            }
            try
            {
                object actionType = GetMember(action, "ActionType");
                int seatIndex = (int)GetMember(action, "SeatIndex");
                object unit = GetMember(action, "Unit");
                object originTile = GetMember(action, "OriginTile");
                object targetTile = GetMember(action, "TargetTile");
                object targetUnit = GetMember(action, "TargetUnit");
                bool isRelevant = ReferenceEquals(unit, expectedUnit)
                                  || (expectedTargetTile != null && ReferenceEquals(targetTile, expectedTargetTile));
                string marker = isRelevant ? "*" : " ";
                sb.Append($"\n    {marker}[{index}] {actionType} seat={seatIndex} " +
                          $"unit={(unit == null ? "<null>" : "u")} " +
                          $"origin={FormatTile(originTile)} target={FormatTile(targetTile)} " +
                          $"targetUnit={(targetUnit == null ? "<null>" : "enemy")}");
            }
            catch (Exception ex)
            {
                sb.Append($"\n    [{index}] <describe error: {ex.GetType().Name}>");
            }
            index++;
        }
        if (truncated) sb.Append($"\n    ... ({index}+ more actions truncated)");
        return sb.ToString();
    }

    [SetUp]
    public void SetUp()
    {
        _turnManagerType = FindType("TurnManager");
        _gridManagerType = FindType("GridManager");
        _tileVisibilityType = FindType("TileVisibility");
        _cityType = FindType("City");
        _unitType = FindType("Unit");
        _legalTurnActionType = FindType("LegalTurnAction");
        _legalActionServiceType = FindType("LegalActionService");
        _planStepType = FindType("AICityCaptureTacticalPlanner+PlanStep");
        _planType = FindType("AICityCaptureTacticalPlanner+Plan");
        _stepTypeEnum = FindType("AICityCaptureTacticalPlanner+StepType");
        _gameModeEnum = FindType("TurnManager+GameMode");
        _gridUtilsType = FindType("GridUtils");
        _moveStepValue = Enum.Parse(_stepTypeEnum, "Move");

        var tmGo = new GameObject("TurnManager");
        tmGo.SetActive(false);
        _turnManager = tmGo.AddComponent(_turnManagerType);
        _sceneObjects.Add(tmGo);

        var gmGo = new GameObject("GridManager");
        gmGo.SetActive(false);
        _gridManager = gmGo.AddComponent(_gridManagerType);
        _sceneObjects.Add(gmGo);

        SetMember(_turnManager, "gridManager", _gridManager);

        int size = 7;
        SetMember(_gridManager, "width", size);
        SetMember(_gridManager, "height", size);
        SetMember(_gridManager, "tileSize", 1f);

        var tileGrid = Array.CreateInstance(_tileVisibilityType, size, size);
        SetMember(_gridManager, "tileGrid", tileGrid);

        var initMethod = _tileVisibilityType.GetMethod("Initialize", new[] { typeof(int), typeof(int) });
        Assert.IsNotNull(initMethod, "TileVisibility.Initialize not found");

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                var tileGo = new GameObject($"Tile_{x}_{y}");
                tileGo.transform.position = new Vector3(x, y, 0);
                var tv = tileGo.AddComponent(_tileVisibilityType);
                initMethod.Invoke(tv, new object[] { x, y });
                tileGrid.SetValue(tv, x, y);
                _sceneObjects.Add(tileGo);
            }
        }

        var cityGo = new GameObject("City");
        _city = cityGo.AddComponent(_cityType);
        SetMember(_city, "x", 3);
        SetMember(_city, "y", 3);
        cityGo.transform.position = new Vector3(3, 3, 0);
        _cityTile = ((Array)GetMember(_gridManager, "tileGrid")).GetValue(3, 3);
        _sceneObjects.Add(cityGo);
    }

    [UnityTest]
    public IEnumerator AdjacentEmptyEnemyCityIsCaptured()
    {
        SetMember(_turnManager, "currentMode", Enum.Parse(_gameModeEnum, "VsAI"));

        // Capture the initial set of cities once so we can assert no unrelated ownership changes.
        // We deliberately do not add more cities in this test; if we ever do, this snapshot
        // becomes the safety net.
        var initialCities = UnityEngine.Object.FindObjectsByType(_cityType,
            FindObjectsSortMode.None);
        var initialCityOwners = new Dictionary<int, int>();
        for (int i = 0; i < initialCities.Length; i++)
        {
            var c = initialCities[i];
            if (c == null) continue;
            int owner = (int)GetMember(c, "ownerSeatIndex");
            int x = (int)GetMember(c, "x");
            int y = (int)GetMember(c, "y");
            initialCityOwners[FlattenCityKey(x, y)] = owner;
        }

        for (int seat = 0; seat <= 1; seat++)
        {
            int actingSeat = seat;
            int enemySeat = 1 - seat;

            for (int off = 0; off < Offsets.Length; off++)
            {
                Vector2Int offset = Offsets[off];
                int ux = 3 + offset.x;
                int uy = 3 + offset.y;
                string caseLabel = $"seat={actingSeat} unit=({ux},{uy})";

                SetMember(_turnManager, "currentTurnSeatIndex", actingSeat);
                SetMember(_turnManager, "isPlayerTurn", actingSeat == 0);
                SetMember(_turnManager, "gameOver", false);

                // ---- Precondition: city starts enemy-owned, not neutral/friendly ----
                InvokeMethod(_city, "SetOwnerSeatIndex", new object[] { enemySeat });
                int preOwner = (int)GetMember(_city, "ownerSeatIndex");
                Assert.AreEqual(enemySeat, preOwner,
                    $"[{caseLabel}] Target city should start owned by enemySeat={enemySeat}, " +
                    $"but ownerSeatIndex was {preOwner}.");
                Assert.AreNotEqual(actingSeat, preOwner,
                    $"[{caseLabel}] Target city must not start owned by actingSeat={actingSeat}.");

                // ---- Visibility setup: both source and target tiles marked visible for acting seat ----
                var hashSetType = typeof(HashSet<>).MakeGenericType(_tileVisibilityType);
                var visibleTiles = Activator.CreateInstance(hashSetType);
                var addMethod = hashSetType.GetMethod("Add");

                InvokeMethod(_cityTile, "SetVisibleForSeat", new object[] { true, actingSeat });
                addMethod.Invoke(visibleTiles, new object[] { _cityTile });

                var unitTile = ((Array)GetMember(_gridManager, "tileGrid")).GetValue(ux, uy);
                InvokeMethod(unitTile, "SetVisibleForSeat", new object[] { true, actingSeat });
                addMethod.Invoke(visibleTiles, new object[] { unitTile });

                var containsMethod = hashSetType.GetMethod("Contains");
                bool cityTileInSet = (bool)containsMethod.Invoke(visibleTiles, new object[] { _cityTile });
                bool unitTileInSet = (bool)containsMethod.Invoke(visibleTiles, new object[] { unitTile });
                Assert.IsTrue(cityTileInSet,
                    $"[{caseLabel}] Synthetic visibleTiles must contain target city tile {FormatTile(_cityTile)}.");
                Assert.IsTrue(unitTileInSet,
                    $"[{caseLabel}] Synthetic visibleTiles must contain source unit tile {FormatTile(unitTile)}.");

                // ---- Acting unit setup ----
                var unitGo = new GameObject($"Unit_{ux}_{uy}");
                var unit = unitGo.AddComponent(_unitType);
                SetMember(unit, "ownerSeatIndex", actingSeat);
                unitGo.transform.position = new Vector3(ux, uy, 0);
                InvokeMethod(unit, "ApplyDefinition", new object[] { "warrior", true });
                SetMember(unit, "movesUsedThisTurn", 0);
                _caseObjects.Add(unitGo);

                // ---- Precondition: acting unit exists, is active, and belongs to acting seat ----
                Assert.IsTrue(unitGo.activeInHierarchy,
                    $"[{caseLabel}] Acting unit GameObject should be active in hierarchy.");
                Assert.AreEqual(actingSeat, (int)GetMember(unit, "ownerSeatIndex"),
                    $"[{caseLabel}] Acting unit must belong to actingSeat={actingSeat}.");

                // ---- Precondition: source/target adjacency (Chebyshev) ----
                int chebyshev = Mathf.Max(Mathf.Abs(ux - 3), Mathf.Abs(uy - 3));
                Assert.AreEqual(1, chebyshev,
                    $"[{caseLabel}] Source ({ux},{uy}) must be Chebyshev-adjacent to target (3,3). " +
                    $"Chebyshev distance was {chebyshev}.");

                // ---- Precondition: target tile contains the intended city ----
                Assert.AreEqual(3, (int)GetMember(_city, "x"),
                    $"[{caseLabel}] City x must be 3, was {(int)GetMember(_city, "x")}.");
                Assert.AreEqual(3, (int)GetMember(_city, "y"),
                    $"[{caseLabel}] City y must be 3, was {(int)GetMember(_city, "y")}.");
                Assert.AreEqual(new Vector2Int(3, 3), GetTileCoords(_cityTile),
                    $"[{caseLabel}] _cityTile must resolve to grid (3,3).");

                // ---- Precondition: target city is empty (stationedUnit == null) ----
                object stationedUnit = GetMember(_city, "stationedUnit");
                Assert.IsNull(stationedUnit,
                    $"[{caseLabel}] Target city should not have a stationedUnit, but had " +
                    $"{(stationedUnit == null ? "null" : stationedUnit.GetType().Name)}.");

                // ---- Precondition: no unit at target tile according to GridUtils.GetUnitAtPosition ----
                if (_gridUtilsType != null)
                {
                    Vector3 cityPos = new Vector3(3, 3, 0);
                    var getUnitMethod = _gridUtilsType.GetMethod("GetUnitAtPosition",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        binder: null,
                        types: new[] { typeof(Vector3), _unitType },
                        modifiers: null);
                    Assert.IsNotNull(getUnitMethod,
                        $"[{caseLabel}] GridUtils.GetUnitAtPosition(Vector3, Unit) not found.");
                    object occupant = getUnitMethod.Invoke(null, new object[] { cityPos, unit });
                    Assert.IsNull(occupant,
                        $"[{caseLabel}] GridUtils.GetUnitAtPosition must return null at the empty " +
                        $"enemy city tile, but returned {(occupant == null ? "null" : occupant.GetType().Name)}.");
                }

                // ---- Precondition: current turn seat / IsTurnOwnedBySeat / isPlayerTurn bridge ----
                Assert.AreEqual(actingSeat, (int)GetMember(_turnManager, "currentTurnSeatIndex"),
                    $"[{caseLabel}] TurnManager.currentTurnSeatIndex must equal actingSeat.");
                bool isTurnOwned = (bool)InvokeMethod(_turnManager, "IsTurnOwnedBySeat",
                    new object[] { actingSeat });
                Assert.IsTrue(isTurnOwned,
                    $"[{caseLabel}] TurnManager.IsTurnOwnedBySeat({actingSeat}) must be true.");
                Assert.AreEqual(actingSeat == 0, (bool)GetMember(_turnManager, "isPlayerTurn"),
                    $"[{caseLabel}] In VsAI, isPlayerTurn must equal (actingSeat == 0).");

                // ---- Enumerate legal actions and extract the exact expected capture-like action ----
                var legalActions = InvokeStaticMethodCompatible(_legalActionServiceType,
                    "GetLegalUnitActionsForSeat",
                    new object[] { _turnManager, actingSeat, visibleTiles });

                var actionTypeProp = _legalTurnActionType.GetProperty("ActionType");
                var targetTileProp = _legalTurnActionType.GetProperty("TargetTile");
                var targetUnitProp = _legalTurnActionType.GetProperty("TargetUnit");
                var originTileProp = _legalTurnActionType.GetProperty("OriginTile");
                var unitProp = _legalTurnActionType.GetProperty("Unit");
                var seatIndexProp = _legalTurnActionType.GetProperty("SeatIndex");
                Assert.IsNotNull(actionTypeProp, "ActionType property");
                Assert.IsNotNull(targetTileProp, "TargetTile property");

                object matchedAction = null;
                int candidateCount = 0;
                int relevantCount = 0;
                foreach (var action in (IEnumerable)legalActions)
                {
                    candidateCount++;
                    var actionType = actionTypeProp.GetValue(action);
                    if (actionType.ToString() != "UnitMove") continue;
                    int actionSeat = (int)seatIndexProp.GetValue(action);
                    if (actionSeat != actingSeat) continue;
                    var actionUnit = unitProp.GetValue(action);
                    if (!ReferenceEquals(actionUnit, unit)) continue;
                    var originTile = originTileProp.GetValue(action);
                    if (!ReferenceEquals(originTile, unitTile)) continue;
                    var targetTile = targetTileProp.GetValue(action);
                    if (!ReferenceEquals(targetTile, _cityTile)) continue;
                    var targetUnit = targetUnitProp.GetValue(action);
                    if (targetUnit != null) continue;
                    relevantCount++;
                    if (matchedAction == null) matchedAction = action;
                }

                if (matchedAction == null)
                {
                    string actionsBlock = DescribeLegalActions(
                        (IEnumerable)legalActions, unit, _cityTile);
                    Assert.Fail(
                        $"[{caseLabel}] Expected a capture-like UnitMove by the acting unit from " +
                        $"source {FormatTile(unitTile)} to enemy-owned empty city {FormatTile(_cityTile)}, " +
                        $"but none was generated. " +
                        $"Setup: preOwner={preOwner}, source visible={unitTileInSet}, " +
                        $"target visible={cityTileInSet}. " +
                        $"Legal action count={candidateCount}; relevant UnitMove candidates for " +
                        $"this unit targeting the city tile={relevantCount}." +
                        actionsBlock);
                }

                // ---- Build a one-step Move plan and execute it via the production executor ----
                var planStepArray = Array.CreateInstance(_planStepType, 1);
                var planStepCtor = _planStepType.GetConstructor(
                    new[] { _stepTypeEnum, _unitType, _tileVisibilityType, _unitType });
                Assert.IsNotNull(planStepCtor, "PlanStep constructor not found");
                var planStep = planStepCtor.Invoke(new object[] { _moveStepValue, unit, _cityTile, null });
                planStepArray.SetValue(planStep, 0);

                ConstructorInfo planCtor = null;
                foreach (var c in _planType.GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var pars = c.GetParameters();
                    if (pars.Length == 3 && pars[0].ParameterType == _cityType
                        && pars[2].ParameterType == typeof(string))
                    {
                        planCtor = c;
                        break;
                    }
                }
                Assert.IsNotNull(planCtor, "Plan constructor not found");
                var plan = planCtor.Invoke(new object[] { _city, planStepArray,
                    $"test: seat={actingSeat} unit=({ux},{uy})" });

                MethodInfo tryExecMethod = null;
                foreach (var m in _turnManagerType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name == "TryExecuteTacticalCityCapturePlan" && m.GetParameters().Length == 2)
                    {
                        tryExecMethod = m;
                        break;
                    }
                }
                Assert.IsNotNull(tryExecMethod, "TryExecuteTacticalCityCapturePlan not found");

                object execReturn = tryExecMethod.Invoke(_turnManager, new object[] { plan, visibleTiles });
                bool executed = execReturn is bool b && b;
                Assert.IsTrue(executed,
                    $"[{caseLabel}] TurnManager.TryExecuteTacticalCityCapturePlan must return true; " +
                    $"executor should have captured the empty enemy city tile.");

                // ---- Postconditions: ownership, side effects, no unrelated city mutation ----
                int postOwner = (int)GetMember(_city, "ownerSeatIndex");
                Assert.AreEqual(actingSeat, postOwner,
                    $"[{caseLabel}] After capture, city ownerSeatIndex should be {actingSeat}, " +
                    $"was {postOwner}.");
                Assert.AreNotEqual(enemySeat, postOwner,
                    $"[{caseLabel}] After capture, city ownerSeatIndex must not still be " +
                    $"enemySeat={enemySeat}.");

                Assert.IsTrue((bool)GetMember(_turnManager, "gameOver"),
                    $"[{caseLabel}] After capture, TurnManager.gameOver should be true.");

                // Acting unit should now resolve to the target city tile.
                var postUnits = UnityEngine.Object.FindObjectsByType(_unitType,
                    FindObjectsSortMode.None);
                object postUnit = null;
                for (int i = 0; i < postUnits.Length; i++)
                {
                    if (ReferenceEquals(postUnits[i], unit)) { postUnit = postUnits[i]; break; }
                }
                Assert.IsNotNull(postUnit, $"[{caseLabel}] Acting unit should still exist after capture.");
                var resolvedTile = ((Array)GetMember(_gridManager, "tileGrid"))
                    .GetValue(3, 3);
                Vector3 expectedPos = new Vector3(3, 3, 0);
                Vector3 actualPos = ((Component)postUnit).transform.position;
                Assert.AreEqual(expectedPos.x, actualPos.x, 0.001f,
                    $"[{caseLabel}] Unit X should be {expectedPos.x}, was {actualPos.x}.");
                Assert.AreEqual(expectedPos.y, actualPos.y, 0.001f,
                    $"[{caseLabel}] Unit Y should be {expectedPos.y}, was {actualPos.y}.");

                // No unrelated city should have changed ownership.
                var postCities = UnityEngine.Object.FindObjectsByType(_cityType,
                    FindObjectsSortMode.None);
                for (int i = 0; i < postCities.Length; i++)
                {
                    var pc = postCities[i];
                    if (pc == null || ReferenceEquals(pc, _city)) continue;
                    int cx = (int)GetMember(pc, "x");
                    int cy = (int)GetMember(pc, "y");
                    int newOwner = (int)GetMember(pc, "ownerSeatIndex");
                    int prevOwner;
                    bool hadPrev = initialCityOwners.TryGetValue(FlattenCityKey(cx, cy), out prevOwner);
                    // The captured city is the one allowed to change; every other city whose
                    // owner we recorded before the test ran must be unchanged after capture.
                    // The test currently creates only one city, so this is a future safety net:
                    // if anyone adds a second city later, any capture-time mutation of it will
                    // fail here with a clear message naming the unrelated (cx,cy).
                    if (hadPrev)
                    {
                        Assert.AreEqual(prevOwner, newOwner,
                            $"[{caseLabel}] Unrelated city ({cx},{cy}) ownership changed from " +
                            $"{prevOwner} to {newOwner}; capture should not affect it.");
                    }
                }

                foreach (var go in _caseObjects)
                {
                    if (go != null)
                        UnityEngine.Object.DestroyImmediate(go);
                }
                _caseObjects.Clear();

                yield return null;
            }
        }
    }

    private static int FlattenCityKey(int x, int y)
    {
        return (y * 1024) + x;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var go in _caseObjects)
        {
            if (go != null)
                UnityEngine.Object.DestroyImmediate(go);
        }
        _caseObjects.Clear();

        foreach (var go in _sceneObjects)
        {
            if (go != null)
                UnityEngine.Object.DestroyImmediate(go);
        }
        _sceneObjects.Clear();
    }
}
