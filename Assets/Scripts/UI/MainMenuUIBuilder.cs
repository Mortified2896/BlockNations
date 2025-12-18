using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Builds a main menu UI at runtime that scales nicely on smartphones.
/// Attach this to an empty GameObject in the MainMenu scene, disable your old Canvas,
/// and it will spawn a new Canvas with scalable layout and hook to MainMenuController.
/// </summary>
public class MainMenuUIBuilder : MonoBehaviour
{
    [Header("Layout")]
    // Portrait reference for smartphones
    public Vector2 referenceResolution = new Vector2(1080, 1920);
    public float buttonHeight = 180f;

    [Header("Styling")]
    public Color backgroundColor = new Color(0.06f, 0.10f, 0.18f, 1f);   // fullscreen bg
    public Color panelColor      = new Color(0.09f, 0.11f, 0.16f, 0.96f); // inner card
    public Color buttonColor     = new Color(0.18f, 0.52f, 0.82f, 1f);
    public Color buttonHoverColor   = new Color(0.24f, 0.62f, 0.94f, 1f);
    public Color buttonPressedColor = new Color(0.15f, 0.42f, 0.70f, 1f);
    public Color buttonTextColor = Color.white;
    public Color titleColor      = Color.white;

    // Slightly larger sizes for mobile readability
    public int titleSize      = 96;
    public int buttonTextSize = 56;
    public int statusTextSize = 42;
    public int inputTextSize  = 40;

    [Header("References")]
    public MainMenuController controller;

    void Awake()
    {
        if (controller == null)
        {
            controller = Object.FindFirstObjectByType<MainMenuController>();
        }

        if (controller == null)
        {
            Debug.LogWarning("MainMenuUIBuilder: No MainMenuController found; UI not built.");
            return;
        }

        Canvas canvas = CreateCanvas();
        EnsureEventSystem();

        RectTransform root = CreateRootPanel(canvas.transform);
        CreateTitle(root, "Main Menu");

        CreateButton(root, "Tutorial",     controller.PlayTutorial);
        CreateButton(root, "Continue",     controller.ContinueLastSave);
        CreateButton(root, "Play vs AI",   controller.PlayVsAI);
        CreateButton(root, "Hotseat",      controller.PlayHotseat);
        CreateButton(root, "Import JSON",  controller.OpenImportPanel);
        CreateButton(root, "Quit",         controller.QuitGame);

        // Import panel overlay
        BuildImportPanel(canvas.transform);
    }

    Canvas CreateCanvas()
    {
        GameObject existing = GameObject.Find("NewUI");
        if (existing != null)
        {
            Canvas existingCanvas = existing.GetComponent<Canvas>();
            if (existingCanvas != null)
                return existingCanvas;
        }

        GameObject go = new GameObject("NewUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100f;

        return canvas;
    }

    void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    /// <summary>
    /// Creates a fullscreen background and a centered card panel that fills most
    /// of the screen. This scales well across smartphone resolutions.
    /// </summary>
    RectTransform CreateRootPanel(Transform parent)
    {
        // Fullscreen background
        GameObject bgGO = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bgGO.transform.SetParent(parent, false);
        RectTransform bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        bgRT.pivot = new Vector2(0.5f, 0.5f);

        Image bgImg = bgGO.GetComponent<Image>();
        bgImg.color = backgroundColor;

        // Centered panel/card for menu
        GameObject panel = new GameObject("MenuPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(bgGO.transform, false);

        RectTransform rt = panel.GetComponent<RectTransform>();
        // Fill about 80–90% of the screen, centered
        rt.anchorMin = new Vector2(0.10f, 0.10f);
        rt.anchorMax = new Vector2(0.90f, 0.90f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);

        Image panelImg = panel.GetComponent<Image>();
        panelImg.color = panelColor;

        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 40f; // distance between title / buttons
        layout.padding = new RectOffset(80, 80, 100, 100); // inner padding of card

        return rt;
    }

    TMP_Text CreateTitle(Transform parent, string text)
    {
        GameObject go = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, buttonHeight);

        LayoutElement le = go.GetComponent<LayoutElement>();
        le.minHeight = buttonHeight * 1.0f;
        le.flexibleHeight = 0f;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        ApplyReadableStyle(tmp, titleSize, true);
        tmp.color = titleColor;

        return tmp;
    }

    Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction action)
    {
        GameObject go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, buttonHeight);

        Image img = go.GetComponent<Image>();
        img.color = buttonColor;

        LayoutElement le = go.GetComponent<LayoutElement>();
        le.minHeight = buttonHeight;
        le.flexibleHeight = 0f;

        Button btn = go.GetComponent<Button>();
        btn.onClick.AddListener(action);

        // Hover effect
        ButtonHoverEffect hover = go.AddComponent<ButtonHoverEffect>();
        hover.Configure(img, buttonColor, buttonHoverColor, buttonPressedColor);

        // Label
        GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(go.transform, false);

        RectTransform trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = textGO.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.alignment = TextAlignmentOptions.Center;
        ApplyReadableStyle(tmp, buttonTextSize, true);
        tmp.color = buttonTextColor;

        return btn;
    }

    void BuildImportPanel(Transform parent)
    {
        // Fullscreen overlay
        GameObject overlay = new GameObject("ImportPanel", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(parent, false);

        RectTransform rt = overlay.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);

        Image img = overlay.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.6f);

        // Inner panel/card
        GameObject panel = new GameObject("Content", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(overlay.transform, false);

        RectTransform prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.08f, 0.18f);
        prt.anchorMax = new Vector2(0.92f, 0.82f);
        prt.offsetMin = Vector2.zero;
        prt.offsetMax = Vector2.zero;
        prt.pivot = new Vector2(0.5f, 0.5f);

        Image pimg = panel.GetComponent<Image>();
        pimg.color = panelColor;

        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 24f;
        layout.padding = new RectOffset(40, 40, 40, 40);

        CreateTitle(panel.transform, "Import JSON");

        TMP_InputField input = CreateTMPInputField(panel.transform, "Paste JSON here...");
        input.textComponent.fontSize = inputTextSize;
        input.pointSize = inputTextSize;

        TMP_Text status = CreateStatusText(panel.transform);
        status.fontSize = statusTextSize;

        // Buttons row
        GameObject row = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(panel.transform, false);

        HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
        h.childControlHeight = true;
        h.childControlWidth = true;
        h.childForceExpandWidth = true;
        h.spacing = 20f;

        LayoutElement rowLE = row.AddComponent<LayoutElement>();
        rowLE.minHeight = buttonHeight;

        Button importBtn = CreateButton(row.transform, "Import", controller.ImportFromPastedJson);
        Button cancelBtn = CreateButton(row.transform, "Cancel", controller.CloseImportPanel);

        // Hook into controller
        controller.ConfigureImportUI(overlay, input, status);
        overlay.SetActive(false);
    }

    TMP_Text CreateStatusText(Transform parent)
    {
        GameObject go = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = string.Empty;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = Color.white;
        ApplyReadableStyle(tmp, statusTextSize, false);

        return tmp;
    }

    TMP_InputField CreateTMPInputField(Transform parent, string placeholderText)
    {
        GameObject root = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
        root.transform.SetParent(parent, false);

        RectTransform rt = root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = new Vector2(0f, 0f);
        rt.offsetMax = new Vector2(0f, 0f);
        rt.sizeDelta = new Vector2(0f, 600f);

        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.35f);

        LayoutElement le = root.GetComponent<LayoutElement>();
        le.minHeight = 400f;
        le.flexibleHeight = 1f;

        // Text Area
        GameObject textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(root.transform, false);
        RectTransform taRT = textArea.GetComponent<RectTransform>();
        taRT.anchorMin = Vector2.zero;
        taRT.anchorMax = Vector2.one;
        taRT.offsetMin = new Vector2(12f, 12f);
        taRT.offsetMax = new Vector2(-12f, -12f);

        // Placeholder
        TextMeshProUGUI placeholder = CreateTMPTextChild(textArea.transform, "Placeholder", placeholderText, TextAlignmentOptions.TopLeft, new Color(1f, 1f, 1f, 0.35f));
        // Text
        TextMeshProUGUI text = CreateTMPTextChild(textArea.transform, "Text", string.Empty, TextAlignmentOptions.TopLeft, Color.white);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.richText = false;

        TMP_InputField input = root.GetComponent<TMP_InputField>();
        input.textViewport = taRT;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.MultiLineNewline;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.interactable = true;

        return input;
    }

    TextMeshProUGUI CreateTMPTextChild(Transform parent, string name, string text, TextAlignmentOptions align, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.alignment = align;
        tmp.color = color;
        ApplyReadableStyle(tmp, inputTextSize, false);

        return tmp;
    }

    void ApplyReadableStyle(TextMeshProUGUI tmp, float size, bool bold)
    {
        tmp.fontSize = size;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.enableAutoSizing = false;

        // If the font has an outline/material, use a subtle outline for readability.
        if (tmp.fontSharedMaterial != null)
        {
            tmp.fontMaterial = Instantiate(tmp.fontSharedMaterial); // avoid mutating shared asset
            var mat = tmp.fontMaterial;
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.15f); // thinner than before to avoid blur
            mat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 0.7f));
        }
    }
}
