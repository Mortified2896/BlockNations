using System;
using System.Linq;
using BlockNations.AI;
using NUnit.Framework;

public sealed class LearnedActionSchemaTests
{
    private static AIObservation Position(int seat)
    {
        var own = new AIUnitState { Seat = seat, X = 3, Y = 4, Type = "custom", Health = 6, MaxHealth = 10,
            Attack = 8, Defense = 2, Range = 2, Vision = 3, MaxMoves = 2, MaxAttacks = 1, Cost = 5, AttackAfterMoving = true };
        var enemy = own; enemy.Seat = 1 - seat; enemy.X = 5;
        return new AIObservation { Width = 11, Height = 11, Seat = seat, Gold = 7, Round = 4,
            Tiles = Enumerable.Repeat(true, 121).ToArray(), Seen = new bool[121], Visible = new bool[121],
            Units = new[] { own, enemy }, Cities = new[] { new AICityState { Seat = seat, X = 1, Y = 1, CurrentlyVisible = true } },
            RecruitTypes = new[] { own }, HostileSeats = new[] { 1 - seat },
            LegalActions = new[] {
                new AIAction { Kind = AIActionKind.Move, Actor = 0, Target = -1, Destination = 4 * 11 + 4 },
                new AIAction { Kind = AIActionKind.Attack, Actor = 0, Target = 1, Destination = 4 * 11 + 5 },
                new AIAction { Kind = AIActionKind.Recruit, Actor = 0, Target = -1, RecruitType = 0, Destination = 12 },
                new AIAction { Kind = AIActionKind.EndTurn, Actor = -1, Target = -1 } } };
    }

    [TestCase(0)] [TestCase(1)]
    public void EveryLegalActionSurvivesSourceAndActionSelection(int seat)
    {
        AIObservation state = Position(seat);
        var sources = LearnedActionSchema.Choices(state, -1);
        Assert.That(sources.Count, Is.EqualTo(3));
        var reconstructed = sources.Keys.Where(source => source != LearnedActionSchema.EndTurn)
            .SelectMany(source => LearnedActionSchema.Choices(state, source).Values)
            .Where(action => action.Kind != AIActionKind.EndTurn).ToArray();
        Assert.That(reconstructed, Is.EquivalentTo(state.LegalActions.Where(action => action.Kind != AIActionKind.EndTurn)));
        Assert.That(LearnedActionSchema.Encode(state, -1).Length, Is.EqualTo(LearnedActionSchema.ObservationSize));
    }

    [Test]
    public void MirroredSeatPerspectivesHaveIdenticalPolicyInputsAndMasks()
    {
        AIObservation zero = Position(0), one = Position(1);
        for (int i = 0; i < one.Units.Length; i++) { AIUnitState unit = one.Units[i]; unit.X = 10 - unit.X; unit.Y = 10 - unit.Y; one.Units[i] = unit; }
        for (int i = 0; i < one.Cities.Length; i++) { AICityState city = one.Cities[i]; city.X = 10 - city.X; city.Y = 10 - city.Y; one.Cities[i] = city; }
        for (int i = 0; i < one.LegalActions.Length; i++) { AIAction action = one.LegalActions[i]; action.Destination = 120 - action.Destination; one.LegalActions[i] = action; }
        Assert.That(LearnedActionSchema.Encode(one, -1), Is.EqualTo(LearnedActionSchema.Encode(zero, -1)));
        Assert.That(LearnedActionSchema.Choices(one, -1).Keys, Is.EquivalentTo(LearnedActionSchema.Choices(zero, -1).Keys));
    }

    [Test]
    public void TypeNamesDoNotBecomeStrategicInputs()
    {
        AIObservation state = Position(0);
        float[] before = LearnedActionSchema.Encode(state, -1);
        for (int i = 0; i < state.Units.Length; i++) { AIUnitState unit = state.Units[i]; unit.Type = "invented new name"; state.Units[i] = unit; }
        AIUnitState type = state.RecruitTypes[0]; type.Type = "different name"; state.RecruitTypes[0] = type;
        Assert.That(LearnedActionSchema.Encode(state, -1), Is.EqualTo(before));
        type.Range++; state.RecruitTypes[0] = type;
        Assert.That(LearnedActionSchema.Encode(state, -1), Is.Not.EqualTo(before), "Capabilities must be represented.");
    }

    [Test]
    public void UnknownTransientRecruitmentIsNotAnInput()
    {
        AIObservation state = Position(0);
        state.Cities = new[] { new AICityState { Seat = 1, X = 8, Y = 8, CurrentlyVisible = false } };
        float[] before = LearnedActionSchema.Encode(state, -1);
        AICityState city = state.Cities[0]; city.Recruited = true; state.Cities[0] = city;
        Assert.That(LearnedActionSchema.Encode(state, -1), Is.EqualTo(before));
    }

    [TestCase(5)] [TestCase(7)] [TestCase(9)] [TestCase(11)]
    public void SmallerBoardsKeepNativeActionsAndMaskCanvasPadding(int size)
    {
        AIObservation state = Position(0);
        state.Width = state.Height = size;
        state.Tiles = Enumerable.Repeat(true, size * size).ToArray();
        state.Seen = state.Visible = new bool[size * size];
        AIUnitState own = state.Units[0]; own.X = 1; own.Y = 2;
        AIUnitState enemy = state.Units[1]; enemy.X = 3; enemy.Y = 2;
        state.Units = new[] { own, enemy };
        state.LegalActions = new[] {
            new AIAction { Kind = AIActionKind.Move, Actor = 0, Target = -1, Destination = 2 * size + 2 },
            new AIAction { Kind = AIActionKind.Attack, Actor = 0, Target = 1, Destination = 2 * size + 3 },
            new AIAction { Kind = AIActionKind.Recruit, Actor = 0, Target = -1, RecruitType = 0, Destination = size + 1 },
            new AIAction { Kind = AIActionKind.EndTurn, Actor = -1, Target = -1 } };
        float[] encoded = LearnedActionSchema.Encode(state, -1);
        Assert.That(encoded.Length, Is.EqualTo(3120));
        Assert.That(Enumerable.Range(0, 121).Sum(i => encoded[i * LearnedActionSchema.TileChannels]), Is.EqualTo(size * size));
        var sources = LearnedActionSchema.Choices(state, -1);
        var actual = sources.Keys.Where(i => i != LearnedActionSchema.EndTurn)
            .SelectMany(i => LearnedActionSchema.Choices(state, i).Values)
            .Where(a => a.Kind != AIActionKind.EndTurn).ToArray();
        Assert.That(actual, Is.EquivalentTo(state.LegalActions.Where(a => a.Kind != AIActionKind.EndTurn)),
            "Action keys must still identify authoritative native-board actions.");
        if (size < 11) Assert.That(sources.ContainsKey(0), Is.False, "Padding must not offer an action.");
        state.Seat = 1;
        for (int i = 0; i < state.Units.Length; i++)
        {
            AIUnitState unit = state.Units[i]; unit.Seat = 1 - unit.Seat;
            unit.X = size - 1 - unit.X; unit.Y = size - 1 - unit.Y; state.Units[i] = unit;
        }
        for (int i = 0; i < state.Cities.Length; i++)
        {
            AICityState city = state.Cities[i]; city.Seat = 1;
            city.X = size - 1 - city.X; city.Y = size - 1 - city.Y; state.Cities[i] = city;
        }
        for (int i = 0; i < state.LegalActions.Length; i++)
        {
            AIAction action = state.LegalActions[i]; action.Destination = size * size - 1 - action.Destination;
            state.LegalActions[i] = action;
        }
        Assert.That(LearnedActionSchema.Encode(state, -1), Is.EqualTo(encoded), "Seat mirroring must work on every board size.");
        Assert.That(LearnedActionSchema.Choices(state, -1).Keys, Is.EquivalentTo(sources.Keys));
    }

    [Test]
    public void CapacityAndBoardMismatchFailInsteadOfDroppingChoices()
    {
        AIObservation state = Position(0); state.Width = 15;
        Assert.Throws<ArgumentException>(() => LearnedActionSchema.Encode(state, -1));
        state.Width = 11; state.RecruitTypes = new AIUnitState[17];
        Assert.Throws<ArgumentException>(() => LearnedActionSchema.Choices(state, -1));
    }

    [Test]
    public void AnEmptyArmyStillHasEndTurn()
    {
        AIObservation state = Position(0); state.Units = Array.Empty<AIUnitState>(); state.Cities = Array.Empty<AICityState>();
        state.LegalActions = new[] { new AIAction { Kind = AIActionKind.EndTurn, Actor = -1 } };
        Assert.That(LearnedActionSchema.Choices(state, -1).Keys, Is.EqualTo(new[] { LearnedActionSchema.EndTurn }));
    }
}
