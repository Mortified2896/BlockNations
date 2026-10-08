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
}
