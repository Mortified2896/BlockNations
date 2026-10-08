using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using BlockNations.AI;
using BlockNations.Simulation;

// A reproducible environment benchmark, not learning or strength evaluation.
// It compiles the actual Unity-used sources without any Unity assembly/package.
internal static class Program
{
    private static int Main(string[] args)
    {
        int games = Argument(args, "--games", 100), size = Argument(args, "--size", 7), seed = Argument(args, "--seed", 42);
        bool encode = !args.Contains("--rules-only");
        if (games < 1 || !LearnedActionSchema.SupportsBoard(size)) throw new ArgumentException("Choose positive games and a supported policy board size.");
        var random = new Random(seed);
        long actions = 0, captures = 0, limits = 0, allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var clock = Stopwatch.StartNew();
        for (int game = 0; game < games; game++)
        {
            int first = random.Next(2);
            var state = new MatchState(size, size, 2, UnitRegistry.AllDefinitions, first, firstSeat: first);
            state.AddCity(new SimulationCity(1, 0, state.Position(1, 1)));
            state.AddCity(new SimulationCity(2, 1, state.Position(size - 2, size - 2)));
            state.SetGold(0, 2); state.SetGold(1, 2); MatchEngine.BeginTurn(state, first);
            var observer = new SimulationObservationSource();
            observer.SetPublicStartingCities(state.Cities.Select(city => new AICityState {
                Seat = city.Seat, X = city.Position % size, Y = city.Position / size }).ToArray());
            for (int step = 0; step < 20000 && !state.GameOver && state.Round <= 100; step++)
            {
                MatchCommand command;
                if (encode)
                {
                    var context = observer.Observe(state, state.CurrentTurnSeat);
                    AIAction action = context.Observation.LegalActions[random.Next(context.Observation.LegalActions.Length)];
                    // Exercise both stages and fixed-shape policy encoding without running a network.
                    int source = LearnedActionSchema.Choices(context.Observation, -1).First(pair =>
                        action.Kind == AIActionKind.EndTurn ? pair.Key == LearnedActionSchema.EndTurn :
                        LearnedActionSchema.Choices(context.Observation, pair.Key).Values.Contains(action)).Key;
                    LearnedActionSchema.Encode(context.Observation, -1);
                    if (source != LearnedActionSchema.EndTurn) LearnedActionSchema.Encode(context.Observation, source);
                    command = context.Commands[action.Key];
                }
                else
                {
                    var legal = MatchEngine.LegalActions(state, state.CurrentTurnSeat);
                    command = legal[random.Next(legal.Count)].Command;
                }
                if (!MatchEngine.Apply(state, command).Applied) throw new InvalidOperationException("Legal action was rejected.");
                actions++;
            }
            if (state.GameOver) captures++; else limits++;
        }
        clock.Stop();
        Console.WriteLine(JsonSerializer.Serialize(new {
            rulesVersion = SimulationRules.Version, backend = "standalone-dotnet", boardSize = size, games, seed,
            policyEncoding = encode, actions, captures, limits, seconds = clock.Elapsed.TotalSeconds,
            actionsPerSecond = actions / clock.Elapsed.TotalSeconds,
            allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocatedBefore,
            peakResidentBytes = Process.GetCurrentProcess().PeakWorkingSet64 > 0 ? (long?)Process.GetCurrentProcess().PeakWorkingSet64 : null,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            note = "Random environment benchmark; excludes PPO, network inference, RPC and rendering."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    private static int Argument(string[] args, string name, int fallback)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? int.Parse(args[index + 1]) : fallback;
    }
}
