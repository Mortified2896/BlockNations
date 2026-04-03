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

        bool isAlive = unit.currentHealth > 0;
        bool isDamaged = unit.currentHealth < unit.maxHealth;
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

        labelText.text = FormatHealthText(unit.currentHealth, unit.maxHealth);
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

    private string FormatHealthText(int currentHealth, int maxHealth)
    {
        switch (displayMode)
        {
            case DisplayMode.CurrentOnly:
                return currentHealth.ToString();
            case DisplayMode.CurrentOverMax:
                return currentHealth + "/" + maxHealth;
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
