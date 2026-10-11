using BlockNations.AI;
using BlockNations.Simulation;
using BlockNations.Training;

internal static class ReplayChecks
{
    internal static TrainingTrace Game()
    {
        var state = new MatchState(7, 7, 2, MatchRuleProfile.CoreThree.PolicyCatalog, 0, firstSeat: 0, ruleProfile: MatchRuleProfile.CoreThree);
        state.SetGold(0, 2); state.SetGold(1, 2);
        state.AddCity(new SimulationCity(1, 0, 8)); state.AddCity(new SimulationCity(2, 1, 40));
        var trace = new TrainingTrace { boardSize = 7, firstSeat = 0, rulesVersion = state.RulesVersion };
        trace.Record(TrainingTraceState.Capture(state, "Opening"));
        var observer = new SimulationObservationSource();
        observer.SetPublicStartingCities(new[] { new AICityState { Seat = 0, X = 1, Y = 1 }, new AICityState { Seat = 1, X = 5, Y = 5 } });
        void Apply(MatchActionKind kind, int destination = -1, string type = null)
        {
            var commands = observer.Observe(state, state.CurrentTurnSeat).Commands.Values;
            var command = commands.Single(c => c.Kind == kind && c.Destination == destination && c.RecruitType == type);
            if (!MatchEngine.Apply(state, command).Applied) throw new Exception("Replay fixture action was rejected.");
            trace.Record(TrainingTraceState.Capture(state, "Recorded legal command", state.GameOver));
        }
        Apply(MatchActionKind.Recruit, 8, "archer"); Apply(MatchActionKind.Move, 9); Apply(MatchActionKind.EndTurn);
        Apply(MatchActionKind.Recruit, 40, "rider"); Apply(MatchActionKind.Move, 24); Apply(MatchActionKind.EndTurn);
        Apply(MatchActionKind.Move, 2); Apply(MatchActionKind.EndTurn);
        Apply(MatchActionKind.Move, 16); Apply(MatchActionKind.EndTurn);
        Apply(MatchActionKind.EndTurn); Apply(MatchActionKind.Move, 8);
        return trace;
    }

    internal static int Run()
    {
        var trace = Game();
        var cases = ReplayCaseExtractor.Extract(trace);
        if (!cases.Any(c => Program.Build(c).decisions.Skip(1).SelectMany(d => d.analysis).Any(a => a.kind == "Attack")))
            throw new Exception("Recorded ranged opportunities were not projected.");
        if (!cases.Any(c => Program.Build(c).decisions.Skip(1).SelectMany(d => d.analysis).Any(a => a.visibleWin)))
            throw new Exception("Recorded city-capture opportunity was not projected.");
        if (cases.Any(c => c.previouslySeen == null || c.previouslySeen.Length != 49)) throw new Exception("Replay observation history was lost.");
        if (cases.Any(c => c.recordedChoices == null || c.recordedChoices.Length == 0)) throw new Exception("Recorded macro choices were lost.");
        foreach (var item in cases)
        {
            var labelled = Program.Build(item);
            var recorded = item.recordedChoices; item.recordedChoices = null;
            var plain = Program.Build(item); item.recordedChoices = recorded;
            if (labelled.decisions.Length != plain.decisions.Length ||
                !labelled.decisions.Zip(plain.decisions).All(pair => pair.First.model == pair.Second.model &&
                pair.First.available.SequenceEqual(pair.Second.available)))
                throw new Exception("Recorded action labels leaked into model inputs.");
            if (recorded.Any(r => r.source == -1))
            {
                if (recorded.Length != labelled.decisions.Length || recorded.Any(r => r.encoded != LearnedActionSchema.EndTurn))
                    throw new Exception("EndTurn paths were not recorded at both decision stages.");
            }
        }
        if (!cases.Any(c => Program.Build(c).decisions.Skip(1).Any(d => d.analysis.Any(a => a.visibleWin &&
            c.recordedChoices.Any(r => r.source == d.source && r.encoded == a.encoded)))))
            throw new Exception("The recorded city-capture choice was not preserved.");
        void Reject(TrainingTrace invalid)
        {
            try { ReplayCaseExtractor.Extract(invalid); throw new Exception("Accepted an invalid replay history."); }
            catch (ArgumentException) { }
        }
        trace = Game(); trace.frames[2].gold[0] += 1; Reject(trace);
        trace = Game(); trace.truncated = true; Reject(trace);
        trace = Game(); trace.frames.RemoveAt(1); Reject(trace);
        trace = Game(); trace.frames[^1].terminal = false; Reject(trace);
        trace = Game(); trace.rulesVersion = MatchRuleProfile.Baseline.Version; Reject(trace);
        trace = Game(); trace.frames[0].units = new[] { new TraceUnit { id = 20, seat = 0, position = 9, type = "rider", health = 30 } }; Reject(trace);
        trace = Game(); trace.frames[1].units[0].health = 9999; Reject(trace);
        trace = Game(); trace.frames[2].units[0].movesUsed = 9999; Reject(trace);
        Console.WriteLine("15 recorded-replay checks passed");
        return 0;
    }
}
