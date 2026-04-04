using UnityEngine;

public class OwnedSprite : MonoBehaviour
{
    [Header("Owner")]
    public bool isPlayerOwned = true;

    [Header("Colors")]
    public Color playerColor = Color.blue;
    public Color aiColor = Color.red;

    [Header("References")]
    [SerializeField] private SpriteRenderer targetRenderer;

    void Awake()
    {
        if (targetRenderer == null)
        {
            targetRenderer = ResolveTargetRenderer();
        }

        UpdateColor();
    }

    public void SetOwner(bool playerOwned)
    {
        isPlayerOwned = playerOwned;
        UpdateColor();
    }

    private void UpdateColor()
    {
        if (targetRenderer == null)
        {
            targetRenderer = ResolveTargetRenderer();
        }

        if (targetRenderer == null) return;

        targetRenderer.color = isPlayerOwned ? playerColor : aiColor;
    }

    private SpriteRenderer ResolveTargetRenderer()
    {
        SpriteRenderer rootRenderer = GetComponent<SpriteRenderer>();
        if (rootRenderer != null)
        {
            return rootRenderer;
        }

        Unit unit = GetComponent<Unit>();
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            if (unit != null && renderer == unit.moveOutline)
            {
                continue;
            }

            return renderer;
        }

        return null;
    }
}
