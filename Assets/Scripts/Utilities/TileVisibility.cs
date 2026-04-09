using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks fog-of-war state for a single tile and updates overlay visuals.
/// </summary>
public class TileVisibility : MonoBehaviour
{
    private const float UnexploredFogAlpha = 0.99f;
    private const float ExploredFogAlpha = 0.5f;

    [Header("Renderers")]
    public SpriteRenderer fogRenderer;       // fully hides the tile when never seen
    public SpriteRenderer exploredRenderer; // grey overlay when explored but not currently visible

    [HideInInspector] public int gridX;
    [HideInInspector] public int gridY;

    // Visibility for the currently active side
    public bool isVisibleNow { get; private set; }
    public bool hasBeenSeen => seenBySeats.Contains(currentViewerSeatIndex);

    private Color fogBaseColor = Color.black;
    private Color exploredBaseColor = new Color(0f, 0f, 0f, 0.5f);

    private readonly HashSet<int> seenBySeats = new HashSet<int>();
    private int currentViewerSeatIndex = 0;

    void Awake()
    {
        if (fogRenderer != null)
            fogBaseColor = fogRenderer.color;
        if (exploredRenderer != null)
            exploredBaseColor = exploredRenderer.color;

        UpdateVisuals();
    }

    public void Initialize(int x, int y)
    {
        gridX = x;
        gridY = y;
        UpdateVisuals();
    }

    public void SetVisibleForSeat(bool visible, int viewerSeatIndex)
    {
        currentViewerSeatIndex = Mathf.Max(0, viewerSeatIndex);
        isVisibleNow = visible;

        if (visible)
        {
            seenBySeats.Add(currentViewerSeatIndex);
        }

        UpdateVisuals();
    }

    public void SetCurrentViewerSeat(int viewerSeatIndex)
    {
        currentViewerSeatIndex = Mathf.Max(0, viewerSeatIndex);
        UpdateVisuals();
    }

    public void SetSeenSeatIndices(IEnumerable<int> seenSeatIndices, int activeViewerSeatIndex)
    {
        seenBySeats.Clear();
        if (seenSeatIndices != null)
        {
            foreach (int seatIndex in seenSeatIndices)
            {
                if (seatIndex >= 0)
                {
                    seenBySeats.Add(seatIndex);
                }
            }
        }

        currentViewerSeatIndex = Mathf.Max(0, activeViewerSeatIndex);
        isVisibleNow = false;
        UpdateVisuals();
    }

    public void GetSeenSeatIndices(List<int> target)
    {
        if (target == null)
        {
            return;
        }

        target.Clear();
        foreach (int seatIndex in seenBySeats)
        {
            target.Add(seatIndex);
        }

        target.Sort();
    }

    public void SetVisibleForSide(bool visible, bool sideIsPlayer)
    {
        SetVisibleForSeat(visible, sideIsPlayer ? 0 : 1);
    }

    public void SetCurrentSide(bool sideIsPlayer)
    {
        SetCurrentViewerSeat(sideIsPlayer ? 0 : 1);
    }

    public void SetSeenState(bool playerSeen, bool opponentSeen, bool activeSideIsPlayer)
    {
        List<int> seenSeatIndices = ListPool.Get();
        if (playerSeen)
        {
            seenSeatIndices.Add(0);
        }

        if (opponentSeen)
        {
            seenSeatIndices.Add(1);
        }

        SetSeenSeatIndices(seenSeatIndices, activeSideIsPlayer ? 0 : 1);
        ListPool.Release(seenSeatIndices);
    }

    public void GetSeenState(out bool playerSeen, out bool opponentSeen)
    {
        playerSeen = seenBySeats.Contains(0);
        opponentSeen = seenBySeats.Contains(1);
    }

    public void ForceUpdate()
    {
        UpdateVisuals();
    }

    /// <summary>
    /// Clears any visibility/exploration data.
    /// </summary>
    public void ResetVisibilityState()
    {
        isVisibleNow = false;
        seenBySeats.Clear();
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        bool seenForThisSide = hasBeenSeen;

        if (fogRenderer != null)
        {
            bool showFog = !seenForThisSide;
            Color c = fogBaseColor;
            c.a = showFog ? UnexploredFogAlpha : 0f;
            fogRenderer.color = c;
            fogRenderer.enabled = showFog;
        }

        if (exploredRenderer != null)
        {
            bool showExplored = seenForThisSide && !isVisibleNow;
            Color c = exploredBaseColor;
            c.a = showExplored ? ExploredFogAlpha : 0f;
            exploredRenderer.color = c;
            exploredRenderer.enabled = showExplored;
        }
    }

    private static class ListPool
    {
        private static readonly Stack<List<int>> Pool = new Stack<List<int>>();

        public static List<int> Get()
        {
            return Pool.Count > 0 ? Pool.Pop() : new List<int>(4);
        }

        public static void Release(List<int> list)
        {
            if (list == null)
            {
                return;
            }

            list.Clear();
            Pool.Push(list);
        }
    }
}
