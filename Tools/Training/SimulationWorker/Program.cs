using System.Diagnostics;
using System.Text.Json;
using BlockNations.AI;
using BlockNations.Simulation;
using BlockNations.Training;

// One bounded batch of independent matches. stdout is exclusively the versioned
// request/response transport; diagnostics go to stderr. No Unity runtime is loaded.
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private sealed class ActionRequest { public int agent { get; set; } public int action { get; set; } }
    private sealed class Request { public string op { get; set; } public ActionRequest[] actions { get; set; } public PolicyAssignment policy { get; set; } }
    private sealed class Watch { public int worker { get; set; } public bool live { get; set; } }
    private static void OwnedDirectory(string path)
    {
        if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Training directories must not be symbolic links.");
        Directory.CreateDirectory(path);
    }
    private static void Atomic(string path, object value)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Training artifacts must not be symbolic links.");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, Json)); File.Move(path + ".tmp", path, true);
    }
    private static string Argument(string[] args, string name, string fallback)
    { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }

    private static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Run(string[] args)
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Protocol uses little-endian float32.");
        string run = Path.GetFullPath(Argument(args, "--run", ".")), session = Argument(args, "--session", "");
        int count = int.Parse(Argument(args, "--workers", "1")), size = int.Parse(Argument(args, "--size", "7")), seed = int.Parse(Argument(args, "--seed", "42"));
        int distance = int.Parse(Argument(args, "--distance", "2"));
        bool curriculum = bool.Parse(Argument(args, "--curriculum", "false"));
        if (count < 1 || count > 16 || !Guid.TryParseExact(session, "N", out _) || distance < 2 || distance > 8 || distance % 2 != 0)
            throw new ArgumentException("Invalid worker/session configuration.");
        OwnedDirectory(run);
        var arenas = Enumerable.Range(0, count).Select(i => new SelfPlayArena(i, size, unchecked(seed + i * 7919), curriculum, distance, UnitRegistry.AllDefinitions)).ToArray();
        string progressPath = Path.Combine(run, $"board-{size}-progress.json");
        var progress = File.Exists(progressPath) ? JsonSerializer.Deserialize<TrainingProgressHistory>(File.ReadAllText(progressPath), Json) : new TrainingProgressHistory { boardSize = size };
        if (progress == null || !progress.IsValid() || progress.boardSize != size) throw new InvalidOperationException("Invalid saved training progress.");
        OwnedDirectory(Path.Combine(run, "workers"));
        foreach (var arena in arenas)
        {
            OwnedDirectory(Path.Combine(run, "workers", arena.Worker.ToString()));
            OwnedDirectory(Path.Combine(run, "workers", arena.Worker.ToString(), "replays"));
        }
        long sequence = 0;
        double nextPublish = 0, nextWatch = 0;
        Watch watch = null;
        bool initialReset = true;
        var clock = Stopwatch.StartNew();
        string journalPath = Path.Combine(run, "match-events.jsonl");
        while (Console.ReadLine() is string line)
        {
            if (line.Length > 131072) throw new InvalidOperationException("Oversized environment request.");
            Request request = JsonSerializer.Deserialize<Request>(line, Json) ?? throw new InvalidOperationException("Missing request.");
            if (request.op == "close") break;
            var packets = new ArenaAgentStep[count][];
            if (request.op == "reset")
            {
                if (!initialReset) foreach (var arena in arenas) arena.Reset(trainerReset: true);
                initialReset = false;
                for (int i = 0; i < count; i++) packets[i] = new[] { arenas[i].Decision() };
            }
            else if (request.op == "status")
                for (int i = 0; i < count; i++) packets[i] = Array.Empty<ArenaAgentStep>();
            else if (request.op == "step")
            {
                var commands = new Dictionary<int, int>();
                foreach (ActionRequest action in request.actions ?? Array.Empty<ActionRequest>())
                    if (action.agent < 0 || action.agent >= count * 2 || !commands.TryAdd(action.agent, action.action)) throw new InvalidOperationException("Invalid or duplicate agent action.");
                long before = arenas.Sum(a => a.Decisions);
                // Every arena owns its RNG, observation memory, source selection and match.
                Parallel.For(0, count, i => {
                    var arena = arenas[i]; int agent = i * 2 + arena.State.CurrentTurnSeat;
                    if (commands.ContainsKey(i * 2 + 1 - arena.State.CurrentTurnSeat)) throw new InvalidOperationException("Action belongs to a non-owning seat.");
                    packets[i] = arena.Advance(commands.TryGetValue(agent, out int action) ? action : null, request.policy);
                });
                progress.decisions += arenas.Sum(a => a.Decisions) - before;
                foreach (var arena in arenas)
                {
                    foreach (ArenaMatchEvent result in arena.Events)
                    {
                        result.session = session; result.sequence = ++sequence;
                        if (File.Exists(journalPath) && new FileInfo(journalPath).Length > 8 * 1024 * 1024) File.Move(journalPath, journalPath + ".1", true);
                        File.AppendAllText(journalPath, JsonSerializer.Serialize(result, Json) + "\n");
                        if (result.kind == "end") progress.RecordMatch(result.fullOpening, result.interrupted, result.winner, result.firstSeat);
                    }
                    if (arena.CompletedTrace != null)
                    {
                        arena.CompletedTrace.session = session;
                        Atomic(Path.Combine(run, "workers", arena.Worker.ToString(), "replays", $"recent-{arena.Games % 4}.json"), arena.CompletedTrace);
                    }
                }
            }
            else throw new InvalidOperationException("Unsupported environment command.");
            double now = clock.Elapsed.TotalSeconds;
            if (now >= nextWatch)
            {
                nextWatch = now + .25; string path = Path.Combine(run, "viewer-watch.json");
                watch = File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromSeconds(10)
                    ? JsonSerializer.Deserialize<Watch>(File.ReadAllText(path), Json) : null;
                if (watch != null && watch.live && watch.worker >= 0 && watch.worker < count)
                {
                    var arena = arenas[watch.worker];
                    Atomic(Path.Combine(run, "workers", watch.worker.ToString(), "live.json"), new TrainingTrace {
                        session = session, worker = watch.worker, match = arena.Trace.match, boardSize = size,
                        firstSeat = arena.State.FirstSeat, rulesVersion = SimulationRules.Version, policy = arena.Trace.policy,
                        policyChanged = arena.Trace.policyChanged, frames = new List<TrainingTraceState> { arena.Trace.frames[0], TrainingTraceState.Capture(arena.State, arena.LastAction) } });
                }
            }
            var stats = arenas.Select(a => new {
                worker = a.Worker, match = a.Trace.match, decisions = a.Decisions, actions = a.Actions, games = a.Games,
                captures = a.Captures, interruptions = a.Interruptions, rejections = 0, trainerResets = a.Resets,
                round = a.State.Round, seat = a.State.CurrentTurnSeat, curriculumDistance = a.CurriculumDistance, seed = a.Seed,
                roundLimit = a.RoundLimit, elapsedSeconds = now, decisionsPerSecond = now > 0 ? a.Decisions / now : 0,
                fullOpening = a.FullOpening, gold0 = a.State.GoldForSeat(0), gold1 = a.State.GoldForSeat(1),
                boardSize = size, schema = LearnedActionSchema.Version, lastAction = a.LastAction, failure = "",
                simulationVersion = SimulationRules.Version, simulationBackend = "standalone-dotnet", policyVersion = ""
            }).ToArray();
            if (now >= nextPublish || request.op == "reset")
            {
                nextPublish = now + 1;
                Atomic(progressPath, progress);
                foreach (var stat in stats) Atomic(Path.Combine(run, "workers", stat.worker.ToString(), "arena-status.json"), stat);
            }
            var steps = packets.SelectMany(batch => batch).Select(step => {
                byte[] bytes = new byte[step.observation.Length * sizeof(float)]; Buffer.BlockCopy(step.observation, 0, bytes, 0, bytes.Length);
                return new { step.agent, step.seat, observation = Convert.ToBase64String(bytes), step.available, step.terminal, step.interrupted, step.reward };
            }).ToArray();
            Console.WriteLine(JsonSerializer.Serialize(new { protocol = 1, rulesVersion = SimulationRules.Version, behavior = LearnedActionSchema.BehaviorName,
                observationSize = LearnedActionSchema.ObservationSize, actionCount = LearnedActionSchema.ActionCount, steps, stats }, Json));
        }
        Atomic(progressPath, progress);
    }
}
