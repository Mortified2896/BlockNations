using System;
using System.Linq;
using BlockNations.AI;
using NUnit.Framework;

public class HardTacticianPolicyTests
{
    private static AIUnitState Warrior(int seat, int x, int y) => new AIUnitState {
        Seat = seat, X = x, Y = y, Type = "warrior", Health = 10, MaxHealth = 10, Attack = 10,
        Range = 1, Vision = 1, MaxMoves = 1, MaxAttacks = 1, AttackAfterMoving = true, Cost = 2 };

    private static AIObservation Board(int seat, params AIUnitState[] units)
    {
        AIObservation board = new AIObservation { Width = 11, Height = 11, Seat = seat, CityVision = 1,
            Tiles = Enumerable.Repeat(true, 121).ToArray(), Seen = new bool[121], Visible = new bool[121],
            Units = units, Cities = Array.Empty<AICityState>(), RecruitTypes = Array.Empty<AIUnitState>(),
            HostileSeats = new[] { seat == 0 ? 1 : 0 } };
        board.LegalActions = new AITacticalState(board).Actions().ToArray();
        return board;
    }

    private static IAIDecision Search(AIObservation observation, int chunk)
    {
        IAIDecision search = new HardTacticianPolicy().BeginDecision(observation, 2048);
        while (!search.Complete)
            for (int i = 0; i < chunk && !search.Complete; i++) search.AdvanceOnce();
        return search;
    }

    [TestCase(0)]
    [TestCase(1)]
    public void CapturesVisibleAdjacentCityForEitherSeat(int seat)
    {
        AIObservation board = Board(seat, Warrior(seat, 4, 5));
        board.Cities = new[] { new AICityState { Seat = 1 - seat, X = 5, Y = 5, CurrentlyVisible = true } };
        board.LegalActions = new AITacticalState(board).Actions().ToArray();
        IAIDecision search = Search(board, 1);
        Assert.That(search.Best.Actions[0].Kind, Is.EqualTo(AIActionKind.Move));
        Assert.That(search.Best.Actions[0].Destination, Is.EqualTo(60));
        Assert.That(search.Best.Evaluation.Capture, Is.EqualTo(1000000));
    }

    [Test]
    public void CoordinatedRiderAttacksFindCapture()
    {
        AIUnitState a = Warrior(1, 4, 5), b = Warrior(1, 5, 4), defender = Warrior(0, 5, 5);
        a.Type = b.Type = "rider"; a.Attack = b.Attack = 5; a.MaxMoves = b.MaxMoves = 2;
        AIObservation board = Board(1, a, b, defender);
        board.Cities = new[] { new AICityState { Seat = 0, X = 5, Y = 5, CurrentlyVisible = true } };
        board.LegalActions = new AITacticalState(board).Actions().ToArray();
        IAIDecision search = Search(board, 7);
        Assert.That(search.Best.Evaluation.Capture, Is.EqualTo(1000000));
        Assert.That(search.Best.Actions.Count(a => a.Kind == AIActionKind.Attack), Is.EqualTo(2));
    }

    [Test]
    public void FrameChunkingDoesNotChangeSearchResultOrWork()
    {
        AIObservation board = Board(1, Warrior(1, 3, 3), Warrior(1, 4, 3), Warrior(0, 5, 4));
        board.Cities = new[] { new AICityState { Seat = 1, X = 2, Y = 2, CurrentlyVisible = true } };
        board.LegalActions = new AITacticalState(board).Actions().ToArray();
        IAIDecision single = Search(board, 1), chunks = Search(board, 43);
        Assert.That(chunks.Best.Actions.Select(a => a.Key), Is.EqualTo(single.Best.Actions.Select(a => a.Key)));
        Assert.That(chunks.Best.Score, Is.EqualTo(single.Best.Score));
        Assert.That(chunks.WorkCompleted, Is.EqualTo(single.WorkCompleted));
    }

    [Test]
    public void RootMaskIsAuthoritativeEvenWhenSimulatorHasMoreOptions()
    {
        AIObservation board = Board(1, Warrior(1, 4, 5));
        board.Cities = new[] { new AICityState { Seat = 0, X = 5, Y = 5, CurrentlyVisible = true } };
        board.LegalActions = new[] { new AIAction { Kind = AIActionKind.EndTurn, Actor = -1, Target = -1 } };
        Assert.That(Search(board, 5).Best.Actions[0].Kind, Is.EqualTo(AIActionKind.EndTurn));
    }

    [Test]
    public void RiderMoveIsCommittedAndArcherCannotAttackAfterMoving()
    {
        AIUnitState rider = Warrior(1, 4, 4); rider.Type = "rider"; rider.MaxMoves = 2;
        AIObservation board = Board(1, rider, Warrior(0, 6, 5));
        AITacticalState moved = new AITacticalState(board).After(new AIAction { Kind = AIActionKind.Move, Actor = 0, Destination = 49, MoveCost = 1 });
        Assert.That(moved.Actions().Any(a => a.Actor == 0 && a.Kind == AIActionKind.Move), Is.False);
        AIUnitState archer = Warrior(1, 4, 4); archer.Type = "archer"; archer.Range = 2; archer.AttackAfterMoving = false;
        board = Board(1, archer, Warrior(0, 6, 5));
        moved = new AITacticalState(board).After(new AIAction { Kind = AIActionKind.Move, Actor = 0, Destination = 49, MoveCost = 1 });
        Assert.That(moved.Actions().Any(a => a.Actor == 0 && a.Kind == AIActionKind.Attack), Is.False);
    }

    [Test]
    public void ArcherKillDoesNotAdvanceIntoCity()
    {
        AIUnitState archer = Warrior(1, 4, 5); archer.Range = 2; archer.Type = "archer";
        AIObservation board = Board(1, archer, Warrior(0, 5, 5));
        board.Cities = new[] { new AICityState { Seat = 0, X = 5, Y = 5, CurrentlyVisible = true } };
        AITacticalState next = new AITacticalState(board).After(new AIAction { Kind = AIActionKind.Attack, Actor = 0, Target = 1, Destination = 60 });
        Assert.That(next.CapturedCity, Is.False);
        Assert.That(next.Units[0].X, Is.EqualTo(4));
        Assert.That(next.Units[1].Health, Is.Zero);
    }

    [Test]
    public void SearchDoesNotMutateObservationAndMemoryIsNotProvenWin()
    {
        AIObservation board = Board(1, Warrior(1, 4, 5));
        board.Cities = new[] { new AICityState { Seat = 0, X = 5, Y = 5, CurrentlyVisible = false } };
        board.LegalActions = new AITacticalState(board).Actions().ToArray();
        Search(board, 9);
        Assert.That(board.Units[0].X, Is.EqualTo(4));
        Assert.That(board.Cities[0].Seat, Is.Zero);
        Assert.That(board.Seen.Any(v => v), Is.False);
        AITacticalState next = new AITacticalState(board).After(new AIAction { Kind = AIActionKind.Move, Actor = 0, Destination = 60, MoveCost = 1 });
        Assert.That(next.CapturedCity, Is.False);
    }

    [Test]
    public void AlliedSeatIsNotAnAttackTarget()
    {
        AIObservation board = Board(0, Warrior(0, 4, 5), Warrior(2, 5, 5));
        Assert.That(new AITacticalState(board).Actions().Any(a => a.Kind == AIActionKind.Attack), Is.False);
    }

    [Test]
    public void DefenderStaysWhenVacatingWouldAllowCityCapture()
    {
        AIObservation board = Board(1, Warrior(1, 2, 2), Warrior(0, 4, 2));
        board.Cities = new[] { new AICityState { Seat = 1, X = 2, Y = 2, CurrentlyVisible = true } };
        AITacticalState state = new AITacticalState(board);
        AITacticalState exposed = state.After(new AIAction { Kind = AIActionKind.Move, Actor = 0, Destination = 12, MoveCost = 1 });
        // Enemy cannot reach the city in one move here; extend to a fast scout to test capture threat.
        AIUnitState enemy = board.Units[1]; enemy.MaxMoves = 2; enemy.Attack = 0; enemy.MaxAttacks = 0;
        board.Units[1] = enemy;
        state = new AITacticalState(board);
        exposed = state.After(new AIAction { Kind = AIActionKind.Move, Actor = 0, Destination = 12, MoveCost = 1 });
        Assert.That(exposed.Evaluate().Safety, Is.LessThan(state.Evaluate().Safety - 10000));
    }
}
