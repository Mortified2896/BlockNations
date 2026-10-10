using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class GameplayGoldVisibilityTests
{
    private static TurnManager CreateInactive(TurnManager.GameMode mode)
    {
        var root = new GameObject("Gold visibility fixture");
        root.SetActive(false);
        var manager = root.AddComponent<TurnManager>();
        manager.currentMode = mode;
        manager.AddGoldForSeat(0, 9);
        manager.AddGoldForSeat(1, 137);
        return manager;
    }

    private static void SetTurn(TurnManager manager, int seat)
    {
        manager.currentTurnSeatIndex = seat;
        manager.isPlayerTurn = seat == 0;
    }

    [TestCase(TurnManager.AIRecruitVariant.Default)]
    [TestCase(TurnManager.AIRecruitVariant.RiderFocus)]
    [TestCase(TurnManager.AIRecruitVariant.HardTactician)]
    public void EveryLocalOpponentKeepsHumanGoldDuringTurnChanges(TurnManager.AIRecruitVariant variant)
    {
        var manager = CreateInactive(TurnManager.GameMode.VsAI);
        try
        {
            manager.aiRecruitVariant = variant;
            foreach (int seat in new[] { 0, 1, 0 })
            {
                SetTurn(manager, seat);
                Assert.That(manager.TryGetDisplayedGoldForUi(out int gold), Is.True);
                Assert.That(gold, Is.EqualTo(9));
                Assert.That(manager.GetDisplayedGoldForUi(), Is.EqualTo(9));
                manager.AddGoldForSeat(1, 5);
            }
            manager.TrySpendGoldForSeat(0, 2);
            Assert.That(manager.GetDisplayedGoldForUi(), Is.EqualTo(7));
        }
        finally { UnityEngine.Object.DestroyImmediate(manager.gameObject); }
    }

    [TestCase(0)]
    [TestCase(1)]
    public void FrozenPolicyPlaytestsShowOnlyTheirExplicitHumanSeat(int humanSeat)
    {
        var manager = CreateInactive(TurnManager.GameMode.VsAI);
        try
        {
            TrainingSceneBuilder.Set(manager, "externallyDrivenMatch", true);
            manager.ConfigureExternalHumanSeat(humanSeat);
            int ownGold = manager.GetGoldForSeat(humanSeat);
            foreach (int seat in new[] { 0, 1, 0 })
            {
                SetTurn(manager, seat);
                Assert.That(manager.GetDisplayedGoldForUi(), Is.EqualTo(ownGold));
                manager.AddGoldForSeat(1 - humanSeat, 10);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(manager.gameObject); }
    }

    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void MultiplayerGoldStaysWithTheLocalSeatAcrossWaitingAndIncomingTurns(int seatCount)
    {
        var manager = CreateInactive(TurnManager.GameMode.PlayByPost);
        string gameId = "gold-visibility-" + Guid.NewGuid().ToString("N");
        try
        {
            typeof(TurnManager).GetField("configuredPlayByPostSeatCount", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, seatCount);
            typeof(TurnManager).GetField("currentGameId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, gameId);
            for (int seat = 2; seat < seatCount; seat++) manager.AddGoldForSeat(seat, 20 + seat);
            for (int localSeat = 0; localSeat < seatCount; localSeat++)
            {
                LocalPlayerSeatStore.SetSeat(gameId, localSeat);
                foreach (bool waiting in new[] { false, true })
                for (int actingSeat = 0; actingSeat < seatCount; actingSeat++)
                {
                    typeof(TurnManager).GetField("isPlayByPostWaitingForExport", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, waiting);
                    SetTurn(manager, actingSeat);
                    Assert.That(manager.GetDisplayedGoldForUi(), Is.EqualTo(manager.GetGoldForSeat(localSeat)));
                }
            }
            LocalPlayerSeatStore.ClearSeat(gameId);
            Assert.That(manager.TryGetDisplayedGoldForUi(out int hidden), Is.False);
            Assert.That(hidden, Is.Zero, "An unassigned viewer must not fall back to another player's resources.");
            if (seatCount < 4)
            {
                LocalPlayerSeatStore.SetSeat(gameId, seatCount);
                Assert.That(manager.TryGetDisplayedGoldForUi(out _), Is.False);
            }
        }
        finally
        {
            LocalPlayerSeatStore.ClearSeat(gameId);
            UnityEngine.Object.DestroyImmediate(manager.gameObject);
        }
    }

    [Test]
    public void NoGameOrUnboundMultiplayerViewerHasNoGoldDisplay()
    {
        var manager = CreateInactive(TurnManager.GameMode.None);
        try
        {
            Assert.That(manager.TryGetDisplayedGoldForUi(out _), Is.False);
            manager.currentMode = TurnManager.GameMode.PlayByPost;
            Assert.That(manager.TryGetDisplayedGoldForUi(out _), Is.False);
        }
        finally { UnityEngine.Object.DestroyImmediate(manager.gameObject); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExplicitDevelopmentSpectatorsCanStillDisplayTheActingSeat(bool external)
    {
        var manager = CreateInactive(TurnManager.GameMode.VsAI);
        try
        {
            TrainingSceneBuilder.Set(manager, external ? "externallyDrivenMatch" : "enableAIVsAIDebugMode", true);
            foreach (int seat in new[] { 0, 1, 0 })
            {
                SetTurn(manager, seat);
                Assert.That(manager.GetDisplayedGoldForUi(), Is.EqualTo(manager.GetGoldForSeat(seat)));
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(manager.gameObject); }
    }

    [UnityTest]
    public IEnumerator NextButtonNeverRendersEnemyGoldInTheHumanPolicyPlaytest()
    {
        AIVsAIBatchRunController.ClearAll();
        TrainingArena configured = TrainingSceneBuilder.Prepare("", 42, false, false, human: true);
        TrainingSceneBuilder.Set(configured, "boardSize", 7);
        TrainingSceneBuilder.ShowBoard();
        yield return new EnterPlayMode();
        var arena = UnityEngine.Object.FindFirstObjectByType<TrainingArena>();
        var manager = TurnManager.Instance;
        for (int frame = 0; frame < 20 && !arena.CanEndHumanTurn; frame++) yield return null;
        Assert.That(arena.CanEndHumanTurn, Is.True);
        Assert.That(manager.GetGoldForSeat(0), Is.EqualTo(2));
        manager.AddGoldForSeat(0, 10);
        manager.AddGoldForSeat(1, 100);
        var hud = UnityEngine.Object.FindFirstObjectByType<GameplayTopHudUITKView>();
        var label = hud.GetComponent<UIDocument>().rootVisualElement.Q<Label>("GoldLabel");
        var opening = hud.GetComponent<UIDocument>().rootVisualElement.Q<Label>("OpeningSeatLabel");
        string ownBalance = "Gold " + manager.GetGoldForSeat(0);
        yield return null;
        Assert.That(label.text, Is.EqualTo(ownBalance));
        Assert.That(opening.text, Is.EqualTo("You are Player 1 (Blue) · You move first"));
        Assert.That(opening.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
        Assert.That(opening.worldBound.yMax, Is.LessThanOrEqualTo(label.worldBound.yMin));
        manager.OnEndTurnButtonPressed();
        arena.Paused = true; // Hold the opponent's turn so every rendered frame can be inspected.
        Assert.That(manager.currentTurnSeatIndex, Is.EqualTo(1));
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
            Assert.That(label.text, Is.EqualTo(ownBalance), "Opponent's turn, rendered frame " + frame);
            Assert.That(label.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
        }
        Assert.That(manager.TryAdvanceExternalMatchTurn(1), Is.True);
        yield return null;
        Assert.That(label.text, Is.EqualTo("Gold " + manager.GetGoldForSeat(0)));
        yield return new ExitPlayMode();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }
}
