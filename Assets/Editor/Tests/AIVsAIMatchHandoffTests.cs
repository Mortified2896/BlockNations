using NUnit.Framework;

// Exercise scheduling and scene-handoff data only; these tests never play AI matches.
public class AIVsAIMatchHandoffTests
{
    private static AIVsAIBatchRunController.SimulationSettings Review()
    {
        var settings = AIVsAIBatchRunController.GetDefaultSimulationSettings();
        settings.mode = AIVsAIBatchRunController.SimulationMode.Tournament;
        settings.tournamentParticipantMask = (1 << 7) | (1 << 9) | (1 << 16);
        settings.tournamentGamesPerPairing = 1;
        settings.tournamentSeatSwap = true;
        settings.tournamentRunContinuously = false;
        return settings;
    }

    private static AIVsAIDebugSelection.Settings Fallback() => new AIVsAIDebugSelection.Settings
    {
        enabled = true,
        sideARecruitVariant = TurnManager.AIRecruitVariant.Default,
        sideBRecruitVariant = TurnManager.AIRecruitVariant.RiderFocus,
        sideAFeatures = AILocalDecisionFeatures.All,
        sideBFeatures = AILocalDecisionFeatures.OffensiveObviousWin,
        sideAProfile = TurnManager.AIDebugProfile.Baseline,
        sideBProfile = TurnManager.AIDebugProfile.Baseline,
        batchSpeedPreset = TurnManager.AIVsAIBatchSpeedPreset.UltraFast
    };

    private static void Begin(AIVsAIBatchRunController.SimulationSettings simulation,
        AIVsAIDebugSelection.Settings first)
    {
        AIVsAIBatchRunController.BeginNewRun(simulation, first.batchSpeedPreset,
            first.sideARecruitVariant, first.sideBRecruitVariant, first.sideAFeatures, first.sideBFeatures,
            first.sideAProfile, first.sideBProfile);
    }

    private static AIVsAIDebugSelection.Settings ConsumeHandoff()
    {
        Assert.That(AIVsAIBatchRunController.TryConsumePendingSimulationSettings(out _), Is.True);
        Assert.That(AIVsAIDebugSelection.TryConsume(out var settings), Is.True);
        return settings;
    }

    private static void RecordSyntheticOutcome(out bool complete)
    {
        Assert.That(AIVsAIBatchRunController.TryRecordMatch(
            new AIVsAIMatchCsvLogger.MatchResult { winner = "SideA", totalTurnCount = 1 },
            out complete, out _), Is.True);
    }

    [SetUp]
    public void SetUp() { AIVsAIBatchRunController.ClearAll(); AIVsAIDebugSelection.TryConsume(out _); }
    [TearDown]
    public void TearDown() { AIVsAIBatchRunController.ClearAll(); AIVsAIDebugSelection.TryConsume(out _); }

    [Test]
    public void EveryReviewHandoffCarriesScheduledPoliciesFeaturesAndSwappedSeats()
    {
        var simulation = Review();
        var fallback = Fallback();
        AIVsAIMatchHandoff.Queue(simulation, fallback, startingNewRun: true);
        Begin(simulation, ConsumeHandoff());
        var models = new[] { TurnManager.AIRecruitVariant.Default, TurnManager.AIRecruitVariant.RiderFocus,
            TurnManager.AIRecruitVariant.HardTactician };
        var features = new[] { AILocalDecisionFeatures.All, AILocalDecisionFeatures.OffensiveObviousWin,
            AILocalDecisionFeatures.None };
        int[,] seats = { { 0, 1 }, { 1, 0 }, { 0, 2 }, { 2, 0 }, { 1, 2 }, { 2, 1 } };
        for (int match = 0; match < 6; match++)
        {
            // Deliberately supply the first pairing: continuation must resolve the actual schedule.
            AIVsAIMatchHandoff.Queue(simulation, fallback, startingNewRun: false);
            var actual = ConsumeHandoff();
            Assert.That(actual.sideARecruitVariant, Is.EqualTo(models[seats[match, 0]]), $"match {match + 1}, seat A");
            Assert.That(actual.sideBRecruitVariant, Is.EqualTo(models[seats[match, 1]]), $"match {match + 1}, seat B");
            Assert.That(actual.sideAFeatures, Is.EqualTo(features[seats[match, 0]]));
            Assert.That(actual.sideBFeatures, Is.EqualTo(features[seats[match, 1]]));
            Assert.That(actual.batchSpeedPreset, Is.EqualTo(TurnManager.AIVsAIBatchSpeedPreset.UltraFast));
            Assert.That(AIVsAIMatchHandoff.MatchesUpcoming(actual), Is.True);
            RecordSyntheticOutcome(out bool complete);
            Assert.That(complete, Is.EqualTo(match == 5));
        }
    }

    [Test]
    public void ScheduleCheckRejectsUnswappedOrMislabeledPolicies()
    {
        var simulation = Review();
        Begin(simulation, Fallback());
        Assert.That(AIVsAIMatchHandoff.MatchesUpcoming(Fallback()), Is.True);
        RecordSyntheticOutcome(out _);
        Assert.That(AIVsAIMatchHandoff.MatchesUpcoming(Fallback()), Is.False);
    }

    [Test]
    public void UpcomingHeadToHeadMatchPreservesProfilesAlongWithSeatSwap()
    {
        var simulation = AIVsAIBatchRunController.GetDefaultSimulationSettings();
        var fallback = Fallback();
        fallback.sideAProfile = TurnManager.AIDebugProfile.TacticalPuzzle;
        Begin(simulation, fallback);
        RecordSyntheticOutcome(out _);
        AIVsAIMatchHandoff.Queue(simulation, fallback, startingNewRun: false);
        var actual = ConsumeHandoff();
        Assert.That(actual.sideAProfile, Is.EqualTo(TurnManager.AIDebugProfile.Baseline));
        Assert.That(actual.sideBProfile, Is.EqualTo(TurnManager.AIDebugProfile.TacticalPuzzle));
        Assert.That(AIVsAIMatchHandoff.MatchesUpcoming(actual), Is.True);
    }

    [Test]
    public void UnplayedOpponentDoesNotAppearAsZeroPercentInRankedPreview()
    {
        Begin(Review(), Fallback());
        RecordSyntheticOutcome(out _);
        Assert.That(AIVsAIBatchRunController.TryGetHudSnapshot(out var snapshot), Is.True);
        Assert.That(snapshot.tournamentStandingsPreview, Does.Not.Contain("Hard Tactician"));
        Assert.That(AIVsAIBatchRunController.TryGetTournamentStandingsSnapshot(out var standings), Is.True);
        var hard = standings.Find(standing => standing.label.StartsWith("Hard Tactician"));
        Assert.That(hard.games, Is.Zero);
        Assert.That(hard.isRanked, Is.False);
    }

    [TestCase(99, 0, false)]
    [TestCase(99, 1, false)]
    [TestCase(100, 0, false)]
    [TestCase(100, 1, true)]
    public void RoundLimitGivesBothSeatsTheirHundredthTurn(int round, int completedSeat, bool stop)
    {
        Assert.That(AIVsAIBatchRunController.MatchRoundLimit, Is.EqualTo(100));
        Assert.That(AIVsAIBatchRunController.HasReachedMatchRoundLimit(round, completedSeat, 2), Is.EqualTo(stop));
    }

    [Test]
    public void AbortedRiderHardGameDoesNotAwardHalfPointsOrAlterTheirLossRates()
    {
        Begin(Review(), Fallback());
        for (int match = 0; match < 4; match++)
            Assert.That(AIVsAIBatchRunController.TryRecordMatch(new AIVsAIMatchCsvLogger.MatchResult {
                winner = match % 2 == 0 ? "SideA" : "SideB", totalTurnCount = 20 }, out _, out _), Is.True);
        Assert.That(AIVsAIBatchRunController.TryRecordMatch(new AIVsAIMatchCsvLogger.MatchResult {
            winner = "Abort", totalTurnCount = 100 }, out _, out _), Is.True);
        Assert.That(AIVsAIBatchRunController.TryGetTournamentStandingsSnapshot(out var standings), Is.True);
        var normal = standings.Find(standing => standing.label.StartsWith("Baseline"));
        var hard = standings.Find(standing => standing.label.StartsWith("Hard Tactician"));
        Assert.That(normal.wins, Is.EqualTo(4));
        Assert.That(normal.scoreRate, Is.EqualTo(1));
        Assert.That(hard.losses, Is.EqualTo(2));
        Assert.That(hard.aborts, Is.EqualTo(1));
        Assert.That(hard.draws, Is.Zero);
        Assert.That(hard.scoreRate, Is.Zero);
    }

    [Test]
    public void AbortOnlyOpponentIsUnrankedButTrueDrawStillScoresHalfPoint()
    {
        Begin(Review(), Fallback());
        Assert.That(AIVsAIBatchRunController.TryRecordMatch(new AIVsAIMatchCsvLogger.MatchResult {
            winner = "Abort", totalTurnCount = 100 }, out _, out _), Is.True);
        Assert.That(AIVsAIBatchRunController.TryGetTournamentStandingsSnapshot(out var standings), Is.True);
        var rider = standings.Find(standing => standing.label.StartsWith("Rider Focus"));
        Assert.That(rider.aborts, Is.EqualTo(1));
        Assert.That(rider.isRanked, Is.False);
        Assert.That(rider.scoreRate, Is.Zero);
        Assert.That(AIVsAIBatchRunController.TryRecordMatch(new AIVsAIMatchCsvLogger.MatchResult {
            winner = "Draw", totalTurnCount = 30 }, out _, out _), Is.True);
        Assert.That(AIVsAIBatchRunController.TryGetTournamentStandingsSnapshot(out standings), Is.True);
        rider = standings.Find(standing => standing.label.StartsWith("Rider Focus"));
        Assert.That(rider.draws, Is.EqualTo(1));
        Assert.That(rider.scoreRate, Is.EqualTo(0.5));
    }
}
