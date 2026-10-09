using System;

namespace BlockNations.Simulation
{
    // All engine and prediction adapters use the same capability arithmetic.
    public static class SimulationRules
    {
        public const string Version = "blocknations-simulation-v2";
        public static int RemainingMoves(bool committedMove, int maximum, int used, bool attackEndsMovement = false, int attacksUsed = 0) =>
            attackEndsMovement && attacksUsed > 0 ? 0 :
            committedMove ? (used > 0 ? 0 : Math.Max(0, maximum)) : Math.Max(0, maximum - used);
        public static int MovementUsedAfterAttack(bool attackEndsMovement, int maximum, int used) =>
            Consume(used, maximum, attackEndsMovement ? maximum : 0);
        public static bool CanAttack(bool afterMoving, int maximum, int used, int movesUsed) =>
            used < maximum && (afterMoving || movesUsed == 0);
        public static int Damage(int attack, int defense) => Math.Max(0, attack - defense);
        public static int Distance(int x1, int y1, int x2, int y2) => Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
        public static int Consume(int used, int maximum, int count = 1) => Math.Min(Math.Max(0, maximum), Math.Max(0, used) + Math.Max(0, count));
        public static int NextSeat(int seat, int seatCount) => (seat + 1) % seatCount;
        public static int NextRound(int round, int nextSeat, int firstSeat) => round + (nextSeat == firstSeat ? 1 : 0);
    }

    public interface ISeatRelationships
    {
        bool IsHostile(int observerSeat, int otherSeat);
    }

    // Today's free-for-all is an explicit relationship policy, not a unit-owner heuristic.
    public sealed class FreeForAllRelationships : ISeatRelationships
    {
        public static readonly FreeForAllRelationships Instance = new FreeForAllRelationships();
        private FreeForAllRelationships() { }
        public bool IsHostile(int observerSeat, int otherSeat) => observerSeat != otherSeat;
    }
}
