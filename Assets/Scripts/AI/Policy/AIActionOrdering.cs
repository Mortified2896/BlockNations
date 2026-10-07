using System;
using System.Collections.Generic;

namespace BlockNations.AI
{
    internal static class AIActionOrdering
    {
        private readonly struct RankedAction
        {
            public readonly AIAction Action;
            public readonly int Priority;
            public RankedAction(AIAction action, int priority) { Action = action; Priority = priority; }
        }

        public static List<AIAction> Order(AITacticalState state, List<AIAction> actions, bool selective)
        {
            List<RankedAction> ranked = new List<RankedAction>(actions.Count);
            foreach (AIAction action in actions) ranked.Add(new RankedAction(action, Priority(state, action)));
            ranked.Sort((a, b) => {
                int comparison = b.Priority.CompareTo(a.Priority);
                return comparison != 0 ? comparison : string.CompareOrdinal(a.Action.Key, b.Action.Key);
            });
            List<AIAction> result = new List<AIAction>();
            Dictionary<int, int> movesPerUnit = new Dictionary<int, int>();
            int recruits = 0;
            foreach (RankedAction candidate in ranked)
            {
                AIAction action = candidate.Action;
                if (selective)
                {
                    // Keep different actors available for coordinated attacks/recruitment.
                    // Root actions are never pruned: their authoritative mask is exhaustive.
                    if (action.Kind == AIActionKind.Move)
                    {
                        movesPerUnit.TryGetValue(action.Actor, out int count);
                        if (count >= 2) continue;
                        movesPerUnit[action.Actor] = count + 1;
                    }
                    if (action.Kind == AIActionKind.Recruit && recruits++ >= 2) continue;
                }
                result.Add(action);
                if (selective && result.Count >= HardTacticianPolicy.SuccessorsPerNode) break;
            }
            return result;
        }

        private static int Priority(AITacticalState state, AIAction action)
        {
            if (action.Kind == AIActionKind.EndTurn) return int.MinValue;
            if (action.Kind == AIActionKind.Recruit)
                return 1000 + AIUnitValue.Material(state.Observation.RecruitTypes[action.RecruitType]);
            AIUnitState unit = state.Units[action.Actor];
            if (action.Kind == AIActionKind.Attack)
            {
                AIUnitState target = state.Units[action.Target];
                int damage = AIActionRules.Damage(unit.Attack, target.Defense);
                bool kills = damage >= target.Health;
                foreach (AICityState city in state.Cities)
                    if (kills && unit.Range <= 1 && state.Observation.IsHostileSeat(city.Seat) &&
                        city.Position(state.Observation.Width) == action.Destination) return 1000000;
                return 10000 + (kills ? AIUnitValue.Material(target) : damage * 30);
            }
            foreach (AICityState city in state.Cities)
                if (state.Observation.IsHostileSeat(city.Seat) && city.Position(state.Observation.Width) == action.Destination)
                    return 1000000;
            int progress = state.Goals.Progress(unit, action.Destination) - state.Goals.Progress(unit, unit.Position(state.Observation.Width));
            int priority = progress * 100;
            foreach (AICityState city in state.Cities)
                if (city.Seat == state.Observation.Seat && !city.Recruited && city.Position(state.Observation.Width) == unit.Position(state.Observation.Width))
                    priority += 800; // Open a recruiting tile; evaluation still decides if it is safe.
            if (unit.AttackAfterMoving && unit.AttacksUsed < unit.MaxAttacks)
                foreach (AIUnitState target in state.Units)
                    if (target.Health > 0 && state.Observation.IsHostileSeat(target.Seat) &&
                        AIActionRules.Distance(action.Destination % state.Observation.Width, action.Destination / state.Observation.Width,
                            target.X, target.Y) <= unit.Range) priority += 1200;
            return priority;
        }
    }

    internal static class AIUnitValue
    {
        public static int Material(AIUnitState unit)
        {
            int value = unit.Cost * 170 + unit.Health * 180 / Math.Max(1, unit.MaxHealth);
            if (unit.Attack > 0)
                value += unit.Attack * 24 + Math.Max(0, unit.Range - 1) * 100 +
                    Math.Max(0, unit.MaxMoves - 1) * 60 + (unit.AttackAfterMoving ? 40 : 0);
            return value;
        }
    }
}
