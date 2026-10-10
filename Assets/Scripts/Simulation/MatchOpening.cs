using System;

namespace BlockNations.Simulation
{
    // Opening economy is a reset recipe, separate from turn income and saved balances.
    public static class MatchOpening
    {
        public const int LegacyEconomyVersion = 1;
        public const int CurrentEconomyVersion = 2;
        public const int StartingGold = 1;
        public const int SecondPlayerGoldBonus = 1;

        public static bool SupportsEconomy(int version) =>
            version == LegacyEconomyVersion || version == CurrentEconomyVersion;

        public static int TurnOrder(int seat, int firstSeat, int seatCount)
        {
            if (seatCount < 1 || seat < 0 || seat >= seatCount || firstSeat < 0 || firstSeat >= seatCount)
                throw new ArgumentOutOfRangeException(nameof(seat), "Opening seats must belong to the match.");
            return (seat - firstSeat + seatCount) % seatCount + 1;
        }

        public static int GoldBeforeIncome(int seat, int firstSeat, int economyVersion = CurrentEconomyVersion)
        {
            int order = TurnOrder(seat, firstSeat, 2);
            if (!SupportsEconomy(economyVersion)) throw new ArgumentOutOfRangeException(nameof(economyVersion));
            return economyVersion == LegacyEconomyVersion ? 2 :
                StartingGold + (order == 2 ? SecondPlayerGoldBonus : 0);
        }
    }
}
