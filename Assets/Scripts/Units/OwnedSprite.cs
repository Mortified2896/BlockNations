using UnityEngine;

public class OwnedSprite : MonoBehaviour
{
    [Header("Owner")]
    public int ownerSeatIndex = 0;
    public bool isPlayerOwned = true;

    [Header("Colors")]
    public Color playerColor = Color.blue;
    public Color aiColor = Color.red;
    public Color player3Color = new Color(0.16f, 0.5f, 0.18f, 1f);
    public Color player4Color = Color.yellow;

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
        SetOwnerSeatIndex(playerOwned ? 0 : 1);
    }

    public void SetOwnerSeatIndex(int seatIndex)
    {
        ownerSeatIndex = Mathf.Max(0, seatIndex);
        isPlayerOwned = ownerSeatIndex == 0;
        UpdateColor();
    }

    private void UpdateColor()
    {
        if (targetRenderer == null)
        {
            targetRenderer = ResolveTargetRenderer();
        }

        if (targetRenderer == null) return;

        targetRenderer.color = ResolveSeatColor(ownerSeatIndex);
    }

    private Color ResolveSeatColor(int seatIndex)
    {
        switch (PlayByPostSeatUtility.NormalizeSeatIndex(seatIndex))
        {
            case 0:
                return playerColor;
            case 1:
                return aiColor;
            case 2:
                return player3Color;
            case 3:
                return player4Color;
            default:
                return playerColor;
        }
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
