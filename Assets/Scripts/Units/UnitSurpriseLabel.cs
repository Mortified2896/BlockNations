using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A unit owns its explanation badge. No scene lookup, input, or rule decisions.
[DisallowMultipleComponent]
public sealed class UnitSurpriseLabel : MonoBehaviour
{
    private Unit owner;
    private Canvas canvas;
    private RectTransform badge;
    private Camera boardCamera;
    private const float Scale = .0041f, FontSize = 30f;
    private static readonly Color BadgeColor = new Color(.34f, .18f, .035f, .96f);

    public void Refresh(Unit unit)
    {
        owner = unit;
        if (canvas == null) CreateBadge();
        canvas.gameObject.SetActive(owner.currentHealthUnits > 0 && owner.IsSurprised && owner.IsPresentationVisible);
    }

    private void LateUpdate()
    {
        if (owner == null || canvas == null) return;
        Refresh(owner); // Match projection expires the status for every seat at a round boundary.
        if (!canvas.gameObject.activeSelf) return;
        if (boardCamera == null) boardCamera = Camera.main;
        if (boardCamera != null)
            canvas.transform.rotation = Quaternion.LookRotation(boardCamera.transform.forward, boardCamera.transform.up);
    }

    private void CreateBadge()
    {
        var root = new GameObject("SurprisedLabelCanvas", typeof(RectTransform), typeof(Canvas));
        root.transform.SetParent(transform, false);
        root.transform.localPosition = new Vector3(-.54f, .85f, 0);
        root.transform.localScale = Vector3.one * Scale;
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 951;
        badge = root.GetComponent<RectTransform>();
        badge.pivot = new Vector2(0, 1);
        badge.sizeDelta = new Vector2(250, 50);
        var image = root.AddComponent<Image>();
        image.color = BadgeColor;
        image.raycastTarget = false;
        var textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(root.transform, false);
        var rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = "Surprised";
        text.fontSize = FontSize; text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(1, .89f, .6f);
        text.raycastTarget = false;
    }

    public UnitHealthLabel.Presentation Presentation()
    {
        Vector3 lower = badge.TransformPoint(badge.rect.min), upper = badge.TransformPoint(badge.rect.max);
        return new UnitHealthLabel.Presentation { bounds = Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y),
            fontWorldSize = FontSize * Mathf.Abs(badge.lossyScale.y), textColor = new Color(1, .89f, .6f),
            badgeColor = BadgeColor, text = "Surprised" };
    }
}
