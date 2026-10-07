using System;

namespace BlockNations.AI
{
    // Stable, unweighted features are usable by a fitted evaluator without Unity or scene access.
    [Serializable]
    public struct AIPositionFeatures
    {
        public const int SchemaVersion = 1;
        public int Outcome, OwnedCities, MaterialBalance, ExpectedUnitLoss, ExpectedDamage, ThreatenedCities;
        public int CombatProgress, ScoutProgress, SupportedUnits, GuardedCities, SeenTiles, Gold;
        public int ExcessScouts, UnprotectedScouts;
    }

    public interface IAIPositionEvaluator
    {
        string Version { get; }
        AIEvaluation Evaluate(AIPositionFeatures features);
    }

    [Serializable]
    public struct AIPositionWeights
    {
        public int Terminal, City, Material, UnitLoss, Damage, CityThreat;
        public int CombatProgress, ScoutProgress, Support, Guard, Exploration, Gold, ExcessScout, UnprotectedScout;
        public static AIPositionWeights Default => new AIPositionWeights {
            Terminal = 1000000, City = 12000, Material = 1, UnitLoss = 1, Damage = 1, CityThreat = 40000,
            CombatProgress = 48, ScoutProgress = 64, Support = 16, Guard = 400, Exploration = 14,
            Gold = 130, ExcessScout = 220, UnprotectedScout = 450 };
    }

    // These initial weights are hand-authored, not learned. A fitted evaluator can implement
    // the same interface; legality, observation, search, and runtime execution remain separate.
    public sealed class LinearAIPositionEvaluator : IAIPositionEvaluator
    {
        public const string DefaultVersion = "linear-features-v1";
        private readonly AIPositionWeights weights;
        public string Version { get; }
        public LinearAIPositionEvaluator() : this(DefaultVersion, AIPositionWeights.Default) { }
        public LinearAIPositionEvaluator(string version, AIPositionWeights weights)
        {
            if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Evaluator version is required.", nameof(version));
            Version = version;
            this.weights = weights;
        }
        public AIEvaluation Evaluate(AIPositionFeatures f)
        {
            if (f.Outcome != 0) return new AIEvaluation { Capture = f.Outcome * weights.Terminal };
            return new AIEvaluation {
                Capture = f.OwnedCities * weights.City,
                Material = f.MaterialBalance * weights.Material,
                Safety = -f.ExpectedUnitLoss * weights.UnitLoss - f.ExpectedDamage * weights.Damage - f.ThreatenedCities * weights.CityThreat,
                Positioning = f.CombatProgress * weights.CombatProgress + f.ScoutProgress * weights.ScoutProgress +
                    f.SupportedUnits * weights.Support + f.GuardedCities * weights.Guard,
                Exploration = f.SeenTiles * weights.Exploration,
                Economy = f.Gold * weights.Gold - f.ExcessScouts * weights.ExcessScout - f.UnprotectedScouts * weights.UnprotectedScout };
        }
    }

    public static class AIPositionFeatureExtractor
    {
        public static AIPositionFeatures Extract(AITacticalState state, bool estimateThreats = true)
        {
            AIObservation observation = state.Observation;
            AIPositionFeatures f = new AIPositionFeatures { Gold = state.Gold,
                Outcome = state.CapturedCity ? 1 : state.LostCity ? -1 : 0 };
            if (f.Outcome != 0) return f;
            int[] occupants = state.Occupants();
            AITacticalThreats threats = estimateThreats ? new AITacticalThreats(state, occupants) : null;
            int combat = 0, scouts = 0;
            for (int i = 0; i < state.Units.Length; i++)
            {
                AIUnitState unit = state.Units[i];
                if (unit.Health <= 0) continue;
                int material = AIUnitValue.Material(unit);
                if (unit.Seat != observation.Seat)
                {
                    if (observation.IsHostileSeat(unit.Seat)) f.MaterialBalance -= material;
                    continue;
                }
                f.MaterialBalance += material;
                if (unit.Attack > 0) combat++; else scouts++;
                if (threats != null)
                {
                    int incoming = threats.IncomingDamage[i];
                    if (incoming >= unit.Health) f.ExpectedUnitLoss += material + 140;
                    else f.ExpectedDamage += incoming * 120 / Math.Max(1, unit.MaxHealth);
                }
                int progress = state.Goals.Progress(unit, unit.Position(observation.Width));
                if (unit.Attack > 0) f.CombatProgress += progress; else f.ScoutProgress += progress;
                if (unit.Attack <= 0 || !state.Goals.HasCombatObjective) continue;
                for (int j = 0; j < state.Units.Length; j++)
                {
                    AIUnitState friend = state.Units[j];
                    if (j != i && friend.Health > 0 && friend.Seat == unit.Seat && friend.Attack > 0 &&
                        AIActionRules.Distance(unit.X, unit.Y, friend.X, friend.Y) <= 2)
                    { f.SupportedUnits++; break; }
                }
            }
            for (int c = 0; c < state.Cities.Length; c++)
            {
                AICityState city = state.Cities[c];
                if (city.Seat != observation.Seat) continue;
                f.OwnedCities++;
                int defender = occupants[city.Position(observation.Width)];
                if (combat > 1 && defender >= 0 && state.Units[defender].Attack > 0) f.GuardedCities++;
                if (threats != null && threats.CityAtRisk[c]) f.ThreatenedCities++;
            }
            int unseen = 0;
            for (int p = 0; p < state.Seen.Length; p++)
                if (observation.Tiles[p]) { if (state.Seen[p]) f.SeenTiles++; else unseen++; }
            if (scouts > 1 || unseen == 0) f.ExcessScouts = scouts;
            if (combat == 0 && scouts > 0) f.UnprotectedScouts = 1;
            return f;
        }
    }
}
