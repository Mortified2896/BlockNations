using System;

// Optional verified benchmark telemetry; gameplay/save and legacy Elo formats remain independent.
[Serializable]
public sealed class TrainingReferenceScore
{
    public int version, boardSize;
    public string source, runId, rulesVersion, suiteId;
    public double updatedUnix;
    public Result champion, latest;
    [Serializable] public sealed class Result
    {
        public string checkpoint;
        public int games, wins, losses, interruptions, firstWins, firstGames, secondWins, secondGames;
        public float Percent => games > 0 ? 100f * wins / games : 0;
        public bool IsValid() => !string.IsNullOrEmpty(checkpoint) && games > 0 && wins >= 0 && losses >= 0 &&
            interruptions >= 0 && wins + losses + interruptions == games && firstGames > 0 && secondGames > 0 &&
            firstGames + secondGames == games && firstWins >= 0 && firstWins <= firstGames &&
            secondWins >= 0 && secondWins <= secondGames && firstWins + secondWins == wins;
    }
    public bool IsValid(int expectedBoard, string expectedRun, string expectedRules) =>
        version == 1 && source == "verified-fixed-reference-v1" && boardSize == expectedBoard && runId == expectedRun &&
        rulesVersion == expectedRules && !string.IsNullOrEmpty(suiteId) && champion != null && latest != null &&
        champion.IsValid() && latest.IsValid() && champion.games == latest.games;
}

[Serializable]
public sealed class TrainingEvaluationStatus
{
    public string owner, runId, state, stage, reason, lastOutcome, lastReason;
    public long candidateStep, lastCandidateStep;
    public double updatedUnix;
    public bool reviewRequired;
    public bool IsValid(string expectedRun) => owner == "BlockNations.AutomaticEvaluation.v1" && runId == expectedRun &&
        updatedUnix > 0 && !double.IsNaN(updatedUnix) && !double.IsInfinity(updatedUnix);
    public string Summary => reviewRequired || state == "review_required" ? "Training paused: benchmark regression needs review." :
        state == "evaluating" ? (stage == "refreshing_baseline" ? "Refreshing champion benchmark…" :
            stage == "confirming" ? "Checking a separate seed pack…" : $"Evaluating checkpoint {candidateStep:N0}…") :
        lastOutcome == "failed" || state == "error" ? "Benchmark failed. The retained champion is preserved." :
        lastOutcome == "promoted" ? "A stronger candidate passed both seed packs and became the playtest champion." :
        lastOutcome == "retained" ? "Latest candidate did not pass promotion. The champion is retained." :
        "Waiting for the next benchmark.";
}
