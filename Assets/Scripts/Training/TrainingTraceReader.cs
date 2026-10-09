using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlockNations.AI;
using BlockNations.Simulation;
using BlockNations.Training;
using UnityEngine;

// Only the viewer maps data records to authored sprites. Workers never load assets.
public sealed class TrainingTraceReader
{
    private readonly SimulationReplayProjector projector;
    private readonly UnitDefinition[] roster;
    private readonly Dictionary<string, (long stamp, bool markers, TrainingReplayHistory.Game game)> cache = new Dictionary<string, (long, bool, TrainingReplayHistory.Game)>();
    public TrainingTraceReader(SimulationReplayProjector projector, IEnumerable<UnitDefinition> roster)
    { this.projector = projector; this.roster = roster.ToArray(); }

    public TrainingReplayHistory.Game Read(string path, bool showActionMarkers = true)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 2_000_000 || (file.Attributes & FileAttributes.ReparsePoint) != 0) return null;
        long stamp = file.LastWriteTimeUtc.Ticks;
        if (cache.TryGetValue(path, out var saved) && saved.stamp == stamp && saved.markers == showActionMarkers) return saved.game;
        var trace = JsonUtility.FromJson<TrainingTrace>(File.ReadAllText(path));
        // A resumed run may retain recent matches from its previous rules version.
        // Preserve those files; do not reinterpret their action budgets as new rules.
        if (trace != null && trace.rulesVersion != SimulationRules.Version) return null;
        if (trace == null || trace.version != TrainingTrace.Version || trace.rulesVersion != SimulationRules.Version ||
            !LearnedActionSchema.SupportsBoard(trace.boardSize) || trace.worker < 0 || trace.worker >= 16 || trace.match < 1 ||
            !Guid.TryParseExact(trace.session, "N", out _) || trace.frames == null || trace.frames.Count == 0 ||
            trace.frames.Count > TrainingTrace.MaximumFrames || trace.frames.Any(f => f == null || f.units == null || f.cities == null) ||
            trace.frames.Sum(f => f.units.Length + f.cities.Length) > TrainingTrace.MaximumPieces)
            throw new ArgumentException("Incompatible or invalid spectator trace.");
        string policy = trace.policy == null ? "Policy assignment unavailable" :
            $"Learning seat {trace.policy.learningSeat} · {Short(trace.policy.learner)} vs {Short(trace.policy.opponent)}";
        if (trace.policyChanged) policy += " · weights changed during match";
        var game = new TrainingReplayHistory.Game { number = trace.match, boardSize = trace.boardSize, firstSeat = trace.firstSeat,
            worker = trace.worker, sourceKey = trace.session + ":" + trace.worker + ":" + trace.match, policyLabel = policy, truncated = trace.truncated };
        foreach (TrainingTraceState frame in trace.frames)
        {
            if (frame.width != trace.boardSize || frame.firstSeat != trace.firstSeat) throw new ArgumentException("Replay configuration changed during match.");
            game.frames.Add(projector.Capture(frame.Restore(roster), frame.description, showActionMarkers));
        }
        cache[path] = (stamp, showActionMarkers, game);
        return game;
    }
    public IEnumerable<TrainingReplayHistory.Game> Recent(string run, int count)
    {
        var files = new List<FileInfo>();
        for (int worker = 0; worker < count; worker++)
        {
            string folder = Path.Combine(run, "workers", worker.ToString(), "replays");
            if (!Directory.Exists(folder) || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
            for (int slot = 0; slot < 4; slot++)
            { var file = new FileInfo(Path.Combine(folder, $"recent-{slot}.json")); if (file.Exists) files.Add(file); }
        }
        // Oldest to newest, as expected by the bounded replay history.
        foreach (FileInfo file in files.OrderByDescending(f => f.LastWriteTimeUtc).ThenBy(f => f.FullName).Take(4).Reverse())
        { var game = Read(file.FullName); if (game != null) yield return game; }
    }
    private static string Short(string id) => string.IsNullOrEmpty(id) ? "unknown" : id.Substring(0, Math.Min(8, id.Length));
}
