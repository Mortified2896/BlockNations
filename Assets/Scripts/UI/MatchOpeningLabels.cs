using BlockNations.Simulation;

public static class MatchOpeningLabels
{
    public static string ForSeat(int seat, int firstSeat, int seatCount, bool showColour)
    {
        int order = MatchOpening.TurnOrder(seat, firstSeat, seatCount);
        string position = order == 1 ? "first" : order == 2 ? "second" :
            order == 3 ? "third" : order == 4 ? "fourth" : "in position " + order;
        string colour = showColour && seatCount == 2 ? (seat == 0 ? " (Blue)" : " (Red)") : "";
        return $"You are Player {seat + 1}{colour} · You move {position}";
    }
}
