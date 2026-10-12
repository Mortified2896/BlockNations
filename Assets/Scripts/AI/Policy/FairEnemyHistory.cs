using System;
using System.Collections.Generic;

namespace BlockNations.AI
{
    // Memory accepts only an already filtered observation. No world objects,
    // persistent enemy IDs, enemy wallet or current enemy counters are retained.
    public sealed class FairEnemyHistory
    {
        public sealed class Sighting { public AIUnitState Unit; public int Round; }
        private readonly Dictionary<int, Sighting> sightings = new Dictionary<int, Sighting>();
        private int seat = -1, width;
        public IEnumerable<KeyValuePair<int, Sighting>> Sightings => sightings;
        public void Reset() { sightings.Clear(); seat = -1; width = 0; }
        public void Observe(AIObservation observation, int publicFirstSeat)
        {
            LearnedObservationSchemaV3.Validate(observation, -1, publicFirstSeat);
            if (seat >= 0 && (seat != observation.Seat || width != observation.Width))
                throw new ArgumentException("Reset history before changing its seat or board.");
            seat = observation.Seat; width = observation.Width;
            var clear = new List<int>();
            foreach (var entry in sightings)
            {
                if (entry.Value.Round > observation.Round) throw new ArgumentException("Observation history moved backwards; reset it.");
                if (observation.Visible[entry.Key]) clear.Add(entry.Key);
            }
            foreach (int position in clear) sightings.Remove(position);
            foreach (AIUnitState observed in observation.Units)
                if (observed.Seat != seat && observation.IsHostileSeat(observed.Seat))
                {
                    AIUnitState remembered = observed;
                    remembered.MovesUsed = remembered.AttacksUsed = 0;
                    sightings[observed.Position(width)] = new Sighting { Unit = remembered, Round = observation.Round };
                }
        }
    }
}
