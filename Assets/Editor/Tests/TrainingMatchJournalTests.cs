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
            Assert.That(result.firstSeat, Is.EqualTo(winner));
            Assert.That(result.interrupted, Is.False);
            Assert.That(result.session, Is.EqualTo(opening.session));
            Assert.That(result.sequence, Is.EqualTo(opening.sequence + 1));
            Assert.That(result.boardSize, Is.EqualTo(5));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public void StandardBackfillJoinsOpeningsAcrossRotationAndIgnoresLegacyTerminalStarter()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bn-turn-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "match-events.jsonl.1"),
                JsonUtility.ToJson(new TrainingMatchJournal.MatchEvent { kind = "begin", boardSize = 6,
                    session = "old", match = 1, firstSeat = 1 }) + "\n");
            var records = new[] {
                new TrainingMatchJournal.MatchEvent { kind = "end", session = "old", match = 1, winner = 1 },
                new TrainingMatchJournal.MatchEvent { kind = "begin", session = "new", match = 1, firstSeat = 1 },
                new TrainingMatchJournal.MatchEvent { kind = "end", session = "new", match = 1, winner = 0 },
                new TrainingMatchJournal.MatchEvent { kind = "begin", session = "new", match = 2, firstSeat = 0 },
                new TrainingMatchJournal.MatchEvent { kind = "end", session = "new", match = 2, interrupted = true },
                new TrainingMatchJournal.MatchEvent { kind = "end", session = "new", match = 3, winner = 0 } // Missing opening.
            };
            foreach (var record in records)
            {
                record.boardSize = 6;
                File.AppendAllText(Path.Combine(directory, "match-events.jsonl"), JsonUtility.ToJson(record) + "\n");
            }
            var progress = new TrainingProgressHistory { boardSize = 6, fullBoardMatches = 4,
                fullBoardCaptures = 3, fullBoardInterruptions = 1 };
            TrainingMatchJournal.RestoreStandardTurnOrder(directory, progress);
            Assert.That(progress.firstPlayerWins, Is.EqualTo(1));
            Assert.That(progress.secondPlayerWins, Is.EqualTo(1));
            Assert.That(progress.turnOrderLimits, Is.EqualTo(1));
            Assert.That(progress.turnOrderTrackedMatches, Is.EqualTo(3));
            Assert.That(progress.IsValid(), Is.True);
            TrainingMatchJournal.RestoreStandardTurnOrder(directory, progress);
            Assert.That(progress.turnOrderTrackedMatches, Is.EqualTo(3), "Resume must not add history twice.");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public void ForeignBoardAndUnmatchedSessionResultsAreNotBackfilled()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bn-turn-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllLines(Path.Combine(directory, "match-events.jsonl"), new[] {
                JsonUtility.ToJson(new TrainingMatchJournal.MatchEvent { boardSize = 5, kind = "begin", session = "old", match = 1 }),
                JsonUtility.ToJson(new TrainingMatchJournal.MatchEvent { boardSize = 5, kind = "end", session = "old", match = 1, winner = 0 }),
                JsonUtility.ToJson(new TrainingMatchJournal.MatchEvent { boardSize = 6, kind = "begin", session = "old", match = 2 }),
                JsonUtility.ToJson(new TrainingMatchJournal.MatchEvent { boardSize = 6, kind = "end", session = "new", match = 2, winner = 0 }) });
            var progress = new TrainingProgressHistory { boardSize = 6, fullBoardMatches = 2, fullBoardCaptures = 2 };
            TrainingMatchJournal.RestoreStandardTurnOrder(directory, progress);
            Assert.That(progress.turnOrderTrackedMatches, Is.Zero);
            Assert.That(progress.IsValid(), Is.True);
        }
        finally { Directory.Delete(directory, true); }
    }
}
