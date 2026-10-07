using System;
using System.IO;
using System.Linq;
using BlockNations.AI;
using NUnit.Framework;
using UnityEngine;

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

    [Test]
    public void CrowdedHomeArmyAdvancesTowardUnexploredTilesInsteadOfEndingTurn()
    {
        // This observation contains only the fair information available in the failed watched review.
        string path = Path.Combine(Application.dataPath, "Editor/Tests/Fixtures/HardAI_CrowdedHome.json");
        AIObservation board = JsonUtility.FromJson<AIObservation>(File.ReadAllText(path));
        IAIDecision search = Search(board, 13);
        AIAction first = search.Best.Actions[0];
        Assert.That(first.Kind, Is.EqualTo(AIActionKind.Move));
        Assert.That(board.LegalActions.Contains(first), Is.True);
        AIUnitState unit = board.Units[first.Actor];
        int[] frontier = Enumerable.Range(0, board.Tiles.Length).Where(p => board.Tiles[p] && !board.Seen[p]).ToArray();
        int before = frontier.Min(p => AIActionRules.Distance(unit.X, unit.Y, p % board.Width, p / board.Width));
        int after = frontier.Min(p => AIActionRules.Distance(first.Destination % board.Width, first.Destination / board.Width,
            p % board.Width, p / board.Width));
        Assert.That(after, Is.LessThan(before), "The chosen root action should make actual exploration progress.");
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
    public void CrowdedPositionKeepsTheSamePlanAcrossFrameChunkSizes()
    {
        string path = Path.Combine(Application.dataPath, "Editor/Tests/Fixtures/HardAI_CrowdedHome.json");
        AIObservation board = JsonUtility.FromJson<AIObservation>(File.ReadAllText(path));
        IAIDecision single = Search(board, 1), chunks = Search(board, 47);
        Assert.That(chunks.Best.Actions.Select(a => a.Key), Is.EqualTo(single.Best.Actions.Select(a => a.Key)));
        Assert.That(chunks.WorkCompleted, Is.EqualTo(single.WorkCompleted));
        Assert.That(chunks.WorkCompleted, Is.LessThanOrEqualTo(HardTacticianPolicy.DecisionWorkBudget));
    }

    [Test]
    public void ExploredMapWithoutCityMemoryStillSearchesUnseenAreas()
    {
        AIObservation board = Board(0, Warrior(0, 1, 1));
        board.Seen = Enumerable.Repeat(true, 121).ToArray();
        for (int y = 0; y <= 2; y++) for (int x = 0; x <= 2; x++) board.Visible[y * 11 + x] = true;
        board.Cities = new[] { new AICityState { Seat = 0, X = 1, Y = 1, CurrentlyVisible = true } };
        board.LegalActions = new AITacticalState(board).Actions().ToArray();
        AIAction first = Search(board, 17).Best.Actions[0];
        Assert.That(first.Kind, Is.EqualTo(AIActionKind.Move));
        Assert.That(board.LegalActions.Contains(first), Is.True);
    }

    [Test]
    public void FrontierProgressFollowsConnectedTerrainRatherThanGeometricDistance()
    {
        AIObservation board = Board(0, Warrior(0, 2, 2));
        board.Seen = Enumerable.Repeat(true, 121).ToArray();
        board.Seen[2 * 11 + 6] = false;
        // The wall forces a step toward its opening at the north edge.
        for (int y = 1; y < 11; y++) board.Tiles[y * 11 + 3] = false;
        board.Cities = new[] { new AICityState { Seat = 0, X = 2, Y = 2, CurrentlyVisible = true } };
        board.LegalActions = new AITacticalState(board).Actions().ToArray();
        AIAction first = Search(board, 7).Best.Actions[0];
        Assert.That(first.Kind, Is.EqualTo(AIActionKind.Move));
        Assert.That(first.Destination / 11, Is.EqualTo(1));
    }

    [Test]
    public void CrowdedArmyStillFindsACombinedAttackOnTheGuardedCity()
    {
        AIUnitState a = Warrior(0, 4, 5), b = Warrior(0, 5, 4);
        a.Type = b.Type = "rider"; a.Attack = b.Attack = 5; a.MaxMoves = b.MaxMoves = 2;
        AIObservation board = Board(0, a, b, Warrior(1, 5, 5),
            Warrior(0, 0, 0), Warrior(0, 1, 0), Warrior(0, 2, 0), Warrior(0, 3, 0),
            Warrior(0, 0, 1), Warrior(0, 1, 1), Warrior(0, 2, 1), Warrior(0, 3, 1));
        board.Cities = new[] { new AICityState { Seat = 1, X = 5, Y = 5, CurrentlyVisible = true } };
        board.LegalActions = new AITacticalState(board).Actions().ToArray();
        IAIDecision search = Search(board, 11);
        Assert.That(search.Best.Evaluation.Capture, Is.EqualTo(1000000));
        Assert.That(search.Best.Actions.Count(a => a.Kind == AIActionKind.Attack), Is.EqualTo(2));
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

    [Test]
    public void OneEnemyAttackCannotCountAsKillingThreeFriendlyUnitsAtOnce()
    {
        AIObservation board = Board(0, Warrior(0, 4, 4), Warrior(0, 4, 5), Warrior(0, 5, 4), Warrior(1, 5, 5));
        int safety = new AITacticalState(board).Evaluate().Safety;
        Assert.That(safety, Is.LessThan(0));
        Assert.That(safety, Is.GreaterThan(-1500), "One enemy with one attack should consume only one target's value.");
    }

    [Test]
    public void TwoObservedRidersAreACapitalCaptureThreatEvenWhenNeitherCanKillAlone()
    {
        AIUnitState riderA = Warrior(1, 4, 5), riderB = Warrior(1, 5, 4);
        riderA.Type = riderB.Type = "rider"; riderA.Attack = riderB.Attack = 5;
        AIObservation board = Board(0, Warrior(0, 5, 5), riderA, riderB);
        board.Cities = new[] { new AICityState { Seat = 0, X = 5, Y = 5, CurrentlyVisible = true } };
        Assert.That(new AITacticalState(board).Evaluate().Safety, Is.LessThan(-40000));
    }
}
