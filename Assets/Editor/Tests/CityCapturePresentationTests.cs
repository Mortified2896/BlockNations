using System;
using System.Collections;
using System.Linq;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public sealed class CityCapturePresentationTests
{
    [TestCase(false, 0, 0, "Victory")]
    [TestCase(false, 0, 1, "Defeat")]
    [TestCase(true, 0, 0, "Victory")]
    [TestCase(true, 0, 1, "Defeat")]
    [TestCase(true, 1, 1, "Victory")]
    [TestCase(true, 1, 0, "Defeat")]
    public void CaptureResultUsesTheHumanSeatAndNamesTheCapturingUnit(bool external, int humanSeat, int winner, string title)
    {
        AIVsAIBatchRunController.ClearAll();
        var root = new GameObject("Inactive result fixture"); root.SetActive(false);
        var cityRoot = new GameObject("Captured city"); cityRoot.SetActive(false);
        var unitRoot = new GameObject("Capturing Rider"); unitRoot.SetActive(false);
        try
        {
            var manager = root.AddComponent<TurnManager>();
            manager.currentMode = TurnManager.GameMode.VsAI; manager.autoSaveEnabled = false;
            if (external)
            {
                TrainingSceneBuilder.Set(manager, "externallyDrivenMatch", true);
                manager.ConfigureExternalHumanSeat(humanSeat);
                Assert.That(manager.GetViewerSeatIndexForRuntime(), Is.EqualTo(humanSeat));
            }
            var rider = unitRoot.AddComponent<Unit>();
            TrainingSceneBuilder.Set(rider, "unitTypeId", "rider");
            var city = cityRoot.AddComponent<City>(); city.stationedUnit = unitRoot;
            manager.OnCityCaptured(winner, city);
            Assert.That(manager.GameOverUiTitle, Is.EqualTo(title));
            Assert.That(manager.GameOverUiMessage, Does.Contain("captured").And.Contain("Rider"));
            Assert.That(manager.GameOverUiMessage, Does.Contain(title == "Victory" ? "enemy city" : "your city"));
            Assert.That(manager.gameOver, Is.True);
            Assert.That(city.ownerSeatIndex, Is.EqualTo(winner));
            if (external) Assert.That(manager.ExternalWinnerSeatIndex, Is.EqualTo(winner));
        }
        finally
        {
            Object.DestroyImmediate(unitRoot); Object.DestroyImmediate(cityRoot); Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ExternalPlayAgainRequiresATerminalHumanMatchAndKeepsItsExplicitCallback()
    {
        var root = new GameObject("Inactive replay fixture"); root.SetActive(false);
        try
        {
            var manager = root.AddComponent<TurnManager>();
            TrainingSceneBuilder.Set(manager, "externallyDrivenMatch", true);
            int calls = 0; manager.ConfigureExternalHumanSeat(0, () => calls++);
            manager.OnPlayAgainButtonPressed(); Assert.That(calls, Is.Zero);
            manager.OnCityCaptured(1);
            manager.OnPlayAgainButtonPressed(); Assert.That(calls, Is.EqualTo(1));
        }
        finally { Object.DestroyImmediate(root); }
    }

    [UnityTest]
    public IEnumerator CapturingRiderRemainsOnFinalBoardAndRematchesRandomizeWithoutReloading()
    {
        AIVsAIBatchRunController.ClearAll();
        var configured = TrainingSceneBuilder.Prepare("", 42, false, false, human: true);
        TrainingSceneBuilder.Set(configured, "boardSize", 7);
        TrainingSceneBuilder.Set(configured, "humanStartingSeat", 0);
        TrainingSceneBuilder.ShowBoard();
        yield return new EnterPlayMode();
        var arena = Object.FindAnyObjectByType<TrainingArena>(); arena.Paused = true;
        for (int frame = 0; frame < 12; frame++) yield return null;
        arena = Object.FindAnyObjectByType<TrainingArena>();
        Assert.That(arena, Is.Not.Null);
        Assert.That(arena.IsHumanPlaytest, Is.True);
        arena.Paused = true;
        var manager = TurnManager.Instance;
        Assert.That(manager, Is.Not.Null);
        var bottomView = Object.FindAnyObjectByType<GameplayBottomHudUITKView>(FindObjectsInactive.Include);
        var topView = Object.FindAnyObjectByType<GameplayTopHudUITKView>(FindObjectsInactive.Include);
        Assert.That(bottomView, Is.Not.Null); Assert.That(topView, Is.Not.Null);
        var hud = bottomView.GetComponent<UIDocument>().rootVisualElement;
        var top = topView.GetComponent<UIDocument>().rootVisualElement;
        Assert.That(hud, Is.Not.Null); Assert.That(top, Is.Not.Null);
        Assert.That(manager.ExternalMatchReady, Is.True);
        manager.ResetExternalMatchWithOpening(1, 0, 1);
        int first = 0, second = 0;
        for (int replay = 0; replay < 32; replay++)
        {
            if (manager.currentTurnSeatIndex == 0) Assert.That(manager.TryAdvanceExternalMatchTurn(0), Is.True);
            City city = Object.FindObjectsByType<City>().Single(value => value.ownerSeatIndex == 0);
            Assert.That(manager.gridManager.TryGetTile(city.x + 1, city.y, out TileVisibility source), Is.True);
            var rider = Object.Instantiate(manager.GetUnitPrefabForType("rider"), source.transform.position, Quaternion.identity).GetComponent<Unit>();
            rider.SetOwnerSeatIndex(1);
            rider.GetComponent<OwnedSprite>().SetOwnerSeatIndex(1);
            rider.ApplyDefinition("rider", preserveCurrentHealth: false);
            manager.RecalculatePlayerVisibility();
            var action = LegalActionService.GetLegalActionsForSeat(manager, 1, new SceneSimulationAdapter(manager).VisibilityForSeat(1))
                .First(value => value.ActionType == LegalActionType.UnitMove &&
                value.Unit == rider && value.TargetTile.gridX == city.x && value.TargetTile.gridY == city.y);
            Assert.That(arena.Execute(action), Is.True);
            Assert.That(manager.gameOver, Is.True);
            Assert.That(arena.CanStartNewMatch, Is.True);
            Assert.That(city.stationedUnit, Is.SameAs(rider.gameObject));
            Assert.That(manager.gridManager.TryGetTile(city.x, city.y, out TileVisibility target), Is.True);
            manager.RecalculatePlayerVisibility();
            Assert.That(target.isVisibleNow, Is.False, "The loser no longer has city vision.");
            Assert.That(target.fogRenderer.enabled, Is.False);
            Assert.That(target.exploredRenderer.enabled, Is.False);
            Assert.That(rider.PrimarySpriteRenderer.enabled, Is.True, "The final capture remains visible.");
            var observation = new SeatAIObservationSource().Observe(manager, 0).Observation;
            Assert.That(observation.Units.Any(unit => unit.Seat == 1), Is.False, "Presentation must not widen policy information.");
            yield return null;
            Assert.That(hud.Q<Label>("GameOverTitleLabel").text, Is.EqualTo("Defeat"));
            Assert.That(hud.Q<Label>("GameOverMessageLabel").text, Does.Contain("Rider"));
            Assert.That(top.Q<Label>("TurnLabel").text, Is.EqualTo("Defeat"));
            if (replay == 0)
            {
                string screenshot = TakeScreenshotMenu.RequestScreenshot("city-capture-result");
                double deadline = EditorApplication.timeSinceStartup + 8;
                while (!File.Exists(screenshot) && EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.That(File.Exists(screenshot), Is.True);
            }
            manager.OnPlayAgainButtonPressed(); arena.Paused = true;
            Assert.That(TurnManager.Instance, Is.SameAs(manager));
            Assert.That(manager.gameOver, Is.False);
            Assert.That(arena.CanStartNewMatch, Is.False);
            Assert.That(arena.FirstSeat, Is.EqualTo(manager.currentTurnSeatIndex));
            if (arena.FirstSeat == 0) first++; else second++;
            yield return null;
            Assert.That(hud.Q<VisualElement>("GameOverOverlay").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(top.Q<Label>("OpeningSeatLabel").text, Does.Contain(arena.FirstSeat == 0 ? "move first" : "move second"));
        }
        Assert.That(first, Is.GreaterThan(0)); Assert.That(second, Is.GreaterThan(0));
        TestContext.WriteLine($"32 rematches: human started {first}, AI started {second}; same manager and frozen opponent.");
        yield return new ExitPlayMode();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }
}
