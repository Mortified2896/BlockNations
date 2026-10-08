using System;
using System.Collections.Generic;
using BlockNations.AI;

// Read-only spectator telemetry. Ratings come from the trainer's opponent pool.
[Serializable]
public sealed class TrainingEloHistory
{
    public int version, schema, boardSize;
    public string runId, behavior;
    public List<Point> points;
    [Serializable]
    public struct Point { public long step; public float elo; }

    public bool IsValid(int expectedBoard, string expectedRun)
    {
        if (version != 1 || schema != LearnedActionSchema.Version || boardSize != expectedBoard ||
            runId != expectedRun || behavior != LearnedActionSchema.BehaviorName ||
            points == null || points.Count < 1 || points.Count > 200 || points[0].step != 0) return false;
        long previous = -1;
        foreach (Point point in points)
        {
            if (point.step <= previous || float.IsNaN(point.elo) || float.IsInfinity(point.elo)) return false;
            previous = point.step;
        }
        return true;
    }
}
