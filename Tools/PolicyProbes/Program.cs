using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlockNations.AI;
using BlockNations.Simulation;
using BlockNations.Training;

// Development-only diagnostic positions. World-state outcome labels are never
// passed to a model: the model receives only the existing fair schema/masks.
internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { IncludeFields = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static int Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--self-test" })) { ProbeChecks.Run(); return ReplayChecks.Run(); }
        if (args.SequenceEqual(new[] { "--replay-fixture" })) { Console.WriteLine(JsonSerializer.Serialize(ReplayChecks.Game(), Json)); return 0; }
        if (args.Length == 2 && args[0] == "--replay-cases") return ReplayCaseExtractor.Write(args[1]);
        if (args.Length != 2 || args[0] != "--cases") throw new ArgumentException("Use --cases <case-file>, --replay-cases <replay-file> or --self-test.");
        if (new FileInfo(args[1]).Length > 1_048_576) throw new ArgumentException("Probe inputs are limited to 1 MiB.");
        byte[] contents = File.ReadAllBytes(args[1]);
        ProbeCase[] cases = JsonSerializer.Deserialize<ProbeCase[]>(contents, Json) ?? throw new ArgumentException("Missing probe cases.");
        if (cases.Length < 1 || cases.Length > 32 || cases.Select(c => c.name).Distinct().Count() != cases.Length)
            throw new ArgumentException("Use 1-32 uniquely named diagnostic positions.");
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Probe model vectors require float32 little-endian encoding.");
        var output = new { version = 1, schema = LearnedActionSchema.Version, encoding = "float32-le", observationSize = LearnedActionSchema.ObservationSize,
            actionCount = LearnedActionSchema.ActionCount, inputSha256 = Hash(contents),
            assemblySha256 = Hash(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location)),
            cases = cases.Select(Build).ToArray() };
        Console.WriteLine(JsonSerializer.Serialize(output, Json));
        return 0;
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static ProbeResult Build(ProbeCase input)
    {
        if (input.version != 1 || input.name == null || !Regex.IsMatch(input.name, "^[a-z0-9_-]{1,64}$") || input.state == null)
            throw new ArgumentException("Use a named, versioned probe position.");
        TrainingTraceState frame = input.state;
        if (frame.width != 7 || frame.height != 7 || frame.gold?.Length != 2 || frame.tiles?.Length != 49 ||
            frame.cities == null || frame.units == null || frame.cities.Length > 49 || frame.units.Length > 49 ||
            frame.round < 1 || frame.gameOver || frame.terminal || input.publicCities?.Length != 2 ||
            input.previouslySeen != null && input.previouslySeen.Length != 49)
            throw new ArgumentException("Use a live 7x7 position with two public starting cities and bounded pieces.");
        MatchRuleProfile rules = MatchRuleProfile.Resolve(input.rulesVersion);
        if (input.publicCities.Any(c => c.Seat < 0 || c.Seat > 1 || c.X < 0 || c.Y < 0 || c.X >= 7 || c.Y >= 7) ||
            input.publicCities.Select(c => c.Seat).Distinct().Count() != 2)
            throw new ArgumentException("Public starting cities need valid positions and distinct seats.");
        // Restore explicitly under the chosen rules; spectator Restore defaults
        // to the historical profile and is not an inference compatibility gate.
        MatchState state = Restore(frame, rules);
        var observer = new SimulationObservationSource();
        observer.SetPublicStartingCities(input.publicCities);
        var context = observer.Observe(state, state.CurrentTurnSeat, previouslySeen: input.previouslySeen);
        var observation = context.Observation;
        var decisions = new List<ProbeDecision> { Decision(observation, -1, null) };
        foreach (int source in decisions[0].available.Where(a => a != LearnedActionSchema.EndTurn))
        {
            var choices = LearnedActionSchema.Choices(observation, source);
            var labels = choices.OrderBy(p => p.Key).Select(pair => {
                MatchCommand command = context.Commands[pair.Value.Key];
                MatchState after = state.Copy();
                MatchTransition transition = MatchEngine.Apply(after, command);
                if (!transition.Applied) throw new InvalidOperationException("A fair probe action was rejected by the shared engine.");
                bool visiblePath = command.Kind != MatchActionKind.Move || MatchEngine.LegalActions(state, state.CurrentTurnSeat, observation.Visible)
                    .Single(a => a.Command.Equals(command)).Path.All(p => observation.Visible[p]);
                return new ProbeLabel { encoded = pair.Key, kind = command.Kind.ToString(), destination = command.Destination,
                    actorPosition = pair.Value.Kind == AIActionKind.EndTurn ? -1 : pair.Value.Kind == AIActionKind.Recruit ?
                        observation.Cities[pair.Value.Actor].Position(7) : observation.Units[pair.Value.Actor].Position(7),
                    actualWin = after.GameOver && after.WinnerSeat == state.CurrentTurnSeat,
                    visibleWin = after.GameOver && after.WinnerSeat == state.CurrentTurnSeat && visiblePath,
                    killedVisibleUnit = transition.KilledUnitId != 0, hiddenBlocker = transition.HiddenBlocker };
            }).ToArray();
            decisions.Add(Decision(observation, source, labels));
        }
        // Recorded choices are analysis labels only. Multiple two-stage paths
        // can produce one macro action, notably EndTurn at either stage.
        if (input.recordedChoices != null && (input.recordedChoices.Length < 1 ||
            input.recordedChoices.Length > LearnedActionSchema.Positions + 1 ||
            input.recordedChoices.Any(c => c == null) ||
            input.recordedChoices.Select(c => (c.source, c.encoded)).Distinct().Count() != input.recordedChoices.Length ||
            input.recordedChoices.Any(c => c.source == -1 && c.encoded != LearnedActionSchema.EndTurn ||
                !decisions.Any(d => d.source == c.source && d.available.Contains(c.encoded)))))
            throw new ArgumentException("Recorded macro choices must belong to the fair two-stage masks.");
        return new ProbeResult { name = input.name, rulesVersion = rules.Version, seat = state.CurrentTurnSeat,
            firstSeat = state.FirstSeat, recordedChoices = input.recordedChoices, decisions = decisions.ToArray() };
    }

    internal static MatchState Restore(TrainingTraceState frame, MatchRuleProfile rules)
    {
        var state = new MatchState(7, 7, 2, rules.PolicyCatalog, frame.seat, frame.round,
            frame.cityVision, frame.incomePerCity, frame.tiles, firstSeat: frame.firstSeat, ruleProfile: rules);
        for (int seat = 0; seat < 2; seat++) state.SetGold(seat, frame.gold[seat]);
        foreach (TraceCity city in frame.cities) state.AddCity(new SimulationCity(city.id, city.seat, city.position, city.recruited));
        foreach (TraceUnit unit in frame.units)
            state.AddUnit(new SimulationUnit(unit.id, unit.seat, unit.position, state.RecruitType(unit.type) ??
                throw new ArgumentException("Unknown probe unit type."), unit.health, unit.movesUsed, unit.attacksUsed, unit.surprisedRound));
        state.RestoreOutcome(frame.gameOver, frame.winner);
        return state;
    }

    private static ProbeDecision Decision(AIObservation observation, int source, ProbeLabel[] labels)
    {
        float[] vector = LearnedActionSchema.Encode(observation, source);
        byte[] bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return new ProbeDecision { source = source, model = Convert.ToBase64String(bytes),
            available = LearnedActionSchema.Choices(observation, source).Keys.OrderBy(a => a).ToArray(), analysis = labels };
    }
}

internal sealed class ProbeCase
{
    public int version = 1;
    public string name, rulesVersion;
    public TrainingTraceState state;
    public AICityState[] publicCities;
    public bool[] previouslySeen = null;
    public ProbeRecordedChoice[] recordedChoices = null;
}
internal sealed class ProbeResult
{
    public string name, rulesVersion;
    public int seat, firstSeat;
    public ProbeRecordedChoice[] recordedChoices;
    public ProbeDecision[] decisions;
}
internal sealed class ProbeDecision
{
    public int source;
    public string model;
    public int[] available;
    public ProbeLabel[] analysis;
}
internal sealed class ProbeLabel
{
    public int encoded, actorPosition, destination;
    public string kind;
    public bool actualWin, visibleWin, killedVisibleUnit, hiddenBlocker;
}
internal sealed class ProbeRecordedChoice
{
    public int source, encoded;
}
