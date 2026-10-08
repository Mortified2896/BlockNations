using System;
using UnityEngine;

// Spectator presentation only. Gold and chart data never enter policy observations.
public sealed class TrainingOverlay
{
    private GUIStyle title, label, small, button;
    private readonly int[] displayedGold = new int[2];
    private double nextGoldRefresh;
    public static float Scale => Mathf.Clamp(Screen.height / 900f, 0.8f, 2.5f);
    public static float ReservedWidth => Mathf.Min(370f * Scale, Screen.width * 0.4f);

    public void Draw(TrainingArena arena)
    {
        if (title == null)
        {
            title = Style(21, true); label = Style(17, false); small = Style(13, false);
            button = new GUIStyle(GUI.skin.button) { fontSize = 17, fixedHeight = 38 };
        }
        Matrix4x4 matrix = GUI.matrix;
        Color color = GUI.color;
        try
        {
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1));
            float width = ReservedWidth / Scale - 24;
            Fill(new Rect(12, 12, width, Screen.height / Scale - 24), new Color(0.035f, 0.13f, 0.20f, 0.97f));
            GUILayout.BeginArea(new Rect(24, 24, width - 24, Screen.height / Scale - 48));
            GUILayout.Label(arena.IsHumanPlaytest ? "Local policy playtest" : "Live self-play training", title);
            GUILayout.Space(12);
            GUILayout.Label($"Match {arena.Games + 1} · round {arena.Round}/{arena.RoundLimit}", label);
            GUILayout.Label(arena.FullOpening ? $"Standard opening · {arena.BoardSize} × {arena.BoardSize}" :
                $"Curriculum stage {arena.CurriculumDistance} · distance {arena.StartingDistance}", small);
            GUILayout.Space(12);
            // Sample balances once per second; fixed rows keep rapid turn changes readable.
            if (Time.realtimeSinceStartupAsDouble >= nextGoldRefresh)
            {
                for (int seat = 0; seat < 2; seat++) displayedGold[seat] = arena.GoldForSeat(seat);
                nextGoldRefresh = Time.realtimeSinceStartupAsDouble + 1;
            }
            for (int seat = 0; seat < 2; seat++)
            {
                Color seatColor = seat == 0 ? new Color(0.35f, 0.75f, 1f) : new Color(1f, 0.45f, 0.4f);
                GUI.color = seatColor;
                string gold = arena.IsHumanPlaytest && seat != arena.HumanSeat ? "Gold hidden" : $"Gold {displayedGold[seat]}";
                Rect row = GUILayoutUtility.GetRect(width - 24, 28);
                string name = seat == 0 ? "Blue" : "Red";
                GUI.Label(new Rect(row.x, row.y, 100, row.height), name + (arena.IsHumanPlaytest && seat == arena.HumanSeat ? " (you)" : " AI"), label);
                GUI.Label(new Rect(row.x + 118, row.y, row.width - 118, row.height), gold, label);
                GUI.color = Color.white;
            }
            GUILayout.Space(12);
            GUILayout.Label($"Captures {arena.Captures} · limits {arena.Interruptions}", label);
            GUILayout.Label($"{arena.Actions:N0} actions · {(arena.Elapsed > 0 ? arena.Decisions / arena.Elapsed : 0):F1} decisions/s", small);
            if (!string.IsNullOrEmpty(arena.Failure)) GUILayout.Label(arena.Failure, small);
            GUI.enabled = arena.CanContinue;
            if (GUILayout.Button(arena.Paused ? "Continue live run" : "Pause live run", button)) arena.Paused = !arena.Paused;
            GUI.enabled = true;
            if (arena.CanEndHumanTurn && GUILayout.Button("End your turn", button)) arena.EndHumanTurn();
            if (arena.CanStartNewMatch && GUILayout.Button("New playtest match", button)) arena.NewHumanMatch();
            if (arena.IsTraining)
            {
                GUILayout.Space(22);
                GUILayout.Label($"{arena.BoardSize} × {arena.BoardSize} progress", title);
                TrainingProgressHistory progress = arena.Progress;
                GUILayout.Label("Capture completion · rolling last 40 matches", small);
                Rect chart = GUILayoutUtility.GetRect(width - 24, 160);
                DrawChart(chart, progress);
                GUILayout.Label($"{progress.fullBoardCaptures} captures · {progress.fullBoardInterruptions} limits", label);
                if (progress.recentResults.Count > 0)
                    GUILayout.Label($"{progress.CapturePercent:F0}% completion · {progress.recentResults.Count} matches in window", small);
                else GUILayout.Label("Awaiting completed standard-opening matches.", small);
                GUILayout.Label("X: training decisions since tracking began.\nCurriculum matches are excluded. Older mixed results cannot be backfilled.", small);
                GUILayout.Space(10);
                GUILayout.Label("Self-play completion is not benchmark strength. Fixed-opponent strength: not measured.", small);
            }
            GUILayout.EndArea();
        }
        finally { GUI.matrix = matrix; GUI.color = color; GUI.enabled = true; }
    }

    private static GUIStyle Style(int size, bool bold) => new GUIStyle(GUI.skin.label)
    {
        fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, wordWrap = true,
        normal = { textColor = new Color(0.91f, 0.95f, 1f) }
    };

    private void DrawChart(Rect rect, TrainingProgressHistory history)
    {
        Rect plot = new Rect(rect.x + 34, rect.y + 12, rect.width - 42, rect.height - 38);
        Fill(plot, new Color(0.02f, 0.09f, 0.14f));
        for (int percent = 0; percent <= 100; percent += 50)
        {
            float y = plot.yMax - percent / 100f * plot.height;
            Fill(new Rect(plot.x, y, plot.width, 1), new Color(0.25f, 0.35f, 0.42f));
            GUI.Label(new Rect(rect.x, y - 9, 34, 22), percent.ToString(), small);
        }
        if (history.points.Count == 0) return;
        long first = history.points[0].decisions;
        long last = history.points[history.points.Count - 1].decisions;
        Vector2 previous = default;
        for (int i = 0; i < history.points.Count; i++)
        {
            TrainingProgressHistory.Point point = history.points[i];
            var current = new Vector2(plot.x + (float)((point.decisions - first) / (double)Math.Max(1, last - first)) * plot.width,
                plot.yMax - point.capturePercent / 100f * plot.height);
            if (i > 0) Line(previous, current, new Color(0.4f, 0.9f, 0.75f));
            Fill(new Rect(current.x - 2, current.y - 2, 4, 4), new Color(0.4f, 0.9f, 0.75f));
            previous = current;
        }
        GUI.Label(new Rect(plot.x, plot.yMax + 4, plot.width / 2, 22), first.ToString("N0"), small);
        GUI.Label(new Rect(plot.center.x, plot.yMax + 4, plot.width / 2, 22), last.ToString("N0"), small);
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
