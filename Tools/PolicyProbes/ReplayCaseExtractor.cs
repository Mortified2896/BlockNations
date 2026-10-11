using System.Text.Json;
using BlockNations.AI;
using BlockNations.Simulation;
using BlockNations.Training;

// Analysis only. Verify complete recorded games and reconstruct the same
// observer memory at each macro-action boundary. No replay labels enter a policy.
internal static class ReplayCaseExtractor
{
    internal static int Write(string path)
    {
        if (new FileInfo(path).Length > 2_000_000) throw new ArgumentException("Replay inputs are limited to 2 MB.");
        byte[] contents = File.ReadAllBytes(path);
        var trace = JsonSerializer.Deserialize<TrainingTrace>(contents, Program.Json) ?? throw new ArgumentException("Missing replay.");
        var cases = Extract(trace);
        Console.WriteLine(JsonSerializer.Serialize(new { version = 1, rulesVersion = trace.rulesVersion,
            replaySha256 = Program.Hash(contents), frames = trace.frames.Count, verifiedTransitions = trace.frames.Count - 1,
            measurement = "verified-replay-position-joint-action-probabilities", cases }, Program.Json));
        return 0;
    }

    internal static ProbeCase[] Extract(TrainingTrace trace)
    {
        if (trace.version != TrainingTrace.Version || trace.rulesVersion != MatchRuleProfile.CoreThreeVersion ||
            trace.boardSize != 7 || trace.firstSeat < 0 || trace.firstSeat > 1 || trace.truncated ||
            trace.frames == null || trace.frames.Count < 2 || trace.frames.Count > TrainingTrace.MaximumFrames ||
            trace.frames.Any(f => f == null || f.width != 7 || f.height != 7 || f.firstSeat != trace.firstSeat ||
                f.gold?.Length != 2 || f.tiles?.Length != 49 || f.cities == null || f.units == null ||
                f.cities.Length > 49 || f.units.Length > 49 || f.round < 1 || f.seat < 0 || f.seat > 1) ||
            trace.frames.Sum(f => f.units.Length + f.cities.Length) > TrainingTrace.MaximumPieces)
            throw new ArgumentException("Use a complete, bounded 7x7 core-three replay.");
        TrainingTraceState opening = trace.frames[0], ending = trace.frames[^1];
        // Full standard games only. Tactical curriculum and incomplete histories
        // cannot silently become evidence of naturally occurring positions.
        if (opening.round != 1 || opening.seat != trace.firstSeat || opening.units.Length != 0 ||
            opening.gameOver || opening.terminal || opening.winner != -1 || !opening.tiles.All(t => t) ||
            opening.cityVision != 1 || opening.incomePerCity != 1 || !opening.gold.SequenceEqual(new[] { 2, 2 }) ||
            opening.cities.Length != 2 || !opening.cities.Any(c => c.seat == 0 && c.position == 8 && !c.recruited) ||
            !opening.cities.Any(c => c.seat == 1 && c.position == 40 && !c.recruited) ||
            !ending.terminal || !ending.gameOver || ending.winner < 0 || ending.winner > 1 ||
            trace.frames.Take(trace.frames.Count - 1).Any(f => f.terminal || f.gameOver))
            throw new ArgumentException("Use a completed city-capture game from the standard opening.");
        var rules = MatchRuleProfile.Resolve(trace.rulesVersion);
        var publicCities = opening.cities.Select(c => new AICityState { Seat = c.seat, X = c.position % 7, Y = c.position / 7 }).ToArray();
        var observer = new SimulationObservationSource();
        observer.SetPublicStartingCities(publicCities);
        var cases = new List<ProbeCase>();
        MatchState state = Program.Restore(opening, rules);
        if (Signature(state) != Signature(opening)) throw new ArgumentException("Replay opening is not a canonical simulation state.");
        for (int index = 0; index < trace.frames.Count - 1; index++)
        {
            TrainingTraceState frame = trace.frames[index];
            var context = observer.Observe(state, state.CurrentTurnSeat);
            // Compare with the recorded fields directly. Restore adapters clamp
            // legacy values; clamping must not validate an altered recording.
            string expected = Signature(trace.frames[index + 1]);
            MatchState next = null;
            var matching = new List<MatchCommand>();
            foreach (MatchCommand command in context.Commands.Values)
            {
                MatchState after = state.Copy();
                if (MatchEngine.Apply(after, command).Applied && Signature(after) == expected)
                { next ??= after; matching.Add(command); }
            }
            if (next == null) throw new ArgumentException($"Replay transition {index} does not follow a fair legal action.");
            var recorded = new List<ProbeRecordedChoice>();
            if (matching.Any(c => c.Kind == MatchActionKind.EndTurn))
                recorded.Add(new ProbeRecordedChoice { source = -1, encoded = LearnedActionSchema.EndTurn });
            foreach (int source in LearnedActionSchema.Choices(context.Observation, -1).Keys.Where(s => s != LearnedActionSchema.EndTurn))
                foreach (var choice in LearnedActionSchema.Choices(context.Observation, source))
                    if (matching.Contains(context.Commands[choice.Value.Key]))
                        recorded.Add(new ProbeRecordedChoice { source = source, encoded = choice.Key });
            var candidate = new ProbeCase { name = $"frame_{index:D3}", rulesVersion = trace.rulesVersion,
                state = frame, publicCities = publicCities, previouslySeen = (bool[])context.Observation.Seen.Clone(),
                recordedChoices = recorded.ToArray() };
            var labels = Program.Build(candidate).decisions.Skip(1).SelectMany(d => d.analysis).ToArray();
            if (labels.Any(a => a.kind == "Attack" || a.visibleWin)) cases.Add(candidate);
            // Keep the actual allocation counters after deaths/recruitment;
            // reconstructing each frame independently loses removed unit IDs.
            state = next;
        }
        return cases.ToArray();
    }

    private static string Signature(MatchState state) => Signature(TrainingTraceState.Capture(state, null));
    private static string Signature(TrainingTraceState frame) => JsonSerializer.Serialize(new {
        frame.width, frame.height, frame.seat, frame.firstSeat, frame.round, frame.cityVision, frame.incomePerCity,
        frame.gameOver, frame.winner, frame.tiles, frame.gold,
        cities = frame.cities.OrderBy(c => c.id).Select(c => new { c.id, c.seat, c.position, c.recruited }).ToArray(),
        units = frame.units.OrderBy(u => u.id).Select(u => new { u.id, u.seat, u.position,
            u.type, u.health, u.movesUsed, u.attacksUsed, u.surprisedRound }).ToArray()
    });
}
