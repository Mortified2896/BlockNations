using System.Linq;
using BlockNations.Simulation;
using NUnit.Framework;

public sealed class UnitBalanceTests
{
    [TestCase("warrior", 3)] [TestCase("rider", 2)] [TestCase("archer", 2)] [TestCase("scout", 1)]
    public void AHitRemovesOneWholeHealthAndDurableUnitsSurviveIt(string type, int health)
    {
        var state = new MatchState(7, 7, 2, UnitRegistry.AllDefinitions);
        var attacker = new SimulationUnit(1, 0, state.Position(2, 2), UnitRegistry.Warrior);
        var target = new SimulationUnit(2, 1, state.Position(3, 2), UnitRegistry.GetDefinitionOrDefault(type));
        state.AddUnit(attacker); state.AddUnit(target);
        Assert.That(target.Health, Is.EqualTo(CombatValues.FromDisplay(health)));
        var result = MatchEngine.Apply(state, new MatchCommand(MatchActionKind.Attack, 0, 1, target.Position, 2));
        Assert.That(result.Applied, Is.True);
        Assert.That(target.Health, Is.EqualTo(CombatValues.FromDisplay(health - 1)));
        Assert.That(result.KilledUnitId, Is.EqualTo(health == 1 ? 2 : 0));
        Assert.That(attacker.RemainingMoves, Is.Zero);
    }

    [Test]
    public void ScoutIsAffordableAtOneGoldAndOtherUnitsStillCostTwo()
    {
        var state = new MatchState(7, 7, 2, UnitRegistry.AllDefinitions);
        var city = new SimulationCity(1, 0, state.Position(1, 1)); state.AddCity(city); state.SetGold(0, 1);
        var recruits = MatchEngine.LegalActions(state, 0).Where(a => a.Command.Kind == MatchActionKind.Recruit).ToArray();
        Assert.That(recruits.Length, Is.EqualTo(1));
        Assert.That(recruits[0].Command.RecruitType, Is.EqualTo(UnitRegistry.ScoutTypeId));
        Assert.That(MatchEngine.Apply(state, recruits[0].Command).Applied, Is.True);
        Assert.That(state.GoldForSeat(0), Is.Zero);
        Assert.That(state.Units.Single().Definition.MaxAttacksPerTurn, Is.Zero);
        Assert.That(state.Units.Single().Definition.VisionRange, Is.EqualTo(2));
        foreach (var definition in UnitRegistry.AllDefinitions.Where(d => d.TypeId != UnitRegistry.ScoutTypeId))
        {
            Assert.That(definition.RecruitCost, Is.EqualTo(2));
            Assert.That(definition.AttackUnits, Is.EqualTo(CombatValues.FromDisplay(1)));
        }
    }
}
