using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class TrainingMatchJournalTests
{
    [TestCase(0)] [TestCase(1)]
    public void CaptureResultNeedsNoLosingAgentDecisionAndHasStableSessionSequence(int winner)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bn-match-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "rating-session.json"), "{\"session\":\"regression\"}");
            var journal = new TrainingMatchJournal(directory, 5);
            journal.Begin(1, winner);
            journal.Complete(1, winner, false);
            var lines = File.ReadAllLines(Path.Combine(directory, "match-events.jsonl"));
            Assert.That(lines.Length, Is.EqualTo(2));
            var opening = JsonUtility.FromJson<TrainingMatchJournal.MatchEvent>(lines[0]);
            var result = JsonUtility.FromJson<TrainingMatchJournal.MatchEvent>(lines[1]);
            Assert.That(opening.firstSeat, Is.EqualTo(winner));
            Assert.That(result.winner, Is.EqualTo(winner));
            Assert.That(result.interrupted, Is.False);
            Assert.That(result.session, Is.EqualTo(opening.session));
            Assert.That(result.sequence, Is.EqualTo(opening.sequence + 1));
            Assert.That(result.boardSize, Is.EqualTo(5));
        }
        finally { Directory.Delete(directory, true); }
    }
}
