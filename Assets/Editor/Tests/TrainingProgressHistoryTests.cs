using NUnit.Framework;
using UnityEngine;

public sealed class TrainingProgressHistoryTests
{
    [Test]
    public void CurriculumOutcomesDoNotInflateFullBoardTrend()
    {
        var history = new TrainingProgressHistory { boardSize = 5 };
        for (int i = 0; i < 100; i++) history.RecordMatch(false, false);
        history.RecordMatch(true, false); history.RecordMatch(true, true);
        Assert.That(history.fullBoardMatches, Is.EqualTo(2));
        Assert.That(history.CapturePercent, Is.EqualTo(50));
        Assert.That(history.curriculumMatches, Is.EqualTo(100));
        Assert.That(history.IsValid(), Is.True);
    }

    [Test]
    public void BoundedHistoryResumesWithoutLosingRollingWindow()
    {
        var history = new TrainingProgressHistory { boardSize = 5 };
        for (int i = 0; i < 240; i++) { history.decisions += 10; history.RecordMatch(true, i >= 200); }
        Assert.That(history.points.Count, Is.EqualTo(200));
        Assert.That(history.recentResults.Count, Is.EqualTo(40));
        Assert.That(history.CapturePercent, Is.Zero);
        var resumed = JsonUtility.FromJson<TrainingProgressHistory>(JsonUtility.ToJson(history));
        resumed.decisions += 10; resumed.RecordMatch(true, false);
        Assert.That(resumed.CapturePercent, Is.EqualTo(2.5f));
        Assert.That(resumed.IsValid(), Is.True);
        resumed.informationContract = "another observation contract";
        Assert.That(resumed.IsValid(), Is.False);
    }
    [Test]
    public void SeatSplitExcludesUnknownHistoricalWinnersAndCurriculum()
    {
        var history = new TrainingProgressHistory { boardSize = 5 };
        history.RecordMatch(true, false); // Old record: winning seat unknown.
        history.RecordMatch(false, false, 0);
        history.RecordMatch(true, false, 0);
        history.RecordMatch(true, false, 1);
        history.RecordMatch(true, true, -1);
        var restored = JsonUtility.FromJson<TrainingProgressHistory>(JsonUtility.ToJson(history));
        Assert.That(restored.fullBoardMatches, Is.EqualTo(4));
        Assert.That(restored.seatTrackedMatches, Is.EqualTo(3));
        Assert.That(restored.blueWins, Is.EqualTo(1));
        Assert.That(restored.redWins, Is.EqualTo(1));
        Assert.That(restored.seatTrackedLimits, Is.EqualTo(1));
        Assert.That(restored.IsValid(), Is.True);
    }

    [TestCase(0, 0, 1, 0)] [TestCase(1, 1, 1, 0)]
    [TestCase(0, 1, 0, 1)] [TestCase(1, 0, 0, 1)]
    public void TurnOrderUsesTheOpeningSeatRatherThanWinningColour(int first, int winner, int firstWins, int secondWins)
    {
        var history = new TrainingProgressHistory { boardSize = 6 };
        history.RecordMatch(true, false, winner, first);
        Assert.That(history.firstPlayerWins, Is.EqualTo(firstWins));
        Assert.That(history.secondPlayerWins, Is.EqualTo(secondWins));
        Assert.That(history.turnOrderTrackedMatches, Is.EqualTo(1));
        Assert.That(history.IsValid(), Is.True);
    }

    [Test]
    public void TurnOrderExcludesUnknownStartersAndCurriculumAndKeepsLimitsSeparateAcrossResume()
    {
        var history = new TrainingProgressHistory { boardSize = 6 };
        history.RecordMatch(true, false, 0); // Old winner alone cannot identify initiative.
        history.RecordMatch(false, false, 0, 0);
        history.RecordMatch(true, false, 1, 1);
        history.RecordMatch(true, false, 0, 1);
        history.RecordMatch(true, true, -1, 0);
        var restored = JsonUtility.FromJson<TrainingProgressHistory>(JsonUtility.ToJson(history));
        Assert.That(restored.turnOrderTrackedMatches, Is.EqualTo(3));
        Assert.That(restored.firstPlayerWins, Is.EqualTo(1));
        Assert.That(restored.secondPlayerWins, Is.EqualTo(1));
        Assert.That(restored.turnOrderLimits, Is.EqualTo(1));
        restored.RecordMatch(true, false, 0, 0);
        Assert.That(restored.firstPlayerWins, Is.EqualTo(2));
        Assert.That(restored.IsValid(), Is.True);
        restored.turnOrderTrackedMatches++;
        Assert.That(restored.IsValid(), Is.False);
    }

    [Test]
    public void EloTelemetryRejectsAnotherRunAndInvalidSeries()
    {
        var history = JsonUtility.FromJson<TrainingEloHistory>(
            "{\"version\":2,\"source\":\"authoritative-match-results-v1\",\"matches\":1000,\"wins\":500,\"losses\":500,\"schema\":2,\"boardSize\":5,\"runId\":\"run\",\"behavior\":\"BlockNationsSeatV2\",\"points\":[{\"step\":0,\"elo\":1200},{\"step\":1000,\"elo\":1250}]}");
        Assert.That(history.IsValid(5, "run"), Is.True);
        Assert.That(history.IsValid(11, "run"), Is.False);
        Assert.That(history.IsValid(5, "other"), Is.False);
        history.version = 1;
        Assert.That(history.IsValid(5, "run"), Is.False, "Legacy trajectory ratings must not be accepted.");
        history.version = 2;
        history.points.Add(new TrainingEloHistory.Point { step = 900, elo = 1300 });
        Assert.That(history.IsValid(5, "run"), Is.False);
    }

    [Test]
    public void LegacyEloTailUsesItsRecordedWindowAndDeclaresMissingHistory()
    {
        var history = EloCurveFixture();
        Assert.That(history.IsValid(5, "run"), Is.True);
        Assert.That(history.FirstRecordedMatch, Is.EqualTo(4139));
        Assert.That(history.ChartStartMatch, Is.EqualTo(4000));
        Assert.That(history.ChartEndMatch, Is.EqualTo(5000));
        history.curveVersion = 1; history.curveStart = 4139; history.curveBucketWidth = 4;
        Assert.That(history.IsValid(5, "run"), Is.True);
        Assert.That(history.FirstRecordedMatch, Is.EqualTo(4139));
    }

    [Test]
    public void WholeRunEloSummaryKeepsItsStartingWindowDespiteSparseSamples()
    {
        var history = EloCurveFixture();
        history.curveVersion = 1; history.curveStart = 0; history.curveBucketWidth = 64;
        Assert.That(history.IsValid(5, "run"), Is.True);
        Assert.That(history.FirstRecordedMatch, Is.Zero);
        Assert.That(history.ChartStartMatch, Is.Zero);
        Assert.That(history.ChartEndMatch, Is.EqualTo(5000));
        history.points.RemoveRange(1, 2);
        history.matches = history.wins = 0;
        Assert.That(history.ChartEndMatch, Is.EqualTo(1000));
    }

    [Test]
    public void EloCurveRejectsFalseCoverageAndInvalidCompactionWidths()
    {
        var history = EloCurveFixture();
        history.curveVersion = 1; history.curveStart = 4000; history.curveBucketWidth = 4;
        Assert.That(history.IsValid(5, "run"), Is.False);
        history.curveStart = 4139; history.curveBucketWidth = 3;
        Assert.That(history.IsValid(5, "run"), Is.False);
        history.curveBucketWidth = 4; history.curveVersion = 2;
        Assert.That(history.IsValid(5, "run"), Is.False);
    }

    private static TrainingEloHistory EloCurveFixture() => JsonUtility.FromJson<TrainingEloHistory>(
        "{\"version\":2,\"source\":\"authoritative-match-results-v1\",\"matches\":4337,\"wins\":4337,\"schema\":2,\"boardSize\":5,\"runId\":\"run\",\"behavior\":\"BlockNationsSeatV2\",\"points\":[{\"step\":0,\"elo\":1200},{\"step\":4139,\"elo\":1119},{\"step\":4337,\"elo\":1119}]}");

}
