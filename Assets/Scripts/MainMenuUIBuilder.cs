using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Builds a basic main menu UI at runtime so you don't have to wire buttons manually.
/// Attach this to an empty GameObject in the MainMenu scene, disable your old Canvas,
/// and it will spawn a new Canvas with scalable layout and hook to MainMenuController.
/// </summary>
public class MainMenuUIBuilder : MonoBehaviour
{
    [Header("Layout")]
    public Vector2 referenceResolution = new Vector2(1080, 1920);
    public float buttonHeight = 120f;
    public float panelWidth = 900f;

    [Header("Styling")]
    public Color backgroundColor = new Color(0.07f, 0.09f, 0.12f, 0.9f);
    public Color panelColor = new Color(0.15f, 0.17f, 0.21f, 0.95f);
    public Color buttonColor = new Color(0.24f, 0.56f, 0.86f, 1f);
    public Color buttonHoverColor = new Color(0.3f, 0.65f, 0.95f, 1f);
    public Color buttonTextColor = Color.white;
    public Color titleColor = Color.white;
    public int titleSize = 48;
    public int buttonTextSize = 32;
    public int statusTextSize = 28;
    public int inputTextSize = 28;

    [Header("References")]
    public MainMenuController controller;

    void Awake()
    {
        if (controller == null)
        {
            controller = FindObjectOfType<MainMenuController>();
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

        CreateButton(root, "Continue", controller.ContinueLastSave);
        CreateButton(root, "Play vs AI", controller.PlayVsAI);
        CreateButton(root, "Hotseat", controller.PlayHotseat);
        CreateButton(root, "Import JSON", controller.OpenImportPanel);
        CreateButton(root, "Quit", controller.QuitGame);

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
            {
                return existingCanvas;
            }
        }

        GameObject go = new GameObject("NewUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        return canvas;
    }

    void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;

        GameObject go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        go.transform.SetAsLastSibling();
    }

    RectTransform CreateRootPanel(Transform parent)
    {
        GameObject panel = new GameObject("MenuRoot", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.transform.SetParent(parent, false);

        Image bg = panel.GetComponent<Image>();
        bg.color = backgroundColor;

        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(panelWidth, 0f);

        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 20f;
        layout.padding = new RectOffset(40, 40, 60, 60);

        ContentSizeFitter fitter = panel.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        return rt;
    }

    TMP_Text CreateTitle(Transform parent, string text)
    {
        GameObject go = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, buttonHeight);

        LayoutElement le = go.GetComponent<LayoutElement>();
        le.minHeight = buttonHeight * 0.9f;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = titleSize;
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

        Button btn = go.GetComponent<Button>();
        btn.onClick.AddListener(action);

        // Hover effect
        ButtonHoverEffect hover = go.AddComponent<ButtonHoverEffect>();
        hover.Configure(img, buttonColor, buttonHoverColor);

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
        tmp.fontSize = buttonTextSize;
        tmp.color = buttonTextColor;

        return btn;
    }

    void BuildImportPanel(Transform parent)
    {
        // Overlay
        GameObject overlay = new GameObject("ImportPanel", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(parent, false);
        RectTransform rt = overlay.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image img = overlay.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.6f);

        // Inner panel
        GameObject panel = new GameObject("Content", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(overlay.transform, false);
        RectTransform prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.1f, 0.15f);
        prt.anchorMax = new Vector2(0.9f, 0.85f);
        prt.offsetMin = Vector2.zero;
        prt.offsetMax = Vector2.zero;

        Image pimg = panel.GetComponent<Image>();
        pimg.color = panelColor;

        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 16f;
        layout.padding = new RectOffset(32, 32, 32, 32);

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

        // Configure controller with created references
        controller.ConfigureImportUI(overlay, input, status);
        overlay.SetActive(false);
    }

    TMP_Text CreateStatusText(Transform parent)
    {
        GameObject go = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = string.Empty;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = Color.white;
        tmp.enableWordWrapping = true;
        return tmp;
    }

    TMP_InputField CreateTMPInputField(Transform parent, string placeholderText)
    {
        GameObject root = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
        root.transform.SetParent(parent, false);

        RectTransform rt = root.GetComponent<RectTransform>();
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
        text.enableWordWrapping = true;
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
        tmp.fontSize = inputTextSize;

        return tmp;
    }
}
