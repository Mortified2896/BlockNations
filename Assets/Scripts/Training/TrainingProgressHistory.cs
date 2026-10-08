using System;
using System.Collections.Generic;

// Generated run telemetry, independent of gameplay saves and policy rewards.
[Serializable]
public sealed class TrainingProgressHistory
{
    public const int Version = 2;
    public const int WindowSize = 40;
    public const int MaximumPoints = 200;
    public int version = Version;
    public string informationContract = "public-starting-cities-canvas-v2";
    public int boardSize = 11;
    public long decisions;
    public int fullBoardMatches, fullBoardCaptures, fullBoardInterruptions, curriculumMatches;
    public int seatTrackedMatches, blueWins, redWins, seatTrackedLimits;
    // Additive development telemetry: older files leave these at zero.
    public int turnOrderTrackedMatches, firstPlayerWins, secondPlayerWins, turnOrderLimits;
    public List<int> recentResults = new List<int>();
    public List<Point> points = new List<Point>();

    [Serializable]
    public struct Point
    {
        public long decisions;
        public int match, samples;
        public float capturePercent;
    }

    public float CapturePercent
    {
        get
        {
            if (recentResults.Count == 0) return 0;
            int captures = 0;
            foreach (int result in recentResults) captures += result;
            return 100f * captures / recentResults.Count;
        }
    }

    public void RecordMatch(bool fullOpening, bool interrupted, int winnerSeat = -1, int firstSeat = -1)
    {
        if (!fullOpening) { curriculumMatches++; return; }
        fullBoardMatches++;
        // Older telemetry omitted winners; never invent results for those matches.
        if (interrupted || winnerSeat == 0 || winnerSeat == 1)
        {
            seatTrackedMatches++;
            if (interrupted) seatTrackedLimits++;
            else if (winnerSeat == 0) blueWins++;
            else redWins++;
        }
        if ((firstSeat == 0 || firstSeat == 1) && (interrupted || winnerSeat == 0 || winnerSeat == 1))
        {
            turnOrderTrackedMatches++;
            if (interrupted) turnOrderLimits++;
            else if (winnerSeat == firstSeat) firstPlayerWins++;
            else secondPlayerWins++;
        }
        if (interrupted) fullBoardInterruptions++; else fullBoardCaptures++;
        recentResults.Add(interrupted ? 0 : 1);
        if (recentResults.Count > WindowSize) recentResults.RemoveAt(0);
        points.Add(new Point { decisions = decisions, match = fullBoardMatches,
            samples = recentResults.Count, capturePercent = CapturePercent });
        if (points.Count > MaximumPoints) points.RemoveAt(0);
    }

    public bool IsValid()
    {
        if (!BlockNations.AI.LearnedActionSchema.SupportsBoard(boardSize) || version != Version || informationContract != "public-starting-cities-canvas-v2" || decisions < 0 ||
            fullBoardMatches < 0 || fullBoardCaptures < 0 || fullBoardInterruptions < 0 || curriculumMatches < 0 ||
            fullBoardCaptures + fullBoardInterruptions != fullBoardMatches ||
            seatTrackedMatches < 0 || seatTrackedMatches > fullBoardMatches || blueWins < 0 || redWins < 0 || seatTrackedLimits < 0 ||
            blueWins + redWins + seatTrackedLimits != seatTrackedMatches ||
            turnOrderTrackedMatches < 0 || turnOrderTrackedMatches > fullBoardMatches || firstPlayerWins < 0 ||
            secondPlayerWins < 0 || turnOrderLimits < 0 ||
            firstPlayerWins + secondPlayerWins + turnOrderLimits != turnOrderTrackedMatches ||
            recentResults == null || points == null || recentResults.Count > WindowSize || points.Count > MaximumPoints)
            return false;
        foreach (int result in recentResults) if (result != 0 && result != 1) return false;
        long previousDecision = -1;
        int previousMatch = 0;
        foreach (Point point in points)
        {
            if (point.decisions < previousDecision || point.decisions > decisions || point.match <= previousMatch ||
                point.match > fullBoardMatches || point.samples < 1 || point.samples > WindowSize ||
                float.IsNaN(point.capturePercent) || point.capturePercent < 0 || point.capturePercent > 100) return false;
            previousDecision = point.decisions;
            previousMatch = point.match;
        }
        return true;
    }
}
