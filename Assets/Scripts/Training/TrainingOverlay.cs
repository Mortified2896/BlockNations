using System;
using UnityEngine;

// Spectator presentation only. Gold and chart data never enter policy observations.
public sealed class TrainingOverlay
{
    private GUIStyle title, label, small, button, middleTick, rightTick;
    private TrainingVision vision = TrainingVision.Shared;
    private Vector2 scroll;
    private readonly int[] displayedGold = new int[2];
    private double nextGoldRefresh;
    private bool showRunFolder;
    private double nextHumanLearningRefresh;
    private string humanLearningDirectory;
    private HumanLearningStatus humanLearning;
    [Serializable] private sealed class HumanLearningStatus
    {
        public int version, boardSize, savedGames, winningGames, updates;
        public long usedExamples;
        public string runId, rulesVersion, error;
    }
    private readonly TrainingReplayView replayView = new TrainingReplayView();
    private readonly TrainingMultiBoardView multiBoardView = new TrainingMultiBoardView();
    public static float Scale => Mathf.Clamp(Screen.height / 900f, 0.8f, 2.5f);
    public static float ReservedWidth => Mathf.Min(370f * Scale, Screen.width * 0.4f);

    public void Draw(ITrainingView arena)
    {
        if (title == null)
        {
            title = Style(21, true); label = Style(17, false); small = Style(13, false);
            button = new GUIStyle(GUI.skin.button) { fontSize = 17, fixedHeight = 38 };
            middleTick = new GUIStyle(small) { alignment = TextAnchor.UpperCenter, wordWrap = false };
            rightTick = new GUIStyle(small) { alignment = TextAnchor.UpperRight, wordWrap = false };
        }
        Matrix4x4 matrix = GUI.matrix;
        Color color = GUI.color;
        try
        {
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1));
            float width = ReservedWidth / Scale - 24;
            Fill(new Rect(12, 12, width, Screen.height / Scale - 24), new Color(0.035f, 0.13f, 0.20f, 0.97f));
            bool allLive = arena is ITrainingSpectator multiple && multiple.ShowingAllLive;
            if (allLive) multiBoardView.Draw((ITrainingSpectator)arena, vision);
            else if (arena.Replay.Inspecting)
                replayView.Draw(arena.Replay.CurrentFrame, arena.Replay.CurrentGame.frames[0], arena.Replay.CurrentGame.boardSize, vision, arena.SpectatorBackgroundColor, showActionMarkers: true);
            else if (arena.IsTraining)
                replayView.Draw(arena.Replay.LatestFrame, arena.Replay.OpeningFrame, arena.BoardSize, vision, arena.SpectatorBackgroundColor, showActionMarkers: false);
            GUILayout.BeginArea(new Rect(24, 24, width - 24, Screen.height / Scale - 48));
            scroll = GUILayout.BeginScrollView(scroll, false, false);
            if (arena is ITrainingSpectator spectator) DrawSpectatorControls(spectator);
            if (arena.IsTraining) DrawVision();
            if (arena.IsTraining) DrawHumanLearning(arena);
            if (arena.Replay.Inspecting)
            {
                DrawInspection(arena);
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }
            GUILayout.Label(arena is ITrainingSpectator ? "Simulation spectator" : arena.IsHumanPlaytest ? "Local policy playtest" : arena.IsRatingCheck ? "Frozen policy rating check" : "Live self-play training", title);
            if (arena is ITrainingSpectator waiting && !waiting.ShowingLive) GUILayout.Label("Waiting for the first completed replay…", label);
            GUILayout.Space(12);
            if (arena.IsTraining) { DrawPlaytest(arena); DrawTrainingTime(arena); DrawStorage(arena); }
            if (arena.IsHumanPlaytest)
            {
                GUILayout.Label("You are Blue and move first. Select your city to recruit, then select units to move or attack.", small);
                if (!string.IsNullOrEmpty(arena.HumanPolicyVersion)) GUILayout.Label("Frozen version: " + arena.HumanPolicyVersion, small);
                if (arena is TrainingArena human && !string.IsNullOrEmpty(human.HumanRecordingMessage))
                    GUILayout.Label(human.HumanRecordingMessage, small);
                GUILayout.Label(arena.CanStartNewMatch ? "Match finished" : arena.Paused ? "Match paused" :
                    arena.CanEndHumanTurn ? "Your turn" : "AI's turn", label);
                if (arena.CanStartNewMatch) GUILayout.Label(arena.LastAction, label);
                if (arena.CanReturnToTraining && GUILayout.Button("Back to training", button)) arena.ReturnToTraining();
            }
            if (allLive) GUILayout.Label($"{((ITrainingSpectator)arena).WorkerCount} live snapshots · {arena.BoardSize} × {arena.BoardSize}", label);
            else GUILayout.Label($"Match {arena.Games + 1} · round {arena.Round}/{arena.RoundLimit}", label);
            GUILayout.Label(arena.FullOpening ? $"Standard opening · {arena.BoardSize} × {arena.BoardSize}" :
                $"Curriculum stage {arena.CurriculumDistance} · distance {arena.StartingDistance}", small);
            GUILayout.Space(12);
            // Sample balances once per second; fixed rows keep rapid turn changes readable.
            if (Time.realtimeSinceStartupAsDouble >= nextGoldRefresh)
            {
                for (int seat = 0; seat < 2; seat++) displayedGold[seat] = arena.GoldForSeat(seat);
                nextGoldRefresh = Time.realtimeSinceStartupAsDouble + 1;
            }
            for (int seat = 0; !allLive && seat < 2; seat++)
            {
                Color seatColor = seat == 0 ? new Color(0.35f, 0.75f, 1f) : new Color(1f, 0.45f, 0.4f);
                GUI.color = seatColor;
                string gold = arena.IsHumanPlaytest && seat != arena.HumanSeat ? "Gold hidden" : $"Gold {displayedGold[seat]}";
                Rect row = GUILayoutUtility.GetRect(width - 48, 28);
                string name = seat == 0 ? "Blue" : "Red";
                GUI.Label(new Rect(row.x, row.y, 100, row.height), name + (arena.IsHumanPlaytest && seat == arena.HumanSeat ? " (you)" : " AI"), label);
                GUI.Label(new Rect(row.x + 118, row.y, row.width - 118, row.height), gold, label);
                GUI.color = Color.white;
            }
            GUILayout.Space(12);
            GUILayout.Label($"Captures {arena.Captures} · limits {arena.Interruptions}", label);
            GUILayout.Label($"{arena.Actions:N0} actions · {(arena.Elapsed > 0 ? arena.Decisions / arena.Elapsed : 0):F1} decisions/s", small);
            if (!string.IsNullOrEmpty(arena.Failure)) GUILayout.Label(arena.Failure, small);
            if (arena.IsTraining && !(arena is ITrainingSpectator))
            {
                GUI.enabled = arena.Replay.CanInspect;
                if (GUILayout.Button("Inspect recent games", button)) arena.Replay.Inspect(Time.realtimeSinceStartupAsDouble);
                GUI.enabled = true;
            }
            GUI.enabled = arena.CanContinue;
            string pauseLabel = arena.IsHumanPlaytest ? (arena.Paused ? "Continue match" : "Pause match") :
                (arena.Paused ? "Continue live run" : "Pause live run");
            if (!(arena is ITrainingSpectator) && GUILayout.Button(pauseLabel, button)) arena.Paused = !arena.Paused;
            GUI.enabled = true;
            if (arena.CanEndHumanTurn && GUILayout.Button("End your turn", button)) arena.EndHumanTurn();
            if (arena.CanStartNewMatch && GUILayout.Button("New playtest match", button)) arena.NewHumanMatch();
            if (arena.IsTraining)
            {
                GUILayout.Space(22);
                TrainingEloHistory elo = arena.EloHistory;
                GUILayout.Label("Match-result Elo", title);
                GUILayout.Label(elo == null ? "Waiting for complete match results…" :
                    $"{elo.points[elo.points.Count - 1].elo:F0} Elo · start {elo.points[0].elo:F0}", label);
                Rect chart = GUILayoutUtility.GetRect(width - 48, 140);
                if (elo != null) DrawChart(chart, elo);
                GUILayout.Label("X: rated matches · Y: self-play Elo", small);
                GUILayout.Label("New history · relative to training opponents. Not human Elo.", small);
                if (elo != null)
                {
                    GUILayout.Label(elo.FirstRecordedMatch > 0 ?
                        $"Earlier chart samples unavailable. Showing match {elo.FirstRecordedMatch:N0} onward." :
                        "Curve summarises the full recorded history.", small);
                    GUILayout.Label($"Rated {elo.wins + elo.losses:N0} · W {elo.wins:N0} / L {elo.losses:N0}", small);
                    GUILayout.Label($"Limits {elo.interruptions:N0} · policy changed {elo.unrated:N0}", small);
                }
                GUILayout.Space(12);
                TrainingProgressHistory progress = arena.Progress;
                GUILayout.Label($"{arena.BoardSize} × {arena.BoardSize} outcomes", title);
                if (progress.turnOrderTrackedMatches > 0)
                {
                    int n = progress.turnOrderTrackedMatches;
                    int captures = progress.firstPlayerWins + progress.secondPlayerWins;
                    GUILayout.Label($"First player wins {progress.firstPlayerWins:N0} ({(captures > 0 ? 100f * progress.firstPlayerWins / captures : 0):F1}%)", label);
                    GUILayout.Label($"Second player wins {progress.secondPlayerWins:N0} ({(captures > 0 ? 100f * progress.secondPlayerWins / captures : 0):F1}%)", label);
                    GUILayout.Label($"Turn limits {progress.turnOrderLimits:N0} ({100f * progress.turnOrderLimits / n:F1}%)", label);
                    GUILayout.Label($"{n:N0} matches with known starter. Win percentages exclude turn limits.", small);
                }
                else GUILayout.Label("First/second-player results await completed matches with a known starter.", small);
                GUILayout.Space(8);
                GUILayout.Label(progress.recentResults.Count > 0 ?
                    $"City capture before limit: {progress.CapturePercent:F0}% of last {progress.recentResults.Count} matches." :
                    "Awaiting completed matches.", small);
                GUILayout.Label("Training opponents also affect this split; paired evaluations isolate first-move advantage.", small);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        finally { GUI.matrix = matrix; GUI.color = color; GUI.enabled = true; }
    }

    private void DrawHumanLearning(ITrainingView arena)
    {
        if (Time.realtimeSinceStartupAsDouble >= nextHumanLearningRefresh || humanLearningDirectory != arena.TrainingRunDirectory)
        {
            nextHumanLearningRefresh = Time.realtimeSinceStartupAsDouble + 5;
            humanLearningDirectory = arena.TrainingRunDirectory;
            humanLearning = null;
            if (!string.IsNullOrEmpty(humanLearningDirectory))
            {
                string path = System.IO.Path.Combine(humanLearningDirectory, "human-learning-status.json");
                try
                {
                    if (System.IO.File.Exists(path))
                    {
                        var status = JsonUtility.FromJson<HumanLearningStatus>(System.IO.File.ReadAllText(path));
                        if (status != null && status.version == 1 && status.boardSize == arena.BoardSize &&
                            status.runId == new System.IO.DirectoryInfo(humanLearningDirectory).Name &&
                            status.rulesVersion == BlockNations.Simulation.SimulationRules.Version) humanLearning = status;
                    }
                }
                catch (System.IO.IOException) { }
                catch (ArgumentException) { }
            }
        }
        if (humanLearning == null) return;
        GUILayout.Label($"Human games: {humanLearning.savedGames} recorded · {humanLearning.winningGames} winning examples", small);
        GUILayout.Label($"Human learning: {humanLearning.updates:N0} updates · {humanLearning.usedExamples:N0} examples used", small);
        if (!string.IsNullOrEmpty(humanLearning.error)) GUILayout.Label(humanLearning.error, small);
        GUILayout.Space(8);
    }

    private void DrawInspection(ITrainingView arena)
    {
        TrainingReplayHistory replay = arena.Replay;
        TrainingReplayHistory.Game game = replay.CurrentGame;
        TrainingReplayHistory.Frame frame = replay.CurrentFrame;
        double now = Time.realtimeSinceStartupAsDouble;
        GUILayout.Label("Recent match replay", title);
        GUILayout.Label($"Match {game.number} · {game.boardSize} × {game.boardSize} · round {frame.round}", label);
        GUILayout.Label($"Blue gold {frame.blueGold}    Red gold {frame.redGold}", label);
        GUILayout.Label($"Simulation {game.worker + 1} · {(game.firstSeat == 0 ? "Blue" : "Red")} moved first", small);
        DrawTrainingTime(arena);
        DrawStorage(arena);
        GUILayout.Label(!arena.CanContinue ? "Training is stopped or disconnected." : arena.Paused ? "Live training is paused." : "Training continues at full speed.", small);
        if (!(arena is ITrainingSpectator) && GUILayout.Button("Back to Live", button)) { replay.BackToLive(); return; }
        DrawPlaytest(arena);
        GUILayout.Space(12);
        if (!string.IsNullOrEmpty(game.sourceKey)) GUILayout.Label(game.policyLabel, small);
        GUILayout.Label($"Recent game {replay.PlaylistIndex + 1}/{replay.PlaylistCount} · round {frame.round}", small);
        GUILayout.Space(12);
        GUILayout.Label($"State {replay.FrameIndex + 1}/{game.frames.Count}", small);
        GUILayout.Label(frame.description, label);
        GUILayout.Space(12);
        if (GUILayout.Button(replay.Playing ? "Pause replay" : "Play replay", button)) replay.Playing = !replay.Playing;
        if (GUILayout.Button("Next action", button)) { replay.Playing = false; replay.Step(now); }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Previous game", button)) replay.SelectGame(-1, now);
        if (GUILayout.Button("Next game", button)) replay.SelectGame(1, now);
        GUILayout.EndHorizontal();
        GUILayout.Space(12);
        GUILayout.Label("Seconds per action", small);
        int speed = replay.SecondsPerAction < 1 ? 0 : replay.SecondsPerAction < 1.5 ? 1 : 2;
        speed = GUILayout.Toolbar(speed, new[] { "0.6 s", "1.2 s", "2 s" });
        replay.SecondsPerAction = speed == 0 ? .6 : speed == 1 ? 1.2 : 2;
        GUILayout.Space(12);
        GUILayout.Label(replay.FollowRecent ? "Playback is independent of learning. A newer game is selected after this replay finishes." :
            "Recorded actions, not a new match. These recent games stay fixed while you inspect. Re-enter inspection to load newer games.", small);
        if (game.truncated) GUILayout.Label("Long match: replay buffer kept the opening and final state; some later actions are omitted.", small);
        if (!string.IsNullOrEmpty(arena.Failure)) GUILayout.Label(arena.Failure, small);
        GUILayout.Space(12);
        GUILayout.Label($"Live run: {arena.Games:N0} matches · {arena.Actions:N0} actions", small);
        if (arena is ITrainingSpectator)
        {
            GUILayout.Label($"Training: {(arena.Elapsed > 0 ? arena.Decisions / arena.Elapsed : 0):F1} decisions/s", small);
            if (arena.EloHistory != null)
            {
                GUILayout.Space(12);
                GUILayout.Label("Match-result Elo", title);
                GUILayout.Label($"{arena.EloHistory.points[arena.EloHistory.points.Count - 1].elo:F0} Elo · relative to training opponents", label);
                DrawChart(GUILayoutUtility.GetRect(1, 140, GUILayout.ExpandWidth(true)), arena.EloHistory);
                GUILayout.Label($"Rated {arena.EloHistory.wins + arena.EloHistory.losses:N0} · self-play telemetry, not human Elo", small);
            }
            int captures = arena.Progress.firstPlayerWins + arena.Progress.secondPlayerWins;
            if (captures > 0) GUILayout.Label($"First-player wins {100f * arena.Progress.firstPlayerWins / captures:F1}% · second {100f * arena.Progress.secondPlayerWins / captures:F1}% · {captures:N0} captures", small);
        }
    }

    private void DrawSpectatorControls(ITrainingSpectator spectator)
    {
        GUILayout.Label("Training viewer", title);
        GUILayout.Label($"{spectator.WorkerCount} parallel {(spectator.WorkerCount == 1 ? "game" : "games")} · learning runs independently", small);
        if (GUILayout.Button(spectator.ShowingLive ? "Watch recent games" : "Watch live training", button))
        { if (spectator.ShowingLive) spectator.WatchRecent(); else spectator.WatchLive(); }
        if (spectator.ShowingLive)
        {
            if (spectator.WorkerCount > 1)
            {
                GUILayout.Label("Live layout", small);
                int layout = GUILayout.Toolbar(spectator.ShowingAllLive ? 1 : 0, new[] { "One game", $"All {spectator.WorkerCount} games" });
                if ((layout == 1) != spectator.ShowingAllLive) spectator.ShowingAllLive = layout == 1;
            }
            if (!spectator.ShowingAllLive)
            {
                string[] names = new string[spectator.WorkerCount];
                for (int i = 0; i < names.Length; i++) names[i] = (i + 1).ToString();
                GUILayout.Label("Live simulation", small);
                int selected = GUILayout.Toolbar(spectator.SelectedWorker, names);
                if (selected != spectator.SelectedWorker) spectator.SelectedWorker = selected;
            }
            GUILayout.Label("Sampled twice per second; intermediate actions are skipped. Training never waits for this display.", small);
        }
        GUI.enabled = spectator.CanContinue;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(spectator.Paused ? "Resume training" : "Pause training", button)) spectator.Paused = !spectator.Paused;
        if (GUILayout.Button("Stop & save", button)) spectator.StopAndSave();
        GUILayout.EndHorizontal();
        GUI.enabled = true;
        GUILayout.Label("Closing this viewer leaves training running.", small);
        GUILayout.Space(12);
    }

    private void DrawTrainingTime(ITrainingView arena)
    {
        if (arena.TotalTrainingSeconds < 0) { GUILayout.Label("Total training time: awaiting telemetry", small); return; }
        double seconds = arena.TotalTrainingSeconds;
        long hours = (long)(seconds / 3600);
        int minutes = (int)(seconds / 60 % 60), remainder = (int)(seconds % 60);
        GUILayout.Label($"Total training: {(arena.TrainingTimeEstimated ? "≈ " : "")}{hours:00}:{minutes:00}:{remainder:00}", label);
        GUILayout.Label(arena.TrainingTimeEstimated ? "Includes estimated earlier sessions." : "Active time across resumes · pauses excluded", small);
    }

    private void DrawPlaytest(ITrainingView arena)
    {
        GUI.enabled = arena.Playtest.Available && !arena.Playtest.Busy && arena.CanContinue;
        if (GUILayout.Button("Play against recent AI", button)) arena.Playtest.Start(arena);
        GUI.enabled = true;
        GUILayout.Label("Opens a separate match with fixed weights. Training continues independently.", small);
        if (!arena.Playtest.Available && !arena.Playtest.Busy)
            GUILayout.Label("Requires a standalone run with a saved checkpoint.", small);
        if (!string.IsNullOrEmpty(arena.Playtest.Message)) GUILayout.Label(arena.Playtest.Message, small);
    }

    private void DrawStorage(ITrainingView arena)
    {
        GUILayout.Space(8);
        TrainingStorageStatus storage = arena.StorageStatus;
        GUILayout.Label(storage != null ? storage.Summary : "Shared storage: awaiting supervisor telemetry", small);
        if (storage != null)
        {
            Rect bar = GUILayoutUtility.GetRect(1, 7, GUILayout.ExpandWidth(true));
            Fill(bar, new Color(0.02f, 0.09f, 0.14f));
            Fill(new Rect(bar.x, bar.y, bar.width * storage.Fraction, bar.height), new Color(0.55f, 0.85f, 0.8f));
        }
        string folder = arena.TrainingRunDirectory;
        if (!string.IsNullOrEmpty(folder))
        {
            if (arena is ITrainingSpectator)
            {
                showRunFolder = GUILayout.Toggle(showRunFolder, "Show training run folder");
                if (showRunFolder)
                {
                    GUILayout.Label(folder, small);
                    if (GUILayout.Button("Copy run folder", button)) GUIUtility.systemCopyBuffer = folder;
                }
            }
            else
            {
                GUILayout.Label("Run folder · checkpoints and match results", small);
                GUILayout.Label(folder, small);
                if (GUILayout.Button("Copy run folder", button)) GUIUtility.systemCopyBuffer = folder;
            }
        }
        GUILayout.Space(8);
    }

    private void DrawVision()
    {
        GUILayout.Label("Spectator vision", small);
        vision = (TrainingVision)GUILayout.Toolbar((int)vision, new[] { "Shared", "Blue", "Red", "All" }, button);
        GUILayout.Label(vision == TrainingVision.Shared ? "Fog where neither seat sees. Cities marked at known starting locations." :
            vision == TrainingVision.All ? "All tiles and units visible." : "Current vision of " + vision + ". Hidden units concealed.", small);
        GUILayout.Label("Gold and match details are spectator statistics.", small);
        GUILayout.Space(12);
    }

    private static GUIStyle Style(int size, bool bold) => new GUIStyle(GUI.skin.label)
    {
        fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, wordWrap = true,
        normal = { textColor = new Color(0.91f, 0.95f, 1f) }
    };

    private void DrawChart(Rect rect, TrainingEloHistory history)
    {
        Rect plot = new Rect(rect.x + 46, rect.y + 12, rect.width - 54, rect.height - 38);
        Fill(plot, new Color(0.02f, 0.09f, 0.14f));
        float low = history.points[0].elo, high = low;
        foreach (var point in history.points) { low = Mathf.Min(low, point.elo); high = Mathf.Max(high, point.elo); }
        low = Mathf.Floor((low - 10) / 50) * 50;
        high = Mathf.Max(low + 100, Mathf.Ceil((high + 10) / 50) * 50);
        for (int i = 0; i <= 2; i++)
        {
            float value = Mathf.Lerp(low, high, i / 2f);
            float y = plot.yMax - i / 2f * plot.height;
            Fill(new Rect(plot.x, y, plot.width, 1), new Color(0.25f, 0.35f, 0.42f));
            GUI.Label(new Rect(rect.x, y - 9, 46, 22), value.ToString("F0"), small);
        }
        float baselineY = plot.yMax - (history.points[0].elo - low) / (high - low) * plot.height;
        // A faint dashed starting reference is distinct from the measured curve.
        for (float x = plot.x; x < plot.xMax; x += 12)
            Fill(new Rect(x, baselineY, Mathf.Min(6, plot.xMax - x), 1), new Color(0.3f, 0.4f, 0.43f));
        long first = history.ChartStartMatch, last = history.ChartEndMatch;
        // Leave room for new matches instead of stretching a fresh history to the edge.
        long horizontalRange = last - first;
        bool hasPrevious = false;
        Vector2 previous = default;
        for (int i = 0; i < history.points.Count; i++)
        {
            TrainingEloHistory.Point point = history.points[i];
            if (point.step < history.FirstRecordedMatch) continue;
            var current = new Vector2(plot.x + (float)((point.step - first) / (double)horizontalRange) * plot.width,
                plot.yMax - (point.elo - low) / (high - low) * plot.height);
            if (hasPrevious) Line(previous, current, new Color(0.4f, 0.9f, 0.75f));
            if (!hasPrevious || i == history.points.Count - 1)
                Fill(new Rect(current.x - 2, current.y - 2, 4, 4), new Color(0.4f, 0.9f, 0.75f));
            previous = current;
            hasPrevious = true;
        }
        GUI.Label(new Rect(plot.x, plot.yMax + 4, plot.width / 2, 22), first.ToString("N0"), small);
        GUI.Label(new Rect(plot.x + plot.width / 4, plot.yMax + 4, plot.width / 2, 22), (first + horizontalRange / 2).ToString("N0"), middleTick);
        GUI.Label(new Rect(plot.center.x, plot.yMax + 4, plot.width / 2, 22), last.ToString("N0"), rightTick);
    }

    private static void Line(Vector2 a, Vector2 b, Color color)
    {
        // Draw in the same clipped coordinate space as the chart markers.
        // Rotating GUI.matrix inside a GUILayout area displaces Retina line segments.
        int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y))));
        for (int step = 0; step <= steps; step++)
        {
            Vector2 point = Vector2.Lerp(a, b, step / (float)steps);
            Fill(new Rect(point.x - 1, point.y - 1, 2, 2), color);
        }
    }
    private static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color; GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous;
    }
}
