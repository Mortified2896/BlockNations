using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UnitHealthLabel : MonoBehaviour
{
    private static Sprite cachedWhiteSprite;

    public enum DisplayMode
    {
        Hidden,
        CurrentOnly,
        CurrentOverMax
    }

    [Header("Display")]
    [SerializeField] private DisplayMode displayMode = DisplayMode.CurrentOverMax;
    [SerializeField] private bool showWhenUndamaged = true;
    [SerializeField] private Vector3 localOffset = new Vector3(-0.54f, 0.62f, 0f);
    [SerializeField] private float canvasScale = 0.0041f;
    [SerializeField] private float fontSize = 36f;
    [SerializeField] private Color textColor = new Color(0.97f, 0.98f, 1f, 1f);
    [SerializeField] private Color outlineColor = new Color(0.05f, 0.09f, 0.16f, 1f);
    [SerializeField] [Range(0f, 1f)] private float outlineWidth = 0.3f;
    [SerializeField] private Color badgeColor = new Color(0.03f, 0.07f, 0.13f, 0.82f);

    private Unit unit;
    private Canvas canvas;
    private TextMeshProUGUI labelText;
    private Camera cachedMainCamera;

    // Read-only presentation data lets recorded spectator views keep the same
    // badge geometry and colours without holding or changing the live Canvas.
    public struct Presentation
    {
        public Rect bounds;
        public float fontWorldSize;
        public Color textColor, badgeColor;
        public string text;
    }

    public struct PresentationTemplate
    {
        public Presentation presentation;
        public DisplayMode mode;
        public bool showUndamaged;
        public bool Project(int current, int maximum, Vector2 offset, out Presentation result)
        {
            result = presentation;
            if (current <= 0 || mode == DisplayMode.Hidden || (!showUndamaged && current >= maximum)) return false;
            result.bounds.position += offset;
            result.text = mode == DisplayMode.CurrentOnly ? CombatValues.FormatUnits(current) : CombatValues.FormatRatio(current, maximum);
            return true;
        }
    }

    public bool TryGetPresentation(out Presentation presentation)
    {
        presentation = default;
        return unit != null && TryGetPresentationTemplate(out PresentationTemplate template) &&
            template.Project(unit.currentHealthUnits, unit.maxHealthUnits, Vector2.zero, out presentation);
    }

    public bool TryGetPresentationTemplate(out PresentationTemplate template)
    {
        template = default;
        if (canvas == null || displayMode == DisplayMode.Hidden) return false;
        var rect = (RectTransform)canvas.transform;
        Vector3 lower = rect.TransformPoint(rect.rect.min), upper = rect.TransformPoint(rect.rect.max);
        var presentation = new Presentation { bounds = Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y),
            fontWorldSize = fontSize * Mathf.Abs(rect.lossyScale.y), textColor = textColor, badgeColor = badgeColor,
            text = string.Empty };
        template = new PresentationTemplate { presentation = presentation, mode = displayMode, showUndamaged = showWhenUndamaged };
        return true;
    }

    private void Awake()
    {
        unit = GetComponent<Unit>();
        EnsureLabel();
        Refresh();
    }

    private void LateUpdate()
    {
        if (canvas == null || !canvas.gameObject.activeSelf)
        {
            return;
        }

        FaceCamera();
    }

    public void Refresh()
    {
        if (unit == null)
        {
            unit = GetComponent<Unit>();
        }

        if (unit == null)
        {
            return;
        }

        EnsureLabel();

        bool isAlive = unit.currentHealthUnits > 0;
        bool isDamaged = unit.currentHealthUnits < unit.maxHealthUnits;
        bool shouldShow = isAlive
            && (showWhenUndamaged || isDamaged)
            && displayMode != DisplayMode.Hidden
            && unit.IsPresentationVisible;

        if (canvas != null)
        {
            canvas.gameObject.SetActive(shouldShow);
        }

        if (!shouldShow || labelText == null)
        {
            return;
        }

        labelText.text = FormatHealthText(unit.currentHealthUnits, unit.maxHealthUnits);
        FaceCamera();
    }

    public void Hide()
    {
        if (canvas != null)
        {
            canvas.gameObject.SetActive(false);
        }
    }

    private void EnsureLabel()
    {
        if (labelText != null && canvas != null)
        {
            return;
        }

        Transform existing = transform.Find("HealthLabelCanvas");
        GameObject root;
        if (existing != null)
        {
            root = existing.gameObject;
        }
        else
        {
            root = new GameObject("HealthLabelCanvas", typeof(RectTransform));
            root.transform.SetParent(transform, false);
        }

        root.transform.localPosition = localOffset;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * canvasScale;

        canvas = root.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = root.AddComponent<Canvas>();
        }

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 950;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = root.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        scaler.referencePixelsPerUnit = 100f;

        GraphicRaycaster raycaster = root.GetComponent<GraphicRaycaster>();
        if (raycaster != null)
        {
            raycaster.enabled = false;
        }

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.sizeDelta = new Vector2(138f, 52f);

        Transform badgeTransform = root.transform.Find("Badge");
        GameObject badgeObject;
        if (badgeTransform != null)
        {
            badgeObject = badgeTransform.gameObject;
        }
        else
        {
            badgeObject = new GameObject("Badge", typeof(RectTransform), typeof(Image));
            badgeObject.transform.SetParent(root.transform, false);
            badgeObject.transform.SetAsFirstSibling();
        }

        RectTransform badgeRect = badgeObject.GetComponent<RectTransform>();
        badgeRect.anchorMin = Vector2.zero;
        badgeRect.anchorMax = Vector2.one;
        badgeRect.offsetMin = Vector2.zero;
        badgeRect.offsetMax = Vector2.zero;

        Image badgeImage = badgeObject.GetComponent<Image>();
        badgeImage.sprite = GetWhiteSprite();
        badgeImage.type = Image.Type.Sliced;
        badgeImage.color = badgeColor;
        badgeImage.raycastTarget = false;

        Transform textTransform = root.transform.Find("Text");
        GameObject textObject;
        if (textTransform != null)
        {
            textObject = textTransform.gameObject;
        }
        else
        {
            textObject = new GameObject("Text", typeof(RectTransform));
            textObject.transform.SetParent(root.transform, false);
        }

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 5f);
        textRect.offsetMax = new Vector2(-10f, -5f);

        labelText = textObject.GetComponent<TextMeshProUGUI>();
        if (labelText == null)
        {
            labelText = textObject.AddComponent<TextMeshProUGUI>();
        }

        labelText.alignment = TextAlignmentOptions.Center;
        labelText.fontSize = fontSize;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = textColor;
        labelText.outlineColor = outlineColor;
        labelText.outlineWidth = outlineWidth;
        labelText.raycastTarget = false;
        labelText.textWrappingMode = TextWrappingModes.NoWrap;
        labelText.enableWordWrapping = false;
        labelText.overflowMode = TextOverflowModes.Overflow;
    }

    private static Sprite GetWhiteSprite()
    {
        if (cachedWhiteSprite != null)
        {
            return cachedWhiteSprite;
        }

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        cachedWhiteSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return cachedWhiteSprite;
    }

    private string FormatHealthText(int currentHealthUnits, int maxHealthUnits)
    {
        switch (displayMode)
        {
            case DisplayMode.CurrentOnly:
                return CombatValues.FormatUnits(currentHealthUnits);
            case DisplayMode.CurrentOverMax:
                return CombatValues.FormatRatio(currentHealthUnits, maxHealthUnits);
            default:
                return string.Empty;
        }
    }

    private void FaceCamera()
    {
        if (cachedMainCamera == null)
        {
            cachedMainCamera = Camera.main;
        }

        if (cachedMainCamera == null || canvas == null)
        {
            return;
        }

        Transform canvasTransform = canvas.transform;
        Vector3 forward = cachedMainCamera.transform.forward;
        if (forward.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        canvasTransform.rotation = Quaternion.LookRotation(forward, cachedMainCamera.transform.up);
    }
}
