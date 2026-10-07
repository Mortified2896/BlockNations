using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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

        int size = 11;
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

    private object CreateHardRuntime() => Activator.CreateInstance(FindType("HardAIRuntime"));

    [Test]
    public void ExternalArenaDisablesHumanCommandsAndFileSaving()
    {
        SetMember(_turnManager, "externallyDrivenMatch", true);
        SetMember(_turnManager, "externalHumanSeatIndex", -1);
        SetMember(_turnManager, "currentMode", Enum.Parse(_gameModeEnum, "VsAI"));
        SetMember(_turnManager, "currentTurnSeatIndex", 0);
        Assert.That(InvokeMethod(_turnManager, "CanLocalPlayerIssueCommands", Array.Empty<object>()), Is.False);
        Assert.That(InvokeMethod(_turnManager, "CanAdvanceTurn", Array.Empty<object>()), Is.False);
        string temporary = Path.Combine(Application.temporaryCachePath, "external-match-must-not-save-" + Guid.NewGuid() + ".json");
        InvokeMethod(_turnManager, "SaveToFile", new object[] { temporary });
        Assert.That(File.Exists(temporary), Is.False);
    }

    [Test]
    public void ExternalSeatProgressionResetsActionsAndCollectsIncomeWithoutUiManagers()
    {
        SetMember(_turnManager, "externallyDrivenMatch", true);
        SetMember(_turnManager, "ExternalMatchReady", true);
        SetMember(_turnManager, "currentMode", Enum.Parse(_gameModeEnum, "VsAI"));
        SetMember(_turnManager, "currentTurnSeatIndex", 0);
        InvokeMethod(_turnManager, "SetSeatGoldFromLegacyBridges", new object[] { 2, 2, 2 });
        InvokeMethod(_city, "SetOwnerSeatIndex", new object[] { 1 });
        SetMember(_city, "hasRecruitedThisTurn", true);
        object unit = CreateUnit(1, 2, 3);
        SetMember(unit, "movesUsedThisTurn", 1);
        SetMember(unit, "attacksUsedThisTurn", 1);
        Assert.That(InvokeMethod(_turnManager, "TryAdvanceExternalMatchTurn", new object[] { 0 }), Is.True);
        Assert.That((int)GetMember(_turnManager, "currentTurnSeatIndex"), Is.EqualTo(1));
        Assert.That((int)GetMember(unit, "movesUsedThisTurn"), Is.Zero);
        Assert.That((int)GetMember(unit, "attacksUsedThisTurn"), Is.Zero);
        Assert.That((bool)GetMember(_city, "hasRecruitedThisTurn"), Is.False);
        Assert.That(InvokeMethod(_turnManager, "GetGoldForSeat", new object[] { 1 }), Is.EqualTo(3));
        Assert.That(InvokeMethod(_turnManager, "TryAdvanceExternalMatchTurn", new object[] { 0 }), Is.False);
    }

    [Test]
    public void ExternalSpectatorDoesNotRevealUnseenTilesToPolicy()
    {
        SetMember(_turnManager, "externallyDrivenMatch", true);
        SetMember(_turnManager, "externalHumanSeatIndex", -1);
        SetMember(_turnManager, "currentMode", Enum.Parse(_gameModeEnum, "VsAI"));
        SetMember(_turnManager, "currentTurnSeatIndex", 0);
        InvokeMethod(_city, "SetOwnerSeatIndex", new object[] { 0 });
        CreateUnit(0, 2, 3);
        CreateUnit(1, 9, 9);
        InvokeMethod(_turnManager, "RecalculatePlayerVisibility", Array.Empty<object>());
        object source = Activator.CreateInstance(FindType("SeatAIObservationSource"));
        object context = InvokeMethod(source, "Observe", new object[] { _turnManager, 0, true, null });
        object observation = GetMember(context, "Observation");
        Assert.That(((Array)GetMember(observation, "Units")).Length, Is.EqualTo(1));
        Assert.That(((bool[])GetMember(observation, "Seen"))[9 * 11 + 9], Is.False);
        Assert.That(((bool[])GetMember(observation, "Visible"))[9 * 11 + 9], Is.False);
    }

    [Test]
    public void TrainerEpisodeResetDiscardsInFlightBufferAndAcceptsFreshDecision()
    {
        SetMember(_turnManager, "externallyDrivenMatch", true);
        SetMember(_turnManager, "ExternalMatchReady", true);
        SetMember(_turnManager, "currentMode", Enum.Parse(_gameModeEnum, "VsAI"));
        SetMember(_turnManager, "currentTurnSeatIndex", 0);
        InvokeMethod(_city, "SetOwnerSeatIndex", new object[] { 0 });

        var arenaObject = new GameObject("ResetTestArena");
        arenaObject.SetActive(false);
        var arena = arenaObject.AddComponent(FindType("TrainingArena"));
        _sceneObjects.Add(arenaObject);
        SetMember(arena, "turnManager", _turnManager);
        SetMember(arena, "ready", true);

        var agentObject = new GameObject("ResetTestAgent");
        agentObject.SetActive(false);
        var agent = agentObject.AddComponent(FindType("TrainingSeatAgent"));
        _sceneObjects.Add(agentObject);
        SetMember(agent, "arena", arena);
        SetMember(agent, "seatIndex", 0);
        object source = Activator.CreateInstance(FindType("SeatAIObservationSource"));
        object context = InvokeMethod(source, "Observe", new object[] { _turnManager, 0, true, null });
        Type bufferType = FindType("Unity.MLAgents.Actuators.ActionBuffers");
        object cleared = Activator.CreateInstance(bufferType, new object[] { Array.Empty<float>(), new[] { 0 } });
        InvokeMethod(agent, "PrepareDecision", new[] { context });
        InvokeMethod(agent, "OnEpisodeBegin", Array.Empty<object>());
        InvokeMethod(agent, "OnActionReceived", new[] { cleared });
        Assert.That(GetMember(arena, "Failure"), Is.Null);
        Assert.That(GetMember(arena, "Rejections"), Is.EqualTo(0));
        Assert.That(GetMember(_turnManager, "currentTurnSeatIndex"), Is.EqualTo(0));

        int endTurn = (int)FindType("BlockNations.AI.LearnedActionSchema").GetField("EndTurn").GetValue(null);
        object fresh = Activator.CreateInstance(bufferType, new object[] { Array.Empty<float>(), new[] { endTurn } });
        InvokeMethod(agent, "PrepareDecision", new[] { context });
        InvokeMethod(agent, "OnActionReceived", new[] { fresh });
        Assert.That(GetMember(_turnManager, "currentTurnSeatIndex"), Is.EqualTo(1));
        Assert.That(GetMember(arena, "Actions"), Is.EqualTo(1L));
        Assert.That(GetMember(arena, "Rejections"), Is.EqualTo(0));
    }

    [Test]
    public void VsAITurnAuthorityUsesSeatAndRejectsInvalidSeatAliases()
    {
        SetMember(_turnManager, "currentMode", Enum.Parse(_gameModeEnum, "VsAI"));
        SetMember(_turnManager, "currentTurnSeatIndex", 1);
        SetMember(_turnManager, "isPlayerTurn", true); // Deliberately stale legacy bridge.
        Assert.That(InvokeMethod(_turnManager, "IsTurnOwnedBySeat", new object[] { 1 }), Is.True);
        Assert.That(InvokeMethod(_turnManager, "IsTurnOwnedBySeat", new object[] { 0 }), Is.False);
        Assert.That(InvokeMethod(_turnManager, "IsTurnOwnedBySeat", new object[] { -1 }), Is.False);
        Assert.That(InvokeMethod(_turnManager, "IsTurnOwnedBySeat", new object[] { 3 }), Is.False);
        Assert.That(InvokeMethod(_turnManager, "CanAdvanceTurn", Array.Empty<object>()), Is.False);
    }

    [Test]
    public void VsAITurnProgressionSynchronizesBridgeAndIncrementsRoundAfterSeatOne()
    {
        SetMember(_turnManager, "currentMode", Enum.Parse(_gameModeEnum, "VsAI"));
        SetMember(_turnManager, "turnNumber", 4);
        SetMember(_turnManager, "currentTurnSeatIndex", 0);
        InvokeMethod(_turnManager, "AdvanceVsAITurnAfterSeat", new object[] { 0 });
        Assert.That((int)GetMember(_turnManager, "currentTurnSeatIndex"), Is.EqualTo(1));
        Assert.That((bool)GetMember(_turnManager, "isPlayerTurn"), Is.False);
        Assert.That((int)GetMember(_turnManager, "turnNumber"), Is.EqualTo(4));
        InvokeMethod(_turnManager, "AdvanceVsAITurnAfterSeat", new object[] { 0 }); // Stale completion.
        Assert.That((int)GetMember(_turnManager, "currentTurnSeatIndex"), Is.EqualTo(1));
        InvokeMethod(_turnManager, "AdvanceVsAITurnAfterSeat", new object[] { 1 });
        Assert.That((int)GetMember(_turnManager, "currentTurnSeatIndex"), Is.Zero);
        Assert.That((bool)GetMember(_turnManager, "isPlayerTurn"), Is.True);
        Assert.That((int)GetMember(_turnManager, "turnNumber"), Is.EqualTo(5));
    }

    [TestCase(4, 3, false, 1)]
    [TestCase(4, 3, true, 0)]
    [TestCase(5, 3, false, 3)]
    [TestCase(5, 2, true, 2)]
    public void SupportedPbpSaveTurnMigrationPreservesLegacyAndExplicitSeatRules(int protocol, int savedSeat, bool legacyPlayerTurn, int expectedSeat)
    {
        object save = Activator.CreateInstance(FindType("TurnManager+GameSave"), nonPublic: true);
        SetMember(save, "protocolVersion", protocol);
        SetMember(save, "currentTurnSeatIndex", savedSeat);
        SetMember(save, "isPlayerTurn", legacyPlayerTurn);
        Assert.That(InvokeStaticMethodCompatible(_turnManagerType, "ResolveCurrentTurnSeatIndex", new object[] { save, 4 }), Is.EqualTo(expectedSeat));
    }

    private object CreateUnit(int seat, int x, int y, string type = "warrior")
    {
        var go = new GameObject($"Test_{type}_{x}_{y}");
        var unit = go.AddComponent(_unitType);
        InvokeMethod(unit, "SetOwnerSeatIndex", new object[] { seat });
        InvokeMethod(unit, "ApplyDefinition", new object[] { type, false });
        go.transform.position = new Vector3(x, y);
        _caseObjects.Add(go);
        return unit;
    }

    [UnityTest]
    public IEnumerator HardRuntimeCapturesForBothSeats()
    {
        SetMember(_turnManager, "currentMode", Enum.Parse(_gameModeEnum, "VsAI"));
        for (int seat = 0; seat < 2; seat++)
        {
            SetMember(_turnManager, "currentTurnSeatIndex", seat);
            SetMember(_turnManager, "isPlayerTurn", seat == 0);
            SetMember(_turnManager, "gameOver", false);
            SetMember(_turnManager, "aiRecruitVariant", Enum.Parse(FindType("TurnManager+AIRecruitVariant"), "HardTactician"));
            InvokeMethod(_city, "SetOwnerSeatIndex", new object[] { 1 - seat });
            object unit = CreateUnit(seat, 2, 3);
            IEnumerator turn = (IEnumerator)InvokeMethod(CreateHardRuntime(), "RunTurn", new object[] { _turnManager, seat });
            yield return turn;
            Assert.That((int)GetMember(_city, "ownerSeatIndex"), Is.EqualTo(seat));
            Assert.That((bool)GetMember(_turnManager, "gameOver"), Is.True);
            Assert.That(((Component)unit).transform.position.x, Is.EqualTo(3));
            foreach (var go in _caseObjects) UnityEngine.Object.DestroyImmediate(go);
            _caseObjects.Clear();
        }
    }

    [UnityTest]
    public IEnumerator CrowdedReviewArmyActuallyMovesAndExploresThroughTheRuntime()
    {
        // Exercise one isolated own turn, with no opposing policy or tournament running.
        Type observationType = FindType("BlockNations.AI.AIObservation");
        string path = Path.Combine(Application.dataPath, "Editor/Tests/Fixtures/HardAI_CrowdedHome.json");
        object observation = JsonUtility.FromJson(File.ReadAllText(path), observationType);
        SetMember(_turnManager, "currentMode", Enum.Parse(_gameModeEnum, "VsAI"));
        SetMember(_turnManager, "currentTurnSeatIndex", 0);
        SetMember(_turnManager, "isPlayerTurn", true);
        SetMember(_turnManager, "playerGold", GetMember(observation, "Gold"));
        InvokeMethod(_city, "SetOwnerSeatIndex", new object[] { 0 });
        SetMember(_city, "x", 1); SetMember(_city, "y", 1);
        ((Component)_city).transform.position = new Vector3(1, 1);
        bool[] seen = (bool[])GetMember(observation, "Seen");
        Array grid = (Array)GetMember(_gridManager, "tileGrid");
        for (int p = 0; p < seen.Length; p++)
            if (seen[p]) InvokeMethod(grid.GetValue(p % 11, p / 11), "RecordSeenBySeat", new object[] { 0 });
        var originalPositions = new Dictionary<Component, Vector3>();
        foreach (object state in (Array)GetMember(observation, "Units"))
        {
            Component unit = (Component)CreateUnit(0, (int)GetMember(state, "X"), (int)GetMember(state, "Y"), (string)GetMember(state, "Type"));
            originalPositions.Add(unit, unit.transform.position);
        }
        FindType("HardAIDiagnostics").GetField("PauseBeforeAction").SetValue(null, false);
        yield return (IEnumerator)InvokeMethod(CreateHardRuntime(), "RunTurn", new object[] { _turnManager, 0 });
        int moved = 0, newlyExplored = 0;
        foreach (var entry in originalPositions) if (entry.Key.transform.position != entry.Value) moved++;
        for (int p = 0; p < seen.Length; p++)
            if (!seen[p] && (bool)InvokeMethod(grid.GetValue(p % 11, p / 11), "HasBeenSeenBySeat", new object[] { 0 })) newlyExplored++;
        Assert.That(moved, Is.GreaterThanOrEqualTo(4), "The execution adapter should advance the army, not stop after one diagnostic move.");
        Assert.That(newlyExplored, Is.GreaterThan(0));
        Assert.That(GetMember(_turnManager, "currentTurnSeatIndex"), Is.EqualTo(0), "The policy cannot take over turn progression.");
        Assert.That(GetMember(_turnManager, "gameOver"), Is.False);
    }

    [UnityTest]
    public IEnumerator ScheduledHardPolicyReachesSearchThroughTurnManagerForBothSeats()
    {
        SetMember(_turnManager, "currentMode", Enum.Parse(_gameModeEnum, "VsAI"));
        SetMember(_turnManager, "enableAIVsAIDebugMode", true);
        Type variant = FindType("TurnManager+AIRecruitVariant");
        Type diagnostics = FindType("HardAIDiagnostics");
        diagnostics.GetField("PauseBeforeAction").SetValue(null, false);
        for (int seat = 0; seat < 2; seat++)
        {
            SetMember(_turnManager, "currentTurnSeatIndex", seat);
            SetMember(_turnManager, "isPlayerTurn", seat == 0);
            SetMember(_turnManager, "gameOver", false);
            SetMember(_turnManager, "aiVsAiSideARecruitVariant", Enum.Parse(variant, seat == 0 ? "HardTactician" : "Default"));
            SetMember(_turnManager, "aiVsAiSideBRecruitVariant", Enum.Parse(variant, seat == 1 ? "HardTactician" : "Default"));
            InvokeMethod(_city, "SetOwnerSeatIndex", new object[] { seat });
            CreateUnit(seat, 2, 3);
            yield return (IEnumerator)InvokeMethod(_turnManager, "RunAITurnForSeat", new object[] { seat });
            Assert.That(diagnostics.GetProperty("PolicyVersion").GetValue(null),
                Is.EqualTo(FindType("BlockNations.AI.HardTacticianPolicy").GetField("PolicyVersion").GetRawConstantValue()));
            object observation = diagnostics.GetProperty("Observation").GetValue(null);
            Assert.That(GetMember(observation, "Seat"), Is.EqualTo(seat));
            Assert.That((bool)GetMember(_turnManager, "gameOver"), Is.False);
            foreach (var go in _caseObjects) UnityEngine.Object.DestroyImmediate(go);
            _caseObjects.Clear();
        }
        SetMember(_turnManager, "enableAIVsAIDebugMode", false);
    }

    [Test]
    public void LiveReviewSpeedPreservesPoliciesAndRejectsChangesOutsideReview()
    {
        SetMember(_turnManager, "currentMode", Enum.Parse(_gameModeEnum, "VsAI"));
        Type speed = FindType("TurnManager+AIVsAIBatchSpeedPreset");
        Type variant = FindType("TurnManager+AIRecruitVariant");
        SetMember(_turnManager, "aiVsAiSideBRecruitVariant", Enum.Parse(variant, "HardTactician"));
        object ultra = Enum.Parse(speed, "UltraFast");
        Assert.That(InvokeMethod(_turnManager, "TrySetAIVsAIDebugSpeed", new[] { ultra }), Is.False);
        SetMember(_turnManager, "enableAIVsAIDebugMode", true);
        SetMember(_turnManager, "gameOver", false);
        Assert.That(InvokeMethod(_turnManager, "TrySetAIVsAIDebugSpeed", new[] { ultra }), Is.True);
        object settings = InvokeMethod(_turnManager, "GetAIVsAIDebugSettings", Array.Empty<object>());
        Assert.That(GetMember(settings, "batchSpeedPreset"), Is.EqualTo(ultra));
        Assert.That(GetMember(settings, "sideBRecruitVariant").ToString(), Is.EqualTo("HardTactician"));
        object invalid = Enum.ToObject(speed, 999);
        Assert.That(InvokeMethod(_turnManager, "TrySetAIVsAIDebugSpeed", new[] { invalid }), Is.False);
        Assert.That(GetMember(_turnManager, "aiVsAiBatchSpeedPreset"), Is.EqualTo(ultra));
        SetMember(_turnManager, "enableAIVsAIDebugMode", false);
    }

    [Test]
    public void HardObservationIgnoresHiddenSceneChangesAndViewerFog()
    {
        SetMember(_turnManager, "currentMode", Enum.Parse(_gameModeEnum, "VsAI"));
        SetMember(_turnManager, "currentTurnSeatIndex", 1);
        SetMember(_turnManager, "isPlayerTurn", false);
        InvokeMethod(_city, "SetOwnerSeatIndex", new object[] { 0 });
        CreateUnit(1, 2, 3);
        object hidden = CreateUnit(0, 9, 9);
        object runtime = CreateHardRuntime();
        object first = GetMember(InvokeMethod(runtime, "Observe", new object[] { _turnManager, 1 }), "Observation");
        string before = JsonUtility.ToJson(first);
        ((Component)hidden).transform.position = new Vector3(8, 8);
        // Deliberately alter the human viewer's rendered visibility; Hard must ignore it.
        foreach (object tile in (Array)GetMember(_gridManager, "tileGrid"))
            InvokeMethod(tile, "SetVisibleForSeat", new object[] { true, 0 });
        object second = GetMember(InvokeMethod(runtime, "Observe", new object[] { _turnManager, 1 }), "Observation");
        Assert.That(JsonUtility.ToJson(second), Is.EqualTo(before));
        Assert.That(((Array)GetMember(second, "Units")).Length, Is.EqualTo(1));
    }

    [Test]
    public void ResetKnowledgeCancelsPausedDecisionWithoutExecuting()
    {
        SetMember(_turnManager, "currentMode", Enum.Parse(_gameModeEnum, "VsAI"));
        SetMember(_turnManager, "currentTurnSeatIndex", 1);
        SetMember(_turnManager, "isPlayerTurn", false);
        InvokeMethod(_city, "SetOwnerSeatIndex", new object[] { 0 });
        object own = CreateUnit(1, 2, 3);
        object runtime = CreateHardRuntime();
        FieldInfo pause = FindType("HardAIDiagnostics").GetField("PauseBeforeAction");
        pause.SetValue(null, true);
        try
        {
            IEnumerator turn = (IEnumerator)InvokeMethod(runtime, "RunTurn", new object[] { _turnManager, 1 });
            Assert.That(turn.MoveNext(), Is.True, "The inspector should pause before executing the winning move.");
            InvokeMethod(runtime, "ResetKnowledge", Array.Empty<object>());
            Assert.That(turn.MoveNext(), Is.False);
            Assert.That((int)GetMember(_city, "ownerSeatIndex"), Is.Zero);
            Assert.That(((Component)own).transform.position.x, Is.EqualTo(2));
        }
        finally { pause.SetValue(null, false); }
    }

    [Test]
    public void HardCityMemoryRetainsLastObservedOwnership()
    {
        SetMember(_turnManager, "currentMode", Enum.Parse(_gameModeEnum, "VsAI"));
        SetMember(_turnManager, "currentTurnSeatIndex", 1);
        SetMember(_turnManager, "isPlayerTurn", false);
        InvokeMethod(_city, "SetOwnerSeatIndex", new object[] { 0 });
        object own = CreateUnit(1, 2, 3);
        object runtime = CreateHardRuntime();
        InvokeMethod(runtime, "Observe", new object[] { _turnManager, 1 });
        ((Component)own).transform.position = new Vector3(0, 0);
        InvokeMethod(_city, "SetOwnerSeatIndex", new object[] { 2 });
        object observation = GetMember(InvokeMethod(runtime, "Observe", new object[] { _turnManager, 1 }), "Observation");
        object remembered = ((Array)GetMember(observation, "Cities")).GetValue(0);
        Assert.That((int)GetMember(remembered, "Seat"), Is.Zero);
        Assert.That((bool)GetMember(remembered, "CurrentlyVisible"), Is.False);
    }

    [Test]
    public void HardMoveStopsAtHiddenBlockerAndRejectsWrongSeat()
    {
        SetMember(_turnManager, "currentMode", Enum.Parse(_gameModeEnum, "VsAI"));
        SetMember(_turnManager, "currentTurnSeatIndex", 1);
        SetMember(_turnManager, "isPlayerTurn", false);
        SetMember(_city, "x", 10); SetMember(_city, "y", 10);
        ((Component)_city).transform.position = new Vector3(10, 10);
        InvokeMethod(_city, "SetOwnerSeatIndex", new object[] { 0 });
        object rider = CreateUnit(1, 1, 1, "rider");
        object blocker = CreateUnit(0, 3, 3);
        object runtime = CreateHardRuntime();
        object context = InvokeMethod(runtime, "Observe", new object[] { _turnManager, 1 });
        IEnumerable legal = (IEnumerable)InvokeStaticMethodCompatible(_legalActionServiceType, "GetLegalActionsForSeat",
            new object[] { _turnManager, 1, GetMember(context, "Visible") });
        object selected = null;
        foreach (object action in legal)
            if (GetMember(action, "ActionType").ToString() == "UnitMove" && ReferenceEquals(GetMember(action, "TargetTile"), _cityTile)) selected = action;
        Assert.That(selected, Is.Not.Null);
        SetMember(_turnManager, "currentTurnSeatIndex", 0); SetMember(_turnManager, "isPlayerTurn", true);
        Assert.That(InvokeStaticMethodCompatible(FindType("HardAIRuntime"), "TryExecute", new object[] { _turnManager, selected }), Is.False);
        SetMember(_turnManager, "currentTurnSeatIndex", 1); SetMember(_turnManager, "isPlayerTurn", false);
        Assert.That(InvokeStaticMethodCompatible(FindType("HardAIRuntime"), "TryExecute", new object[] { _turnManager, selected }), Is.True);
        Assert.That(((Component)rider).transform.position, Is.EqualTo(new Vector3(2, 2)));
        Assert.That(((Component)blocker).transform.position, Is.EqualTo(new Vector3(3, 3)));
        Assert.That((int)GetMember(rider, "movesUsedThisTurn"), Is.EqualTo(2));
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
