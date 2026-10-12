using System;
using BlockNations.AI;
using BlockNations.Simulation;

namespace BlockNations.Training
{
    [Serializable] public sealed class EpisodeStartCommand
    { public int kind, seat, actorId, destination, targetId; public string recruitType; }
    [Serializable] public sealed class EpisodeStartCase
    { public string id; public int firstSeat; public EpisodeStartCommand[] commands; }
    [Serializable] public sealed class EpisodeStartRecipe
    {
        public int version, boardSize, openingEconomyVersion;
        public string rulesVersion, sourcePartition;
        public double probability;
        public EpisodeStartCase[] cases;
        public void Validate(int size, string rules, int opening)
        {
            if (version != 1 || boardSize != size || rulesVersion != rules || openingEconomyVersion != opening ||
                sourcePartition != "training" || double.IsNaN(probability) || probability < 0 || probability > .2 ||
                cases == null || cases.Length < 1 || cases.Length > 256)
                throw new ArgumentException("Use a bounded training-only episode-start recipe under unchanged rules.");
            var ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var item in cases)
                if (item == null || string.IsNullOrEmpty(item.id) || item.id.Length > 128 || !ids.Add(item.id) ||
                    item.firstSeat < 0 || item.firstSeat > 1 || item.commands == null || item.commands.Length < 1 || item.commands.Length > 512)
                    throw new ArgumentException("Invalid or duplicate reachable episode-start case.");
        }
        public EpisodeStartCase Choose(Random random) => random.NextDouble() < probability ? cases[random.Next(cases.Length)] : null;
        public static void Replay(EpisodeStartCase item, MatchState state, SimulationObservationSource observer, FairEnemyHistory[] history)
        {
            foreach (var step in item.commands)
            {
                if (step == null || step.kind < 0 || step.kind > 3 || state.GameOver || step.seat != state.CurrentTurnSeat)
                    throw new ArgumentException("Episode prefix is not a legal ongoing standard-opening game.");
                var context = observer.Observe(state, step.seat);
                history[step.seat].Observe(context.Observation, state.FirstSeat);
                var command = new MatchCommand((MatchActionKind)step.kind, step.seat, step.actorId, step.destination, step.targetId, step.recruitType);
                if (!MatchEngine.Apply(state, command).Applied) throw new ArgumentException("Episode prefix contains a rejected command.");
            }
            if (state.GameOver || state.Round > 100) throw new ArgumentException("Episode start is terminal or beyond the round guard.");
        }
    }
}
