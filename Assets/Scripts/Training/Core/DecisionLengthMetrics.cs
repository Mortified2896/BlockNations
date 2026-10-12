using System;
using System.Collections.Generic;

namespace BlockNations.Training
{
    // Bounded aggregate counters. No positions, actions, policy tensors or
    // private match histories are written to diagnostic output.
    public sealed class DecisionLengthMetrics
    {
        private readonly int[] game = new int[2], turn = new int[2];
        private readonly int[] limits = { 0, 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, int.MaxValue };
        private readonly long[] games = new long[13], turns = new long[13];
        public long RootSelections, ConditionalSelections, RootEndTurns, ConditionalEndTurns;
        public void Decision(int seatIndex, bool conditional, bool endTurn)
        {
            game[seatIndex]++; turn[seatIndex]++;
            if (conditional) { ConditionalSelections++; if (endTurn) ConditionalEndTurns++; }
            else { RootSelections++; if (endTurn) RootEndTurns++; }
        }
        private void Record(long[] histogram, int count)
        { for (int i = 0; i < limits.Length; i++) if (count <= limits[i]) { histogram[i]++; break; } }
        public void EndTurn(int seatIndex) { Record(turns, turn[seatIndex]); turn[seatIndex] = 0; }
        public void EndGame(bool interrupted)
        {
            if (!interrupted) for (int seatIndex = 0; seatIndex < 2; seatIndex++)
            { Record(games, game[seatIndex]); if (turn[seatIndex] > 0) Record(turns, turn[seatIndex]); }
            ResetMatch();
        }
        public void ResetMatch() { Array.Clear(game, 0, game.Length); Array.Clear(turn, 0, turn.Length); }
        public object Snapshot() => new { version = 1, upperBounds = limits, rootSelections = RootSelections,
            conditionalSelections = ConditionalSelections, rootEndTurns = RootEndTurns, conditionalEndTurns = ConditionalEndTurns,
            completedSeatGameLengths = (long[])games.Clone(), completedSeatTurnLengths = (long[])turns.Clone() };
    }
}
