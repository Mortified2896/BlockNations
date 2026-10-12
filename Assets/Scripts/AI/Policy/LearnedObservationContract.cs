using System;

namespace BlockNations.AI
{
    public static class LearnedObservationContract
    {
        public const int HistoryVersion = 4, HistoryChannels = 12;
        public const int HistorySize = LearnedObservationSchemaV3.ObservationSize + LearnedActionSchema.Positions * HistoryChannels + 1;
        public static bool Supports(int version) => version == 2 || version == 3 || version == 4;
        public static int Size(int version) => version == 2 ? LearnedActionSchema.ObservationSize :
            version == 3 ? LearnedObservationSchemaV3.ObservationSize : version == 4 ? HistorySize :
            throw new ArgumentException("Unsupported observation contract.");
        public static string Behavior(int version) { Size(version); return "BlockNationsSeatV" + version; }
        public static float[] Encode(int version, AIObservation observation, int selectedSource, int publicFirstSeat, FairEnemyHistory history = null)
        {
            if (version == 2) return LearnedActionSchema.Encode(observation, selectedSource);
            float[] prefix = LearnedObservationSchemaV3.Encode(observation, selectedSource, publicFirstSeat);
            if (version == 3) return prefix;
            if (version != HistoryVersion || history == null) throw new ArgumentException("History contract requires explicit per-seat memory.");
            history.Observe(observation, publicFirstSeat);
            var values = new float[HistorySize]; Array.Copy(prefix, values, prefix.Length);
            foreach (var entry in history.Sightings)
            {
                int position = LearnedActionSchema.CanonicalPosition(LearnedActionSchema.CanvasPosition(observation, entry.Key), observation.Seat);
                int offset = prefix.Length + position * HistoryChannels; AIUnitState unit = entry.Value.Unit;
                values[offset] = 1; values[offset + 1] = unit.Health / 10f; values[offset + 2] = unit.MaxHealth / 10f;
                values[offset + 3] = unit.Attack / 10f; values[offset + 4] = unit.Defense / 10f;
                values[offset + 5] = unit.Range / 11f; values[offset + 6] = unit.Vision / 11f;
                values[offset + 7] = unit.MaxMoves / 11f; values[offset + 8] = unit.AttackAfterMoving ? 1 : 0;
                values[offset + 9] = unit.CommittedMove ? 1 : 0; values[offset + 10] = unit.AttackEndsMovement ? 1 : 0;
                values[offset + 11] = Math.Min(100, observation.Round - entry.Value.Round) / 100f;
            }
            values[values.Length - 1] = HistoryVersion;
            return values;
        }
    }
}
