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

public sealed class MatchOpeningPresentationTests
{
    [UnityTest]
    public IEnumerator HumanPlayingSecondKeepsTheirIdentityAndGetsThreeGoldOnTheirFirstTurn()
    {
        AIVsAIBatchRunController.ClearAll();
        var configured = TrainingSceneBuilder.Prepare("", 42, false, false, human: true);
        TrainingSceneBuilder.Set(configured, "boardSize", 7);
        TrainingSceneBuilder.Set(configured, "humanStartingSeat", 1);
        yield return new EnterPlayMode();
        var arena = UnityEngine.Object.FindAnyObjectByType<TrainingArena>();
        arena.Paused = true;
        for (int frame = 0; frame < 20 && arena.FirstSeat != 1; frame++) yield return null;
        Assert.That(arena.FirstSeat, Is.EqualTo(1));
        var manager = TurnManager.Instance;
        manager.ResetExternalMatchWithOpening(1, 1, 1);
        var root = UnityEngine.Object.FindAnyObjectByType<GameplayTopHudUITKView>().GetComponent<UIDocument>().rootVisualElement;
        for (int frame = 0; frame < 3; frame++) yield return null;
        Assert.That(root.Q<Label>("OpeningSeatLabel").text, Is.EqualTo("You are Player 1 (Blue) · You move second"));
        Assert.That(root.Q<Label>("GoldLabel").text, Is.EqualTo("Gold 2"), "Before the human's first turn, income is not yet granted.");
        Assert.That(manager.TryAdvanceExternalMatchTurn(1), Is.True);
        yield return null;
        Assert.That(manager.GetGoldForSeat(0), Is.EqualTo(3));
        Assert.That(root.Q<Label>("GoldLabel").text, Is.EqualTo("Gold 3"));
        Assert.That(root.Q<Label>("OpeningSeatLabel").text, Does.Contain("move second"));
        yield return new ExitPlayMode();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    [UnityTest]
    public IEnumerator SinglePlayerSetupShowsIdentityOrderAndActualFirstTurnBalances()
    {
        AIVsAIBatchRunController.ClearAll();
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        TrainingSceneBuilder.ShowBoard();
        yield return new EnterPlayMode();
        var view = UnityEngine.Object.FindAnyObjectByType<MainMenuUITKView>();
        Assert.That(view, Is.Not.Null);
        for (int frame = 0; frame < 12; frame++) yield return null;
        Type mode = typeof(MainMenuUITKView).GetNestedType("PendingGeneralSettingsMode", BindingFlags.NonPublic);
        typeof(MainMenuUITKView).GetMethod("ShowGeneralSettingsPanel", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(view, new[] { Enum.Parse(mode, "VsAI") });
        for (int frame = 0; frame < 12; frame++) yield return null;
        var opening = view.GetComponent<UIDocument>().rootVisualElement.Q<Label>("GeneralSettingsOpeningLabel");
        Assert.That(opening.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
        Assert.That(opening.text, Does.Contain("Player 1 (Blue)").And.Contain("move first"));
        Assert.That(opening.text, Does.Contain("2 gold").And.Contain("3 gold"));
        Assert.That(opening.worldBound.width, Is.GreaterThan(200));
        string screenshot = TakeScreenshotMenu.RequestScreenshot("opening-economy-setup");
        double deadline = EditorApplication.timeSinceStartup + 8;
        while (!File.Exists(screenshot) && EditorApplication.timeSinceStartup < deadline) yield return null;
        Assert.That(File.Exists(screenshot), Is.True);
        yield return new ExitPlayMode();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }
}
