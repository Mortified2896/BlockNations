using System;
using System.IO;
using UnityEngine;

// The supervisor owns process/model loading. A playtest never changes the viewer's pause state.
// A separate inference arena keeps human actions out of the learner and rating journal.
public sealed class TrainingPlaytestBridge
{
    [Serializable] private sealed class SupervisorStatus
    {
        public bool playtestAvailable;
        public int checkpointCount;
        public string state;
    }
    [Serializable] private sealed class Request { public string requestId; }
    [Serializable] private sealed class Status { public string requestId, state, message, checkpoint; }
    private string requestId;
    private double requestedAt, nextPoll;
    public bool Available { get; private set; }
    public bool Busy => requestId != null;
    public string Message { get; private set; }

    public void Poll(TrainingArena arena)
    {
        if ((!arena.IsTraining && !arena.CanReturnToTraining) || string.IsNullOrEmpty(arena.TrainingRunDirectory) || Time.realtimeSinceStartupAsDouble < nextPoll) return;
        nextPoll = Time.realtimeSinceStartupAsDouble + .5;
        try
        {
            string directory = arena.TrainingRunDirectory;
            if (arena.IsHumanPlaytest)
            {
                if (arena.CanReturnToTraining && File.Exists(Path.Combine(directory, "close.request"))) arena.ReturnToTraining();
                return;
            }
            string supervisorPath = Path.Combine(directory, "supervisor-status.json");
            SupervisorStatus supervisor = File.Exists(supervisorPath) ? JsonUtility.FromJson<SupervisorStatus>(File.ReadAllText(supervisorPath)) : null;
            Available = supervisor != null && supervisor.playtestAvailable && supervisor.checkpointCount > 0 && supervisor.state == "running";
            string path = Path.Combine(directory, "playtest-status.json");
            Status status = File.Exists(path) ? JsonUtility.FromJson<Status>(File.ReadAllText(path)) : null;
            // A supervisor-owned launch can also restore an open playtest after a viewer restart.
            if (!Busy && status != null && (status.state == "starting" || status.state == "playing") &&
                Guid.TryParseExact(status.requestId, "N", out _)) requestId = status.requestId;
            if (!Busy) return;
            if (status != null && status.requestId == requestId)
            {
                Message = status.message;
                if (status.state == "finished" || status.state == "error") Finish();
            }
            else if (Time.realtimeSinceStartupAsDouble - requestedAt > 15)
            {
                Message = "The supervisor did not accept the playtest request. Training can continue.";
                File.Delete(Path.Combine(directory, "playtest.request.json"));
                Finish();
            }
        }
        catch (IOException error) { Message = "Cannot read playtest status: " + error.Message; }
    }

    public void Start(TrainingArena arena)
    {
        if (!Available || Busy || !arena.CanContinue) return;
        string path = Path.Combine(arena.TrainingRunDirectory, "playtest.request.json");
        requestId = Guid.NewGuid().ToString("N");
        requestedAt = Time.realtimeSinceStartupAsDouble;
        Message = "Opening a human match against the newest saved checkpoint…";
        try
        {
            File.WriteAllText(path + ".tmp", JsonUtility.ToJson(new Request { requestId = requestId }));
            if (File.Exists(path)) File.Replace(path + ".tmp", path, null);
            else File.Move(path + ".tmp", path);
        }
        catch (IOException error) { Message = "Cannot start playtest: " + error.Message; Finish(); }
    }

    private void Finish() => requestId = null;
}
