using System;
using System.Collections.Generic;

namespace BlockNations.AI
{
    // A conservative next-turn estimate, not an opposing-turn search. Each observed enemy
    // spends its attack slots once, rather than threatening every nearby unit simultaneously.
    internal sealed class AITacticalThreats
    {
        public readonly int[] IncomingDamage;
        public readonly bool[] CityAtRisk;

        public AITacticalThreats(AITacticalState state, int[] occupants)
        {
            AIObservation observation = state.Observation;
            AIUnitState[] units = state.Units;
            IncomingDamage = new int[units.Length];
            CityAtRisk = new bool[state.Cities.Length];
            Dictionary<int, int[]> reach = new Dictionary<int, int[]>();
            for (int e = 0; e < units.Length; e++)
            {
                AIUnitState enemy = units[e];
                if (enemy.Health <= 0 || !observation.IsHostileSeat(enemy.Seat)) continue;
                int[] reachable = state.Reachable(enemy.X, enemy.Y, enemy.MaxMoves, occupants);
                reach[e] = reachable;
                for (int slot = 0; slot < enemy.MaxAttacks; slot++)
                {
                    int target = -1, bestThreat = -1;
                    for (int i = 0; i < units.Length; i++)
                    {
                        AIUnitState friendly = units[i];
                        int damage = AIActionRules.Damage(enemy.Attack, friendly.Defense);
                        if (friendly.Health <= IncomingDamage[i] || friendly.Seat != observation.Seat || damage <= 0 ||
                            !CanAttack(enemy, friendly, reachable, observation.Width)) continue;
                        int threat = damage * 12 + (damage + IncomingDamage[i] >= friendly.Health ? AIUnitValue.Material(friendly) : 0);
                        foreach (AICityState city in state.Cities)
                            if (city.Seat == observation.Seat && city.X == friendly.X && city.Y == friendly.Y) threat += 40000;
                        if (threat > bestThreat) { bestThreat = threat; target = i; }
                    }
                    if (target < 0) break;
                    IncomingDamage[target] += AIActionRules.Damage(enemy.Attack, units[target].Defense);
                }
            }
            for (int c = 0; c < state.Cities.Length; c++)
            {
                AICityState city = state.Cities[c];
                if (city.Seat != observation.Seat) continue;
                int p = city.Position(observation.Width), defender = occupants[p];
                foreach (KeyValuePair<int, int[]> entry in reach)
                {
                    AIUnitState enemy = units[entry.Key];
                    if (defender < 0 && entry.Value[p] > 0) CityAtRisk[c] = true;
                    else if (defender >= 0 && IncomingDamage[defender] >= units[defender].Health && enemy.Range <= 1 &&
                        enemy.MaxAttacks > 0 && AIActionRules.Damage(enemy.Attack, units[defender].Defense) > 0 &&
                        CanAttack(enemy, units[defender], entry.Value, observation.Width)) CityAtRisk[c] = true;
                }
            }
        }

        private static bool CanAttack(AIUnitState enemy, AIUnitState target, int[] reachable, int width)
        {
            if (AIActionRules.Distance(enemy.X, enemy.Y, target.X, target.Y) <= enemy.Range) return true;
            if (!enemy.AttackAfterMoving) return false;
            for (int p = 0; p < reachable.Length; p++)
                if (reachable[p] > 0 && AIActionRules.Distance(p % width, p / width, target.X, target.Y) <= enemy.Range) return true;
            return false;
        }
    }
}
