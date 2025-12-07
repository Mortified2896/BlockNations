using UnityEngine;

public class TileHighlighter : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Color baseColor;

    public Color hoverColor = Color.yellow;
    public Color selectedColor = Color.green;

    private bool isHovered = false;
    private bool isSelected = false;

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
        else
        {
            spriteRenderer.color = baseColor;
        }
    }
}