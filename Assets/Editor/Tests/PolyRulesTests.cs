using System;
using System.Linq;
using BlockNations.Simulation;
using BlockNations.Simulation.Poly;
using NUnit.Framework;

public sealed class PolyRulesTests
{
    private static void Apply(PolyMatch m, PolyActionKind kind, int actor = 0, int destination = -1, string choice = null)
    {
        Assert.That(m.TryApply(new PolyAction(kind, m.currentTurnSeatIndex, actor, destination, choice), out var reason), Is.True, reason);
    }
    [Test] public void MapHasFixedTerritoriesAndSeparateCatalog()
    {
        var m = new PolyMatch(8);
        Assert.That(m.Board.Width, Is.EqualTo(11)); Assert.That(m.Cities.Count, Is.EqualTo(7));
        foreach (var c in m.Cities) Assert.That(m.Tiles.Count(t => t.cityId == c.id), Is.EqualTo(9));
        Assert.That(m.Board.Roster.Count, Is.EqualTo(8));
        Assert.That(m.Board.RecruitType("poly-archer").VisionRange, Is.EqualTo(2));
        Assert.That(UnitRegistry.AllDefinitions.Any(u => u.TypeId.StartsWith("poly-")), Is.False);
    }
    [Test] public void ResearchRequiresParentAndOwnTurnAndGold()
    {
        var m = new PolyMatch(2); m.Board.SetGold(0, 100);
        Assert.That(m.TryApply(new PolyAction(PolyActionKind.Research, 0, choice: "smithing"), out _), Is.False);
        Assert.That(m.TryApply(new PolyAction(PolyActionKind.Research, 1, choice: "climbing"), out _), Is.False);
        Apply(m, PolyActionKind.Research, choice: "climbing"); Apply(m, PolyActionKind.Research, choice: "mining");
        Assert.That(m.Board.GoldForSeat(0), Is.EqualTo(85));
        m.Board.SetGold(0, 0); Assert.That(m.TryApply(new PolyAction(PolyActionKind.Research, 0, choice: "smithing"), out _), Is.False);
    }
    [Test] public void DevelopmentLevelsCityAndRequiresRewardWithoutExpansion()
    {
        var m = new PolyMatch(3); m.Board.SetGold(0, 100); Apply(m, PolyActionKind.Research, choice: "organization");
        Apply(m, PolyActionKind.Research, choice: "farming"); Apply(m, PolyActionKind.Develop, 1, 11, "farm");
        Assert.That(m.City(1).level, Is.EqualTo(2)); Assert.That(m.Income(0), Is.EqualTo(3));
        Assert.That(m.LegalActions(0).All(a => a.Kind == PolyActionKind.Reward), Is.True);
        Apply(m, PolyActionKind.Reward, 1, choice: "workshop"); Assert.That(m.Income(0), Is.EqualTo(4));
        Assert.That(m.Tiles.Count(t => t.cityId == 1), Is.EqualTo(9));
        Assert.That(m.TryApply(new PolyAction(PolyActionKind.Develop, 0, 1, 108, "fruit"), out _), Is.False);
    }
    [Test] public void IncomingSeatCollectsIncomeExactlyOnceIncludingRestore()
    {
        var m = new PolyMatch(3); int gold = m.Board.GoldForSeat(1);
        Apply(m, PolyActionKind.EndTurn); Assert.That(m.Board.GoldForSeat(1), Is.EqualTo(gold + m.Income(1)));
        var restored = PolyMatch.Restore(m.CreateSave()); Assert.That(restored.Board.GoldForSeat(1), Is.EqualTo(m.Board.GoldForSeat(1)));
        Apply(restored, PolyActionKind.EndTurn); Assert.That(restored.Board.Round, Is.EqualTo(2));
    }
    [Test] public void SaveRestoresExactActionsResearchAndFogAndRejectsWrongRules()
    {
        var m = new PolyMatch(6); Apply(m, PolyActionKind.Move, 1, 13); Apply(m, PolyActionKind.Research, choice: "organization");
        var save = m.CreateSave(); var n = PolyMatch.Restore(save);
        Assert.That(n.UnitState(1).moved, Is.True); Assert.That(n.Board.GetUnit(1).Position, Is.EqualTo(13));
        Assert.That(n.HasTechnology(0, "organization"), Is.True);
        Assert.That(n.Visibility(0), Is.EqualTo(m.Visibility(0)));
        Assert.That(Enumerable.Range(0,121).Select(p => n.KnownTile(0,p) != null), Is.EqualTo(Enumerable.Range(0,121).Select(p => m.KnownTile(0,p) != null)));
        save.rulesVersion = "future"; Assert.Throws<ArgumentException>(() => PolyMatch.Restore(save));
    }
    [Test] public void HeuristicCannotObserveHiddenEnemyUnitsOrTreasury()
    {
        var m = new PolyMatch(2); var a = PolyObservation.ForSeat(m, 0);
        Assert.That(a.Units.All(u => u.Seat == 0), Is.True);
        var save = m.CreateSave(); save.seats[1].gold = 900;
        save.units[1].position = 97; var changed = PolyMatch.Restore(save);
        var b = PolyObservation.ForSeat(changed, 0);
        Assert.That(a.KnownCityOwners, Is.EqualTo(b.KnownCityOwners));
        Assert.That(PolyOpponent.Choose(a).Kind, Is.EqualTo(PolyOpponent.Choose(b).Kind));
        Assert.That(PolyOpponent.Choose(a).Choice, Is.EqualTo(PolyOpponent.Choose(b).Choice));
    }
    [Test] public void NormalFogShrinksAfterSpotterLeavesAndKeepsTerrainMemory()
    {
        var m = new PolyMatch(2); var s = m.CreateSave(); s.units[0].position = 49;
        var n = PolyMatch.Restore(s); Assert.That(n.Visibility(0)[48], Is.True);
        n.Tiles[50].terrain = PolyTerrain.Plains; Apply(n, PolyActionKind.Move, 1, 50);
        Assert.That(n.Visibility(0)[48], Is.False); Assert.That(n.KnownTile(0,48), Is.Not.Null);
    }
    [Test] public void NeutralCaptureDoesNotEndMatchAndWaitsUntilFollowingTurn()
    {
        var m = new PolyMatch(2); var s = m.CreateSave(); s.units[0].position = 55;
        var n = PolyMatch.Restore(s); Apply(n, PolyActionKind.Move, 1, 56);
        Assert.That(n.TryApply(new PolyAction(PolyActionKind.Capture, 0, 1), out _), Is.False);
        Apply(n, PolyActionKind.EndTurn); Apply(n, PolyActionKind.EndTurn); Apply(n, PolyActionKind.Capture, 1);
        Assert.That(n.City(3).ownerSeatIndex, Is.EqualTo(0)); Assert.That(n.Board.GameOver, Is.False);
    }
    [Test] public void CaptureLastEnemyCityWins()
    {
        var m = new PolyMatch(2); var s = m.CreateSave(); s.units[0].position = 108; s.units[1].position = 109;
        var n = PolyMatch.Restore(s); Apply(n, PolyActionKind.Capture, 1); Assert.That(n.Board.GameOver, Is.True); Assert.That(n.Board.WinnerSeat, Is.EqualTo(0));
    }
    [Test] public void RetaliationAndRiderRetreatAndKnightChainAreLegal()
    {
        var m = new PolyMatch(2); var s = m.CreateSave(); s.units[0].type = "poly-rider"; s.units[0].position = 49; s.units[1].position = 50;
        var n = PolyMatch.Restore(s); Apply(n, PolyActionKind.Attack, 1, 50);
        Assert.That(n.Board.GetUnit(1).Health, Is.LessThan(100)); Assert.That(n.Paths(n.Board.GetUnit(1)).Count, Is.GreaterThan(0));
        s.units[0].type = "poly-knight"; s.units[1].health = 10; n = PolyMatch.Restore(s); Apply(n, PolyActionKind.Attack, 1, 50);
        Assert.That(n.UnitState(1).attacked, Is.False); Assert.That(n.Board.GetUnit(1).Position, Is.EqualTo(50));
    }
    [Test] public void CatapultCannotFireAfterMovingOrRetaliate()
    {
        var m = new PolyMatch(2); var s = m.CreateSave(); s.units[0].type = "poly-catapult"; s.units[0].position = 49; s.units[1].position = 61;
        var n = PolyMatch.Restore(s); n.Tiles[50].terrain = PolyTerrain.Plains; Apply(n, PolyActionKind.Move, 1, 50);
        Assert.That(n.LegalActions(0).Any(a => a.Kind == PolyActionKind.Attack && a.ActorId == 1), Is.False);
    }
    [Test] public void ScriptedTurnsAreBoundedAndReturnControlWithoutTraining()
    {
        var m = new PolyMatch(2);
        for (int turn=0;turn<8 && !m.Board.GameOver;turn++) {
            Apply(m, PolyActionKind.EndTurn); int actions = 0;
            while (m.IsTurnOwnedBySeat(1) && actions++ < 200) {
                var action = PolyOpponent.Choose(PolyObservation.ForSeat(m,1)); Assert.That(action, Is.Not.Null);
                Assert.That(m.TryApply(action,out var reason), Is.True,reason);
            }
            Assert.That(actions, Is.LessThan(200)); Assert.That(m.IsTurnOwnedBySeat(1), Is.False);
        }
    }
}
