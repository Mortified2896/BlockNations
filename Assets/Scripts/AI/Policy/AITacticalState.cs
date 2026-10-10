using System;
using System.Collections.Generic;
using System.Text;

namespace BlockNations.AI
{
    // A bounded, observed-information predictor. It is not a complete training environment:
    // it deliberately cannot reveal unknown units, cities, or terrain during lookahead.
    public sealed class AITacticalState
    {
        private static readonly int[] Dx = { 1, 0, -1, 0, 1, 1, -1, -1 };
        private static readonly int[] Dy = { 0, 1, 0, -1, 1, -1, 1, -1 };
        public readonly AIObservation Observation;
        internal readonly AIStrategicGoals Goals;
        public AIUnitState[] Units;
        public AICityState[] Cities;
        public bool[] Seen;
        public int Gold;
        public bool CapturedCity;
        public bool LostCity;

        public AITacticalState(AIObservation observation)
        {
            Observation = observation;
            Goals = new AIStrategicGoals(observation);
            Units = (AIUnitState[])observation.Units.Clone();
            Cities = (AICityState[])observation.Cities.Clone();
            Seen = (bool[])observation.Seen.Clone();
            Gold = observation.Gold;
        }

        private AITacticalState(AITacticalState other)
        {
            Observation = other.Observation;
            Goals = other.Goals;
            Units = (AIUnitState[])other.Units.Clone();
            Cities = (AICityState[])other.Cities.Clone();
            Seen = (bool[])other.Seen.Clone();
            Gold = other.Gold;
            CapturedCity = other.CapturedCity;
            LostCity = other.LostCity;
        }

        // Only already-observed units are projected. Opponent gold/recruits and hidden
        // discoveries are unknown, so this is a reply predictor, not a training environment.
        public AITacticalState BeginObservedTurn(int seat)
        {
            AITacticalState next = new AITacticalState(this);
            for (int i = 0; i < next.Units.Length; i++)
            {
                AIUnitState unit = next.Units[i];
                if (unit.Seat != seat) continue;
                unit.MovesUsed = unit.AttacksUsed = 0;
                next.Units[i] = unit;
            }
            return next;
        }

        public bool AreHostile(int actorSeat, int targetSeat) =>
            (actorSeat == Observation.Seat && Observation.IsHostileSeat(targetSeat)) ||
            (targetSeat == Observation.Seat && Observation.IsHostileSeat(actorSeat));

        public AITacticalState After(AIAction action)
        {
            AITacticalState next = new AITacticalState(this);
            if (action.Kind == AIActionKind.Recruit)
            {
                if (!Observation.CanRecruitType(action.RecruitType)) throw new ArgumentException("Recruit type is disabled by the public rules.");
                AICityState city = next.Cities[action.Actor];
                AIUnitState unit = Observation.RecruitTypes[action.RecruitType];
                unit.Seat = Observation.Seat;
                unit.X = city.X;
                unit.Y = city.Y;
                Array.Resize(ref next.Units, next.Units.Length + 1);
                next.Units[next.Units.Length - 1] = unit;
                next.Gold -= unit.Cost;
                city.Recruited = true;
                next.Cities[action.Actor] = city;
                next.Reveal(unit);
            }
            else if (action.Kind == AIActionKind.Move || action.Kind == AIActionKind.Attack)
            {
                AIUnitState unit = next.Units[action.Actor];
                if (action.Kind == AIActionKind.Attack)
                {
                    AIUnitState target = next.Units[action.Target];
                    target.Health = Math.Max(0, target.Health - AIActionRules.Damage(unit.Attack, target.Defense));
                    unit.AttacksUsed++;
                    unit.MovesUsed = BlockNations.Simulation.SimulationRules.MovementUsedAfterAttack(unit.AttackEndsMovement, unit.MaxMoves, unit.MovesUsed);
                    if (target.Health == 0 && unit.Range <= 1)
                    {
                        unit.X = target.X;
                        unit.Y = target.Y;
                    }
                    next.Units[action.Target] = target;
                }
                else
                {
                    unit.X = action.Destination % Observation.Width;
                    unit.Y = action.Destination / Observation.Width;
                    unit.MovesUsed = Math.Min(unit.MaxMoves, unit.MovesUsed + action.MoveCost);
                }
                next.Units[action.Actor] = unit;
                for (int i = 0; i < next.Cities.Length; i++)
                {
                    AICityState city = next.Cities[i];
                    if (next.AreHostile(unit.Seat, city.Seat) && city.X == unit.X && city.Y == unit.Y)
                    {
                        // Current VsAI ends immediately on any hostile city capture.
                        // A remembered city is an objective, not a proven current capture.
                        if (unit.Seat == Observation.Seat) next.CapturedCity = city.CurrentlyVisible;
                        else if (city.Seat == Observation.Seat) next.LostCity = true;
                        city.Seat = unit.Seat;
                        next.Cities[i] = city;
                    }
                }
                if (unit.Seat == Observation.Seat) next.Reveal(unit);
            }
            return next;
        }

        private void Reveal(AIUnitState unit)
        {
            for (int dy = -unit.Vision; dy <= unit.Vision; dy++)
                for (int dx = -unit.Vision; dx <= unit.Vision; dx++)
                {
                    int x = unit.X + dx, y = unit.Y + dy;
                    if (InBounds(x, y)) Seen[y * Observation.Width + x] = true;
                }
        }

        public List<AIAction> Actions() => ActionsForSeat(Observation.Seat);

        public List<AIAction> ActionsForSeat(int seat)
        {
            List<AIAction> result = new List<AIAction>();
            if (CapturedCity || LostCity) return result;
            int[] occupants = Occupants();
            for (int actor = 0; actor < Units.Length; actor++)
            {
                AIUnitState unit = Units[actor];
                if (unit.Health <= 0 || unit.Seat != seat) continue;
                if (AIActionRules.CanAttack(unit.AttackAfterMoving, unit.MaxAttacks, unit.AttacksUsed, unit.MovesUsed))
                    for (int target = 0; target < Units.Length; target++)
                    {
                        AIUnitState enemy = Units[target];
                        if (enemy.Health <= 0 || !AreHostile(unit.Seat, enemy.Seat) || Distance(unit, enemy) > unit.Range ||
                            Distance(unit, enemy) == 0 || AIActionRules.Damage(unit.Attack, enemy.Defense) == 0) continue;
                        result.Add(new AIAction { Kind = AIActionKind.Attack, Actor = actor, Target = target,
                            Destination = enemy.Position(Observation.Width) });
                    }
                int range = AIActionRules.RemainingMoves(unit.CommittedMove, unit.MaxMoves, unit.MovesUsed, unit.AttackEndsMovement, unit.AttacksUsed);
                if (range <= 0) continue;
                int[] distances = Reachable(unit.X, unit.Y, range, occupants);
                for (int position = 0; position < distances.Length; position++)
                    if (distances[position] > 0)
                        result.Add(new AIAction { Kind = AIActionKind.Move, Actor = actor, Destination = position,
                            MoveCost = distances[position], Target = -1 });
            }
            for (int cityIndex = 0; cityIndex < Cities.Length; cityIndex++)
            {
                AICityState city = Cities[cityIndex];
                if (seat != Observation.Seat || city.Seat != seat || city.Recruited || occupants[city.Position(Observation.Width)] >= 0) continue;
                for (int type = 0; type < Observation.RecruitTypes.Length; type++)
                    if (Observation.CanRecruitType(type) && Observation.RecruitTypes[type].Cost <= Gold)
                        result.Add(new AIAction { Kind = AIActionKind.Recruit, Actor = cityIndex, RecruitType = type,
                            Destination = city.Position(Observation.Width), Target = -1 });
            }
            result.Add(new AIAction { Kind = AIActionKind.EndTurn, Actor = -1, Target = -1 });
            return result;
        }

        public int[] Occupants()
        {
            int[] occupants = new int[Observation.Tiles.Length];
            for (int i = 0; i < occupants.Length; i++) occupants[i] = -1;
            for (int i = 0; i < Units.Length; i++)
                if (Units[i].Health > 0) occupants[Units[i].Position(Observation.Width)] = i;
            return occupants;
        }

        public int[] Reachable(int x, int y, int range, int[] occupants)
        {
            int[] distances = new int[Observation.Tiles.Length];
            for (int i = 0; i < distances.Length; i++) distances[i] = -1;
            Queue<int> queue = new Queue<int>();
            int origin = y * Observation.Width + x;
            queue.Enqueue(origin);
            distances[origin] = 0;
            while (queue.Count > 0)
            {
                int position = queue.Dequeue();
                if (distances[position] >= range) continue;
                for (int neighbor = 0; neighbor < Dx.Length; neighbor++)
                {
                    int nx = position % Observation.Width + Dx[neighbor];
                    int ny = position / Observation.Width + Dy[neighbor];
                    if (!InBounds(nx, ny)) continue;
                    int next = ny * Observation.Width + nx;
                    if (!Observation.Tiles[next] || distances[next] >= 0 || occupants[next] >= 0) continue;
                    distances[next] = distances[position] + 1;
                    queue.Enqueue(next);
                }
            }
            return distances;
        }

        public AIEvaluation Evaluate() => new LinearAIPositionEvaluator().Evaluate(AIPositionFeatureExtractor.Extract(this));

        public string StateKey()
        {
            StringBuilder key = new StringBuilder();
            key.Append(Gold).Append(',').Append(CapturedCity).Append(',').Append(LostCity).Append('|');
            for (int i = 0; i < Units.Length; i++)
            {
                AIUnitState u = Units[i];
                key.Append(u.Seat).Append(',').Append(u.Type).Append(',').Append(u.X).Append(',').Append(u.Y)
                    .Append(',').Append(u.Health).Append(',').Append(u.MovesUsed).Append(',').Append(u.AttacksUsed).Append(';');
            }
            for (int c = 0; c < Cities.Length; c++) key.Append(Cities[c].Seat).Append(',').Append(Cities[c].Recruited).Append(';');
            // Seen history affects exploration value and must be part of transposition identity.
            for (int p = 0; p < Seen.Length; p++) key.Append(Seen[p] ? '1' : '0');
            return key.ToString();
        }

        private bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Observation.Width && y < Observation.Height;
        private static int Distance(AIUnitState a, AIUnitState b) => AIActionRules.Distance(a.X, a.Y, b.X, b.Y);
    }

    [Serializable]
    public struct AIEvaluation
    {
        public int Capture, Material, Safety, Positioning, Exploration, Economy;
        public int Total => Capture + Material + Safety + Positioning + Exploration + Economy;
        public override string ToString() => $"{Total} (city {Capture}, material {Material}, safety {Safety}, position {Positioning}, exploration {Exploration}, economy {Economy})";
    }
}
