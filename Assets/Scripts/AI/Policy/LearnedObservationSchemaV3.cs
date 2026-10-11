using System;
using System.Collections.Generic;

namespace BlockNations.AI
{
    // Opt-in observation contract. Existing v2 inference/training keeps its exact
    // tensor and action meanings. The unchanged v2 prefix allows a function-
    // preserving initialization: append zero columns to the first linear layer.
    public static class LearnedObservationSchemaV3
    {
        public const int Version = 3;
        public const string BehaviorName = "BlockNationsSeatV3";
        public const int ActionSchemaVersion = LearnedActionSchema.Version;
        public const int ActionCount = LearnedActionSchema.ActionCount;
        public const int LegacyPrefixSize = LearnedActionSchema.ObservationSize;
        public const int UnitAttackEndsMovementOffset = LegacyPrefixSize;
        public const int RecruitAttackEndsMovementOffset = UnitAttackEndsMovementOffset + LearnedActionSchema.Positions;
        public const int FirstPlayerOffset = RecruitAttackEndsMovementOffset + LearnedActionSchema.RecruitCapacity;
        public const int VersionOffset = FirstPlayerOffset + 1;
        public const int ObservationSize = VersionOffset + 1;

        public static void Validate(AIObservation observation, int selectedSource, int publicFirstSeat)
        {
            LearnedActionSchema.Validate(observation);
            if (publicFirstSeat < 0 || publicFirstSeat > 1)
                throw new ArgumentOutOfRangeException(nameof(publicFirstSeat), "Provide the public starting seat explicitly.");
            if (selectedSource < -1 || selectedSource >= LearnedActionSchema.Positions)
                throw new ArgumentOutOfRangeException(nameof(selectedSource));
            foreach (AIUnitState unit in observation.Units)
            {
                int position = unit.Position(observation.Width);
                // World state must pass through the seat's fair observation
                // source. The new adapter fails closed if given private pieces
                // or opponent action counters, even on a currently visible unit.
                if (unit.Seat < 0 || unit.Seat > 1 || unit.X < 0 || unit.X >= observation.Width ||
                    unit.Y < 0 || unit.Y >= observation.Height || !observation.Tiles[position] ||
                    unit.Seat != observation.Seat && (!observation.Visible[position] || unit.MovesUsed != 0 || unit.AttacksUsed != 0))
                    throw new ArgumentException("Schema v3 requires seat-visible units with neutral opponent action counters.");
            }
        }

        public static Dictionary<int, AIAction> Choices(AIObservation observation, int selectedSource, int publicFirstSeat)
        {
            Validate(observation, selectedSource, publicFirstSeat);
            return LearnedActionSchema.Choices(observation, selectedSource);
        }

        // Only public role and declared capabilities are appended. These are not
        // strategy scores, unit-name bonuses, enemy gold estimates or memory.
        // Canonical positions and stable recruit slots retain v2's meanings.
        public static float[] Encode(AIObservation observation, int selectedSource, int publicFirstSeat)
        {
            Validate(observation, selectedSource, publicFirstSeat);
            var values = new float[ObservationSize];
            Array.Copy(LearnedActionSchema.Encode(observation, selectedSource), values, LegacyPrefixSize);
            foreach (AIUnitState unit in observation.Units)
            {
                int position = LearnedActionSchema.CanonicalPosition(
                    LearnedActionSchema.CanvasPosition(observation, unit.Position(observation.Width)), observation.Seat);
                values[UnitAttackEndsMovementOffset + position] = unit.AttackEndsMovement ? 1 : 0;
            }
            for (int slot = 0; slot < observation.RecruitTypes.Length; slot++)
                values[RecruitAttackEndsMovementOffset + slot] = observation.RecruitTypes[slot].AttackEndsMovement ? 1 : 0;
            values[FirstPlayerOffset] = observation.Seat == publicFirstSeat ? 1 : 0;
            values[VersionOffset] = Version;
            return values;
        }
    }
}
