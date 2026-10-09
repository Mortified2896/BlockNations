using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlockNations.AI;
using BlockNations.Simulation;
using UnityEngine;

// Fair policy examples are separate from complete-state spectator replays and PPO
// trajectories. Only completed human wins are eligible for imitation by the trainer.
public sealed class TrainingHumanGameRecorder : IDisposable
{
    public const int Version = 1, MaximumSamples = 1024, MaximumGames = 32;
    public const long MaximumStorageBytes = 128L * 1024 * 1024;
    [Serializable] public sealed class Sample
    { public int action; public int[] legal; public string observation; }
    [Serializable] public sealed class Game
    {
        public int version = Version, schema = LearnedActionSchema.Version, boardSize, humanSeat, winnerSeat = -1;
        public string id, rulesVersion = SimulationRules.Version, policyVersion, startedUtc, finishedUtc;
        public bool completed, truncated;
        public List<Sample> samples = new List<Sample>();
    }
    private readonly TurnManager manager;
    private readonly string directory;
    private readonly SimulationObservationSource observations = new SimulationObservationSource();
    private readonly Game game;
    private List<Sample> pending;
    private bool closed;
    public string SavedPath { get; private set; }
    public string Failure { get; private set; }

    public TrainingHumanGameRecorder(TurnManager manager, string directory, int humanSeat, string policyVersion)
    {
        this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
        MatchState opening = new SceneSimulationAdapter(manager).State;
        if (!opening.IsTurnOwnedBySeat(humanSeat)) throw new InvalidOperationException("Human recording must start at the opening.");
        game = new Game { id = Guid.NewGuid().ToString("N"), boardSize = opening.Width, humanSeat = humanSeat,
            policyVersion = policyVersion, startedUtc = DateTime.UtcNow.ToString("O") };
        observations.SetPublicStartingCities(opening.Cities.Select(city => new AICityState {
            Seat = city.Seat, X = city.Position % opening.Width, Y = city.Position / opening.Width }).ToArray());
        observations.Observe(opening, humanSeat);
        manager.SimulationCommandPreparing += Preparing;
        manager.SimulationCommandCompleted += Applied;
    }

    private void Preparing(MatchState state, MatchCommand command)
    {
        pending = null;
        if (closed || command.Seat != game.humanSeat) return;
        try { pending = EncodeCommand(observations.Observe(state, game.humanSeat), command); }
        catch (ArgumentException error) { Failure = error.Message; }
        catch (InvalidOperationException error) { Failure = error.Message; }
    }

    public static List<Sample> EncodeCommand(SimulationObservationSource.Context context, MatchCommand command)
    {
        AIObservation observation = context.Observation;
        AIAction action = observation.LegalActions.Single(candidate => context.Commands[candidate.Key].Equals(command));
        var samples = new List<Sample>(2);
        var sources = LearnedActionSchema.Choices(observation, -1);
        if (action.Kind == AIActionKind.EndTurn)
        { samples.Add(Encode(observation, sources, -1, LearnedActionSchema.EndTurn)); return samples; }
        int nativePosition = action.Kind == AIActionKind.Recruit ? observation.Cities[action.Actor].Position(observation.Width) :
            observation.Units[action.Actor].Position(observation.Width);
        int source = LearnedActionSchema.CanonicalPosition(LearnedActionSchema.CanvasPosition(observation, nativePosition), observation.Seat);
        samples.Add(Encode(observation, sources, -1, source));
        var targets = LearnedActionSchema.Choices(observation, source);
        int target = targets.Single(candidate => candidate.Value.Equals(action)).Key;
        samples.Add(Encode(observation, targets, source, target));
        return samples;
    }

    private static Sample Encode(AIObservation observation, Dictionary<int, AIAction> choices, int source, int action)
    {
        if (!choices.ContainsKey(action)) throw new ArgumentException("Human action is outside its fair legal mask.");
        float[] values = LearnedActionSchema.Encode(observation, source);
        byte[] bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        // The interchange format explicitly uses float32 little endian on every host.
        if (!BitConverter.IsLittleEndian) for (int i = 0; i < bytes.Length; i += 4) Array.Reverse(bytes, i, 4);
        return new Sample { action = action, legal = choices.Keys.OrderBy(value => value).ToArray(), observation = Convert.ToBase64String(bytes) };
    }

    private void Applied(MatchState state, MatchCommand command, MatchTransition result)
    {
        if (closed) return;
        if (result.Applied)
        {
            if (pending != null)
            {
                if (game.samples.Count + pending.Count <= MaximumSamples) game.samples.AddRange(pending);
                else game.truncated = true;
            }
            // Maintain only information visible to this observer between its turns.
            observations.Observe(state, game.humanSeat);
            if (state.GameOver) Complete(state.WinnerSeat);
        }
        pending = null;
    }

    public void Complete(int winner)
    {
        if (closed) return;
        game.completed = winner >= 0 && winner < 2;
        game.winnerSeat = game.completed ? winner : -1;
        SaveAndClose();
    }

    private void SaveAndClose()
    {
        if (closed) return;
        closed = true;
        manager.SimulationCommandPreparing -= Preparing;
        manager.SimulationCommandCompleted -= Applied;
        game.finishedUtc = DateTime.UtcNow.ToString("O");
        try
        {
            Directory.CreateDirectory(directory);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Human recording directory must stay inside its owning run.");
            string contents = JsonUtility.ToJson(game);
            long bytes = System.Text.Encoding.UTF8.GetByteCount(contents);
            if (bytes > MaximumStorageBytes) throw new IOException("Human game exceeds the recording storage limit.");
            var files = new DirectoryInfo(directory).GetFiles("*.json").OrderBy(file => file.LastWriteTimeUtc).ToList();
            long total = files.Sum(file => file.Length);
            while (files.Count > 0 && (files.Count >= MaximumGames || total + bytes > MaximumStorageBytes))
            { total -= files[0].Length; files[0].Delete(); files.RemoveAt(0); }
            SavedPath = Path.Combine(directory, game.id + ".json");
            File.WriteAllText(SavedPath + ".tmp", contents);
            File.Move(SavedPath + ".tmp", SavedPath);
        }
        catch (IOException error) { Failure = "Cannot record human game: " + error.Message; SavedPath = null; }
        catch (UnauthorizedAccessException error) { Failure = "Cannot record human game: " + error.Message; SavedPath = null; }
    }

    public void Dispose() => SaveAndClose(); // Leaving mid-match records an incomplete, ineligible game.
}
