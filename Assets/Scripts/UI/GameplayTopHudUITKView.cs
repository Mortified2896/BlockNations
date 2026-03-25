using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;

public sealed class GameplayTopHudUITKView : MonoBehaviour
{
    private const string MainMenuSceneName = "MainMenu";
    private const string LegacyTopHudName = "Upper HUD";
    private const string LayoutResourceName = "GameplayTopHud_UITK";
    private const string StyleResourceName = "GameplayTopHud_UITK";
    private const string ThemeResourceName = "GameplayTopHud_UITK_Theme";
    private const string PanelSettingsResourceName = "GameplayTopHud_UITK_PanelSettings";
    private const int OverlaySortingOrder = 1000;

    private static GameplayTopHudUITKView instance;

    [Header("Spike Toggle")]
    [SerializeField] private bool enableGameplayTopHudUITK = true;

    private UIDocument uiDocument;
    private PanelSettings panelSettings;
    private VisualTreeAsset layoutAsset;
    private StyleSheet styleAsset;
    private ThemeStyleSheet themeAsset;

    private VisualElement root;
    private VisualElement hudRoot;
    private Label turnLabel;
    private Label goldLabel;
    private Label statusLabel;
    private bool uiReady;
    private bool overlayAttachedOnce;

    private TurnManager turnManager;
    private TMP_Text sourceTurnText;
    private TMP_Text sourceGoldText;
    private TMP_Text sourceStatusText;
    private RectTransform legacyUpperHudRoot;
    private CanvasGroup legacyUpperHudCanvasGroup;
    private bool cachedLegacyCanvasGroupState;
    private float cachedLegacyAlpha = 1f;
    private bool cachedLegacyInteractable = true;
    private bool cachedLegacyBlocksRaycasts = true;
    private bool legacyHudHidden;

    private Rect lastSafeArea = Rect.zero;
    private Vector2Int lastScreenSize = Vector2Int.zero;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private float nextOverlayDebugLogTime = -1f;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
        {
            return;
        }

        GameObject go = new GameObject("GameplayTopHudUITKView");
        DontDestroyOnLoad(go);
        go.AddComponent<GameplayTopHudUITKView>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        LoadResources();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        ClearSceneBindings();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        DisableOverlay();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }

        RestoreLegacyUpperHud();
    }

    private void Update()
    {
        LoadResources();

        if (!enableGameplayTopHudUITK)
        {
            DisableOverlay();
            return;
        }

        if (!TryBindGameplayScene())
        {
            DisableOverlay();
            return;
        }

        if (!EnsureOverlayReady())
        {
            RestoreLegacyUpperHud();
            return;
        }

        RefreshLabels();
        ApplySafeArea(force: false);

        if (IsOverlayAttached())
        {
            overlayAttachedOnce = true;
            HideLegacyUpperHud();
        }
        else
        {
            RestoreLegacyUpperHud();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogOverlayState("panel_not_attached");
#endif
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ClearSceneBindings();
    }

    private void LoadResources()
    {
        if (panelSettings == null)
        {
            panelSettings = Resources.Load<PanelSettings>(PanelSettingsResourceName);
        }

        if (layoutAsset == null)
        {
            layoutAsset = Resources.Load<VisualTreeAsset>(LayoutResourceName);
        }

        if (styleAsset == null)
        {
            styleAsset = Resources.Load<StyleSheet>(StyleResourceName);
        }

        if (themeAsset == null)
        {
            themeAsset = Resources.Load<ThemeStyleSheet>(ThemeResourceName);
        }
    }

    private bool TryBindGameplayScene()
    {
        if (turnManager == null || !IsGameplayScene(turnManager.gameObject.scene))
        {
            turnManager = TurnManager.Instance;
            if (turnManager == null)
            {
                turnManager = UnityEngine.Object.FindFirstObjectByType<TurnManager>();
            }
        }

        if (turnManager == null || !IsGameplayScene(turnManager.gameObject.scene))
        {
            return false;
        }

        if (!ShouldShowForMode(turnManager.currentMode))
        {
            return false;
        }

        sourceTurnText = turnManager.turnText;
        sourceGoldText = turnManager.goldText;
        legacyUpperHudRoot = ResolveLegacyUpperHudRoot(turnManager.gameObject.scene);
        sourceStatusText = ResolvePbpStatusText();
        return sourceTurnText != null && sourceGoldText != null;
    }

    private static bool ShouldShowForMode(TurnManager.GameMode mode)
    {
        return mode == TurnManager.GameMode.VsAI || mode == TurnManager.GameMode.PlayByPost;
    }

    private bool IsGameplayScene(Scene scene)
    {
        return scene.IsValid() &&
               scene.isLoaded &&
               !string.IsNullOrEmpty(scene.name) &&
               !string.Equals(scene.name, MainMenuSceneName, StringComparison.Ordinal);
    }

    private RectTransform ResolveLegacyUpperHudRoot(Scene scene)
    {
        RectTransform byName = FindRectByNameInScene(LegacyTopHudName, scene);
        if (byName != null)
        {
            return byName;
        }

        if (sourceTurnText == null)
        {
            return null;
        }

        Transform cursor = sourceTurnText.transform;
        while (cursor != null)
        {
            if (string.Equals(cursor.name, LegacyTopHudName, StringComparison.Ordinal))
            {
                return cursor as RectTransform;
            }

            cursor = cursor.parent;
        }

        return sourceTurnText.transform.parent as RectTransform;
    }

    private TMP_Text ResolvePbpStatusText()
    {
        if (legacyUpperHudRoot == null)
        {
            return null;
        }

        TMP_Text[] texts = legacyUpperHudRoot.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text text = texts[i];
            if (text == null)
            {
                continue;
            }

            string name = text.name.ToLowerInvariant();
            if (name.Contains("pbp") || name.Contains("connection"))
            {
                return text;
            }
        }

        return null;
    }

    private static RectTransform FindRectByNameInScene(string objectName, Scene scene)
    {
        RectTransform[] rects = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include);
        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];
            if (rect == null)
            {
                continue;
            }

            if (rect.gameObject.scene != scene)
            {
                continue;
            }

            if (string.Equals(rect.name, objectName, StringComparison.Ordinal))
            {
                return rect;
            }
        }

        return null;
    }

    private bool TryResolveSceneDocument(Scene scene)
    {
        if (uiDocument != null && uiDocument.gameObject.scene == scene)
        {
            return true;
        }

        uiDocument = null;

        UIDocument[] docs = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include);
        for (int i = 0; i < docs.Length; i++)
        {
            UIDocument candidate = docs[i];
            if (candidate == null || candidate.gameObject.scene != scene)
            {
                continue;
            }

            if (panelSettings != null && candidate.panelSettings == panelSettings)
            {
                uiDocument = candidate;
                break;
            }
        }

        if (uiDocument == null)
        {
            for (int i = 0; i < docs.Length; i++)
            {
                UIDocument candidate = docs[i];
                if (candidate == null || candidate.gameObject.scene != scene)
                {
                    continue;
                }

                if (candidate.panelSettings == null || panelSettings == null)
                {
                    continue;
                }

                if (string.Equals(candidate.panelSettings.name, panelSettings.name, StringComparison.Ordinal))
                {
                    uiDocument = candidate;
                    break;
                }
            }
        }

        uiReady = false;
        return uiDocument != null;
    }

    private bool EnsureOverlayReady()
    {
        if (turnManager == null)
        {
            return false;
        }

        if (!TryResolveSceneDocument(turnManager.gameObject.scene))
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogOverlayState("scene_uidocument_missing");
#endif
            return false;
        }

        if (uiDocument == null)
        {
            return false;
        }

        if (uiDocument.panelSettings == null && panelSettings != null)
        {
            uiDocument.panelSettings = panelSettings;
            uiReady = false;
        }

        if (uiDocument.panelSettings == null)
        {
            return false;
        }

        uiDocument.panelSettings.sortingOrder = OverlaySortingOrder;
        uiDocument.sortingOrder = OverlaySortingOrder;

        if (uiDocument.panelSettings.themeStyleSheet == null && themeAsset != null)
        {
            uiDocument.panelSettings.themeStyleSheet = themeAsset;
        }

        if (!uiDocument.enabled)
        {
            uiDocument.enabled = true;
            uiReady = false;
            return false;
        }

        root = uiDocument.rootVisualElement;
        if (root == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogOverlayState("root_missing");
#endif
            return false;
        }

        if (!IsOverlayAttached())
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogOverlayState("panel_missing");
#endif
            return false;
        }

        if (uiReady)
        {
            return true;
        }

        if (layoutAsset == null)
        {
            return false;
        }

        root.Clear();
        layoutAsset.CloneTree(root);

        if (styleAsset != null)
        {
            root.styleSheets.Remove(styleAsset);
            root.styleSheets.Add(styleAsset);
        }

        SetNonInteractive(root);
        hudRoot = root.Q<VisualElement>("GameplayTopHudRoot");
        turnLabel = root.Q<Label>("TurnLabel");
        goldLabel = root.Q<Label>("GoldLabel");
        statusLabel = root.Q<Label>("PbpStatusLabel");

        if (turnLabel == null || goldLabel == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogOverlayState("labels_missing");
#endif
            return false;
        }

        if (hudRoot == null)
        {
            hudRoot = root;
        }

        ApplySafeArea(force: true);
        uiReady = true;
        return true;
    }

    private bool IsOverlayAttached()
    {
        return uiDocument != null &&
               uiDocument.enabled &&
               root != null &&
               root.panel != null;
    }

    private static void SetNonInteractive(VisualElement element)
    {
        if (element == null)
        {
            return;
        }

        element.pickingMode = PickingMode.Ignore;
        foreach (VisualElement child in element.Children())
        {
            SetNonInteractive(child);
        }
    }

    private void RefreshLabels()
    {
        if (!uiReady)
        {
            return;
        }

        turnLabel.text = sourceTurnText != null ? sourceTurnText.text : string.Empty;
        goldLabel.text = sourceGoldText != null ? sourceGoldText.text : string.Empty;

        if (statusLabel != null)
        {
            bool showStatus = turnManager != null &&
                              turnManager.currentMode == TurnManager.GameMode.PlayByPost &&
                              sourceStatusText != null &&
                              sourceStatusText.gameObject.activeInHierarchy &&
                              sourceStatusText.enabled &&
                              !string.IsNullOrWhiteSpace(sourceStatusText.text);

            statusLabel.text = showStatus ? sourceStatusText.text : string.Empty;
            statusLabel.style.display = showStatus ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void HideLegacyUpperHud()
    {
        if (legacyHudHidden || legacyUpperHudRoot == null)
        {
            return;
        }

        legacyUpperHudCanvasGroup = legacyUpperHudRoot.GetComponent<CanvasGroup>();
        if (legacyUpperHudCanvasGroup == null)
        {
            legacyUpperHudCanvasGroup = legacyUpperHudRoot.gameObject.AddComponent<CanvasGroup>();
        }

        cachedLegacyAlpha = legacyUpperHudCanvasGroup.alpha;
        cachedLegacyInteractable = legacyUpperHudCanvasGroup.interactable;
        cachedLegacyBlocksRaycasts = legacyUpperHudCanvasGroup.blocksRaycasts;
        cachedLegacyCanvasGroupState = true;

        legacyUpperHudCanvasGroup.alpha = 0f;
        legacyUpperHudCanvasGroup.interactable = false;
        legacyUpperHudCanvasGroup.blocksRaycasts = false;
        legacyHudHidden = true;
    }

    private void RestoreLegacyUpperHud()
    {
        if (!legacyHudHidden || legacyUpperHudCanvasGroup == null)
        {
            legacyHudHidden = false;
            return;
        }

        if (cachedLegacyCanvasGroupState)
        {
            legacyUpperHudCanvasGroup.alpha = cachedLegacyAlpha;
            legacyUpperHudCanvasGroup.interactable = cachedLegacyInteractable;
            legacyUpperHudCanvasGroup.blocksRaycasts = cachedLegacyBlocksRaycasts;
        }

        legacyHudHidden = false;
    }

    private void ApplySafeArea(bool force)
    {
        if (!uiReady || root == null)
        {
            return;
        }

        Rect safeArea = Screen.safeArea;
        Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
        if (screenSize.x <= 0 || screenSize.y <= 0)
        {
            return;
        }

        if (!force && safeArea == lastSafeArea && screenSize == lastScreenSize)
        {
            return;
        }

        lastSafeArea = safeArea;
        lastScreenSize = screenSize;

        float leftInset = safeArea.xMin;
        float rightInset = screenSize.x - safeArea.xMax;
        float topInset = screenSize.y - safeArea.yMax;

        VisualElement safeAreaTarget = hudRoot != null ? hudRoot : root;
        safeAreaTarget.style.paddingLeft = leftInset;
        safeAreaTarget.style.paddingRight = rightInset;
        safeAreaTarget.style.paddingTop = topInset;
        safeAreaTarget.style.paddingBottom = 0f;
    }

    private void DisableOverlay()
    {
        RestoreLegacyUpperHud();

        if (uiDocument != null)
        {
            uiDocument.enabled = false;
        }

        root = null;
        hudRoot = null;
        turnLabel = null;
        goldLabel = null;
        statusLabel = null;
        uiReady = false;
        overlayAttachedOnce = false;
    }

    private void ClearSceneBindings()
    {
        RestoreLegacyUpperHud();
        turnManager = null;
        sourceTurnText = null;
        sourceGoldText = null;
        sourceStatusText = null;
        legacyUpperHudRoot = null;
        legacyUpperHudCanvasGroup = null;
        cachedLegacyCanvasGroupState = false;
        lastSafeArea = Rect.zero;
        lastScreenSize = Vector2Int.zero;
        DisableOverlay();
        uiDocument = null;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void LogOverlayState(string reason)
    {
        if (Time.unscaledTime < nextOverlayDebugLogTime)
        {
            return;
        }

        nextOverlayDebugLogTime = Time.unscaledTime + 1f;

        string panelName = panelSettings != null ? panelSettings.name : "<null>";
        string hostName = uiDocument != null ? uiDocument.gameObject.name : "<null>";
        string rootName = root != null ? root.name : "<null>";
        string turnValue = turnLabel != null ? turnLabel.text : "<null>";
        string goldValue = goldLabel != null ? goldLabel.text : "<null>";
        string sourceTurnValue = sourceTurnText != null ? sourceTurnText.text : "<null>";
        string sourceGoldValue = sourceGoldText != null ? sourceGoldText.text : "<null>";

        Debug.Log(
            $"[GameplayTopHudUITK] state reason={reason} " +
            $"docEnabled={(uiDocument != null && uiDocument.enabled)} " +
            $"runtimePanel={(uiDocument != null && uiDocument.runtimePanel != null)} " +
            $"host={hostName} panelSettings={panelName} root={rootName} " +
            $"uiReady={uiReady} overlayAttachedOnce={overlayAttachedOnce} " +
            $"turnLabel={turnValue} goldLabel={goldValue} " +
            $"sourceTurn={sourceTurnValue} sourceGold={sourceGoldValue}");
    }
#endif
}
