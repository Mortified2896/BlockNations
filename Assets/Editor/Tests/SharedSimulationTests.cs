using System;
using System.Linq;
using BlockNations.AI;
using BlockNations.Simulation;
using NUnit.Framework;

public sealed class SharedSimulationTests
{
    private static MatchState Board(int seat = 0, int size = 7, int seats = 2) =>
        new MatchState(size, size, seats, UnitRegistry.AllDefinitions, seat, firstSeat: seat);
    private static SimulationUnit Add(MatchState state, int id, int seat, int x, int y, UnitDefinition type = null,
        int moves = 0, int attacks = 0)
    {
        var unit = new SimulationUnit(id, seat, state.Position(x, y), type ?? UnitRegistry.Warrior,
            movesUsed: moves, attacksUsed: attacks);
        state.AddUnit(unit); return unit;
    }
    private static MatchCommand Move(MatchState state, SimulationUnit unit, int x, int y) =>
        new MatchCommand(MatchActionKind.Move, unit.Seat, unit.Id, state.Position(x, y));
    private static MatchCommand Attack(SimulationUnit unit, SimulationUnit target) =>
        new MatchCommand(MatchActionKind.Attack, unit.Seat, unit.Id, target.Position, target.Id);
    private static MatchCommand End(int seat) => new MatchCommand(MatchActionKind.EndTurn, seat);

    [TestCase(0)] [TestCase(1)]
    public void BothSeatsMoveAndCaptureUsingOnlyExplicitOwnership(int seat)
    {
        MatchState state = Board(seat);
        SimulationUnit unit = Add(state, 10, seat, 2, 2);
        var city = new SimulationCity(20, 1 - seat, state.Position(3, 3)); state.AddCity(city);
        MatchTransition result = MatchEngine.Apply(state, Move(state, unit, 3, 3));
        Assert.That(result.Applied, Is.True); Assert.That(result.CapturedCityId, Is.EqualTo(city.Id));
        Assert.That(state.GameOver, Is.True); Assert.That(state.WinnerSeat, Is.EqualTo(seat));
        Assert.That(city.Seat, Is.EqualTo(seat)); Assert.That(unit.MovesUsed, Is.EqualTo(1));
        Assert.That(MatchEngine.LegalActions(state, seat), Is.Empty);
    }

    [Test]
    public void HiddenBlockerDoesNotLeakThroughLegalMaskButConsumesAttemptAndAttacks()
    {
        MatchState state = Board();
        SimulationUnit rider = Add(state, 1, 0, 1, 1, UnitRegistry.Rider);
        Add(state, 2, 1, 3, 1);
        bool[] visible = MatchEngine.Visibility(state, 0);
        Assert.That(visible[state.Position(3, 1)], Is.False);
        MatchCommand command = Move(state, rider, 3, 1);
        Assert.That(MatchEngine.LegalActions(state, 0).Any(action => action.Command.Equals(command)), Is.True);
        MatchTransition result = MatchEngine.Apply(state, command);
        Assert.That(result.Applied && result.HiddenBlocker, Is.True);
        Assert.That(result.MovedSteps, Is.EqualTo(1)); Assert.That(rider.Position, Is.EqualTo(state.Position(2, 1)));
        Assert.That(rider.MovesUsed, Is.EqualTo(2)); Assert.That(rider.AttacksUsed, Is.EqualTo(rider.Definition.MaxAttacksPerTurn));
    }

    [Test]
    public void VisibleOccupantsBlockPathsIncludingDiagonalMovement()
    {
        MatchState state = Board(); SimulationUnit unit = Add(state, 1, 0, 1, 1);
        Add(state, 2, 0, 2, 2); Add(state, 3, 1, 2, 1);
        var actions = MatchEngine.LegalUnitActions(state, 0, unit.Id);
        Assert.That(actions.Any(a => a.Command.Kind == MatchActionKind.Move && a.Command.Destination == state.Position(2, 2)), Is.False);
        Assert.That(actions.Any(a => a.Command.Kind == MatchActionKind.Attack && a.Command.TargetId == 3), Is.True);
        Assert.That(MatchEngine.Apply(state, Move(state, unit, 2, 2)).Applied, Is.False);
        Assert.That(unit.MovesUsed, Is.Zero);
    }

    [Test]
    public void PathTieOrderMatchesTheCharacterizedHumanPath()
    {
        MatchState state = Board(); SimulationUnit rider = Add(state, 1, 0, 1, 1, UnitRegistry.Rider);
        int[] path = MatchEngine.ReachablePaths(state, rider, MatchEngine.Visibility(state, 0))[state.Position(3, 2)];
        Assert.That(path, Is.EqualTo(new[] { state.Position(2, 1), state.Position(3, 2) }));
    }

    [TestCase(false)] [TestCase(true)]
    public void AttacksUseTheirOwnBudgetAndOnlyMeleeAdvances(bool ranged)
    {
        var weak = new UnitDefinition("weak", "Weak", 1, 1, "weak", 5, 1, 1, true, 0, 1, 1);
        MatchState state = Board();
        SimulationUnit unit = Add(state, 1, 0, 2, 2, ranged ? UnitRegistry.Archer : UnitRegistry.Warrior);
        SimulationUnit target = Add(state, 2, 1, 3, 2, weak);
        MatchTransition result = MatchEngine.Apply(state, Attack(unit, target));
        Assert.That(result.KilledUnitId, Is.EqualTo(target.Id)); Assert.That(state.GetUnit(target.Id), Is.Null);
        Assert.That(unit.MovesUsed, Is.Zero); Assert.That(unit.AttacksUsed, Is.EqualTo(1));
        Assert.That(unit.Position, Is.EqualTo(state.Position(ranged ? 2 : 3, 2)));
        Assert.That(MatchEngine.Apply(state, Attack(unit, target)).Applied, Is.False);
    }

    [Test]
    public void RangedAttackCannotCaptureTheDefendersCityByStayingAtRange()
    {
        MatchState state = Board(); SimulationUnit archer = Add(state, 1, 0, 2, 2, UnitRegistry.Archer);
        var weak = new UnitDefinition("weak", "Weak", 1, 1, "weak", 5, 1, 1, true, 0, 1, 1);
        SimulationUnit target = Add(state, 2, 1, 3, 2, weak);
        state.AddCity(new SimulationCity(3, 1, target.Position));
        Assert.That(MatchEngine.Apply(state, Attack(archer, target)).Applied, Is.True);
        Assert.That(state.GameOver, Is.False);
    }

    [Test]
    public void ArmorAndZeroDamageAttacksDoNotRemoveHealth()
    {
        MatchState state = Board(); SimulationUnit rider = Add(state, 1, 0, 2, 2, UnitRegistry.Rider);
        var armored = new UnitDefinition("armored", "Armor", 3, 1, "armor", 10, 10, 1, true, 6, 1, 1);
        SimulationUnit target = Add(state, 2, 1, 3, 2, armored);
        MatchEngine.Apply(state, Attack(rider, target));
        Assert.That(target.Health, Is.EqualTo(10)); Assert.That(rider.RemainingMoves, Is.EqualTo(2));
        Assert.That(rider.AttacksUsed, Is.EqualTo(1));
    }

    [Test]
    public void NonAttackingAndAttackBeforeMovementCapabilitiesAreEnforced()
    {
        MatchState state = Board(); SimulationUnit scout = Add(state, 1, 0, 2, 2, UnitRegistry.Scout);
        SimulationUnit target = Add(state, 2, 1, 3, 2);
        Assert.That(MatchEngine.Apply(state, Attack(scout, target)).Applied, Is.False);
        state = Board(); SimulationUnit archer = Add(state, 1, 0, 2, 2, UnitRegistry.Archer, moves: 1);
        target = Add(state, 2, 1, 3, 2);
        Assert.That(MatchEngine.Apply(state, Attack(archer, target)).Applied, Is.False);
    }

    [Test]
    public void RecruitmentPaysOnceAndTurnStartResetsOnlyTheActingSeat()
    {
        MatchState state = Board(); state.SetGold(0, 2); state.SetGold(1, 7);
        state.AddCity(new SimulationCity(1, 0, state.Position(1, 1)));
        state.AddCity(new SimulationCity(2, 1, state.Position(5, 5), recruited: true));
        var command = new MatchCommand(MatchActionKind.Recruit, 0, 1, state.Position(1, 1), recruitType: UnitRegistry.RiderTypeId);
        MatchTransition result = MatchEngine.Apply(state, command);
        SimulationUnit rider = state.GetUnit(result.RecruitedUnitId);
        Assert.That(state.GoldForSeat(0), Is.Zero); Assert.That(state.GetCity(1).Recruited, Is.True);
        Assert.That(rider.RemainingMoves, Is.EqualTo(2));
        Assert.That(MatchEngine.Apply(state, command).Applied, Is.False);
        MatchEngine.Apply(state, Move(state, rider, 2, 1));
        MatchEngine.Apply(state, End(0));
        Assert.That(state.GoldForSeat(1), Is.EqualTo(8)); Assert.That(state.GetCity(2).Recruited, Is.False);
        Assert.That(rider.RemainingMoves, Is.Zero);
        MatchEngine.Apply(state, End(1));
        Assert.That(state.GoldForSeat(0), Is.EqualTo(1)); Assert.That(rider.RemainingMoves, Is.EqualTo(2));
        Assert.That(state.GetCity(1).Recruited, Is.False); Assert.That(state.Round, Is.EqualTo(2));
    }

    [TestCase(0)] [TestCase(1)] [TestCase(4)]
    public void RoundCountsACompleteCycleFromAnyStartingSeat(int first)
    {
        MatchState state = Board(first, seats: 5);
        for (int i = 0; i < 5; i++)
        {
            Assert.That(state.Round, Is.EqualTo(1));
            Assert.That(MatchEngine.Apply(state, End((first + i) % 5)).Applied, Is.True);
        }
        Assert.That(state.CurrentTurnSeat, Is.EqualTo(first)); Assert.That(state.Round, Is.EqualTo(2));
    }

    [Test]
    public void ArbitraryNewUnitUsesCapabilitiesAndNotATypeName()
    {
        var type = new UnitDefinition("new-custom-unit", "Custom", 3, 3, "custom", 20, 12, 3, true, 4, 3, 2, usesCommittedMoveAction: true);
        var state = new MatchState(9, 6, 3, new[] { type });
        SimulationUnit unit = Add(state, 1, 0, 2, 2, type);
        Assert.That(MatchEngine.Visibility(state, 0)[state.Position(5, 5)], Is.True);
        Assert.That(MatchEngine.Apply(state, Move(state, unit, 5, 2)).Applied, Is.True);
        Assert.That(unit.RemainingMoves, Is.Zero); Assert.That(unit.CanAttack, Is.True);
    }

    [Test]
    public void OccupancyAndCopiesStayIndependentAfterMovesKillsAndRecruitment()
    {
        MatchState state = Board(); SimulationUnit unit = Add(state, 1, 0, 2, 2);
        MatchState copy = state.Copy();
        MatchEngine.Apply(copy, Move(copy, copy.GetUnit(1), 3, 3));
        Assert.That(state.UnitAt(state.Position(2, 2)), Is.SameAs(unit));
        Assert.That(state.UnitAt(state.Position(3, 3)), Is.Null);
        Assert.That(copy.UnitAt(copy.Position(2, 2)), Is.Null);
        Assert.That(copy.UnitAt(copy.Position(3, 3)).Id, Is.EqualTo(1));
    }

    [Test]
    public void InvalidAndOutOfTurnCommandsLeaveStateUnchanged()
    {
        MatchState state = Board(); SimulationUnit own = Add(state, 1, 0, 1, 1), other = Add(state, 2, 1, 5, 5);
        Assert.That(MatchEngine.Apply(state, Move(state, other, 4, 4)).Applied, Is.False);
        Assert.That(MatchEngine.Apply(state, Move(state, own, -1, 1)).Applied, Is.False);
        Assert.That(MatchEngine.Apply(state, Attack(own, other)).Applied, Is.False);
        Assert.That(own.MovesUsed + own.AttacksUsed, Is.Zero); Assert.That(state.Round, Is.EqualTo(1));
        Assert.That(state.CurrentTurnSeat, Is.Zero); Assert.That(other.Health, Is.EqualTo(10));
    }

    [Test]
    public void EnemyPrivateCountersGoldAndRecruitmentDoNotChangeNetworkInputs()
    {
        MatchState a = Board(); Add(a, 1, 0, 2, 2); Add(a, 2, 1, 3, 2, moves: 1, attacks: 1);
        a.AddCity(new SimulationCity(3, 1, a.Position(3, 3), recruited: true)); a.SetGold(1, 500);
        MatchState b = Board(); Add(b, 1, 0, 2, 2); Add(b, 2, 1, 3, 2);
        b.AddCity(new SimulationCity(3, 1, b.Position(3, 3))); b.SetGold(1, 1);
        var ao = new SimulationObservationSource().Observe(a, 0).Observation;
        var bo = new SimulationObservationSource().Observe(b, 0).Observation;
        Assert.That(LearnedActionSchema.Encode(ao, -1), Is.EqualTo(LearnedActionSchema.Encode(bo, -1)));
        Assert.That(ao.LegalActions.Select(action => action.Key), Is.EqualTo(bo.LegalActions.Select(action => action.Key)));
    }

    [Test]
    public void HiddenUnitsAndStableIdsDoNotChangeNetworkInputsOrActionMasks()
    {
        MatchState a = Board(); Add(a, 1, 0, 1, 1); Add(a, 2, 1, 5, 5);
        MatchState b = Board(); Add(b, 500, 0, 1, 1); Add(b, 600, 1, 6, 6, UnitRegistry.Rider);
        var ao = new SimulationObservationSource().Observe(a, 0).Observation;
        var bo = new SimulationObservationSource().Observe(b, 0).Observation;
        Assert.That(ao.Units.Length, Is.EqualTo(1)); Assert.That(bo.Units.Length, Is.EqualTo(1));
        Assert.That(LearnedActionSchema.Encode(ao, -1), Is.EqualTo(LearnedActionSchema.Encode(bo, -1)));
        Assert.That(ao.LegalActions.Select(action => action.Key), Is.EqualTo(bo.LegalActions.Select(action => action.Key)));
    }

    [Test]
    public void StartingCitiesArePublicButHiddenOwnershipNeverRefreshesMemory()
    {
        MatchState a = Board(); Add(a, 1, 0, 1, 1); a.AddCity(new SimulationCity(2, 1, a.Position(5, 5)));
        var source = new SimulationObservationSource();
        source.SetPublicStartingCities(new[] { new AICityState { Seat = 1, X = 5, Y = 5 } });
        AIObservation before = source.Observe(a, 0).Observation;
        Assert.That(before.Cities.Single().CurrentlyVisible, Is.False);
        // An unseen third party cannot refresh remembered ownership.
        var three = Board(seats: 3); Add(three, 1, 0, 1, 1); three.AddCity(new SimulationCity(2, 2, three.Position(5, 5)));
        AIObservation after = source.Observe(three, 0).Observation;
        Assert.That(after.Cities.Single().Seat, Is.EqualTo(1)); Assert.That(after.Cities.Single().CurrentlyVisible, Is.False);
        Assert.That(after.Cities.Single().Recruited, Is.False);
    }
}
