using System;
using UnityEngine;

// All panels render the same cached board/fog palette. No cameras or simulations
// are created for the additional views, and none of their controls issues actions.
public sealed class TrainingMultiBoardView
{
    private readonly TrainingReplayView board = new TrainingReplayView();
    private GUIStyle heading, small;

    public static Rect[] Panels(Rect area, int count)
    {
        if (count < 1 || count > 16) throw new ArgumentOutOfRangeException(nameof(count));
        int columns = Mathf.CeilToInt(Mathf.Sqrt(count)), rows = Mathf.CeilToInt((float)count / columns);
        const float gap = 12;
        float width = (area.width - gap * (columns + 1)) / columns, height = (area.height - gap * (rows + 1)) / rows;
        var panels = new Rect[count];
        for (int i = 0; i < count; i++) panels[i] = new Rect(area.x + gap + i % columns * (width + gap),
            area.y + gap + i / columns * (height + gap), width, height);
        return panels;
    }

    public void Draw(ITrainingSpectator spectator, TrainingVision vision)
    {
        if (heading == null)
        {
            heading = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = false };
            small = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = false };
        }
        float left = TrainingOverlay.ReservedWidth / TrainingOverlay.Scale;
        var panels = Panels(new Rect(left, 0, Screen.width / TrainingOverlay.Scale - left, Screen.height / TrainingOverlay.Scale), spectator.WorkerCount);
        for (int i = 0; i < panels.Length; i++)
        {
            Rect panel = panels[i];
            TrainingLiveBoard live = i < spectator.LiveBoards.Count ? spectator.LiveBoards[i] : null;
            TrainingReplayHistory.Frame frame = live?.Current;
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(panel.x + 10, panel.y + 5, panel.width - 96, 24), frame == null ? $"Game {i + 1}" :
                $"Game {i + 1} · match {live.Game.number} · round {frame.round}/{live.RoundLimit}", heading);
            if (GUI.Button(new Rect(panel.xMax - 80, panel.y + 5, 70, 25), "Focus"))
            { spectator.SelectedWorker = i; spectator.ShowingAllLive = false; }
            if (frame == null)
                GUI.Label(new Rect(panel.x + 10, panel.y + 32, panel.width - 20, 24), "Waiting for a current snapshot…", small);
            else
            {
                Color previous = GUI.color;
                GUI.color = new Color(.35f, .75f, 1f);
                GUI.Label(new Rect(panel.x + 10, panel.y + 31, panel.width / 2 - 10, 24), $"Blue gold {frame.blueGold}", small);
                GUI.color = new Color(1f, .45f, .4f);
                GUI.Label(new Rect(panel.x + panel.width / 2, panel.y + 31, panel.width / 2 - 10, 24), $"Red gold {frame.redGold}", small);
                GUI.color = previous;
            }
            board.Draw(frame, live?.Opening, spectator.BoardSize, vision, spectator.SpectatorBackgroundColor, false,
                new Rect(panel.x + 4, panel.y + 59, panel.width - 8, panel.height - 63));
        }
    }
}
