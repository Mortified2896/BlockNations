using UnityEngine;

public class TileHighlighter : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Color baseColor;

    public Color hoverColor = Color.yellow;
    public Color selectedColor = Color.green;
    public Color reachableColor = Color.cyan;

    private bool isHovered = false;
    private bool isSelected = false;
    private bool isReachable = false;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        baseColor = spriteRenderer.color;
        UpdateColor();
    }

    public void SetHighlighted(bool highlighted)
    {
        isHovered = highlighted;
        UpdateColor();
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        UpdateColor();
    }

    public void SetReachable(bool reachable)
    {
        isReachable = reachable;
        UpdateColor();
    }

    private void UpdateColor()
    {
        if (isSelected)
        {
            // Selected has highest priority
            spriteRenderer.color = selectedColor;
        }
        else if (isHovered)
        {
            spriteRenderer.color = hoverColor;
        }
        else if (isReachable)
        {
            spriteRenderer.color = reachableColor;
        }
        else
        {
            spriteRenderer.color = baseColor;
        }
    }
}
