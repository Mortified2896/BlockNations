using System;
using System.Collections.Generic;
using UnityEngine;

// Bounded spectator copies: no live scene objects, policies, rewards, or gameplay saves.
public enum TrainingVision { Shared, Blue, Red, All }

public sealed class TrainingReplayHistory
{
    public const int MaximumGames = 4, MaximumFrames = 256, MaximumPieces = 8192;
    public struct Piece
    {
        public int x, y, seat, health, maxHealth;
        public bool city;
        public Sprite sprite;
        public Color color;
        public Rect bounds;
        public bool flipX, flipY, hasHealthPresentation;
        public UnitHealthLabel.Presentation healthPresentation;
        public Sprite outlineSprite;
        public Rect outlineBounds;
        public Color outlineColor;
    }
    public sealed class Frame
    {
        public int round, seat, blueGold, redGold;
        public string description;
        public int visionWidth;
        public float tileSpacing = 1f;
        public Vector2 tileWorldSize = new Vector2(.9f, .9f);
        public bool[] blueVision = Array.Empty<bool>(), redVision = Array.Empty<bool>();

        public bool Visible(int x, int y, TrainingVision view)
        {
            if (view == TrainingVision.All) return true;
            if (x < 0 || y < 0 || x >= visionWidth || y >= visionWidth) return false;
            int index = y * visionWidth + x;
            bool blue = index < blueVision.Length && blueVision[index];
            bool red = index < redVision.Length && redVision[index];
            return view == TrainingVision.Blue ? blue : view == TrainingVision.Red ? red : blue || red;
        }
        public Piece[] pieces = Array.Empty<Piece>();
    }
    public sealed class Game
    {
        public int number, boardSize, firstSeat, worker;
        public string sourceKey, policyLabel;
        public bool truncated;
        public readonly List<Frame> frames = new List<Frame>();
    }
    private readonly List<Game> completed = new List<Game>();
    private Game recording;
    private int pieceCount;
    private Game[] playlist;
    private int gameIndex, frameIndex;
    private double nextFrameTime;
    public Frame LatestFrame { get; private set; }
    public Frame OpeningFrame { get; private set; }
    public bool Inspecting => playlist != null;
    public bool Playing { get; set; } = true;
    public bool FollowRecent { get; set; }
    public double SecondsPerAction { get; set; } = 1.2;
    public bool CanInspect => completed.Count > 0;
    public bool CanRecord => recording != null && !recording.truncated;
    public int CompletedCount => completed.Count;
    public int PlaylistCount => playlist?.Length ?? 0;
    public int PlaylistIndex => gameIndex;
    public int FrameIndex => frameIndex;
    public Game CurrentGame => Inspecting ? playlist[gameIndex] : null;
    public Frame CurrentFrame => CurrentGame?.frames[frameIndex];

    public void Begin(int number, int boardSize, int firstSeat, Frame opening)
    {
        recording = new Game { number = number, boardSize = boardSize, firstSeat = firstSeat };
        pieceCount = 0;
        OpeningFrame = opening;
        Record(opening);
    }
    public void Record(Frame frame)
    {
        LatestFrame = frame;
        if (!CanRecord) return;
        if (recording.frames.Count >= MaximumFrames - 1 || pieceCount + frame.pieces.Length > MaximumPieces)
        { recording.truncated = true; return; }
        recording.frames.Add(frame);
        pieceCount += frame.pieces.Length;
    }
    public void Complete(Frame terminal)
    {
        LatestFrame = terminal;
        if (recording == null) return;
        while (recording.frames.Count > 0 && (recording.frames.Count >= MaximumFrames || pieceCount + terminal.pieces.Length > MaximumPieces))
        {
            int last = recording.frames.Count - 1;
            pieceCount -= recording.frames[last].pieces.Length;
            recording.frames.RemoveAt(last);
            recording.truncated = true;
        }
        recording.frames.Add(terminal);
        completed.Add(recording);
        if (completed.Count > MaximumGames) completed.RemoveAt(0);
        recording = null;
    }
    public bool Inspect(double now)
    {
        if (!CanInspect) return false;
        playlist = completed.ToArray();
        Array.Reverse(playlist); // Latest completed match first; freeze these matches during inspection.
        gameIndex = frameIndex = 0;
        Playing = true;
        nextFrameTime = now + SecondsPerAction;
        return true;
    }
    public void BackToLive() { playlist = null; }
    public void SetCompletedGames(IEnumerable<Game> games)
    {
        completed.Clear();
        foreach (Game game in games)
        {
            if (game == null || game.frames.Count == 0 || game.frames.Count > MaximumFrames) throw new ArgumentException("Invalid replay game.");
            completed.Add(game);
            if (completed.Count > MaximumGames) completed.RemoveAt(0);
        }
    }
    public void SetLiveFrames(Frame opening, Frame current) { OpeningFrame = opening; LatestFrame = current; }
    public void Tick(double now)
    {
        if (!Inspecting || !Playing || now < nextFrameTime) return;
        Step(now);
    }
    public void Step(double now)
    {
        if (!Inspecting) return;
        if (++frameIndex >= CurrentGame.frames.Count)
        {
            if (FollowRecent && CanInspect && completed[completed.Count - 1].sourceKey != CurrentGame.sourceKey)
            { bool playing = Playing; Inspect(now); Playing = playing; }
            else SelectGame(1, now);
        }
        else nextFrameTime = now + (frameIndex == CurrentGame.frames.Count - 1 ? 2 : SecondsPerAction);
    }
    public void SelectGame(int offset, double now)
    {
        if (!Inspecting) return;
        gameIndex = (gameIndex + offset + playlist.Length) % playlist.Length;
        frameIndex = 0;
        nextFrameTime = now + SecondsPerAction;
    }
}
