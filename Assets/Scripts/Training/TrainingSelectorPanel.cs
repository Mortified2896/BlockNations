using System;
using System.IO;
using System.Linq;
using UnityEngine;

// A native presentation client. The owned selector service validates actions/models.
public sealed class TrainingSelectorPanel
{
    [Serializable] public sealed class Run
    {
        public string id, label, trainingVision, state;
        public bool active, paused, managed;
        public int modelCount;
        public long latestStep, games, decisions;
        public string Caption => (trainingVision == "FullVision" ? "No fog" : "Fog") + " · " + state + " · " + id;
    }
    [Serializable] public sealed class Model
    {
        public string id, runId, name, trainingVision, selection;
        public long step;
        public string Caption => selection + " · step " + step.ToString("N0");
    }
    [Serializable] public sealed class Catalog
    {
        public string owner, activeRun, desiredRun, message, requestId;
        public double updatedUnix;
        public int batteryPercent;
        public bool acPower;
        public Run[] runs;
        public Model[] models;
    }
    [Serializable] private sealed class Request
    {
        public string requestId, op, runId, modelId, matchVision, difficulty;
        public int firstSeat;
    }
    private Catalog catalog;
    private string root, selectedRun, selectedModel, pending, error;
    private bool showRuns, showModels;
    private int matchVision, modelFilter, difficulty = 2, startingRole = 2;
    private double nextPoll;
    public bool Available => catalog != null && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - catalog.updatedUnix < 15;
    public string SelectedRun => selectedRun;

    public void Poll(ITrainingView arena)
    {
        if (arena == null || arena.IsHumanPlaytest || string.IsNullOrEmpty(arena.TrainingRunDirectory)) return;
        Load(arena.TrainingRunDirectory, arena);
    }
    private void Load(string directory, ITrainingView arena)
    {
        if (Time.realtimeSinceStartupAsDouble < nextPoll) return;
        nextPoll = Time.realtimeSinceStartupAsDouble + .5;
        root = Directory.GetParent(directory)?.Parent?.FullName;
        try
        {
            string path = Path.Combine(root, "training-selector.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 2_000_000) return;
            var value = JsonUtility.FromJson<Catalog>(File.ReadAllText(path));
            if (value?.owner != "BlockNations.TrainingSelector.v1" || value.runs == null || value.models == null) return;
            catalog = value;
            if (pending == catalog.requestId) pending = null;
            if (selectedRun == null || !catalog.runs.Any(r => r.id == selectedRun))
            {
                string preferred = PlayerPrefs.GetString("TrainingSelector.Run", new DirectoryInfo(directory).Name);
                SelectRun(catalog.runs.FirstOrDefault(r => r.id == preferred) ?? catalog.runs.FirstOrDefault(), arena);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is ArgumentException) { error = ex.Message; }
    }
    public void DrawEditor(string trainingRoot, GUIStyle heading, GUIStyle text, GUIStyle small, GUIStyle button)
    {
        Load(Path.Combine(trainingRoot, "runs", PlayerPrefs.GetString("TrainingSelector.Run", "mac-7x7-core3-gentle-20261011")), null);
        Draw(null, heading, text, small, button);
    }

    private void SelectRun(Run run, ITrainingView arena)
    {
        if (run == null) return;
        selectedRun = run.id; selectedModel = null; showRuns = false;
        PlayerPrefs.SetString("TrainingSelector.Run", selectedRun);
        if (arena is TrainingViewer viewer) viewer.SelectRun(Path.Combine(root, "runs", run.id));
        SelectModel(catalog.models.FirstOrDefault(m => m.runId == selectedRun));
    }
    private void SelectModel(Model model)
    {
        selectedModel = model?.id;
        matchVision = model?.trainingVision == "FullVision" ? 1 : 0;
        showModels = false;
    }
    private void Send(string op)
    {
        try
        {
            string path = Path.Combine(root, "training-selector.request.json");
            if (File.Exists(path)) throw new IOException("A selector request is already waiting.");
            pending = Guid.NewGuid().ToString("N");
            File.WriteAllText(path + ".tmp", JsonUtility.ToJson(new Request { requestId = pending, op = op,
                runId = selectedRun, modelId = selectedModel, matchVision = matchVision == 1 ? "FullVision" : "Fog",
                difficulty = new[] { "Easy", "Medium", "Hard" }[difficulty],
                firstSeat = startingRole == 2 ? UnityEngine.Random.Range(0, 2) : startingRole }));
            File.Move(path + ".tmp", path);
        }
        catch (IOException ex) { error = ex.Message; pending = null; }
    }

    public void Draw(ITrainingView arena, GUIStyle heading, GUIStyle text, GUIStyle small, GUIStyle button)
    {
        Poll(arena);
        if (!Available) return;
        button = new GUIStyle(button) { fontSize = 13, wordWrap = true, fixedHeight = 48 };
        GUILayout.Label("Training and opponent", heading);
        Run running = catalog.runs.FirstOrDefault(r => r.id == catalog.activeRun);
        GUILayout.Label(string.IsNullOrEmpty(catalog.activeRun) ? "No training running" : "Running " +
            (running?.trainingVision == "FullVision" ? "without fog: " : "with fog: ") + catalog.activeRun, small);
        foreach (Run active in catalog.runs.Where(r => r.active && r.id != catalog.activeRun))
            GUILayout.Label("Also running: " + active.Caption, small);
        if (!string.IsNullOrEmpty(catalog.desiredRun) && catalog.desiredRun != catalog.activeRun)
            GUILayout.Label("Queued: " + catalog.desiredRun, small);
        Run selected = catalog.runs.FirstOrDefault(r => r.id == selectedRun);
        if (GUILayout.Button(selected?.Caption ?? "Choose training run", button)) showRuns = !showRuns;
        if (showRuns)
        {
            foreach (Run run in catalog.runs.OrderByDescending(r => r.managed).ThenByDescending(r => r.active))
                if (GUILayout.Button(run.Caption)) SelectRun(run, arena);
        }
        if (selected != null)
        {
            GUILayout.Label("Training visibility: " + (selected.trainingVision == "FullVision" ? "Full board for both AIs" : "Normal fog for both AIs"), text);
            GUILayout.Label(selected.latestStep > 0 ? "Latest saved step: " + selected.latestStep.ToString("N0") : "No trained model yet", small);
            GUI.enabled = pending == null && selected.managed;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(selected.active ? selected.paused ? "Resume" : "Pause" : "Start / resume", button))
                Send(selected.active ? selected.paused ? "resume" : "pause" : "start");
            if (GUILayout.Button("Stop & save", button)) Send("stop");
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            if (!selected.managed) GUILayout.Label("Historical run: available to inspect and play.", small);
        }
        GUILayout.Label(catalog.message, small);
        GUILayout.Label((catalog.acPower ? "AC power" : "Battery") + " · " + catalog.batteryPercent + "% · training starts at AC / 80%", small);
        GUILayout.Space(8);
        GUILayout.Label("Opponent model", text);
        int filter = GUILayout.Toolbar(modelFilter, new[] { "This run", "Fog-trained", "No-fog-trained" });
        if (filter != modelFilter) { modelFilter = filter; selectedModel = null; showModels = true; }
        Model model = catalog.models.FirstOrDefault(m => m.id == selectedModel);
        if (GUILayout.Button(model?.Caption ?? "Choose a saved model", button)) showModels = !showModels;
        if (showModels)
        {
            foreach (Model choice in catalog.models.Where(m => modelFilter == 0 ? m.runId == selectedRun :
                m.trainingVision == (modelFilter == 1 ? "Fog" : "FullVision")))
                if (GUILayout.Button(choice.Caption + "\n" + choice.runId, button)) SelectModel(choice);
        }
        if (model != null)
        {
            GUILayout.Label(model.name, small);
            GUILayout.Label("Trained " + (model.trainingVision == "FullVision" ? "without fog" : "with fog") + " · " + model.runId, small);
        }
        else GUILayout.Label("No model selected. Runs without a completed trained checkpoint are unavailable to play.", small);
        GUILayout.Label("Match visibility (both players)", small);
        matchVision = GUILayout.Toolbar(matchVision, new[] { "Fog", "Full board" });
        difficulty = GUILayout.Toolbar(difficulty, new[] { "Easy", "Medium", "Hard" });
        startingRole = GUILayout.Toolbar(startingRole, new[] { "You first", "AI first", "Random first" });
        GUI.enabled = pending == null && model != null;
        if (GUILayout.Button("Play selected model", button)) Send("play");
        GUI.enabled = true;
        if (!string.IsNullOrEmpty(error)) GUILayout.Label(error, small);
        GUILayout.Space(12);
    }
}
