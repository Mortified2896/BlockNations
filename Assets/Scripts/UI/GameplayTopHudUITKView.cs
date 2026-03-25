using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class GameplayTopHudUITKView : MonoBehaviour
{
    private const string LegacyTopHudName = "Upper HUD";
    private const string ThemeResourceName = "GameplayTopHud_UITK_Theme";

    [Header("Spike Toggle")]
    [SerializeField] private bool enableGameplayTopHudUITK = true;

    [Header("Optional Source Overrides")]
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private TMP_Text sourceStatusText;
    [SerializeField] private RectTransform legacyUpperHudRoot;

    private UIDocument uiDocument;
    private ThemeStyleSheet themeAsset;

    private VisualElement root;
    private VisualElement hudRoot;
    private Label turnLabel;
    private Label goldLabel;
    private Label statusLabel;
    private bool uiReady;
    private bool warnedMissingPanelSettings;
    private bool warnedMissingLabels;

    private TMP_Text sourceTurnText;
    private TMP_Text sourceGoldText;

    private CanvasGroup legacyUpperHudCanvasGroup;
    private bool cachedLegacyCanvasGroupState;
    private float cachedLegacyAlpha = 1f;
    private bool cachedLegacyInteractable = true;
    private bool cachedLegacyBlocksRaycasts = true;
    private bool legacyHudHidden;

    private Rect lastSafeArea = Rect.zero;
    private Vector2Int lastScreenSize = Vector2Int.zero;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
        if (themeAsset == null)
        {
            themeAsset = Resources.Load<ThemeStyleSheet>(ThemeResourceName);
        }
    }

    private void OnEnable()
    {
        ResolveSceneReferences(force: true);
        CacheUiElements(force: true);
    }

    private void OnDisable()
    {
        RestoreLegacyUpperHud();
        ClearUiCache();
    }

    private void OnDestroy()
    {
        RestoreLegacyUpperHud();
    }

    private void Update()
    {
        if (!enableGameplayTopHudUITK)
        {
            DisableOverlay();
            return;
        }

        if (!ResolveSceneReferences(force: false))
        {
            DisableOverlay();
            return;
        }

        if (!ShouldShowForMode(turnManager.currentMode))
        {
            DisableOverlay();
            return;
        }

        if (!EnsureUiReady())
        {
            RestoreLegacyUpperHud();
            return;
        }

        RefreshLabels();
        ApplySafeArea(force: false);
        HideLegacyUpperHud();
    }

    private bool ResolveSceneReferences(bool force)
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        if (turnManager == null || force)
        {
            turnManager = TurnManager.Instance;
            if (turnManager == null)
            {
                turnManager = UnityEngine.Object.FindFirstObjectByType<TurnManager>();
            }
        }

        if (turnManager == null)
        {
            return false;
        }

        sourceTurnText = turnManager.turnText;
        sourceGoldText = turnManager.goldText;
        TryResolveFallbackSourceTexts(turnManager.gameObject.scene);

        if (legacyUpperHudRoot == null || force)
        {
            legacyUpperHudRoot = ResolveLegacyUpperHudRoot(turnManager.gameObject.scene);
        }

        if (sourceStatusText == null || force)
        {
            sourceStatusText = ResolveStatusSourceText(turnManager.gameObject.scene);
        }

        return uiDocument != null && sourceTurnText != null && sourceGoldText != null;
    }

    private void TryResolveFallbackSourceTexts(UnityEngine.SceneManagement.Scene scene)
    {
        if (sourceTurnText != null && sourceGoldText != null)
        {
            return;
        }

        TMP_Text[] texts = UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text text = texts[i];
            if (text == null || text.gameObject.scene != scene)
            {
                continue;
            }

            string lowerName = text.name.ToLowerInvariant();

            if (sourceTurnText == null && lowerName.Contains("turn"))
            {
                sourceTurnText = text;
            }

            if (sourceGoldText == null && lowerName.Contains("gold"))
            {
                sourceGoldText = text;
            }

            if (sourceTurnText != null && sourceGoldText != null)
            {
                return;
            }
        }
    }

    private static bool ShouldShowForMode(TurnManager.GameMode mode)
    {
        return mode == TurnManager.GameMode.None ||
               mode == TurnManager.GameMode.VsAI ||
               mode == TurnManager.GameMode.PlayByPost;
    }

    private RectTransform ResolveLegacyUpperHudRoot(UnityEngine.SceneManagement.Scene scene)
    {
        if (sourceTurnText != null)
        {
            Transform cursor = sourceTurnText.transform;
            while (cursor != null)
            {
                if (string.Equals(cursor.name, LegacyTopHudName, StringComparison.Ordinal))
                {
                    return cursor as RectTransform;
                }

                cursor = cursor.parent;
            }

            RectTransform parentRect = sourceTurnText.transform.parent as RectTransform;
            if (parentRect != null)
            {
                return parentRect;
            }
        }

        RectTransform[] rects = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];
            if (rect == null || rect.gameObject.scene != scene)
            {
                continue;
            }

            if (string.Equals(rect.name, LegacyTopHudName, StringComparison.Ordinal))
            {
                return rect;
            }
        }

        return null;
    }

    private TMP_Text ResolveStatusSourceText(UnityEngine.SceneManagement.Scene scene)
    {
        PbpConnectionStatusView[] statusViews = UnityEngine.Object.FindObjectsByType<PbpConnectionStatusView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < statusViews.Length; i++)
        {
            PbpConnectionStatusView view = statusViews[i];
            if (view == null || view.gameObject.scene != scene)
            {
                continue;
            }

            TMP_Text textOnSameObject = view.GetComponent<TMP_Text>();
            if (textOnSameObject != null)
            {
                return textOnSameObject;
            }

            TMP_Text childText = view.GetComponentInChildren<TMP_Text>(true);
            if (childText != null)
            {
                return childText;
            }
        }

        if (legacyUpperHudRoot != null)
        {
            TMP_Text[] legacyTexts = legacyUpperHudRoot.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < legacyTexts.Length; i++)
            {
                TMP_Text text = legacyTexts[i];
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
        }

        return null;
    }

    private bool EnsureUiReady()
    {
        if (uiDocument == null)
        {
            return false;
        }

        if (!uiDocument.enabled)
        {
            uiDocument.enabled = true;
            uiReady = false;
        }

        if (uiDocument.panelSettings == null)
        {
            if (!warnedMissingPanelSettings)
            {
                warnedMissingPanelSettings = true;
                Debug.LogWarning("GameplayTopHudUITKView: UIDocument requires a PanelSettings asset assigned in scene.", this);
            }

            return false;
        }

        if (uiDocument.panelSettings.themeStyleSheet == null && themeAsset != null)
        {
            uiDocument.panelSettings.themeStyleSheet = themeAsset;
        }

        warnedMissingPanelSettings = false;
        return CacheUiElements(force: false);
    }

    private bool CacheUiElements(bool force)
    {
        if (uiDocument == null)
        {
            return false;
        }

        VisualElement currentRoot = uiDocument.rootVisualElement;
        if (currentRoot == null)
        {
            return false;
        }

        if (!force && uiReady && root == currentRoot)
        {
            return true;
        }

        root = currentRoot;
        hudRoot = root.Q<VisualElement>("GameplayTopHudRoot") ?? root;
        turnLabel = root.Q<Label>("TurnLabel");
        goldLabel = root.Q<Label>("GoldLabel");
        statusLabel = root.Q<Label>("PbpStatusLabel");

        if (turnLabel == null || goldLabel == null)
        {
            if (!warnedMissingLabels)
            {
                warnedMissingLabels = true;
                Debug.LogWarning("GameplayTopHudUITKView: TurnLabel/GoldLabel not found in UIDocument source asset.", this);
            }

            uiReady = false;
            return false;
        }

        warnedMissingLabels = false;
        SetNonInteractive(root);
        ApplySafeArea(force: true);
        uiReady = true;
        return true;
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

        if (statusLabel == null)
        {
            return;
        }

        bool showStatus = sourceStatusText != null &&
                          sourceStatusText.gameObject.activeInHierarchy &&
                          sourceStatusText.enabled &&
                          !string.IsNullOrWhiteSpace(sourceStatusText.text);

        statusLabel.text = showStatus ? sourceStatusText.text : string.Empty;
        statusLabel.style.display = showStatus ? DisplayStyle.Flex : DisplayStyle.None;
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

        VisualElement safeAreaTarget = hudRoot ?? root;
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

        ClearUiCache();
    }

    private void ClearUiCache()
    {
        root = null;
        hudRoot = null;
        turnLabel = null;
        goldLabel = null;
        statusLabel = null;
        uiReady = false;
        lastSafeArea = Rect.zero;
        lastScreenSize = Vector2Int.zero;
    }
}
