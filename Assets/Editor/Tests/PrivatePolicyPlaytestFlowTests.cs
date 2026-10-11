using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BlockNations.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

// Execute in the credential-free snapshot with --validate-flow. The source
// gameplay scenes are never rewritten, and synthetic games never reach training.
public sealed class PrivatePolicyPlaytestFlowTests
{
    [OneTimeSetUp]
    public void ValidateAndCompileEditorInferenceJobs()
    {
        if (!File.Exists(PrivatePolicyPlaytestBuild.Marker)) return;
        Assert.That(File.Exists(Environment.GetEnvironmentVariable("BLOCKNATIONS_POLICY_VALIDATION_DATA")), Is.True,
            "Supply --cpu-fixtures for the private flow validation.");
        // Editor inference jobs compile synchronously on first use. Validate
        // the frozen probability vectors before timing gameplay; native builds
        // compile Burst ahead of time. Do not change the shipping CPU backend.
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        LearnedPolicyReleaseBuilder.Build();
        TestContext.WriteLine($"Editor CPU parity/compilation setup: {elapsed.Elapsed.TotalSeconds:0.000}s.");
    }

    [SetUp]
    public void OwnedSnapshotOnly()
    {
        if (!File.Exists(PrivatePolicyPlaytestBuild.Marker))
            Assert.Ignore("Use Tools/NativePlaytest/build.py --validate-flow in its owned snapshot.");
    }

    private static void Prepare(TurnManager.AIRecruitVariant difficulty, bool throughMenu = false)
    {
        PrivatePolicyPlaytestBuild.PrepareGameplayScene();
        AIRecruitVariantSelection.SetPending(difficulty);
        if (throughMenu) EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        var view = EditorWindow.GetWindow(type); view.Show(); view.Focus();
    }

    private static void SetRandom(PrivatePolicyPlaytestController driver, int seed) =>
        typeof(PrivatePolicyPlaytestController).GetField("random", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(driver, new System.Random(seed));

    private static IEnumerator WaitReady()
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        PrivatePolicyPlaytestController driver;
        while (((driver = Object.FindFirstObjectByType<PrivatePolicyPlaytestController>()) == null || !driver.Ready) &&
               elapsed.Elapsed.TotalSeconds < 10) yield return null;
        Assert.That(driver, Is.Not.Null);
        Assert.That(driver.Ready, Is.True);
    }

    private static void Submit(VisualElement root, string name)
    {
        var button = root.Q<Button>(name);
        Assert.That(button, Is.Not.Null, name);
        Assert.That(button.enabledInHierarchy, Is.True, name);
        Assert.That(button.resolvedStyle.display, Is.Not.EqualTo(DisplayStyle.None), name);
        using var submit = NavigationSubmitEvent.GetPooled();
        submit.target = button;
        button.SendEvent(submit);
    }

    [UnityTest] public IEnumerator EasyUsesLocalInferenceAndReturnsToMenu() =>
        CheckDifficulty(TurnManager.AIRecruitVariant.LearnedEasy);
    [UnityTest] public IEnumerator MediumUsesLocalInferenceAndReturnsToMenu() =>
        CheckDifficulty(TurnManager.AIRecruitVariant.LearnedMedium);
    [UnityTest] public IEnumerator HardUsesLocalInferenceAndReturnsToMenu() =>
        CheckDifficulty(TurnManager.AIRecruitVariant.LearnedHard);

    private static IEnumerator CheckDifficulty(TurnManager.AIRecruitVariant difficulty)
    {
        Prepare(TurnManager.AIRecruitVariant.LearnedMedium, throughMenu: true);
        yield return new EnterPlayMode();
        // A hidden batch Editor can lose OS focus. Keep its player loop running
        // for validation; the exported human app retains its background pause.
        Application.runInBackground = true;
        yield return null;
        var menu = Object.FindFirstObjectByType<MainMenuUITKView>().GetComponent<UIDocument>().rootVisualElement;
        Submit(menu, "PlayVsAIButton");
        yield return null;
        string selected = difficulty == TurnManager.AIRecruitVariant.LearnedEasy ? "Easy" :
            difficulty == TurnManager.AIRecruitVariant.LearnedMedium ? "Medium" : "Hard";
        Submit(menu, "GeneralSettingsAi" + selected + "Button");
        Submit(menu, "GeneralSettingsConfirmButton");
        yield return WaitReady();
        var driver = Object.FindFirstObjectByType<PrivatePolicyPlaytestController>();
        var manager = Object.FindFirstObjectByType<TurnManager>();
        Assert.That(driver.Difficulty, Is.EqualTo(difficulty));
        Assert.That(manager.RuleProfile.Version, Is.EqualTo(MatchRuleProfile.CoreThreeVersion));
        Assert.That(PublicWebBuild.IsSinglePlayer, Is.True);
        Assert.That(PublicWebBuild.UsesFrozenOpponent, Is.False, "Private presets must not be collapsed to public Hard.");
        SetRandom(driver, 1);
        manager.gameOver = true;
        manager.OnPlayAgainButtonPressed();
        Assert.That(driver.FirstSeat, Is.Zero);
        Assert.That(manager.GetGoldForSeat(0), Is.EqualTo(2));
        Assert.That(manager.GetGoldForSeat(1), Is.EqualTo(2));
        Time.timeScale = 0;
        yield return null;
        var hud = Object.FindFirstObjectByType<GameplayTopHudUITKView>().GetComponent<UIDocument>().rootVisualElement;
        Assert.That(hud.Q<Label>("OpeningSeatLabel").text, Is.EqualTo("You are Player 1 (Blue) · You move first"));
        City own = null;
        foreach (City city in Object.FindObjectsByType<City>()) if (city.ownerSeatIndex == 0) own = city;
        Assert.That(own, Is.Not.Null);
        Assert.That(own.TrySpawnUnit(UnitRegistry.RiderTypeId), Is.True);
        int humanGold = manager.GetGoldForSeat(0);
        manager.OnEndTurnButtonPressed();
        Assert.That(manager.currentTurnSeatIndex, Is.EqualTo(1));
        Assert.That(manager.GetGoldForSeat(1), Is.EqualTo(3));
        yield return null;
        Assert.That(hud.Q<Label>("GoldLabel").text, Is.EqualTo("Gold " + humanGold), "Enemy-turn HUD must retain human gold.");
        Time.timeScale = 1;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!manager.gameOver && manager.currentTurnSeatIndex == 1 && driver.Failure == null &&
               elapsed.Elapsed.TotalSeconds < 30) yield return null;
        var runtime = (LearnedAIRuntime)typeof(TurnManager).GetField("learnedAIRuntime", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
        var thinking = typeof(PrivatePolicyPlaytestController).GetField("thinking", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(driver);
        TestContext.WriteLine($"{difficulty}: local turn {elapsed.Elapsed.TotalSeconds:0.000}s, " +
            $"{runtime.CompletedDecisions} decisions, last decision {runtime.LastDecisionMilliseconds:0.000}ms, " +
            $"ready={driver.Ready}, enabled={driver.enabled}, timeScale={Time.timeScale}, paused={manager.IsAIVsAIDebugPausedForUi()}, " +
            $"focused={Application.isFocused}, background={Application.runInBackground}, thinking={thinking != null}.");
        Assert.That(driver.Failure, Is.Null);
        Assert.That(manager.LearnedAIStatusForUi, Is.Null.Or.Empty);
        Assert.That(manager.gameOver || manager.currentTurnSeatIndex == 0, Is.True, "Frozen CPU inference must complete its turn without a trainer.");
        Assert.That(driver.Difficulty, Is.EqualTo(difficulty));
        Object.FindFirstObjectByType<GameMenuActions>().QuitToMainMenu();
        yield return null;
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("MainMenu"));
        Assert.That(Object.FindFirstObjectByType<PrivatePolicyPlaytestController>(), Is.Null);
        LogAssert.NoUnexpectedReceived();
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator PlayAgainRetainsDifficultyAndRngAcrossBothStartingRoles()
    {
        Prepare(TurnManager.AIRecruitVariant.LearnedHard);
        yield return new EnterPlayMode();
        Application.runInBackground = true;
        yield return WaitReady();
        var driver = Object.FindFirstObjectByType<PrivatePolicyPlaytestController>();
        var manager = Object.FindFirstObjectByType<TurnManager>();
        SetRandom(driver, 42);
        var expected = new System.Random(42);
        int firstCount = 0;
        for (int game = 0; game < 100; game++)
        {
            manager.gameOver = true; // Exercise the real rematch callback, not a rated match.
            manager.OnPlayAgainButtonPressed();
            int first = expected.Next(2);
            Assert.That(driver.FirstSeat, Is.EqualTo(first));
            Assert.That(manager.currentTurnSeatIndex, Is.EqualTo(first));
            Assert.That(manager.GetGoldForSeat(first), Is.EqualTo(2));
            Assert.That(manager.GetGoldForSeat(1 - first), Is.EqualTo(2));
            Assert.That(driver.Difficulty, Is.EqualTo(TurnManager.AIRecruitVariant.LearnedHard));
            firstCount += first == 0 ? 1 : 0;
            Time.timeScale = 0;
            yield return null;
            var hud = Object.FindFirstObjectByType<GameplayTopHudUITKView>().GetComponent<UIDocument>().rootVisualElement;
            Assert.That(hud.Q<Label>("OpeningSeatLabel").text, Is.EqualTo(MatchOpeningLabels.ForSeat(0, first, 2, true)));
        }
        Assert.That(firstCount, Is.InRange(35, 65));
        Assert.That(driver.MatchNumber, Is.EqualTo(101));
        LogAssert.NoUnexpectedReceived();
        yield return new ExitPlayMode();
    }
}
