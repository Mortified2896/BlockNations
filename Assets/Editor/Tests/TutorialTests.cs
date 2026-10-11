using System;
using System.Collections;
using System.IO;
using System.Linq;
using BlockNations.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public sealed class TutorialTests
{
    private bool restoreEditorOptions, previousOptionsEnabled, hadCompletionPreference;
    private EnterPlayModeOptions previousOptions;
    private int previousCompletion;

    [UnityTearDown]
    public IEnumerator RestoreEditorAfterSceneTest()
    {
        if (!restoreEditorOptions) yield break;
        if (Application.isPlaying) yield return new ExitPlayMode();
        EditorSettings.enterPlayModeOptions = previousOptions;
        EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
        if (hadCompletionPreference) PlayerPrefs.SetInt(TutorialController.CompletionKey, previousCompletion);
        else PlayerPrefs.DeleteKey(TutorialController.CompletionKey);
        PlayerPrefs.Save();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        restoreEditorOptions = false;
    }

    [TestCase(TutorialStep.WarriorMove)]
    [TestCase(TutorialStep.Recruit)]
    [TestCase(TutorialStep.WarriorAttack)]
    [TestCase(TutorialStep.RiderMove)]
    [TestCase(TutorialStep.RiderAttack)]
    [TestCase(TutorialStep.ArcherAttack)]
    [TestCase(TutorialStep.ArcherRetreat)]
    [TestCase(TutorialStep.Capture)]
    public void EveryExerciseHasAnAchievableRealLegalAction(TutorialStep step)
    {
        MatchState state = TutorialLesson.CreateBoard(step);
        Assert.That(state.Width, Is.EqualTo(7));
        Assert.That(state.Height, Is.EqualTo(7));
        Assert.That(state.RuleProfile.RecruitableUnits.Select(u => u.TypeId),
            Is.EquivalentTo(new[] { "warrior", "rider", "archer" }));
        var expectedKind = step == TutorialStep.Recruit ? MatchActionKind.Recruit :
            step == TutorialStep.WarriorAttack || step == TutorialStep.RiderAttack || step == TutorialStep.ArcherAttack
            ? MatchActionKind.Attack : MatchActionKind.Move;
        var action = MatchEngine.LegalActions(state, 0).Single(a => a.Command.Kind == expectedKind &&
            a.Command.Destination == TutorialLesson.Target(step) &&
            (expectedKind != MatchActionKind.Recruit || a.Command.RecruitType == "warrior"));
        MatchTransition transition = MatchEngine.Apply(state, action.Command);
        Assert.That(TutorialLesson.Completes(step, state, action.Command, transition), Is.True);
        if (expectedKind == MatchActionKind.Attack)
            Assert.That(state.GetUnit(TutorialLesson.OpponentUnitId).Health, Is.EqualTo(CombatValues.FromDisplay(2)));
        if (step == TutorialStep.Capture)
        {
            Assert.That(state.GameOver, Is.True);
            Assert.That(state.WinnerSeat, Is.EqualTo(0));
        }
    }

    [Test]
    public void OpponentResponseReturnsTheTurnAndIncomeWithoutKillingTheLearner()
    {
        MatchState state = TutorialLesson.CreateBoard(TutorialStep.EndTurn);
        var endHuman = new MatchCommand(MatchActionKind.EndTurn, 0);
        MatchTransition ended = MatchEngine.Apply(state, endHuman);
        Assert.That(TutorialLesson.Completes(TutorialStep.EndTurn, state, endHuman, ended), Is.False);
        var response = MatchEngine.LegalActions(state, 1).Single(a => a.Command.Kind == MatchActionKind.Attack);
        Assert.That(MatchEngine.Apply(state, response.Command).Applied, Is.True);
        var endOpponent = new MatchCommand(MatchActionKind.EndTurn, 1);
        ended = MatchEngine.Apply(state, endOpponent);
        Assert.That(TutorialLesson.Completes(TutorialStep.EndTurn, state, endOpponent, ended), Is.True);
        Assert.That(state.GetUnit(TutorialLesson.PracticeUnitId).Health, Is.EqualTo(CombatValues.FromDisplay(2)));
        Assert.That(state.GetUnit(TutorialLesson.PracticeUnitId).RemainingMoves, Is.EqualTo(1));
        Assert.That(state.GoldForSeat(0), Is.EqualTo(4));
        Assert.That(state.GameOver, Is.False);
    }

    [Test]
    public void AnUnsuccessfulOrDifferentActionDoesNotAdvanceTheLesson()
    {
        MatchState state = TutorialLesson.CreateBoard(TutorialStep.WarriorMove);
        var different = MatchEngine.LegalActions(state, 0).First(a => a.Command.Kind == MatchActionKind.Move &&
            a.Command.Destination != TutorialLesson.Target(TutorialStep.WarriorMove));
        var result = MatchEngine.Apply(state, different.Command);
        Assert.That(result.Applied, Is.True);
        Assert.That(TutorialLesson.Completes(TutorialStep.WarriorMove, state, different.Command, result), Is.False);
        var requested = new MatchCommand(MatchActionKind.Move, 0, TutorialLesson.PracticeUnitId,
            TutorialLesson.Target(TutorialStep.WarriorMove));
        Assert.That(TutorialLesson.Completes(TutorialStep.WarriorMove, state, requested, default), Is.False);
    }

    [Test]
    public void AWrongRecruitIsRecoverableWithAnUnchangedLessonFixture()
    {
        MatchState state = TutorialLesson.CreateBoard(TutorialStep.Recruit);
        var archer = MatchEngine.LegalActions(state, 0).Single(a => a.Command.Kind == MatchActionKind.Recruit && a.Command.RecruitType == "archer");
        var transition = MatchEngine.Apply(state, archer.Command);
        Assert.That(TutorialLesson.Completes(TutorialStep.Recruit, state, archer.Command, transition), Is.False);
        MatchState retry = TutorialLesson.CreateBoard(TutorialStep.Recruit);
        Assert.That(retry.GoldForSeat(0), Is.EqualTo(3));
        Assert.That(MatchEngine.LegalActions(retry, 0).Count(a => a.Command.Kind == MatchActionKind.Recruit), Is.EqualTo(3));
        Assert.That(retry.UnitAt(retry.Position(1, 1)), Is.Null);
    }

    [UnityTest]
    public IEnumerator SceneRunsEveryLessonWithoutWritingASaveAndSupportsRetryAndRestart()
    {
        PrepareTutorialScene();
        yield return new EnterPlayMode(false);
        yield return WaitForTutorialReady();
        var controller = Object.FindAnyObjectByType<TutorialController>();
        var manager = TurnManager.Instance;
        Assert.That(manager, Is.Not.Null);
        Assert.That(manager.IsTutorialMatch, Is.True);
        Assert.That(manager.RuleProfile, Is.SameAs(MatchRuleProfile.CoreThree));
        string probePath = Path.Combine(Application.temporaryCachePath, "tutorial-must-not-save-" + Guid.NewGuid().ToString("N") + ".json");
        manager.SaveToFile(probePath);
        manager.AutoSaveIfEnabled();
        Assert.That(File.Exists(probePath), Is.False);
        controller.Continue(); yield return null;
        Assert.That(controller.Step, Is.EqualTo(TutorialStep.WarriorMove));
        for (int lesson = 1; lesson <= TutorialLesson.LessonCount; lesson++)
        {
            Assert.That((int)controller.Step, Is.EqualTo(lesson));
            if (controller.Step == TutorialStep.EndTurn)
            {
                Assert.That(manager.TryAdvanceExternalMatchTurn(0), Is.True);
                float deadline = Time.realtimeSinceStartup + 5f;
                while (!controller.LessonCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            }
            else
            {
                LegalActionType kind = controller.Step == TutorialStep.Recruit ? LegalActionType.CityRecruit :
                    controller.Step == TutorialStep.WarriorAttack || controller.Step == TutorialStep.RiderAttack ||
                    controller.Step == TutorialStep.ArcherAttack ? LegalActionType.UnitAttack : LegalActionType.UnitMove;
                var action = LegalActionService.GetLegalActionsForSeat(manager, 0, new SceneSimulationAdapter(manager).VisibilityForSeat(0))
                    .Single(a => a.ActionType == kind && a.TargetTile != null &&
                        a.TargetTile.gridX + a.TargetTile.gridY * 7 == TutorialLesson.Target(controller.Step) &&
                        (kind != LegalActionType.CityRecruit || a.RecruitUnitTypeId == "warrior"));
                Assert.That(new SceneSimulationAdapter(manager).TryApply(action), Is.True);
                manager.RecalculatePlayerVisibility();
                yield return null;
            }
            Assert.That(controller.LessonCompleted, Is.True, controller.Step.ToString());
            if (controller.Step == TutorialStep.Capture)
            {
                Assert.That(manager.gameOver, Is.True);
                Assert.That(manager.IsGameOverUiVisible, Is.False, "The tutorial owns its completion controls.");
            }
            controller.Continue(); yield return null;
        }
        Assert.That(controller.Step, Is.EqualTo(TutorialStep.Complete));
        controller.Restart(); yield return null;
        Assert.That(controller.Step, Is.EqualTo(TutorialStep.Welcome));
        controller.Continue(); yield return null;
        var unit = Object.FindObjectsByType<Unit>().Single(u => u.ownerSeatIndex == 0);
        controller.Retry(); yield return null;
        var replacement = Object.FindObjectsByType<Unit>().Single(u => u.ownerSeatIndex == 0);
        Assert.That(replacement, Is.Not.SameAs(unit));
        Assert.That(replacement.movesUsedThisTurn, Is.Zero);
    }

    [UnityTest]
    public IEnumerator PlayerCanWatchOrClickThroughEveryLessonWithTwoCardControls()
    {
        PrepareTutorialScene();
        yield return new EnterPlayMode(false);
        yield return WaitForTutorialReady();
        var controller = Object.FindAnyObjectByType<TutorialController>();
        var root = Object.FindObjectsByType<UIDocument>().Single(d => d.rootVisualElement.Q("TutorialCard") != null).rootVisualElement;
        // Stress the width constraint independently of the Editor Game view's
        // current shape. Every logical tile edge must remain inside the view.
        Camera.main.aspect = 0.25f;
        yield return null; yield return null;
        AssertBoardInsideViewport();
        var card = root.Q("TutorialCard");
        Assert.That(card.Query<Button>().ToList().Count, Is.EqualTo(2));
        Assert.That(card.Contains(root.Q<Button>("TutorialExit")), Is.False);
        controller.Continue(); yield return null;
        controller.ShowDemonstration();
        Assert.That(controller.IsDemonstrating, Is.True);
        float deadline = Time.realtimeSinceStartup + 8f;
        while (controller.IsDemonstrating && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(controller.IsDemonstrating, Is.False);
        Assert.That(controller.Step, Is.EqualTo(TutorialStep.WarriorMove), "Show me stays on the current lesson.");
        Assert.That(controller.LessonCompleted, Is.True);
        controller.ShowDemonstration(); yield return null;
        Assert.That(controller.LessonCompleted, Is.False, "Try again restores a hands-on exercise.");
        for (int lesson = 1; lesson <= TutorialLesson.LessonCount; lesson++)
        {
            TutorialStep step = (TutorialStep)lesson;
            Assert.That(controller.Step, Is.EqualTo(step));
            controller.Continue();
            Assert.That(controller.IsDemonstrating, Is.True);
            deadline = Time.realtimeSinceStartup + 8f;
            while (controller.Step == step && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(controller.IsDemonstrating, Is.False);
            Assert.That((int)controller.Step, Is.EqualTo(lesson + 1), "Continue must show and advance every exercise without a board tap.");
        }
        Assert.That(controller.Step, Is.EqualTo(TutorialStep.Complete));
        Assert.That(TurnManager.Instance.gameOver, Is.True);
        Assert.That(TurnManager.Instance.IsGameOverUiVisible, Is.False);
    }

    private void PrepareTutorialScene()
    {
        previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        previousOptions = EditorSettings.enterPlayModeOptions;
        hadCompletionPreference = PlayerPrefs.HasKey(TutorialController.CompletionKey);
        previousCompletion = PlayerPrefs.GetInt(TutorialController.CompletionKey);
        restoreEditorOptions = true;
        // Keep the test's Assembly-CSharp references valid across the runner's
        // Editor-to-Play transition. Restore this setting even on a failed assert.
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorSceneManager.OpenScene("Assets/Scenes/Tutorial.unity");
    }

    private static void AssertBoardInsideViewport()
    {
        var grid = TurnManager.Instance.gridManager;
        foreach (var tile in grid.GetComponentsInChildren<TileVisibility>())
            foreach (float x in new[] { -0.5f, 0.5f })
                foreach (float y in new[] { -0.5f, 0.5f })
                {
                    var point = Camera.main.WorldToViewportPoint(tile.transform.position +
                        new Vector3(x * grid.tileSize, y * grid.tileSize));
                    Assert.That(point.x, Is.InRange(0f, 1f), "A tile edge is clipped horizontally.");
                    Assert.That(point.y, Is.InRange(0f, 1f), "A tile edge is clipped vertically.");
                }
    }

    private IEnumerator WaitForTutorialReady()
    {
        for (int frame = 0; frame < 12; frame++) yield return null;
        var controller = Object.FindAnyObjectByType<TutorialController>();
        Assert.That(controller, Is.Not.Null, "The authored Tutorial scene must remain loaded when entering Play Mode.");
        for (int i = 0; i < 180 && (controller == null || !controller.IsReady); i++) yield return null;
        Assert.That(controller, Is.Not.Null);
        Assert.That(controller.IsReady, Is.True);
    }
}
