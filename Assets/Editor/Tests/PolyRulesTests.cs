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
        Assert.That(m.City(1).level, Is.EqualTo(1), "Food improvements grow on owner turns, not on construction.");
        for (int i = 0; i < 4; i++) { Apply(m, PolyActionKind.EndTurn); Apply(m, PolyActionKind.EndTurn); }
        Assert.That(m.City(1).level, Is.EqualTo(2)); Assert.That(m.Income(0), Is.EqualTo(3));
        Assert.That(m.LegalActions(0).All(a => a.Kind == PolyActionKind.Reward), Is.True);
        Apply(m, PolyActionKind.Reward, 1, choice: "workshop"); Assert.That(m.Income(0), Is.EqualTo(4));
        Assert.That(m.Tiles.Count(t => t.cityId == 1), Is.EqualTo(9));
        Assert.That(m.TryApply(new PolyAction(PolyActionKind.Develop, 0, 1, 108, "fruit"), out _), Is.False);
    }
    [Test] public void HuntingHutKeepsAnimalsAndFeedsCityEveryOwnerTurn()
    {
        var m = new PolyMatch(3); m.Board.SetGold(0, 50);
        Apply(m, PolyActionKind.ResearchAndDevelop, 1, 23, "hunt");
        Assert.That(m.Tiles[23].resource, Is.EqualTo(PolyResource.Animals));
        Assert.That(m.Tiles[23].improvement, Is.EqualTo(PolyImprovement.HuntingHut));
        Assert.That(m.KnownYield(0, 23).Food, Is.EqualTo(2));
        Apply(m, PolyActionKind.EndTurn); Assert.That(m.City(1).food, Is.Zero);
        Apply(m, PolyActionKind.EndTurn); Assert.That(m.City(1).food, Is.EqualTo(2));
        var n = PolyMatch.Restore(m.CreateSave()); Assert.That(n.City(1).food, Is.EqualTo(2));
        Apply(n, PolyActionKind.EndTurn); Apply(n, PolyActionKind.EndTurn);
        Assert.That(n.City(1).food, Is.EqualTo(4)); Assert.That(n.Tiles[23].resource, Is.EqualTo(PolyResource.Animals));
    }
    [Test] public void MinePermanentlyAddsGoldAndResearchAndBuildIsAtomic()
    {
        var m = new PolyMatch(3); m.Board.SetGold(0, 19);
        var option = m.DevelopmentOptions(0, 1).Single(a => a.Choice == "mine");
        Assert.That(option.Technologies, Is.EqualTo(new[] { "climbing", "mining" }));
        Assert.That(option.Cost, Is.EqualTo(20)); Assert.That(option.GoldYield, Is.EqualTo(2));
        Assert.That(m.TryApply(option, out _), Is.False);
        Assert.That(m.Board.GoldForSeat(0), Is.EqualTo(19)); Assert.That(m.HasTechnology(0, "climbing"), Is.False);
        m.Board.SetGold(0, 25); option.Cost = 0;
        Assert.That(m.TryApply(option, out _), Is.True, "The engine recomputes the bill rather than trusting UI values.");
        Assert.That(m.Board.GoldForSeat(0), Is.EqualTo(5)); Assert.That(m.Income(0), Is.EqualTo(4));
        Assert.That(m.Tiles[1].resource, Is.EqualTo(PolyResource.Ore));
        Apply(m, PolyActionKind.EndTurn); Apply(m, PolyActionKind.EndTurn);
        Assert.That(m.Board.GoldForSeat(0), Is.EqualTo(9));
        Assert.That(m.TryApply(new PolyAction(PolyActionKind.ResearchAndDevelop, 0, 1, 1, "mine"), out _), Is.False);
    }
    [Test] public void TileResearchUsesAlreadyBoughtPrerequisitesAndRejectsEnemyOccupation()
    {
        var m = new PolyMatch(3); m.Board.SetGold(0, 100);
        Apply(m, PolyActionKind.Research, choice: "organization");
        var farm = m.DevelopmentOptions(0, 11).Single(a => a.Choice == "farm");
        Assert.That(farm.TechnologyCost, Is.EqualTo(10)); Assert.That(farm.Cost, Is.EqualTo(15));
        Assert.That(farm.Technologies, Is.EqualTo(new[] { "farming" }));
        var save = m.CreateSave(); save.units[1].position = 11; var occupied = PolyMatch.Restore(save);
        Assert.That(occupied.DevelopmentOptions(0, 11), Is.Empty);
        Assert.That(occupied.TryApply(farm, out _), Is.False);
        farm.SeatIndex = 1; Assert.That(m.TryApply(farm, out _), Is.False);
    }
    [Test] public void WindmillYieldAndPreviewIncludeAdjacentFarms()
    {
        var m = new PolyMatch(3); m.Board.SetGold(0, 100);
        Apply(m, PolyActionKind.ResearchAndDevelop, 1, 11, "farm");
        m.Tiles[0].terrain = PolyTerrain.Plains;
        var mill = m.DevelopmentOptions(0, 0).Single(a => a.Choice == "windmill");
        Assert.That(mill.Food, Is.EqualTo(2)); Assert.That(mill.TechnologyCost, Is.EqualTo(15));
        Assert.That(m.TryApply(mill, out _), Is.True);
        Assert.That(m.CityFood(1), Is.EqualTo(5)); Assert.That(m.KnownYield(0, 0).Food, Is.EqualTo(2));
    }
    [Test] public void LegacyHarvestsAndBuildingsMigrateWithoutChangingProgressTreasuryOrActions()
    {
        var m = new PolyMatch(3); var save = m.CreateSave(); save.rulesVersion = PolyRules.LegacyVersion;
        save.tiles[13].resource = PolyResource.None; save.tiles[23].resource = PolyResource.None;
        save.tiles[11].resource = PolyResource.None; save.tiles[11].improvement = PolyImprovement.Farm;
        save.cities[0].level = 2; save.cities[0].population = 1; save.cities[0].workshops = 1;
        save.units[0].moved = true; save.seats[0].gold = 4;
        var n = PolyMatch.Restore(save);
        Assert.That(n.Tiles[13].improvement, Is.EqualTo(PolyImprovement.Orchard));
        Assert.That(n.Tiles[23].improvement, Is.EqualTo(PolyImprovement.HuntingHut));
        Assert.That(n.Tiles[11].resource, Is.EqualTo(PolyResource.Crops));
        Assert.That(n.CityFood(1), Is.EqualTo(6)); Assert.That(n.City(1).food, Is.Zero);
        Assert.That(n.City(1).level, Is.EqualTo(2)); Assert.That(n.City(1).population, Is.EqualTo(1));
        Assert.That(n.Board.GoldForSeat(0), Is.EqualTo(4)); Assert.That(n.UnitState(1).moved, Is.True);
        Assert.That(n.CreateSave().rulesVersion, Is.EqualTo(PolyRules.Version));
        Assert.That(save.tiles[13].improvement, Is.EqualTo(PolyImprovement.None), "Migration must not mutate its source checkpoint.");
        save.cities[0].food = -1; Assert.Throws<ArgumentException>(() => PolyMatch.Restore(save));
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
