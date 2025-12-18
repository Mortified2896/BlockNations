using UnityEngine;

/// <summary>
/// Tracks fog-of-war state for a single tile and updates overlay visuals.
/// </summary>
public class TileVisibility : MonoBehaviour
{
    [Header("Renderers")]
    public SpriteRenderer fogRenderer;       // fully hides the tile when never seen
    public SpriteRenderer exploredRenderer; // grey overlay when explored but not currently visible
    [Range(0f, 1f)]
    public float exploredAlpha = 0.5f;

    [HideInInspector] public int gridX;
    [HideInInspector] public int gridY;

    // Visibility for the currently active side
    public bool isVisibleNow { get; private set; }
    public bool hasBeenSeen => currentSideIsPlayer ? hasBeenSeenPlayer : hasBeenSeenOpponent;

    private Color fogBaseColor = Color.black;
    private Color exploredBaseColor = new Color(0f, 0f, 0f, 0.5f);

    // Per-side exploration memory (Player1 = isPlayerOwned=true, Player2/AI = false)
    private bool hasBeenSeenPlayer = false;
    private bool hasBeenSeenOpponent = false;
    private bool currentSideIsPlayer = true;

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

    public void SetVisibleForSide(bool visible, bool sideIsPlayer)
    {
        currentSideIsPlayer = sideIsPlayer;
        isVisibleNow = visible;

        if (visible)
        {
            if (sideIsPlayer)
                hasBeenSeenPlayer = true;
            else
                hasBeenSeenOpponent = true;
        }

        UpdateVisuals();
    }

    public void SetCurrentSide(bool sideIsPlayer)
    {
        currentSideIsPlayer = sideIsPlayer;
        UpdateVisuals();
    }

    public void SetSeenState(bool playerSeen, bool opponentSeen, bool activeSideIsPlayer)
    {
        hasBeenSeenPlayer = playerSeen;
        hasBeenSeenOpponent = opponentSeen;
        currentSideIsPlayer = activeSideIsPlayer;
        isVisibleNow = false;
        UpdateVisuals();
    }

    public void GetSeenState(out bool playerSeen, out bool opponentSeen)
    {
        playerSeen = hasBeenSeenPlayer;
        opponentSeen = hasBeenSeenOpponent;
    }

    public void ForceUpdate()
    {
        UpdateVisuals();
    }

    /// <summary>
    /// Clears any visibility/exploration data (used for per-side fog in hotseat).
    /// </summary>
    public void ResetVisibilityState()
    {
        isVisibleNow = false;
        hasBeenSeenPlayer = false;
        hasBeenSeenOpponent = false;
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        bool seenForThisSide = hasBeenSeen;

        if (fogRenderer != null)
        {
            bool showFog = !seenForThisSide;
            Color c = fogBaseColor;
            c.a = showFog ? 1f : 0f;
            fogRenderer.color = c;
            fogRenderer.enabled = showFog;
        }

        if (exploredRenderer != null)
        {
            bool showExplored = seenForThisSide && !isVisibleNow;
            Color c = exploredBaseColor;
            c.a = showExplored ? exploredAlpha : 0f;
            exploredRenderer.color = c;
            exploredRenderer.enabled = showExplored;
        }
    }
}
