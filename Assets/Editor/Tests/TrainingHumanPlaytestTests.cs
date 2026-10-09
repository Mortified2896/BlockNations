using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class TrainingHumanPlaytestTests
{
    [TestCase(false, "finished")]
    [TestCase(true, "finished")]
    [TestCase(false, "error")]
    [TestCase(true, "error")]
    public void HumanPlaytestDoesNotChangeOrLockTrainingPause(bool previousPause, string result)
    {
        string directory = Path.Combine(Path.GetTempPath(), "bn-human-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var root = new GameObject("Inactive pause test"); root.SetActive(false);
        try
        {
            TrainingArena arena = root.AddComponent<TrainingArena>();
            TrainingSceneBuilder.Set(arena, "requireTrainer", true);
            TrainingSceneBuilder.Set(arena, "statusPath", Path.Combine(directory, "arena-status.json"));
            arena.Paused = previousPause;
            File.WriteAllText(Path.Combine(directory, "supervisor-status.json"),
                "{\"state\":\"running\",\"playtestAvailable\":true,\"checkpointCount\":1}");
            arena.Playtest.Poll(arena);
            Assert.That(arena.Playtest.Available, Is.True);
            arena.Playtest.Start(arena);
            Assert.That(arena.Paused, Is.EqualTo(previousPause), "Starting a human match must leave training running or paused as it was.");
            Assert.That(arena.CanContinue, Is.True, "Training pause controls remain available during a human match.");
            arena.Paused = !previousPause; // A manual pause/resume during the match remains the user's choice.
            string id = JsonUtility.FromJson<Request>(File.ReadAllText(Path.Combine(directory, "playtest.request.json"))).requestId;
            File.WriteAllText(Path.Combine(directory, "playtest-status.json"),
                "{\"requestId\":\"" + id + "\",\"state\":\"" + result + "\",\"message\":\"returned\"}");
            typeof(TrainingPlaytestBridge).GetField("nextPoll", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(arena.Playtest, -1d);
            arena.Playtest.Poll(arena);
            Assert.That(arena.Playtest.Busy, Is.False);
            Assert.That(arena.Paused, Is.EqualTo(!previousPause), "Returning must not undo the user's latest pause/resume choice.");
            Assert.That(arena.CanContinue, Is.True);
            Assert.That(Directory.GetFiles(directory, "match-events*"), Is.Empty);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); Directory.Delete(directory, true); }
    }
    [Serializable] private sealed class Request { public string requestId; }

    [Test]
    public void SupervisorOpenedPlaytestIsDetectedWithoutPausingTraining()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bn-human-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var root = new GameObject("Inactive restored playtest"); root.SetActive(false);
        try
        {
            TrainingArena arena = root.AddComponent<TrainingArena>();
            TrainingSceneBuilder.Set(arena, "requireTrainer", true);
            TrainingSceneBuilder.Set(arena, "statusPath", Path.Combine(directory, "arena-status.json"));
            File.WriteAllText(Path.Combine(directory, "playtest-status.json"),
                "{\"requestId\":\"" + Guid.NewGuid().ToString("N") + "\",\"state\":\"playing\",\"message\":\"Training continues.\"}");
            arena.Playtest.Poll(arena);
            Assert.That(arena.Playtest.Busy, Is.True, "An open inference player disables duplicate launches.");
            Assert.That(arena.Playtest.Message, Is.EqualTo("Training continues."));
            Assert.That(arena.Paused, Is.False);
            Assert.That(arena.CanContinue, Is.True);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); Directory.Delete(directory, true); }
    }

    [Test]
    public void ConnectedHumanInferenceIsSeparateFromTrainingAndDirectSelfPlay()
    {
        var root = new GameObject("Inactive human playtest");
        root.SetActive(false);
        try
        {
            TrainingArena arena = root.AddComponent<TrainingArena>();
            TrainingSceneBuilder.Set(arena, "requireTrainer", true);
            TrainingSceneBuilder.Set(arena, "humanSeatIndex", 0);
            Assert.That(arena.IsHumanPlaytest, Is.True);
            Assert.That(arena.IsTraining, Is.False);
            Assert.That(arena.UsesDirectSimulation, Is.False);
            Assert.That(arena.HumanSeat, Is.Zero);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void ExternalHumanInputMustBeConfiguredBeforeMatchAndWithAnExplicitSeat()
    {
        var root = new GameObject("Inactive explicit external seat"); root.SetActive(false);
        try
        {
            TurnManager manager = root.AddComponent<TurnManager>();
            Assert.Throws<InvalidOperationException>(() => manager.ConfigureExternalHumanSeat(0));
            TrainingSceneBuilder.Set(manager, "externallyDrivenMatch", true);
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.ConfigureExternalHumanSeat(2));
            manager.ConfigureExternalHumanSeat(0);
            Assert.That(new SerializedObject(manager).FindProperty("externalHumanSeatIndex").intValue, Is.Zero);
            typeof(TurnManager).GetProperty(nameof(TurnManager.ExternalMatchReady)).SetValue(manager, true);
            Assert.Throws<InvalidOperationException>(() => manager.ConfigureExternalHumanSeat(1));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void HumanArenaHasAuthoredReferencesForCityUnitUIAndInput()
    {
        try
        {
            TrainingArena arena = TrainingSceneBuilder.Prepare("", 42, false, false, human: true);
            var fields = new SerializedObject(arena);
            var presentation = fields.FindProperty("humanPresentation").objectReferenceValue as TrainingHumanPresentation;
            Assert.That(presentation, Is.Not.Null);
            var wiring = new SerializedObject(presentation);
            SerializedProperty ui = wiring.FindProperty("humanUIRoots"), input = wiring.FindProperty("humanInput");
            Assert.That(ui.arraySize, Is.GreaterThanOrEqualTo(4));
            Assert.That(input.arraySize, Is.GreaterThanOrEqualTo(2));
            for (int i = 0; i < ui.arraySize; i++)
                Assert.That(((GameObject)ui.GetArrayElementAtIndex(i).objectReferenceValue).activeSelf, Is.True);
            for (int i = 0; i < input.arraySize; i++)
                Assert.That(((MonoBehaviour)input.GetArrayElementAtIndex(i).objectReferenceValue).enabled, Is.True);
        }
        finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
    }
}
