using System;
using System.Collections.Generic;

namespace BlockNations.AI
{
    // Goal fields are built once from the decision's fair observation and shared by lookahead
    // states. Predicted vision cannot invent a city or move the objective during a sequence.
    internal sealed class AIStrategicGoals
    {
        private readonly AIObservation observation;
        private readonly int[] combatDistance, explorationDistance;
        private readonly int combatOriginDistance, explorationOriginDistance;
        public readonly bool HasCombatObjective;
        public readonly bool HasExplorationObjective;

        public AIStrategicGoals(AIObservation observation)
        {
            this.observation = observation;
            List<int> combat = new List<int>(), exploration = new List<int>();
            foreach (AICityState city in observation.Cities)
                if (observation.IsHostileSeat(city.Seat)) combat.Add(city.Position(observation.Width));
            if (combat.Count == 0)
                foreach (AIUnitState unit in observation.Units)
                    if (unit.Health > 0 && observation.IsHostileSeat(unit.Seat)) combat.Add(unit.Position(observation.Width));
            for (int p = 0; p < observation.Tiles.Length; p++)
                if (observation.Tiles[p] && !observation.Seen[p]) exploration.Add(p);
            // Save loading can preserve explored tiles without preserving historical city memory.
            // Search currently unseen areas again instead of interpreting that as a finished game.
            if (exploration.Count == 0 && combat.Count == 0)
                for (int p = 0; p < observation.Tiles.Length; p++)
                    if (observation.Tiles[p] && !observation.Visible[p]) exploration.Add(p);
            HasCombatObjective = combat.Count > 0;
            HasExplorationObjective = exploration.Count > 0;
            explorationDistance = BuildDistances(exploration);
            combatDistance = HasCombatObjective ? BuildDistances(combat) : explorationDistance;
            explorationOriginDistance = OriginDistance(explorationDistance);
            combatOriginDistance = OriginDistance(combatDistance);
        }

        public int Progress(AIUnitState unit, int position)
        {
            int[] distances = unit.Attack > 0 ? combatDistance : explorationDistance;
            int origin = unit.Attack > 0 ? combatOriginDistance : explorationOriginDistance;
            if (distances[position] < 0) return 0;
            // New recruits at home start at zero progress; recruiting is not penalized by
            // the distance of an undiscovered objective. Blocked terrain has no gradient.
            return Math.Max(0, origin - distances[position]);
        }

        private int OriginDistance(int[] distances)
        {
            int origin = int.MaxValue;
            foreach (AICityState city in observation.Cities)
                if (city.Seat == observation.Seat && distances[city.Position(observation.Width)] >= 0)
                    origin = Math.Min(origin, distances[city.Position(observation.Width)]);
            if (origin != int.MaxValue) return origin;
            int furthest = 0;
            foreach (int distance in distances) furthest = Math.Max(furthest, distance);
            return furthest;
        }

        private int[] BuildDistances(List<int> targets)
        {
            int[] distances = new int[observation.Tiles.Length];
            for (int i = 0; i < distances.Length; i++) distances[i] = -1;
            Queue<int> pending = new Queue<int>();
            foreach (int target in targets)
                if (observation.Tiles[target] && distances[target] < 0)
                { distances[target] = 0; pending.Enqueue(target); }
            while (pending.Count > 0)
            {
                int p = pending.Dequeue(), x = p % observation.Width, y = p / observation.Width;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= observation.Width || ny >= observation.Height) continue;
                        int neighbor = ny * observation.Width + nx;
                        if (!observation.Tiles[neighbor] || distances[neighbor] >= 0) continue;
                        distances[neighbor] = distances[p] + 1;
                        pending.Enqueue(neighbor);
                    }
            }
            return distances;
        }
    }
}
