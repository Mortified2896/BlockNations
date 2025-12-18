using UnityEngine;

public class OwnedSprite : MonoBehaviour
{
    [Header("Owner")]
    public bool isPlayerOwned = true;

    [Header("Colors")]
    public Color playerColor = Color.blue;
    public Color aiColor = Color.red;

    private SpriteRenderer spriteRenderer;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        UpdateColor();
    }

    public void SetOwner(bool playerOwned)
    {
        isPlayerOwned = playerOwned;
        UpdateColor();
    }

    private void UpdateColor()
    {
        if (spriteRenderer == null) return;

        spriteRenderer.color = isPlayerOwned ? playerColor : aiColor;
    }
}
