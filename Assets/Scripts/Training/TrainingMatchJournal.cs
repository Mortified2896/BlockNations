using System;
using System.IO;
using UnityEngine;

// Bounded, authoritative training results. Independent of Agent decisions/rewards.
public sealed class TrainingMatchJournal
{
    private const long MaximumBytes = 8 * 1024 * 1024;
    private readonly string path, session;
    private readonly int boardSize;
    private long sequence;
    private int firstSeat;
    [Serializable] private sealed class Session { public string session; }
    [Serializable] public sealed class MatchEvent
    {
        public int version = 1, boardSize, match, firstSeat, winner = -1, blueDecisions, redDecisions;
        public long sequence;
        public string session, kind;
        public bool interrupted;
    }
    public TrainingMatchJournal(string directory, int size)
    {
        path = Path.Combine(directory, "match-events.jsonl");
        boardSize = size;
        session = JsonUtility.FromJson<Session>(File.ReadAllText(Path.Combine(directory, "rating-session.json"))).session;
        if (string.IsNullOrEmpty(session)) throw new IOException("Missing rating session identity.");
    }
    public void Begin(int match, int firstSeat)
    {
        this.firstSeat = firstSeat;
        Write(new MatchEvent { kind = "begin", match = match, firstSeat = firstSeat });
    }
    public void Complete(int match, int winner, bool interrupted, int blueDecisions = 0, int redDecisions = 0) =>
        Write(new MatchEvent { kind = "end", match = match, firstSeat = firstSeat, winner = winner, interrupted = interrupted,
            blueDecisions = blueDecisions, redDecisions = redDecisions });

    // Only call for standard-only runs: old journal entries have no opening-type flag.
    // Join terminal events to openings; old terminal firstSeat values defaulted to zero.
    // Retained journal coverage may be partial. Never infer starters from seat totals.
    public static void RestoreStandardTurnOrder(string directory, TrainingProgressHistory progress)
    {
        int first = 0, second = 0, limits = 0;
        MatchEvent opening = null;
        try
        {
            foreach (string name in new[] { "match-events.jsonl.1", "match-events.jsonl" })
            {
                string file = Path.Combine(directory, name);
                if (!File.Exists(file)) continue;
                foreach (string line in File.ReadLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var result = JsonUtility.FromJson<MatchEvent>(line);
                    if (result == null || result.version != 1 || result.boardSize != progress.boardSize) { opening = null; continue; }
                    if (result.kind == "begin") { opening = result; continue; }
                    if (result.kind != "end") continue;
                    if (opening != null && opening.session == result.session && opening.match == result.match &&
                        (opening.firstSeat == 0 || opening.firstSeat == 1))
                    {
                        if (result.interrupted) limits++;
                        else if (result.winner == 0 || result.winner == 1)
                        {
                            if (result.winner == opening.firstSeat) first++; else second++;
                        }
                    }
                    opening = null;
                }
            }
        }
        catch (IOException) { return; }
        catch (ArgumentException) { return; }
        int matches = first + second + limits;
        if (matches > progress.fullBoardMatches || progress.turnOrderTrackedMatches != 0) return;
        progress.firstPlayerWins = first; progress.secondPlayerWins = second;
        progress.turnOrderLimits = limits; progress.turnOrderTrackedMatches = matches;
    }
    private void Write(MatchEvent result)
    {
        result.session = session; result.sequence = ++sequence; result.boardSize = boardSize;
        if (File.Exists(path) && new FileInfo(path).Length >= MaximumBytes)
        {
            string rotated = path + ".1";
            if (File.Exists(rotated)) File.Delete(rotated);
            File.Move(path, rotated);
        }
        File.AppendAllText(path, JsonUtility.ToJson(result) + "\n");
    }
}
