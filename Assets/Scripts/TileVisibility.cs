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

    public bool isVisibleNow { get; private set; }
    public bool hasBeenSeen { get; private set; }

    private Color fogBaseColor = Color.black;
    private Color exploredBaseColor = new Color(0f, 0f, 0f, 0.5f);

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

    public void SetVisible(bool visible)
    {
        isVisibleNow = visible;
        if (visible)
        {
            hasBeenSeen = true;
        }
        UpdateVisuals();
    }

    public void ForceUpdate()
    {
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (fogRenderer != null)
        {
            bool showFog = !hasBeenSeen;
            Color c = fogBaseColor;
            c.a = showFog ? 1f : 0f;
            fogRenderer.color = c;
            fogRenderer.enabled = showFog;
        }

        if (exploredRenderer != null)
        {
            bool showExplored = hasBeenSeen && !isVisibleNow;
            Color c = exploredBaseColor;
            c.a = showExplored ? exploredAlpha : 0f;
            exploredRenderer.color = c;
            exploredRenderer.enabled = showExplored;
        }
    }
}
