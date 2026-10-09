using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BlockNations.AI;
using BlockNations.Simulation;
using BlockNations.Training;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class SceneSimulationParityTests
{
    private TurnManager manager;
    private GridManager grid;

    private void CreateArena(int size)
    {
        var board = new GameObject("Parity board"); grid = board.AddComponent<GridManager>(); grid.enabled = false;
        grid.width = grid.height = size;
        grid.tilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BasicTile.prefab");
        grid.cityPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/City.prefab");
        TrainingSceneBuilder.Set(grid, "externallyDrivenBoard", true);
        var owner = new GameObject("Inactive parity driver"); owner.SetActive(false);
        manager = owner.AddComponent<TurnManager>(); manager.gridManager = grid;
        manager.currentMode = TurnManager.GameMode.VsAI;
        manager.autoSaveEnabled = false; manager.autoEndTurnWhenNoActions = false;
        TrainingSceneBuilder.Set(manager, "externallyDrivenMatch", true);
        typeof(TurnManager).GetProperty(nameof(TurnManager.ExternalMatchReady)).SetValue(manager, true);
        foreach (UnitDefinition definition in UnitRegistry.AllDefinitions)
            manager.officialUnitRegistrations.Add(new TurnManager.OfficialUnitRegistration { unitTypeId = definition.TypeId,
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + definition.DisplayName + ".prefab"), recruitable = true });
    }

    [UnityTest]
    public IEnumerator SeededCompleteMatchesHaveIdenticalSceneAndDirectStateAndPolicyInputs()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        CreateArena(7);
        var random = new System.Random(73491);
        int executed = 0, captures = 0;
        for (int game = 0; game < 8; game++)
        {
            manager.ResetExternalMatch(2, game % 2);
            MatchState direct = new SceneSimulationAdapter(manager).State.Copy();
            var sceneObserver = new SeatAIObservationSource(); var directObserver = new SimulationObservationSource();
            AICityState[] publicCities = direct.Cities.Select(city => new AICityState { Seat = city.Seat,
                X = city.Position % direct.Width, Y = city.Position / direct.Width }).ToArray();
            sceneObserver.SetPublicStartingCities(publicCities); directObserver.SetPublicStartingCities(publicCities);
            for (int step = 0; step < 300 && !direct.GameOver && direct.Round <= 30; step++)
            {
                int seat = direct.CurrentTurnSeat;
                var pure = directObserver.Observe(direct, seat);
                var scene = sceneObserver.Observe(manager, seat);
                Assert.That(scene.Observation.LegalActions.Select(a => a.Key), Is.EqualTo(pure.Observation.LegalActions.Select(a => a.Key)), $"game {game}, step {step}: action mask");
                Assert.That(LearnedActionSchema.Encode(scene.Observation, -1), Is.EqualTo(LearnedActionSchema.Encode(pure.Observation, -1)), $"game {game}, step {step}: policy input");
                AIAction chosen = pure.Observation.LegalActions[random.Next(pure.Observation.LegalActions.Length)];
                Assert.That(MatchEngine.Apply(direct, pure.Commands[chosen.Key]).Applied, Is.True);
                LegalTurnAction action = scene.RuntimeActions[chosen.Key];
                Assert.That(action.ActionType == LegalActionType.EndTurn ? manager.TryAdvanceExternalMatchTurn(seat) :
                    new SceneSimulationAdapter(manager).TryApply(action), Is.True, $"game {game}, step {step}: scene application");
                MatchState live = new SceneSimulationAdapter(manager).State;
                Assert.That(StateKey(live), Is.EqualTo(StateKey(direct)), $"game {game}, step {step}: state transition");
                executed++;
            }
            if (direct.GameOver) captures++;
            // Flush deferred destruction between matches; it must never create stale occupants.
            yield return null;
        }
        TestContext.WriteLine($"Scene/direct parity: {executed} actions across 8 seeded matches; {captures} captures.");
        Assert.That(executed, Is.GreaterThan(300)); Assert.That(captures, Is.GreaterThan(0));
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator CachedSpectatorGeometryFogAndMarkersMatchSceneFrames()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        CreateArena(7); manager.ResetExternalMatch(2, 0);
        MatchState opening = new SceneSimulationAdapter(manager).State;
        var projector = new SimulationReplayProjector(manager, opening);
        SimulationCity city = opening.Cities.First(c => c.Seat == 0);
        grid.TryGetTile(city.Position % opening.Width, city.Position / opening.Width, out TileVisibility tile);
        var recruit = new LegalTurnAction(LegalActionType.CityRecruit, 0, null, tile, tile, null, null,
            Object.FindObjectsByType<City>().First(c => c.ownerSeatIndex == 0), UnitRegistry.RiderTypeId, 2);
        Assert.That(new SceneSimulationAdapter(manager).TryApply(recruit), Is.True);
        MatchState state = new SceneSimulationAdapter(manager).State;
        var projected = projector.Capture(state, "projected");
        var transport = JsonUtility.FromJson<TrainingTraceState>(JsonUtility.ToJson(TrainingTraceState.Capture(state, "transport")));
        MatchState restored = transport.Restore(state.Roster);
        Assert.That(StateKey(restored), Is.EqualTo(StateKey(state)), "Data-only replay must preserve authoritative resources and state.");
        var received = projector.Capture(restored, "received");
        var scene = TrainingReplayRecorder.Capture(manager, "scene");
        Assert.That(received.blueVision, Is.EqualTo(projected.blueVision));
        Assert.That(received.redVision, Is.EqualTo(projected.redVision));
        Assert.That(received.pieces.Single(p => !p.city).outlineSprite, Is.SameAs(projected.pieces.Single(p => !p.city).outlineSprite));
        SameRect(received.pieces.Single(p => !p.city).bounds, projected.pieces.Single(p => !p.city).bounds);
        Assert.That(projected.blueVision, Is.EqualTo(scene.blueVision)); Assert.That(projected.redVision, Is.EqualTo(scene.redVision));
        var actual = scene.pieces.Single(p => !p.city);
        var cached = projected.pieces.Single(p => !p.city);
        Assert.That(cached.sprite, Is.SameAs(actual.sprite)); Assert.That(cached.color, Is.EqualTo(actual.color));
        SameRect(cached.bounds, actual.bounds); Assert.That(cached.outlineColor, Is.EqualTo(actual.outlineColor));
        SameRect(cached.outlineBounds, actual.outlineBounds); Assert.That(cached.healthPresentation.text, Is.EqualTo(actual.healthPresentation.text));
        SameRect(cached.healthPresentation.bounds, actual.healthPresentation.bounds);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator HumanInputUsesTheSharedAttackThenMoveRule()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        CreateArena(7); manager.ResetExternalMatch(2, 0);
        TrainingSceneBuilder.Set(manager, "externalHumanSeatIndex", 0);
        grid.TryGetTile(2, 2, out TileVisibility origin);
        grid.TryGetTile(3, 2, out TileVisibility defenderTile);
        grid.TryGetTile(3, 3, out TileVisibility destination);
        GameObject ownObject = manager.InstantiateConfiguredUnit(UnitRegistry.ArcherTypeId,
            manager.GetUnitPrefabForType(UnitRegistry.ArcherTypeId), origin.transform.position, 0, null, true);
        GameObject targetObject = manager.InstantiateConfiguredUnit(UnitRegistry.WarriorTypeId,
            manager.GetUnitPrefabForType(UnitRegistry.WarriorTypeId), defenderTile.transform.position, 1, null, true);
        Unit own = ownObject.GetComponent<Unit>(); Unit target = targetObject.GetComponent<Unit>(); target.SetCurrentHealthUnits(5);
        var inputObject = new GameObject("Human input adapter");
        var input = inputObject.AddComponent<UnitSelectionManager>(); input.enabled = false; input.turnManager = manager;
        manager.RecalculatePlayerVisibility();
        input.SelectUnit(own); input.TryMoveOrAttackAtPosition(defenderTile.transform.position);
        Assert.That(target.currentHealthUnits, Is.Zero); Assert.That(own.attacksUsedThisTurn, Is.EqualTo(1));
        Assert.That(own.movesUsedThisTurn, Is.Zero); Assert.That(own.transform.position, Is.EqualTo(origin.transform.position));
        input.TryMoveOrAttackAtPosition(destination.transform.position);
        Assert.That(own.transform.position, Is.EqualTo(destination.transform.position)); Assert.That(own.movesUsedThisTurn, Is.EqualTo(1));
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator HumanWarriorMovementAndAttackBudgetsMatchTheSharedRules()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        CreateArena(7);
        var input = new GameObject("Warrior human input").AddComponent<UnitSelectionManager>();
        input.enabled = false; input.turnManager = manager;
        // The human playtest exposes Blue; the pure rules cover both acting seats.
        const int seat = 0;
        foreach (bool movedFirst in new[] { false, true })
        foreach (bool kill in new[] { false, true })
        {
            string scenario = $"seat {seat}, moved first {movedFirst}, kill {kill}";
            TestContext.WriteLine(scenario);
            manager.ResetExternalMatch(2, seat);
            TrainingSceneBuilder.Set(manager, "externalHumanSeatIndex", seat);
            grid.TryGetTile(2, 2, out TileVisibility origin);
            grid.TryGetTile(3, 2, out TileVisibility movedTile);
            grid.TryGetTile(movedFirst ? 4 : 3, 2, out TileVisibility targetTile);
            grid.TryGetTile(3, 3, out TileVisibility extraMove);
            Unit own = manager.InstantiateConfiguredUnit(UnitRegistry.WarriorTypeId,
                manager.GetUnitPrefabForType(UnitRegistry.WarriorTypeId), origin.transform.position, seat, null, true).GetComponent<Unit>();
            Unit target = manager.InstantiateConfiguredUnit(UnitRegistry.WarriorTypeId,
                manager.GetUnitPrefabForType(UnitRegistry.WarriorTypeId), targetTile.transform.position, 1 - seat, null, true).GetComponent<Unit>();
            target.maxHealthUnits = kill ? 5 : 20; target.SetCurrentHealthUnits(target.maxHealthUnits);
            manager.RecalculatePlayerVisibility(); input.SelectUnit(own);
            Assert.That(manager.CanControlUnit(own), Is.True, scenario);
            Assert.That(input.SelectedUnit, Is.SameAs(own), scenario);
            if (movedFirst)
            {
                input.TryMoveOrAttackAtPosition(movedTile.transform.position);
                Assert.That(own.transform.position, Is.EqualTo(movedTile.transform.position));
                Assert.That(own.CanAttackThisTurn(), Is.True);
                Assert.That(own.attacksUsedThisTurn, Is.Zero);
            }
            Assert.That(input.SelectedUnit, Is.SameAs(own), scenario);
            Assert.That(LegalActionService.GetLegalActionsForUnit(manager, own, seat,
                new SceneSimulationAdapter(manager).VisibilityForSeat(seat)).Any(a =>
                a.ActionType == LegalActionType.UnitAttack && a.TargetUnit == target), Is.True, scenario);
            input.TryMoveOrAttackAtPosition(targetTile.transform.position);
            Assert.That(target.currentHealthUnits, Is.EqualTo(kill ? 0 : 10), scenario);
            Assert.That(own.attacksUsedThisTurn, Is.EqualTo(1));
            Assert.That(own.CanMoveThisTurn(), Is.False);
            Assert.That(own.CanAttackThisTurn(), Is.False);
            Vector3 expected = (kill ? targetTile : movedFirst ? movedTile : origin).transform.position;
            Assert.That(own.transform.position, Is.EqualTo(expected), "Only the automatic melee advance follows a kill.");
            input.TryMoveOrAttackAtPosition(extraMove.transform.position);
            Assert.That(own.transform.position, Is.EqualTo(expected));
            Assert.That(LegalActionService.GetLegalActionsForUnit(manager, own, seat, new SceneSimulationAdapter(manager).VisibilityForSeat(seat)), Is.Empty);
            yield return null;
        }
        yield return new ExitPlayMode();
    }

    private static void SameRect(Rect a, Rect b)
    {
        Assert.That(a.x, Is.EqualTo(b.x).Within(.00001f)); Assert.That(a.y, Is.EqualTo(b.y).Within(.00001f));
        Assert.That(a.width, Is.EqualTo(b.width).Within(.00001f)); Assert.That(a.height, Is.EqualTo(b.height).Within(.00001f));
    }

    private static string StateKey(MatchState state) =>
        $"{state.CurrentTurnSeat}/{state.Round}/{state.GameOver}/{state.WinnerSeat}/" +
        string.Join(",", Enumerable.Range(0, state.SeatCount).Select(state.GoldForSeat)) + "/" +
        string.Join(";", state.Cities.OrderBy(c => c.Position).Select(c => $"{c.Position},{c.Seat},{c.Recruited}")) + "/" +
        string.Join(";", state.Units.OrderBy(u => u.Position).Select(u => $"{u.Position},{u.Seat},{u.Definition.TypeId},{u.Health},{u.MovesUsed},{u.AttacksUsed}"));
}
