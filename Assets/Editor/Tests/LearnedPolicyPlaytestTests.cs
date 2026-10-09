using System;
using System.Linq;
using BlockNations.AI;
using BlockNations.Simulation;
using NUnit.Framework;
using UnityEngine;

public sealed class LearnedPolicyPlaytestTests
{
    private static MatchState Board(int seat, int id, bool changePrivateState)
    {
        var state = new MatchState(7, 7, 2, UnitRegistry.AllDefinitions, seat, firstSeat: seat);
        int own = seat == 0 ? 1 : 5, enemy = seat == 0 ? 5 : 1, near = seat == 0 ? 2 : 4;
        state.AddCity(new SimulationCity(id + 1, seat, state.Position(own, own)));
        state.AddCity(new SimulationCity(id + 2, 1 - seat, state.Position(enemy, enemy), recruited: changePrivateState));
        state.AddUnit(new SimulationUnit(id + 3, seat, state.Position(own, own), UnitRegistry.Rider));
        state.AddUnit(new SimulationUnit(id + 4, 1 - seat, state.Position(near, own), UnitRegistry.Warrior,
            movesUsed: changePrivateState ? 1 : 0, attacksUsed: changePrivateState ? 1 : 0));
        state.AddUnit(new SimulationUnit(id + 5, 1 - seat, state.Position(enemy, enemy),
            changePrivateState ? UnitRegistry.Rider : UnitRegistry.Warrior));
        if (changePrivateState) state.AddUnit(new SimulationUnit(id + 6, 1 - seat, state.Position(enemy, near), UnitRegistry.Archer));
        state.SetGold(seat, 3); state.SetGold(1 - seat, changePrivateState ? 999 : 0);
        return state;
    }

    private static AIObservation Observe(MatchState state, int seat)
    {
        var source = new SimulationObservationSource();
        source.SetPublicStartingCities(state.Cities.Select(city => new AICityState {
            Seat = city.Seat, X = city.Position % 7, Y = city.Position / 7 }).ToArray());
        return source.Observe(state, seat).Observation;
    }

    [TestCase(0)] [TestCase(1)]
    public void BothDecisionStagesIgnorePrivateEnemyStateAndStableIds(int seat)
    {
        AIObservation before = Observe(Board(seat, 0, false), seat), after = Observe(Board(seat, 100, true), seat);
        Assert.That(before.Units.Length, Is.EqualTo(2)); Assert.That(after.Units.Length, Is.EqualTo(2));
        Assert.That(before.Gold, Is.EqualTo(3)); Assert.That(after.Gold, Is.EqualTo(3));
        Assert.That(before.LegalActions.Select(action => action.Key), Is.EqualTo(after.LegalActions.Select(action => action.Key)));
        int[] sources = LearnedActionSchema.Choices(before, -1).Keys.Where(i => i < LearnedActionSchema.Positions).Concat(new[] { -1 }).ToArray();
        foreach (int source in sources)
        {
            Assert.That(LearnedActionSchema.Encode(before, source), Is.EqualTo(LearnedActionSchema.Encode(after, source)));
            Assert.That(LearnedActionSchema.Choices(before, source).Keys, Is.EquivalentTo(LearnedActionSchema.Choices(after, source).Keys));
        }
    }

    [TestCase(0)] [TestCase(1)]
    public void HiddenBlockerLeavesPolicyInputsUnchangedUntilAttemptedMove(int seat)
    {
        var state = new MatchState(7, 7, 2, UnitRegistry.AllDefinitions, seat, firstSeat: seat);
        int origin = seat == 0 ? 1 : 5;
        state.AddUnit(new SimulationUnit(1, seat, state.Position(origin, 1), UnitRegistry.Rider));
        AIObservation clear = Observe(state, seat);
        state.AddUnit(new SimulationUnit(2, 1 - seat, state.Position(3, 1), UnitRegistry.Warrior));
        AIObservation blocked = Observe(state, seat);
        Assert.That(blocked.Visible[state.Position(3, 1)], Is.False);
        foreach (int source in LearnedActionSchema.Choices(clear, -1).Keys.Where(i => i < LearnedActionSchema.Positions).Concat(new[] { -1 }))
        {
            Assert.That(LearnedActionSchema.Encode(clear, source), Is.EqualTo(LearnedActionSchema.Encode(blocked, source)));
            Assert.That(LearnedActionSchema.Choices(clear, source).Keys, Is.EquivalentTo(LearnedActionSchema.Choices(blocked, source).Keys));
        }
        var move = new MatchCommand(MatchActionKind.Move, seat, 1, state.Position(3, 1));
        Assert.That(MatchEngine.LegalActions(state, seat).Any(action => action.Command.Equals(move)), Is.True);
        MatchTransition result = MatchEngine.Apply(state, move);
        Assert.That(result.Applied && result.HiddenBlocker, Is.True); Assert.That(result.MovedSteps, Is.EqualTo(1));
    }

    [TestCase(LearnedDifficulty.Easy)] [TestCase(LearnedDifficulty.Medium)] [TestCase(LearnedDifficulty.Hard)]
    public void SamplingVariesTiesButNeverSelectsMaskedChoices(LearnedDifficulty difficulty)
    {
        float[] probabilities = new float[259]; probabilities[3] = 100; probabilities[1] = probabilities[2] = 0.5f;
        var random = new System.Random(42);
        int[] selected = Enumerable.Range(0, 200).Select(_ => LearnedActionSampling.Choose(probabilities, new[] { 1, 2 }, difficulty, random)).ToArray();
        Assert.That(selected.Distinct(), Is.EquivalentTo(new[] { 1, 2 }));
        var repeat = new System.Random(42);
        Assert.That(Enumerable.Range(0, 200).Select(_ => LearnedActionSampling.Choose(probabilities, new[] { 2, 1 }, difficulty, repeat)), Is.EqualTo(selected));
    }

    [Test]
    public void PresetsIncreasePreferenceForFavouredActionsWithoutChangingWeights()
    {
        var probabilities = new float[259]; probabilities[1] = 0.6f; probabilities[2] = 0.4f;
        int Favourites(LearnedDifficulty difficulty)
        {
            var random = new System.Random(42);
            return Enumerable.Range(0, 2000).Count(_ => LearnedActionSampling.Choose(probabilities, new[] { 1, 2 }, difficulty, random) == 1);
        }
        int easy = Favourites(LearnedDifficulty.Easy), medium = Favourites(LearnedDifficulty.Medium), hard = Favourites(LearnedDifficulty.Hard);
        Assert.That(easy, Is.LessThan(medium)); Assert.That(medium, Is.LessThan(hard)); Assert.That(hard, Is.EqualTo(2000));
        Assert.Throws<InvalidOperationException>(() => LearnedActionSampling.Choose(new float[259], new[] { 1 }, LearnedDifficulty.Hard, new System.Random(42)));
    }

    [Test]
    public void StandardBoardChangesWithoutReinterpretingLegacySavePresets()
    {
        Assert.That(TurnManager.GetDefaultMapSizePreset(), Is.EqualTo(TurnManager.MapSizePreset.Standard7));
        TurnManager.GetBoardDimensionsForPreset(TurnManager.GetDefaultMapSizePreset(), out int width, out int height);
        Assert.That(width, Is.EqualTo(7)); Assert.That(height, Is.EqualTo(7));
        foreach (var pair in new[] { (TurnManager.MapSizePreset.Small, 11), (TurnManager.MapSizePreset.Large, 15) })
        {
            TurnManager.GetBoardDimensionsForPreset(pair.Item1, out width, out height);
            Assert.That(width, Is.EqualTo(pair.Item2)); Assert.That(height, Is.EqualTo(pair.Item2));
            Assert.That(TurnManager.ParseMapSizePresetOrDefault(pair.Item1.ToString()), Is.EqualTo(pair.Item1));
        }
        Assert.That(TurnManager.ParseMapSizePresetOrDefault(null), Is.EqualTo(TurnManager.MapSizePreset.Small));
        Assert.That(TurnManager.ParseMapSizePresetOrDefault("Standard7"), Is.EqualTo(TurnManager.MapSizePreset.Standard7));
    }

    [Test]
    public void ReleaseHasExplicitModelsAndRejectsAnUntrainedBoard()
    {
        LearnedPolicyRelease release = Resources.Load<LearnedPolicyRelease>(LearnedPolicyRelease.ResourcePath);
        Assert.That(release, Is.Not.Null);
        Assert.That(release.Validate(7).schema, Is.EqualTo(2));
        foreach (LearnedDifficulty difficulty in Enum.GetValues(typeof(LearnedDifficulty))) Assert.That(release.ModelFor(difficulty), Is.Not.Null);
        Assert.Throws<InvalidOperationException>(() => release.Validate(11));
    }
}
