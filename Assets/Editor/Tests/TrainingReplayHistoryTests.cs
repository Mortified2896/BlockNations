using NUnit.Framework;

public sealed class TrainingReplayHistoryTests
{
    private static TrainingReplayHistory.Frame Frame(string description = "action", int pieces = 2) =>
        new TrainingReplayHistory.Frame { description = description, pieces = new TrainingReplayHistory.Piece[pieces] };

    [Test]
    public void InspectionUsesLatestCompletedGamesAndDoesNotChangeWhenTrainingContinues()
    {
        var history = new TrainingReplayHistory();
        Assert.That(history.Inspect(0), Is.False);
        for (int i = 1; i <= 8; i++) { history.Begin(i, 5, 0, Frame("opening")); history.Complete(Frame("win")); }
        Assert.That(history.CompletedCount, Is.EqualTo(4));
        history.Inspect(0);
        Assert.That(history.CurrentGame.number, Is.EqualTo(8));
        history.Begin(9, 5, 1, Frame()); history.Complete(Frame());
        Assert.That(history.CurrentGame.number, Is.EqualTo(8));
        history.SelectGame(1, 0);
        Assert.That(history.CurrentGame.number, Is.EqualTo(7));
        history.BackToLive(); history.Inspect(0);
        Assert.That(history.CurrentGame.number, Is.EqualTo(9));
    }

    [Test]
    public void SlowPlaybackPauseStepAndReturnAreIndependentOfRecording()
    {
        var history = new TrainingReplayHistory();
        history.Begin(1, 5, 0, Frame("opening")); history.Record(Frame("recruit")); history.Complete(Frame("capture"));
        history.Inspect(0);
        history.Tick(.5); Assert.That(history.CurrentFrame.description, Is.EqualTo("opening"));
        history.Tick(1.3); Assert.That(history.CurrentFrame.description, Is.EqualTo("recruit"));
        history.Playing = false;
        history.Tick(10); Assert.That(history.CurrentFrame.description, Is.EqualTo("recruit"));
        history.Step(10); Assert.That(history.CurrentFrame.description, Is.EqualTo("capture"));
        history.BackToLive(); Assert.That(history.Inspecting, Is.False);
        Assert.That(history.CanInspect, Is.True);
    }

    [TestCase(2)]
    [TestCase(100)]
    public void FrameAndPieceBudgetsRetainTerminalStateAndDeclareOmissions(int perFrame)
    {
        var history = new TrainingReplayHistory();
        history.Begin(1, 11, 0, Frame("opening", perFrame));
        for (int i = 0; i < 1000; i++) history.Record(Frame("move", perFrame));
        history.Complete(Frame("terminal", perFrame)); history.Inspect(0);
        var game = history.CurrentGame;
        Assert.That(game.truncated, Is.True);
        Assert.That(game.frames.Count, Is.LessThanOrEqualTo(TrainingReplayHistory.MaximumFrames));
        int pieces = 0; foreach (var frame in game.frames) pieces += frame.pieces.Length;
        Assert.That(pieces, Is.LessThanOrEqualTo(TrainingReplayHistory.MaximumPieces));
        Assert.That(game.frames[game.frames.Count - 1].description, Is.EqualTo("terminal"));
    }

    [Test]
    public void TrainerResetDiscardsPartialMatchInsteadOfPublishingIt()
    {
        var history = new TrainingReplayHistory();
        history.Begin(1, 5, 0, Frame("discarded"));
        history.Begin(1, 5, 1, Frame("fresh")); history.Complete(Frame()); history.Inspect(0);
        Assert.That(history.CurrentFrame.description, Is.EqualTo("fresh"));
        Assert.That(history.CompletedCount, Is.EqualTo(1));
    }
}
