using System.Security.Cryptography;
using System.Text.Json;
using BlockNations.AI;
using BlockNations.Simulation;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--self-test" })) return ContractChecks.Run();
        if (args.Length != 2 || args[0] != "--fixtures")
            throw new ArgumentException("Use --self-test or --fixtures <new-file>.");
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Fixtures require float32 little endian.");
        var fixtures = new List<object>();
        foreach (int size in new[] { 5, 6, 7, 9, 11 })
        foreach (int seat in new[] { 0, 1 })
        foreach (int first in new[] { 0, 1 })
        {
            var context = Observe(Position(size, seat, first), seat);
            foreach (int source in Sources(context.Observation))
            {
                float[] legacy = LearnedActionSchema.Encode(context.Observation, source);
                float[] expanded = LearnedObservationSchemaV3.Encode(context.Observation, source, context.PublicFirstSeat);
                fixtures.Add(new { name = $"board-{size}-seat-{seat}-first-{first}-source-{source}", boardSize = size,
                    seat, firstSeat = context.PublicFirstSeat, source, legacy = Bytes(legacy), expanded = Bytes(expanded),
                    available = LearnedObservationSchemaV3.Choices(context.Observation, source, context.PublicFirstSeat).Keys.OrderBy(v => v).ToArray() });
            }
        }
        byte[] contents = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, observationSchema = LearnedObservationSchemaV3.Version,
            actionSchema = LearnedObservationSchemaV3.ActionSchemaVersion, legacyObservationSize = LearnedActionSchema.ObservationSize,
            observationSize = LearnedObservationSchemaV3.ObservationSize, actionCount = LearnedObservationSchemaV3.ActionCount,
            encoding = "float32-le", rulesVersion = MatchRuleProfile.CoreThreeVersion, fixtures });
        if (contents.Length > 8 * 1024 * 1024) throw new InvalidOperationException("Fixture output exceeded its bound.");
        using (var output = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write)) output.Write(contents);
        Console.WriteLine(JsonSerializer.Serialize(new { fixtures = fixtures.Count, bytes = contents.Length,
            sha256 = Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant() }));
        return 0;
    }

    internal static MatchState Position(int size, int seat, int first)
    {
        var rules = MatchRuleProfile.CoreThree;
        var state = new MatchState(size, size, 2, rules.PolicyCatalog, seat, round: 4, firstSeat: first, ruleProfile: rules);
        int Point(int x, int y) => seat == 0 ? state.Position(x, y) : state.Position(size - 1 - x, size - 1 - y);
        state.SetGold(seat, 5); state.SetGold(1 - seat, 5);
        state.AddCity(new SimulationCity(1, seat, Point(1, 1)));
        state.AddCity(new SimulationCity(2, 1 - seat, Point(size - 2, size - 2)));
        rules.TryGetDefinition("archer", out var archer);
        rules.TryGetDefinition("rider", out var rider);
        rules.TryGetDefinition("warrior", out var warrior);
        state.AddUnit(new SimulationUnit(1, seat, Point(1, 2), archer));
        state.AddUnit(new SimulationUnit(2, seat, Point(2, 1), rider));
        state.AddUnit(new SimulationUnit(3, seat, Point(0, 1), warrior));
        state.AddUnit(new SimulationUnit(4, 1 - seat, Point(size - 2, size - 3), rider));
        return state;
    }

    internal static SimulationObservationSource.Context Observe(MatchState state, int seat)
    {
        var observer = new SimulationObservationSource();
        observer.SetPublicStartingCities(state.Cities.Select(c => new AICityState {
            Seat = c.Seat, X = c.Position % state.Width, Y = c.Position / state.Width }).ToArray());
        return observer.Observe(state, seat);
    }

    internal static int[] Sources(AIObservation observation) => new[] { -1 }.Concat(
        LearnedActionSchema.Choices(observation, -1).Keys.Where(v => v != LearnedActionSchema.EndTurn).OrderBy(v => v)).ToArray();

    private static string Bytes(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }
}
