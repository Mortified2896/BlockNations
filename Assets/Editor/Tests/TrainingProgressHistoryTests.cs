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

    [Test]
    public void EloTelemetryRejectsAnotherRunAndInvalidSeries()
    {
        var history = JsonUtility.FromJson<TrainingEloHistory>(
            "{\"version\":1,\"schema\":2,\"boardSize\":5,\"runId\":\"run\",\"behavior\":\"BlockNationsSeatV2\",\"points\":[{\"step\":0,\"elo\":1200},{\"step\":1000,\"elo\":1250}]}");
        Assert.That(history.IsValid(5, "run"), Is.True);
        Assert.That(history.IsValid(11, "run"), Is.False);
        Assert.That(history.IsValid(5, "other"), Is.False);
        history.points.Add(new TrainingEloHistory.Point { step = 900, elo = 1300 });
        Assert.That(history.IsValid(5, "run"), Is.False);
    }

}
