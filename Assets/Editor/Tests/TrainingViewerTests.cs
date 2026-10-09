using System;
using System.Collections;
using System.IO;
using BlockNations.Training;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class TrainingViewerTests
{
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
        Assert.That(File.ReadAllText(Path.Combine(run, "viewer-watch.json")), Does.Contain("\"live\":true"));
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
