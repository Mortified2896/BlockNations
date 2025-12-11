using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Canvas-based hotseat handoff screen that hides the board and waits for the next player.
/// </summary>
public class HotseatTurnOverlay : MonoBehaviour
{
    [Header("Layout")]
    public float referenceWidth = 1170f;   // iPhone 12 Pro ref
    public float referenceHeight = 2532f;

    [Header("Colors")]
    public Color overlayColor = new Color(0f, 0f, 0f, 0.95f);
    public Color panelColor = new Color(0.16f, 0.16f, 0.16f, 1f);
    public Color textColor = Color.white;
    public Color buttonTextColor = Color.white;

    [Header("Fonts")]
    public Font font;

    private Canvas canvas;
    private GameObject overlayRoot;
    private Image overlayImage;
    private Text titleText;
    private Text infoText;
    private Button continueButton;
    private Text buttonText;
    private RectTransform panelRect;
    private GraphicRaycaster raycaster;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Object.FindFirstObjectByType<HotseatTurnOverlay>() != null)
            return;

        GameObject go = new GameObject("HotseatTurnOverlay");
        go.AddComponent<HotseatTurnOverlay>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        EnsureEventSystem();
        BuildCanvas();
    }

    void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
        DontDestroyOnLoad(es);
    }

    void BuildCanvas()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500; // above normal UI
        gameObject.AddComponent<CanvasScaler>();
        raycaster = gameObject.AddComponent<GraphicRaycaster>();

        overlayRoot = new GameObject("Overlay");
        overlayRoot.transform.SetParent(canvas.transform, false);
        RectTransform rootRect = overlayRoot.AddComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        overlayImage = overlayRoot.AddComponent<Image>();
        overlayImage.color = overlayColor;

        GameObject panel = new GameObject("Panel");
        panel.transform.SetParent(overlayRoot.transform, false);
        panelRect = panel.AddComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(600f, 360f);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;

        Image panelBg = panel.AddComponent<Image>();
        panelBg.color = panelColor;

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.spacing = 12f;

        // Larger font sizes for better readability on devices.
        titleText = CreateText(panel.transform, "Title", 64, FontStyle.Bold);
        infoText = CreateText(panel.transform, "Info", 40, FontStyle.Normal);
        infoText.text = "Pass the device to the next player, then continue.";

        continueButton = CreateButton(panel.transform, out buttonText);
        buttonText.text = "Continue";

        continueButton.onClick.AddListener(() =>
        {
            if (TurnManager.Instance != null)
            {
                TurnManager.Instance.ContinueHotseatTurn();
            }
        });

        overlayRoot.SetActive(false);
    }

    Text CreateText(Transform parent, string name, int fontSize, FontStyle style)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text text = go.AddComponent<Text>();
        text.alignment = TextAnchor.MiddleCenter;
        text.font = font != null ? font : GetDefaultFont();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = textColor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    Button CreateButton(Transform parent, out Text label)
    {
        GameObject go = new GameObject("Button");
        go.transform.SetParent(parent, false);

        Image img = go.AddComponent<Image>();
        img.color = new Color(0.22f, 0.22f, 0.22f, 1f);

        Button btn = go.AddComponent<Button>();

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0f, 72f);

        // Larger button label font size as well.
        label = CreateText(go.transform, "Label", 40, FontStyle.Bold);
        label.color = buttonTextColor;

        HorizontalLayoutGroup hLayout = go.AddComponent<HorizontalLayoutGroup>();
        hLayout.childAlignment = TextAnchor.MiddleCenter;
        hLayout.padding = new RectOffset(12, 12, 12, 12);

        return btn;
    }

    Font GetDefaultFont()
    {
        // LegacyRuntime.ttf is the supported built-in font in newer Unity
        Font builtIn = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (builtIn != null)
            return builtIn;

        // Fallback for versions where LegacyRuntime is unavailable
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    void Update()
    {
        TurnManager tm = TurnManager.Instance;
        bool shouldShow = tm != null && tm.currentMode == TurnManager.GameMode.Hotseat && tm.IsHotseatHandoff;

        if (overlayRoot.activeSelf != shouldShow)
        {
            overlayRoot.SetActive(shouldShow);
        }

        // Ensure raycasts only when visible
        if (raycaster != null)
        {
            raycaster.enabled = shouldShow;
        }

        canvas.enabled = shouldShow;

        if (!shouldShow)
            return;

        // Scale panel for device size
        float scale = Mathf.Clamp(Mathf.Min(Screen.width / referenceWidth, Screen.height / referenceHeight), 0.75f, 1.3f);
        panelRect.localScale = new Vector3(scale, scale, 1f);

        if (tm != null)
        {
            titleText.text = $"{tm.GetCurrentSideName()} Turn";
        }

        overlayImage.color = overlayColor;
        titleText.color = textColor;
        infoText.color = textColor;
        buttonText.color = buttonTextColor;
    }
}
