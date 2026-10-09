using System;
using System.Collections;
using System.IO;
using System.Linq;
using BlockNations.AI;
using BlockNations.Training;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class TrainingViewerTests
{
    [TestCase(4, 1400, 900)] [TestCase(4, 2400, 1398)]
    [TestCase(1, 1200, 720)] [TestCase(8, 1400, 900)] [TestCase(16, 2400, 1398)]
    public void AllGamePanelsStayInsideTheirViewportWithoutOverlapping(int count, int width, int height)
    {
        Rect area = new Rect(370, 0, width - 370, height);
        Rect[] panels = TrainingMultiBoardView.Panels(area, count);
        Assert.That(panels.Length, Is.EqualTo(count));
        for (int i = 0; i < count; i++)
        {
            Assert.That(panels[i].width, Is.GreaterThan(100)); Assert.That(panels[i].height, Is.GreaterThan(100));
            Assert.That(area.Contains(panels[i].min) && area.Contains(panels[i].max), Is.True);
            for (int j = i + 1; j < count; j++) Assert.That(panels[i].Overlaps(panels[j]), Is.False);
        }
        if (count == 4)
        {
            Assert.That(panels[0].y, Is.EqualTo(panels[1].y)); Assert.That(panels[2].y, Is.EqualTo(panels[3].y));
            Assert.That(panels[0].x, Is.EqualTo(panels[2].x)); Assert.That(panels[1].x, Is.EqualTo(panels[3].x));
        }
    }

    [UnityTest]
    public IEnumerator FourLiveBoardsRenderDistinctWorkerSnapshotsWithoutControllingTraining()
    {
        string run = Path.Combine(Path.GetTempPath(), "bn-four-viewer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        File.WriteAllText(Path.Combine(run, "run.json"), "{\"owner\":\"BlockNations.LocalTraining.v1\",\"schema\":2,\"boardSize\":7}");
        string session = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(run, "rating-session.json"), "{\"session\":\"" + session + "\"}");
        var policy = new PolicyAssignment { learningSeat = 0, learner = "fixture-a", opponent = "fixture-b" };
        for (int worker = 0; worker < 4; worker++)
        {
            string folder = Path.Combine(run, "workers", worker.ToString()); Directory.CreateDirectory(Path.Combine(folder, "replays"));
            var arena = new SelfPlayArena(worker, 7, 42 + worker * 7919, false, 2, UnitRegistry.AllDefinitions);
            for (int i = 0; i < worker * 2; i++) arena.Advance(LearnedActionSchema.EndTurn, policy);
            arena.Advance(arena.Decision().available.First(a => a != LearnedActionSchema.EndTurn), policy);
            arena.Advance(LearnedActionSchema.RecruitOffset + worker, policy);
            var trace = new TrainingTrace { session = session, worker = worker, match = 11 + worker, boardSize = 7,
                firstSeat = arena.State.FirstSeat, rulesVersion = BlockNations.Simulation.SimulationRules.Version, policy = policy,
                frames = new System.Collections.Generic.List<TrainingTraceState> { arena.Trace.frames[0], TrainingTraceState.Capture(arena.State, "fixture") } };
            File.WriteAllText(Path.Combine(folder, "live.json"), JsonUtility.ToJson(trace));
            var status = new TrainingArena.ArenaStatus { boardSize = 7, schema = 2, workerCount = 4, trainerConnected = true,
                simulationBackend = "standalone-dotnet", simulationVersion = BlockNations.Simulation.SimulationRules.Version,
                round = arena.State.Round, seat = arena.State.CurrentTurnSeat, roundLimit = 100 };
            File.WriteAllText(Path.Combine(folder, "arena-status.json"), JsonUtility.ToJson(status));
            if (worker == 0) File.WriteAllText(Path.Combine(run, "arena-status.json"), JsonUtility.ToJson(status));
        }
        TrainingSceneBuilder.PrepareViewer(run); TrainingSceneBuilder.ShowBoard();
        SessionState.SetString("BlockNations.FourViewerFixture", run);
        yield return new EnterPlayMode();
        for (int frame = 0; frame < 20; frame++) yield return null;
        var viewer = UnityEngine.Object.FindAnyObjectByType<TrainingViewer>();
        run = viewer.TrainingRunDirectory;
        string authoritative = File.ReadAllText(Path.Combine(run, "arena-status.json"));
        File.SetLastWriteTimeUtc(Path.Combine(run, "arena-status.json"), DateTime.UtcNow);
        for (int i = 0; i < 4; i++) File.SetLastWriteTimeUtc(Path.Combine(run, "workers", i.ToString(), "live.json"), DateTime.UtcNow);
        viewer.ShowingAllLive = true;
        Assert.That(viewer.ShowingLive && viewer.ShowingAllLive, Is.True);
        Assert.That(viewer.LiveBoards.Count, Is.EqualTo(4));
        for (int i = 0; i < 4; i++)
        {
            Assert.That(viewer.LiveBoards[i].Current, Is.Not.Null, viewer.Failure);
            Assert.That(viewer.LiveBoards[i].Game.worker, Is.EqualTo(i));
            Assert.That(viewer.LiveBoards[i].Game.number, Is.EqualTo(11 + i));
            Assert.That(viewer.LiveBoards[i].Current.round, Is.EqualTo(i + 1));
            Assert.That(viewer.LiveBoards[i].Current.pieces.Count(p => !p.city), Is.EqualTo(1));
            Assert.That(viewer.LiveBoards[i].Current.pieces.All(p => p.outlineSprite == null), Is.True, "Live snapshots omit action markers.");
        }
        Assert.That(Unity.MLAgents.Academy.IsInitialized, Is.False);
        Assert.That(Application.targetFrameRate, Is.EqualTo(15)); Assert.That(QualitySettings.vSyncCount, Is.Zero);
        Assert.That(File.Exists(Path.Combine(run, "viewer-watch.json")), Is.False);
        Assert.That(File.Exists(Path.Combine(run, "training-control.json")), Is.False);
        Assert.That(File.ReadAllText(Path.Combine(run, "arena-status.json")), Is.EqualTo(authoritative));
        for (int frame = 0; frame < 4; frame++) yield return null;
        string screenshot = TakeScreenshotMenu.RequestScreenshot("four-independent-live-boards");
        double deadline = Time.realtimeSinceStartupAsDouble + 10;
        while (!File.Exists(screenshot) && Time.realtimeSinceStartupAsDouble < deadline) yield return new WaitForSecondsRealtime(.1f);
        Assert.That(File.Exists(screenshot), Is.True); TestContext.WriteLine("Four-board screenshot: " + screenshot);
        viewer.SelectedWorker = 3; viewer.ShowingAllLive = false;
        Assert.That(viewer.Replay.LatestFrame, Is.SameAs(viewer.LiveBoards[3].Current));
        viewer.WatchRecent(); Assert.That(viewer.ShowingAllLive, Is.False);
        yield return new ExitPlayMode();
        Directory.Delete(SessionState.GetString("BlockNations.FourViewerFixture", ""), true);
        SessionState.EraseString("BlockNations.FourViewerFixture");
    }

    [UnityTest]
    public IEnumerator ViewerDefaultsToReplayWithoutAcademyAndSeparatesReplayAndTrainingControls()
    {
        string run = Path.Combine(Path.GetTempPath(), "bn-viewer-fixture-" + Guid.NewGuid().ToString("N"));
        string replayFolder = Path.Combine(run, "workers", "0", "replays");
        Directory.CreateDirectory(replayFolder);
        File.WriteAllText(Path.Combine(run, "run.json"), "{\"owner\":\"BlockNations.LocalTraining.v1\",\"schema\":2,\"boardSize\":5}");
        var arena = new SelfPlayArena(0, 5, 42, false, 2, UnitRegistry.AllDefinitions);
        var policy = new PolicyAssignment { learningSeat = 0, learner = "fixture-a", opponent = "fixture-b" };
        foreach (int action in new[] { 48, 243, 48, 72 }) arena.Advance(action, policy);
        arena.CompletedTrace.session = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(run, "rating-session.json"), "{\"session\":\"" + arena.CompletedTrace.session + "\"}");
        File.WriteAllText(Path.Combine(replayFolder, "recent-1.json"), JsonUtility.ToJson(arena.CompletedTrace));
        File.WriteAllText(Path.Combine(run, "workers", "0", "live.json"), JsonUtility.ToJson(arena.CompletedTrace));
        var status = new TrainingArena.ArenaStatus { boardSize = 5, schema = 2, workerCount = 1, trainerConnected = true,
            simulationBackend = "standalone-dotnet", simulationVersion = BlockNations.Simulation.SimulationRules.Version, games = 1, actions = 2,
            decisions = 4, round = 1, seat = arena.State.CurrentTurnSeat, gold0 = arena.State.GoldForSeat(0), gold1 = arena.State.GoldForSeat(1), fullOpening = true };
        string serializedStatus = JsonUtility.ToJson(status);
        File.WriteAllText(Path.Combine(run, "arena-status.json"), serializedStatus);
        File.WriteAllText(Path.Combine(run, "workers", "0", "arena-status.json"), serializedStatus);
        TrainingSceneBuilder.PrepareViewer(run); TrainingSceneBuilder.ShowBoard();
        SessionState.SetString("BlockNations.ViewerFixture", run);
        yield return new EnterPlayMode();
        for (int frame = 0; frame < 30; frame++) yield return null;
        TrainingViewer viewer = UnityEngine.Object.FindAnyObjectByType<TrainingViewer>();
        Assert.That(viewer, Is.Not.Null);
        run = viewer.TrainingRunDirectory; // Unity reloads the test iterator when entering Play Mode.
        serializedStatus = File.ReadAllText(Path.Combine(run, "arena-status.json"));
        File.SetLastWriteTimeUtc(Path.Combine(run, "arena-status.json"), DateTime.UtcNow);
        viewer.WatchLive(); viewer.WatchRecent(); // Refresh a heartbeat aged by Editor domain reload.
        Assert.That(viewer.ShowingLive, Is.False);
        Assert.That(viewer.Replay.Inspecting, Is.True);
        Assert.That(Unity.MLAgents.Academy.IsInitialized, Is.False, "A spectator must not initialize an ML environment.");
        Assert.That(viewer.Replay.CurrentGame.sourceKey, Does.EndWith(":0:1"));
        viewer.Replay.Playing = false;
        viewer.Replay.Step(0); // Show the recorded Rider before its winning move.
        Assert.That(File.Exists(Path.Combine(run, "training-control.json")), Is.False, "Pausing replay never pauses the learner.");
        viewer.Paused = true;
        Assert.That(File.ReadAllText(Path.Combine(run, "training-control.json")), Does.Contain("true"));
        Assert.That(File.ReadAllText(Path.Combine(run, "arena-status.json")), Is.EqualTo(serializedStatus), "The viewer never writes authoritative worker telemetry.");
        string screenshot = TakeScreenshotMenu.RequestScreenshot("decoupled-replay-view");
        double captureDeadline = Time.realtimeSinceStartupAsDouble + 10;
        while (!File.Exists(screenshot) && Time.realtimeSinceStartupAsDouble < captureDeadline)
            yield return new WaitForSecondsRealtime(.1f);
        Assert.That(File.Exists(screenshot), Is.True); TestContext.WriteLine("Replay screenshot: " + screenshot);
        viewer.WatchLive();
        File.SetLastWriteTimeUtc(Path.Combine(run, "workers", "0", "live.json"), DateTime.UtcNow);
        viewer.WatchLive();
        double liveDeadline = Time.realtimeSinceStartupAsDouble + 3;
        while (viewer.Replay.LatestFrame == null && Time.realtimeSinceStartupAsDouble < liveDeadline)
            yield return new WaitForSecondsRealtime(.1f);
        Assert.That(viewer.Replay.Inspecting, Is.False);
        Assert.That(viewer.Replay.LatestFrame, Is.Not.Null, viewer.Failure);
        Assert.That(File.Exists(Path.Combine(run, "viewer-watch.json")), Is.False, "Live display must not signal the simulation or require acknowledgements.");
        viewer.WatchRecent();
        Assert.That(viewer.Replay.Inspecting, Is.True);
        File.SetLastWriteTimeUtc(Path.Combine(run, "arena-status.json"), DateTime.UtcNow);
        viewer.WatchLive(); viewer.WatchRecent();
        viewer.StopAndSave(); Assert.That(File.Exists(Path.Combine(run, "stop.request")), Is.True);
        yield return new ExitPlayMode();
        Directory.Delete(SessionState.GetString("BlockNations.ViewerFixture", ""), recursive: true);
        SessionState.EraseString("BlockNations.ViewerFixture");
    }
}
