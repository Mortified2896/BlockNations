using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class TrainingHumanPlaytestTests
{
    [TestCase(false, "finished")]
    [TestCase(true, "finished")]
    [TestCase(false, "error")]
    [TestCase(true, "error")]
    public void HumanPlaytestDoesNotChangeOrLockTrainingPause(bool previousPause, string result)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bn-human-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var root = new GameObject("Inactive pause test"); root.SetActive(false);
        try
        {
            TrainingArena arena = root.AddComponent<TrainingArena>();
            TrainingSceneBuilder.Set(arena, "requireTrainer", true);
            TrainingSceneBuilder.Set(arena, "statusPath", Path.Combine(directory, "arena-status.json"));
            arena.Paused = previousPause;
            File.WriteAllText(Path.Combine(directory, "supervisor-status.json"),
                "{\"state\":\"running\",\"playtestAvailable\":true,\"checkpointCount\":1}");
            arena.Playtest.Poll(arena);
            Assert.That(arena.Playtest.Available, Is.True);
            arena.Playtest.Start(arena);
            Assert.That(arena.Paused, Is.EqualTo(previousPause), "Starting a human match must leave training running or paused as it was.");
            Assert.That(arena.CanContinue, Is.True, "Training pause controls remain available during a human match.");
            arena.Paused = !previousPause; // A manual pause/resume during the match remains the user's choice.
            string id = JsonUtility.FromJson<Request>(File.ReadAllText(Path.Combine(directory, "playtest.request.json"))).requestId;
            File.WriteAllText(Path.Combine(directory, "playtest-status.json"),
                "{\"requestId\":\"" + id + "\",\"state\":\"" + result + "\",\"message\":\"returned\"}");
            typeof(TrainingPlaytestBridge).GetField("nextPoll", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(arena.Playtest, -1d);
            arena.Playtest.Poll(arena);
            Assert.That(arena.Playtest.Busy, Is.False);
            Assert.That(arena.Paused, Is.EqualTo(!previousPause), "Returning must not undo the user's latest pause/resume choice.");
            Assert.That(arena.CanContinue, Is.True);
            Assert.That(Directory.GetFiles(directory, "match-events*"), Is.Empty);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); Directory.Delete(directory, true); }
    }
    [Serializable] private sealed class Request { public string requestId, difficulty; public int firstSeat; }

    [TestCase(BlockNations.AI.LearnedDifficulty.Easy)]
    [TestCase(BlockNations.AI.LearnedDifficulty.Medium)]
    [TestCase(BlockNations.AI.LearnedDifficulty.Hard)]
    public void FrozenDifficultyCanLaunchFromStoppedViewerWithoutStartingTraining(BlockNations.AI.LearnedDifficulty difficulty)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bn-stopped-playtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var root = new GameObject("Stopped difficulty selection"); root.SetActive(false);
        try
        {
            var viewer = root.AddComponent<TrainingViewer>();
            TrainingSceneBuilder.Set(viewer, "runDirectory", directory);
            File.WriteAllText(Path.Combine(directory, "supervisor-status.json"),
                "{\"state\":\"playtesting\",\"playtestAvailable\":true,\"checkpointCount\":1,\"frozenPlaytestCheckpoint\":\"Policy-123\"}");
            Assert.That(viewer.CanContinue, Is.False);
            viewer.Playtest.Difficulty = difficulty;
            viewer.Playtest.HumanStarts = false;
            viewer.Playtest.Poll(viewer);
            Assert.That(viewer.Playtest.TrainingActive, Is.False);
            Assert.That(viewer.Playtest.Available, Is.True);
            Assert.That(viewer.Playtest.FrozenCheckpoint, Is.EqualTo("Policy-123"));
            viewer.Playtest.Start(viewer);
            var request = JsonUtility.FromJson<Request>(File.ReadAllText(Path.Combine(directory, "playtest.request.json")));
            Assert.That(request.difficulty, Is.EqualTo(difficulty.ToString()));
            Assert.That(request.firstSeat, Is.EqualTo(1));
            Assert.That(viewer.Playtest.Busy, Is.True);
            Assert.That(File.Exists(Path.Combine(directory, "training-control.json")), Is.False);
            Assert.That(Directory.GetFiles(directory, "match-events*"), Is.Empty);
            Assert.That(viewer.CanContinue, Is.False);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); Directory.Delete(directory, true); }
    }

    [Test]
    public void PlaytestMenuInvokesItsExplicitExitWithoutLoadingMainMenu()
    {
        var root = new GameObject("Playtest exit"); root.SetActive(false);
        try
        {
            var menu = root.AddComponent<GameMenuActions>();
            TrainingSceneBuilder.Set(menu, "useAlternateExit", true);
            menu.mainMenuSceneName = "Missing scene must not be loaded";
            int exited = 0; menu.AlternateExit.AddListener(() => exited++);
            menu.QuitToMainMenu();
            Assert.That(exited, Is.EqualTo(1));
            Assert.That(menu.MenuLabel, Is.EqualTo("Back to training"));
            LogAssert.NoUnexpectedReceived();
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void AuthoredTrainingMenuExitTargetsItsArenaAndProductMenuIsPreserved()
    {
        try
        {
            var arena = TrainingSceneBuilder.Prepare("", 42, false, false, human: true);
            var menu = UnityEngine.Object.FindFirstObjectByType<GameMenuActions>();
            Assert.That(menu.AlternateExit.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(menu.AlternateExit.GetPersistentTarget(0), Is.SameAs(arena));
            Assert.That(menu.AlternateExit.GetPersistentMethodName(0), Is.EqualTo(nameof(TrainingArena.ReturnToTraining)));
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            Assert.That(UnityEngine.Object.FindFirstObjectByType<GameMenuActions>().MenuLabel, Is.EqualTo("Menu"));
        }
        finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
    }

    [TestCase(BlockNations.AI.LearnedDifficulty.Easy)]
    [TestCase(BlockNations.AI.LearnedDifficulty.Medium)]
    [TestCase(BlockNations.AI.LearnedDifficulty.Hard)]
    public void SupervisorOpenedPlaytestIsDetectedWithoutPausingTraining(BlockNations.AI.LearnedDifficulty difficulty)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bn-human-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var root = new GameObject("Inactive restored playtest"); root.SetActive(false);
        try
        {
            TrainingArena arena = root.AddComponent<TrainingArena>();
            TrainingSceneBuilder.Set(arena, "requireTrainer", true);
            TrainingSceneBuilder.Set(arena, "statusPath", Path.Combine(directory, "arena-status.json"));
            File.WriteAllText(Path.Combine(directory, "playtest-status.json"),
                "{\"requestId\":\"" + Guid.NewGuid().ToString("N") + "\",\"state\":\"playing\",\"difficulty\":\"" + difficulty + "\",\"message\":\"Training continues.\"}");
            arena.Playtest.Poll(arena);
            Assert.That(arena.Playtest.Busy, Is.True, "An open inference player disables duplicate launches.");
            Assert.That(arena.Playtest.Message, Is.EqualTo("Training continues."));
            Assert.That(arena.Playtest.Difficulty, Is.EqualTo(difficulty));
            Assert.That(arena.Paused, Is.False);
            Assert.That(arena.CanContinue, Is.True);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); Directory.Delete(directory, true); }
    }

    [Test]
    public void ConnectedHumanInferenceIsSeparateFromTrainingAndDirectSelfPlay()
    {
        var root = new GameObject("Inactive human playtest");
        root.SetActive(false);
        try
        {
            TrainingArena arena = root.AddComponent<TrainingArena>();
            TrainingSceneBuilder.Set(arena, "requireTrainer", true);
            TrainingSceneBuilder.Set(arena, "humanSeatIndex", 0);
            Assert.That(arena.IsHumanPlaytest, Is.True);
            Assert.That(arena.IsTraining, Is.False);
            Assert.That(arena.UsesDirectSimulation, Is.False);
            Assert.That(arena.HumanSeat, Is.Zero);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void ExternalHumanInputMustBeConfiguredBeforeMatchAndWithAnExplicitSeat()
    {
        var root = new GameObject("Inactive explicit external seat"); root.SetActive(false);
        try
        {
            TurnManager manager = root.AddComponent<TurnManager>();
            Assert.Throws<InvalidOperationException>(() => manager.ConfigureExternalHumanSeat(0));
            TrainingSceneBuilder.Set(manager, "externallyDrivenMatch", true);
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.ConfigureExternalHumanSeat(2));
            manager.ConfigureExternalHumanSeat(0);
            Assert.That(new SerializedObject(manager).FindProperty("externalHumanSeatIndex").intValue, Is.Zero);
            typeof(TurnManager).GetProperty(nameof(TurnManager.ExternalMatchReady)).SetValue(manager, true);
            Assert.Throws<InvalidOperationException>(() => manager.ConfigureExternalHumanSeat(1));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void HumanArenaHasAuthoredReferencesForCityUnitUIAndInput()
    {
        try
        {
            TrainingArena arena = TrainingSceneBuilder.Prepare("", 42, false, false, human: true);
            var fields = new SerializedObject(arena);
            var presentation = fields.FindProperty("humanPresentation").objectReferenceValue as TrainingHumanPresentation;
            Assert.That(presentation, Is.Not.Null);
            var wiring = new SerializedObject(presentation);
            SerializedProperty ui = wiring.FindProperty("humanUIRoots"), input = wiring.FindProperty("humanInput");
            SerializedProperty documents = wiring.FindProperty("humanDocuments");
            Assert.That(ui.arraySize, Is.GreaterThanOrEqualTo(4));
            Assert.That(input.arraySize, Is.GreaterThanOrEqualTo(2));
            Assert.That(documents.arraySize, Is.EqualTo(4));
            for (int i = 0; i < documents.arraySize; i++)
                Assert.That(documents.GetArrayElementAtIndex(i).objectReferenceValue, Is.TypeOf<UIDocument>());
            for (int i = 0; i < ui.arraySize; i++)
                Assert.That(((GameObject)ui.GetArrayElementAtIndex(i).objectReferenceValue).activeSelf, Is.True);
            for (int i = 0; i < input.arraySize; i++)
                Assert.That(((MonoBehaviour)input.GetArrayElementAtIndex(i).objectReferenceValue).enabled, Is.True);
        }
        finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
    }

    [UnityTest]
    public IEnumerator HumanMenuAndPanelsUseTheBoardViewportAcrossWidthChangesAndDocumentRebuilds()
    {
        TrainingArena configured = TrainingSceneBuilder.Prepare("", 42, false, false, human: true);
        TrainingSceneBuilder.Set(configured, "boardSize", 7);
        TrainingSceneBuilder.ShowBoard();
        yield return new EnterPlayMode();
        var arena = UnityEngine.Object.FindFirstObjectByType<TrainingArena>();
        var presentation = arena.GetComponent<TrainingHumanPresentation>();
        var wiring = new SerializedObject(presentation).FindProperty("humanDocuments");
        var documents = new UIDocument[wiring.arraySize];
        for (int i = 0; i < documents.Length; i++) documents[i] = (UIDocument)wiring.GetArrayElementAtIndex(i).objectReferenceValue;
        for (int frame = 0; frame < 12; frame++) yield return null;
        foreach (float left in new[] { TrainingOverlay.ReservedWidth / Screen.width, .4f, .2f })
        {
            presentation.SetViewport(new Rect(left, 0, 1 - left, 1));
            for (int frame = 0; frame < 4; frame++) yield return null;
            foreach (UIDocument document in documents)
            {
                VisualElement root = document.rootVisualElement;
                float screenWidth = root.panel.visualTree.worldBound.width;
                Assert.That(root.worldBound.xMin, Is.EqualTo(screenWidth * left).Within(1));
                Assert.That(root.worldBound.xMax, Is.EqualTo(screenWidth).Within(1));
                Button menu = root.Q<Button>("MenuButton");
                if (menu == null) continue;
                Assert.That(menu.worldBound.xMin, Is.GreaterThanOrEqualTo(screenWidth * left));
                Assert.That(menu.worldBound.xMax, Is.LessThanOrEqualTo(screenWidth));
                Assert.That(menu.worldBound.width, Is.GreaterThan(100));
                Assert.That(root.panel.Pick(menu.worldBound.center), Is.SameAs(menu), "The visible button must also receive pointer input.");
                TestContext.WriteLine($"Menu: {menu.worldBound}; sidebar edge {screenWidth * left}; panel width {screenWidth}.");
            }
        }
        foreach (UIDocument document in documents) { document.enabled = false; document.enabled = true; }
        for (int frame = 0; frame < 6; frame++) yield return null;
        foreach (UIDocument document in documents)
            Assert.That(document.rootVisualElement.worldBound.xMin,
                Is.EqualTo(document.rootVisualElement.panel.visualTree.worldBound.width * .2f).Within(1));
        presentation.SetViewport(new Rect(TrainingOverlay.ReservedWidth / Screen.width, 0, 1 - TrainingOverlay.ReservedWidth / Screen.width, 1));
        for (int frame = 0; frame < 4; frame++) yield return null;
        string screenshot = TakeScreenshotMenu.RequestScreenshot("human-playtest-hud-fixed");
        float deadline = Time.realtimeSinceStartup + 5;
        while (!File.Exists(screenshot) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(File.Exists(screenshot), Is.True);
        TestContext.WriteLine("Layout screenshot: " + screenshot);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator HumanSurpriseBadgeAndReturnButtonAreRenderedInTheGameView()
    {
        var configured = TrainingSceneBuilder.Prepare("", 42, false, false, human: true);
        TrainingSceneBuilder.Set(configured, "boardSize", 7);
        TrainingSceneBuilder.ShowBoard();
        yield return new EnterPlayMode();
        var arena = UnityEngine.Object.FindFirstObjectByType<TrainingArena>();
        arena.Paused = true;
        for (int i = 0; i < 12; i++) yield return null;
        var manager = UnityEngine.Object.FindFirstObjectByType<TurnManager>();
        var grid = manager.gridManager;
        grid.TryGetTile(1, 2, out TileVisibility origin); grid.TryGetTile(3, 2, out TileVisibility hidden);
        Unit own = manager.InstantiateConfiguredUnit(UnitRegistry.RiderTypeId, manager.GetUnitPrefabForType(UnitRegistry.RiderTypeId),
            origin.transform.position, 0, null, true).GetComponent<Unit>();
        manager.InstantiateConfiguredUnit(UnitRegistry.WarriorTypeId, manager.GetUnitPrefabForType(UnitRegistry.WarriorTypeId),
            hidden.transform.position, 1, null, true);
        manager.RecalculatePlayerVisibility();
        var input = UnityEngine.Object.FindFirstObjectByType<UnitSelectionManager>();
        input.SelectUnit(own); input.TryMoveOrAttackAtPosition(hidden.transform.position);
        manager.RecalculatePlayerVisibility();
        for (int i = 0; i < 5; i++) yield return null;
        Assert.That(own.IsSurprised, Is.True);
        Assert.That(own.transform.Find("SurprisedLabelCanvas").gameObject.activeInHierarchy, Is.True);
        // Use the fully wired human scene for the scene/direct presentation check.
        var state = new SceneSimulationAdapter(manager).State;
        var live = TrainingReplayRecorder.Capture(manager, "Hidden enemy");
        var projected = new SimulationReplayProjector(manager, state).Capture(state, "Hidden enemy");
        var actualPiece = System.Linq.Enumerable.Single(live.pieces, piece => !piece.city && piece.seat == 0);
        var cachedPiece = System.Linq.Enumerable.Single(projected.pieces, piece => !piece.city && piece.seat == 0);
        Assert.That(actualPiece.hasSurprisePresentation && cachedPiece.hasSurprisePresentation, Is.True);
        Rect actualBounds = actualPiece.surprisePresentation.bounds, cachedBounds = cachedPiece.surprisePresentation.bounds;
        Assert.That(cachedBounds.x, Is.EqualTo(actualBounds.x).Within(.00001f));
        Assert.That(cachedBounds.y, Is.EqualTo(actualBounds.y).Within(.00001f));
        Assert.That(cachedBounds.width, Is.EqualTo(actualBounds.width).Within(.00001f));
        Assert.That(cachedBounds.height, Is.EqualTo(actualBounds.height).Within(.00001f));
        input.ClearSelection();
        for (int i = 0; i < 5; i++) yield return null;
        string screenshot = TakeScreenshotMenu.RequestScreenshot("human-surprised-and-return");
        float deadline = Time.realtimeSinceStartup + 5;
        while (!File.Exists(screenshot) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(File.Exists(screenshot), Is.True);
        TestContext.WriteLine("Surprise/return screenshot: " + screenshot);
        own.SetFogVisibility(false, false);
        Assert.That(own.transform.Find("SurprisedLabelCanvas").gameObject.activeSelf, Is.False);
        manager.TryAdvanceExternalMatchTurn(0);
        Assert.That(own.IsSurprised, Is.True);
        manager.TryAdvanceExternalMatchTurn(1);
        Assert.That(own.IsSurprised, Is.False);
        yield return new ExitPlayMode();
    }
}
