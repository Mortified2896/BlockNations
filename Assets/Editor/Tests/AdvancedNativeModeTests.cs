using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BlockNations.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public sealed class AdvancedNativeModeTests
{
    private bool restoreOptions, previousOptionsEnabled;
    private EnterPlayModeOptions previousOptions;

    [UnityTearDown]
    public IEnumerator RestoreEditor()
    {
        if (!restoreOptions) yield break;
        if (Application.isPlaying) yield return new ExitPlayMode();
        EditorSettings.enterPlayModeOptions = previousOptions;
        EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        restoreOptions = false;
    }

    [UnityTest]
    public IEnumerator MainMenuLaunchUsesNativeRecruitmentMovementEconomyAndScriptedOpponent()
    {
        Prepare("MainMenu");
        yield return new EnterPlayMode(false);
        yield return null; yield return null;
        var menu = Object.FindAnyObjectByType<MainMenuUITKView>();
        Assert.That(menu.GetComponent<UIDocument>().rootVisualElement.Q<Button>("AdvancedPlaytestButton"), Is.Not.Null);
        typeof(MainMenuUITKView).GetMethod("HandleAdvancedPlaytestClicked", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
        yield return WaitForAdvanced();
        AdvancedMatchController controller = TurnManager.Instance.AdvancedController;
        TurnManager manager = controller.Manager;
        Assert.That(manager.gameObject.scene.name, Is.EqualTo("Advanced"));
        Assert.That(manager.IsAdvancedMatch, Is.True);
        Assert.That(manager.IsTutorialMatch, Is.False);
        Assert.That(manager.gridManager.GetAllTiles().Count(), Is.EqualTo(49));
        Assert.That(Object.FindAnyObjectByType<TutorialController>(), Is.Null);
        Assert.That(Object.FindAnyObjectByType<CameraController>().enabled, Is.True);
        var advancedUi = Object.FindAnyObjectByType<AdvancedGameplayUITKView>().GetComponent<UIDocument>();
        Assert.That(advancedUi.panelSettings.themeStyleSheet, Is.Not.Null, "The advanced panel needs a valid serialized runtime theme.");
        Assert.That(advancedUi.rootVisualElement.worldBound.height, Is.GreaterThan(100));
        Assert.That(advancedUi.rootVisualElement.Q<Label>("AdvancedGold").worldBound.height, Is.GreaterThan(12),
            "Resource text must have a font and a measurable rendered size.");
        Assert.That(advancedUi.rootVisualElement.Q<Button>("AdvancedEndTurn").worldBound.height, Is.GreaterThan(30));
        Assert.That(new SceneSimulationAdapter(manager).State, Is.SameAs(controller.Match.Board));
        controller.RecruitSex = PopulationSex.Female;
        City city = controller.HumanCity;
        Assert.That(city.TrySpawnUnit("warrior"), Is.True);
        SimulationUnit soldier = controller.Match.Board.Units.Single(u => u.Seat == 0);
        Unit live = controller.SceneUnits[soldier.Id];
        Assert.That(controller.Match.Soldier(soldier.Id).Sex, Is.EqualTo(PopulationSex.Female));
        Assert.That(controller.Match.CityEconomy(1).Men, Is.EqualTo(6));
        Assert.That(controller.Match.CityEconomy(1).Women, Is.EqualTo(5));
        Assert.That(manager.GetGoldForSeat(0), Is.EqualTo(7));
        Assert.That(live.GetInstanceID(), Is.Not.EqualTo(soldier.Id), "Scene object IDs must never replace stable advanced IDs.");
        var input = Object.FindAnyObjectByType<UnitSelectionManager>();
        manager.gridManager.TryGetTile(2, 1, out TileVisibility destination);
        input.SelectUnit(live); input.TryMoveOrAttackAtPosition(destination.transform.position);
        Assert.That(soldier.Position, Is.EqualTo(controller.Match.Board.Position(2, 1)));
        Assert.That(live.transform.position.x, Is.EqualTo(destination.transform.position.x));
        Assert.That(city.stationedUnit, Is.Null);
        Assert.That(controller.TryUpgrade(city, CityUpgrade.Housing), Is.True);
        Assert.That(controller.Match.Forecast(1).Housing, Is.EqualTo(20));
        Assert.That(manager.GetGoldForSeat(0), Is.EqualTo(3));
        Assert.That(manager.TryAdvanceExternalMatchTurn(0), Is.True);
        Assert.That(controller.Match.Board.CurrentTurnSeat, Is.EqualTo(1));
        Assert.That(city.TrySpawnUnit("archer"), Is.False, "Human recruitment is illegal on the opponent's turn.");
        float deadline = Time.realtimeSinceStartup + 15f;
        while (!controller.Match.Board.IsTurnOwnedBySeat(0) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(controller.Match.Board.IsTurnOwnedBySeat(0), Is.True);
        Assert.That(controller.Match.Board.Round, Is.EqualTo(2));
        Assert.That(manager.GetGoldForSeat(0), Is.EqualTo(5), "Exactly one city income and one upkeep settlement.");
        Assert.That(controller.Match.CityEconomy(1).StoredFood, Is.EqualTo(20));
        Assert.That(controller.Match.CityEconomy(1).GrowthProgress, Is.EqualTo(11));
        Assert.That(controller.Match.Board.Units.Any(u => u.Seat == 1), Is.True, "The opponent recruits without a learned policy.");
        Assert.That(live.movesUsedThisTurn, Is.Zero);
    }

    [UnityTest]
    public IEnumerator CaptureRestartBalanceAndSaveIsolationUseTheAuthoritativeAdvancedState()
    {
        Prepare("Advanced");
        yield return new EnterPlayMode(false);
        yield return WaitForAdvanced();
        AdvancedMatchController controller = TurnManager.Instance.AdvancedController;
        string path = Path.Combine(Application.temporaryCachePath, "advanced-no-save-" + Guid.NewGuid().ToString("N") + ".json");
        controller.Manager.SaveToFile(path); controller.Manager.AutoSaveIfEnabled();
        Assert.That(File.Exists(path), Is.False);
        Assert.That(controller.Manager.LoadFromJsonString("{}"), Is.False);
        Assert.That(controller.Manager.LoadFromFile(path), Is.False);
        MatchState board = controller.Match.Board;
        board.AddUnit(new SimulationUnit(100, 0, board.Position(5, 4), board.RecruitType("warrior")));
        typeof(AdvancedMatchController).GetMethod("Project", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
        Unit captor = controller.SceneUnits[100];
        var input = Object.FindAnyObjectByType<UnitSelectionManager>();
        controller.Manager.gridManager.TryGetTile(5, 5, out TileVisibility target);
        input.SelectUnit(captor); input.TryMoveOrAttackAtPosition(target.transform.position);
        Assert.That(board.GameOver, Is.True);
        Assert.That(controller.Manager.gameOver, Is.True);
        Assert.That(controller.Manager.ExternalWinnerSeatIndex, Is.EqualTo(0));
        Assert.That(controller.Manager.GameOverUiMessage, Does.Contain("captured"));
        controller.RestartWithBalance(RecruitmentBalance.ClassStrengthExperiment);
        yield return null;
        Assert.That(controller.Match.Board.GameOver, Is.False);
        Assert.That(controller.Manager.gameOver, Is.False);
        Assert.That(controller.SceneUnits, Is.Empty);
        Assert.That(controller.Match.Board.GoldForSeat(0), Is.EqualTo(9));
        controller.RecruitSex = PopulationSex.Female;
        Assert.That(controller.HumanCity.TrySpawnUnit("warrior"), Is.True);
        var unit = controller.Match.Board.Units.Single();
        Assert.That(controller.SceneUnits[unit.Id].attackUnits, Is.EqualTo(unit.Definition.AttackUnits));
        Assert.That(unit.Definition.AttackUnits, Is.EqualTo(unit.Definition.TypeId == "warrior"
            ? controller.Match.Board.RecruitType("warrior").AttackUnits * 80 / 100 : 0));
        Assert.That(controller.TryDisband(controller.SceneUnits[unit.Id]), Is.True);
        Assert.That(controller.Match.CityEconomy(1).Women, Is.EqualTo(6));
        Assert.That(controller.SceneUnits, Is.Empty);
    }

    private void Prepare(string scene)
    {
        previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        previousOptions = EditorSettings.enterPlayModeOptions;
        restoreOptions = true;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorSceneManager.OpenScene("Assets/Scenes/" + scene + ".unity");
    }
    private static IEnumerator WaitForAdvanced()
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while ((TurnManager.Instance == null || !TurnManager.Instance.IsAdvancedMatch || !TurnManager.Instance.AdvancedController.IsReady) &&
            Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(TurnManager.Instance, Is.Not.Null);
        Assert.That(TurnManager.Instance.IsAdvancedMatch, Is.True);
        Assert.That(TurnManager.Instance.AdvancedController.IsReady, Is.True);
        yield return null; yield return null;
    }
}
