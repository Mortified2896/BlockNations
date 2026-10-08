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
    public void Begin(int match, int firstSeat) => Write(new MatchEvent { kind = "begin", match = match, firstSeat = firstSeat });
    public void Complete(int match, int winner, bool interrupted, int blueDecisions = 0, int redDecisions = 0) =>
        Write(new MatchEvent { kind = "end", match = match, winner = winner, interrupted = interrupted,
            blueDecisions = blueDecisions, redDecisions = redDecisions });
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
