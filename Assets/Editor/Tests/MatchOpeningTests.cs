using System;
using System.Reflection;
using BlockNations.Simulation;
using BlockNations.Training;
using NUnit.Framework;
using UnityEngine;

public sealed class MatchOpeningTests
{
    [TestCase(0)] [TestCase(1)]
    public void FirstAndSecondPlayersGetTwoAndThreeOnTheirOwnFirstTurns(int first)
    {
        var state = new MatchState(7, 7, 2, UnitRegistry.AllDefinitions, first, firstSeat: first);
        for (int seat = 0; seat < 2; seat++)
        {
            state.AddCity(new SimulationCity(seat + 1, seat, state.Position(seat == 0 ? 1 : 5, seat == 0 ? 1 : 5)));
            state.SetGold(seat, MatchOpening.GoldBeforeIncome(seat, first));
        }
        MatchEngine.BeginTurn(state, first);
        Assert.That(state.GoldForSeat(first), Is.EqualTo(2));
        Assert.That(state.GoldForSeat(1 - first), Is.EqualTo(2), "The second seat has not collected first-turn income yet.");
        MatchEngine.Apply(state, new MatchCommand(MatchActionKind.EndTurn, first));
        Assert.That(state.GoldForSeat(1 - first), Is.EqualTo(3));
        MatchEngine.Apply(state, new MatchCommand(MatchActionKind.EndTurn, 1 - first));
        Assert.That(state.GoldForSeat(first), Is.EqualTo(3), "Later income is the same one gold per city.");
    }

    [Test]
    public void ActualSelfPlayResetUsesTheOpeningForEitherStartingSeat()
    {
        var starts = new System.Collections.Generic.HashSet<int>();
        for (int seed = 0; seed < 16; seed++)
        {
            var arena = new SelfPlayArena(0, 7, seed, false, 2, UnitRegistry.AllDefinitions);
            int first = arena.State.FirstSeat; starts.Add(first);
            Assert.That(arena.State.GoldForSeat(first), Is.EqualTo(2));
            MatchEngine.Apply(arena.State, new MatchCommand(MatchActionKind.EndTurn, first));
            Assert.That(arena.State.GoldForSeat(1 - first), Is.EqualTo(3));
        }
        Assert.That(starts, Is.EquivalentTo(new[] { 0, 1 }));
    }

    [Test]
    public void LegacyTrainingRecipesKeepTheirThreeAndThreeOpening()
    {
        var arena = new SelfPlayArena(0, 7, 42, false, 2, UnitRegistry.AllDefinitions, MatchOpening.LegacyEconomyVersion);
        int first = arena.State.FirstSeat;
        Assert.That(arena.State.GoldForSeat(first), Is.EqualTo(3));
        MatchEngine.Apply(arena.State, new MatchCommand(MatchActionKind.EndTurn, first));
        Assert.That(arena.State.GoldForSeat(1 - first), Is.EqualTo(3));
    }

    [TestCase(TurnManager.GameMode.VsAI, 2, 1, 2)]
    [TestCase(TurnManager.GameMode.PlayByPost, 2, 2, 2)]
    [TestCase(TurnManager.GameMode.PlayByPost, 4, 2, 2)]
    public void NewGameInitializationAppliesTheAiOpeningWithoutChangingMultiplayer(TurnManager.GameMode mode, int count, int firstGold, int secondGold)
    {
        var root = new GameObject("Opening economy fixture"); root.SetActive(false);
        try
        {
            var manager = root.AddComponent<TurnManager>(); manager.currentMode = mode; manager.startingGold = 2;
            typeof(TurnManager).GetField("configuredPlayByPostSeatCount", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, count);
            typeof(TurnManager).GetMethod("InitializeSeatGoldForNewGame", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(manager, new object[] { count });
            Assert.That(manager.GetGoldForSeat(0), Is.EqualTo(firstGold));
            Assert.That(manager.GetGoldForSeat(1), Is.EqualTo(secondGold));
            if (count == 4) Assert.That(manager.GetGoldForSeat(3), Is.EqualTo(2));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [TestCase(0, 0, 2, true, "You are Player 1 (Blue) · You move first")]
    [TestCase(1, 0, 2, true, "You are Player 2 (Red) · You move second")]
    [TestCase(0, 1, 2, true, "You are Player 1 (Blue) · You move second")]
    [TestCase(1, 1, 2, true, "You are Player 2 (Red) · You move first")]
    [TestCase(2, 0, 4, false, "You are Player 3 · You move third")]
    [TestCase(3, 0, 4, false, "You are Player 4 · You move fourth")]
    public void PlayerIdentityAndOpeningOrderAreExplicit(int seat, int first, int count, bool colour, string expected)
    {
        Assert.That(MatchOpeningLabels.ForSeat(seat, first, count, colour), Is.EqualTo(expected));
    }

    [TestCase(0)] [TestCase(1)]
    public void LocalPlayerLabelKeepsItsSeatWhileTheActiveTurnChanges(int humanSeat)
    {
        var root = new GameObject("Opening viewer fixture"); root.SetActive(false);
        try
        {
            var manager = root.AddComponent<TurnManager>(); manager.currentMode = TurnManager.GameMode.VsAI;
            TrainingSceneBuilder.Set(manager, "externallyDrivenMatch", true);
            manager.ConfigureExternalHumanSeat(humanSeat);
            foreach (int turn in new[] { 0, 1, 0 })
            {
                manager.currentTurnSeatIndex = turn;
                object[] values = { 0, 0, 0 };
                Assert.That(typeof(TurnManager).GetMethod("TryGetLocalOpeningSeatForUi", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(manager, values), Is.True);
                int seat = (int)values[0], count = (int)values[2];
                Assert.That(seat, Is.EqualTo(humanSeat)); Assert.That(count, Is.EqualTo(2));
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void InvalidOpeningIsRejectedRatherThanSilentlyChangingTheRecipe()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchOpening.GoldBeforeIncome(0, 0, 99));
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchOpening.TurnOrder(2, 0, 2));
    }
}
