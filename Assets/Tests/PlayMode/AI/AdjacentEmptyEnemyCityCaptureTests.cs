using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using NUnit.Framework;
using UnityEngine.TestTools;

public class AdjacentEmptyEnemyCityCaptureTests
{
    private object _turnManager, _gridManager, _city, _cityTile;
    private Type _tileVisibilityType, _cityType, _unitType, _turnManagerType;
    private Type _gridManagerType, _legalTurnActionType, _legalActionServiceType;
    private Type _planStepType, _planType, _stepTypeEnum, _gameModeEnum;
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

    private static object InvokeStaticMethod(Type type, string name, object[] args)
    {
        foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (m.Name == name && m.GetParameters().Length == args.Length)
                return m.Invoke(null, args);
        }
        Assert.Fail($"Static method '{name}' with {args.Length} params not found on {type.Name}");
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

        for (int seat = 0; seat <= 1; seat++)
        {
            int actingSeat = seat;
            int enemySeat = 1 - seat;

            for (int off = 0; off < Offsets.Length; off++)
            {
                Vector2Int offset = Offsets[off];
                int ux = 3 + offset.x;
                int uy = 3 + offset.y;

                SetMember(_turnManager, "currentTurnSeatIndex", actingSeat);
                SetMember(_turnManager, "isPlayerTurn", actingSeat == 0);
                SetMember(_turnManager, "gameOver", false);

                InvokeMethod(_city, "SetOwnerSeatIndex", new object[] { enemySeat });

                var hashSetType = typeof(HashSet<>).MakeGenericType(_tileVisibilityType);
                var visibleTiles = Activator.CreateInstance(hashSetType);
                var addMethod = hashSetType.GetMethod("Add");

                InvokeMethod(_cityTile, "SetVisibleForSeat", new object[] { true, actingSeat });
                addMethod.Invoke(visibleTiles, new object[] { _cityTile });

                var unitTile = ((Array)GetMember(_gridManager, "tileGrid")).GetValue(ux, uy);
                InvokeMethod(unitTile, "SetVisibleForSeat", new object[] { true, actingSeat });
                addMethod.Invoke(visibleTiles, new object[] { unitTile });

                var unitGo = new GameObject($"Unit_{ux}_{uy}");
                var unit = unitGo.AddComponent(_unitType);
                SetMember(unit, "ownerSeatIndex", actingSeat);
                unitGo.transform.position = new Vector3(ux, uy, 0);
                InvokeMethod(unit, "ApplyDefinition", new object[] { "warrior", true });
                SetMember(unit, "movesUsedThisTurn", 0);
                _caseObjects.Add(unitGo);

                var legalActions = InvokeStaticMethodCompatible(_legalActionServiceType,
                    "GetLegalUnitActionsForSeat",
                    new object[] { _turnManager, actingSeat, visibleTiles });

                var actionTypeProp = _legalTurnActionType.GetProperty("ActionType");
                var targetTileProp = _legalTurnActionType.GetProperty("TargetTile");
                Assert.IsNotNull(actionTypeProp, "ActionType property");
                Assert.IsNotNull(targetTileProp, "TargetTile property");

                bool hasLegalMove = false;
                foreach (var action in (IEnumerable)legalActions)
                {
                    var actionType = actionTypeProp.GetValue(action);
                    if (actionType.ToString() == "UnitMove")
                    {
                        var targetTile = targetTileProp.GetValue(action);
                        var tx = (int)GetMember(targetTile, "gridX");
                        var ty = (int)GetMember(targetTile, "gridY");
                        if (tx == 3 && ty == 3)
                        {
                            hasLegalMove = true;
                            break;
                        }
                    }
                }

                Assert.IsTrue(hasLegalMove,
                    $"Seat {actingSeat} unit at ({ux},{uy}) should have legal move to city (3,3)");

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
                tryExecMethod.Invoke(_turnManager, new object[] { plan, visibleTiles });

                Assert.IsTrue((bool)GetMember(_turnManager, "gameOver"),
                    $"Seat {actingSeat} unit at ({ux},{uy}) should end game via city capture");

                Assert.AreEqual(actingSeat, GetMember(_city, "ownerSeatIndex"),
                    $"Seat {actingSeat} unit at ({ux},{uy}) should own captured city");

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
