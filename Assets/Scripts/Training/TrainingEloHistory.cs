using System;
using System.Collections.Generic;
using BlockNations.AI;

// Whole-match telemetry. Legacy trajectory-based ratings are never loaded.
[Serializable]
public sealed class TrainingEloHistory
{
    public int version, schema, boardSize;
    public string runId, behavior, source;
    public int matches, wins, losses, interruptions, unrated, selfMatches, abandoned;
    public int curveVersion;
    public long curveStart, curveBucketWidth;
    public List<Point> points;
    [Serializable]
    public struct Point { public long step; public float elo; }

    public long FirstRecordedMatch => curveVersion == 1 ? curveStart :
        points.Count > 1 && points[1].step > 1 ? points[1].step : 0;
    public long ChartStartMatch => FirstRecordedMatch / 1000 * 1000;
    public long ChartEndMatch => Math.Max(ChartStartMatch + 1000,
        (points[points.Count - 1].step + 999) / 1000 * 1000);

    public bool IsValid(int expectedBoard, string expectedRun)
    {
        if (version != 2 || source != "authoritative-match-results-v1" || matches < 0 ||
            wins < 0 || losses < 0 || interruptions < 0 || unrated < 0 ||
            wins + losses + interruptions + unrated != matches || schema != LearnedActionSchema.Version || boardSize != expectedBoard ||
            runId != expectedRun || behavior != LearnedActionSchema.BehaviorName ||
            points == null || points.Count < 1 || points.Count > 200 || points[0].step != 0) return false;
        long previous = -1;
        foreach (Point point in points)
        {
            if (point.step <= previous || point.step > wins + losses || float.IsNaN(point.elo) || float.IsInfinity(point.elo)) return false;
            previous = point.step;
        }
        if (curveVersion != 0 && curveVersion != 1) return false;
        if (curveVersion == 1 && (curveStart < 0 || curveStart > previous || curveBucketWidth < 1 ||
            (curveBucketWidth & (curveBucketWidth - 1)) != 0 ||
            (curveStart > 0 && !points.Exists(point => point.step == curveStart)))) return false;
        return true;
    }
}
