using System.Linq;
using BlockNations.Simulation;
using NUnit.Framework;

public sealed class AdvancedSimpleOpponentTests
{
    [TestCase(0)] [TestCase(1)]
    public void OpponentRecruitsMovesAndReturnsControlWithinABoundedTurn(int seat)
    {
        var match = new AdvancedMatch(firstSeat: seat); var ai = new AdvancedSimpleOpponent(seat);
        int count = 0;
        while (match.Board.IsTurnOwnedBySeat(seat) && count++ < 64) Assert.That(ai.Step(match, out _), Is.True);
        Assert.That(match.Board.CurrentTurnSeat, Is.EqualTo(1 - seat));
        Assert.That(match.Board.Units.Count(u => u.Seat == seat), Is.EqualTo(1));
        var home = match.Board.Cities.Single(c => c.Seat == seat);
        Assert.That(match.Board.Units.Single(u => u.Seat == seat).Position, Is.Not.EqualTo(home.Position));
        Assert.That(count, Is.LessThan(64));
    }

    [Test]
    public void WrongSeatCannotActOrConsumeEconomy()
    {
        var match = new AdvancedMatch();
        Assert.That(new AdvancedSimpleOpponent(1).Step(match, out _), Is.False);
        Assert.That(match.Board.Units, Is.Empty); Assert.That(match.Board.GoldForSeat(0), Is.EqualTo(9));
    }

    [Test]
    public void HiddenEnemyUnitsAndGoldDoNotChangeTheOpponentOpening()
    {
        var a = new AdvancedMatch(firstSeat: 1); var b = new AdvancedMatch(firstSeat: 1);
        a.Board.AddUnit(new SimulationUnit(100, 0, a.Board.Position(0, 0), UnitRegistry.Warrior));
        b.Board.AddUnit(new SimulationUnit(100, 0, b.Board.Position(0, 1), UnitRegistry.Archer));
        a.Board.SetGold(0, 1000); b.Board.SetGold(0, 0);
        var ai = new AdvancedSimpleOpponent(1);
        while (a.Board.IsTurnOwnedBySeat(1))
        {
            Assert.That(ai.Step(a, out string first), Is.True); Assert.That(ai.Step(b, out string second), Is.True);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(b.Board.Units.Where(u => u.Seat == 1).Select(u => (u.Position, u.Definition.TypeId)),
                Is.EqualTo(a.Board.Units.Where(u => u.Seat == 1).Select(u => (u.Position, u.Definition.TypeId))));
            Assert.That(a.CityEconomy(2).Civilians, Is.EqualTo(b.CityEconomy(2).Civilians));
        }
    }

    [Test]
    public void VisibleOpponentInRangeIsAttackedThroughTheSharedLegalMask()
    {
        var match = new AdvancedMatch(firstSeat: 1);
        Assert.That(match.TryRecruit(1, 2, "warrior", PopulationSex.Male, out _), Is.True);
        var target = new SimulationUnit(100, 0, match.Board.Position(4, 5), UnitRegistry.Warrior); match.Board.AddUnit(target);
        Assert.That(new AdvancedSimpleOpponent(1).Step(match, out string description), Is.True);
        Assert.That(description, Does.Contain("attacked")); Assert.That(target.Health, Is.EqualTo(20));
    }

    [TestCase(0)] [TestCase(1)]
    public void ScriptedHumanStandInCanCompleteAWholeGameAgainstTheOpponent(int starter)
    {
        var match = new AdvancedMatch(firstSeat: starter);
        var scripts = new[] { new AdvancedSimpleOpponent(0), new AdvancedSimpleOpponent(1) };
        int actions = 0;
        while (!match.Board.GameOver && actions++ < 1500)
            Assert.That(scripts[match.Board.CurrentTurnSeat].Step(match, out _), Is.True);
        Assert.That(match.Board.GameOver, Is.True, "Scripts should progress to a city capture instead of stall forever.");
        Assert.That(match.Board.WinnerSeat, Is.InRange(0, 1));
    }
}
