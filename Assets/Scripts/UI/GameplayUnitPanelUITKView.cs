using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class GameplayUnitPanelUITKView : MonoBehaviour
{
    private const string LayoutResourceName = "GameplayUnitPanel_UITK";
    private const string ThemeResourceName = "GameplayTopHud_UITK_Theme";
    private const string LegacyUnitPanelName = "UnitPanel";

    [Header("Migration Toggle")]
    [SerializeField] private bool enableUnitPanelUITK = true;

    [Header("Optional Source Overrides")]
    [SerializeField] private UnitUIManager unitUIManager;
    [SerializeField] private RectTransform legacyUnitPanelRoot;

    private UIDocument uiDocument;
    private VisualTreeAsset layoutAsset;
    private ThemeStyleSheet themeAsset;

    private VisualElement root;
    private VisualElement hudRoot;
    private VisualElement unitPanelContainer;
    private Label unitNameLabel;
    private Label unitHealthLabel;
    private Label unitAttackLabel;
    private Label unitDefenseLabel;
    private Button closeButton;

    private bool uiReady;
    private bool callbacksBound;
    private bool warnedMissingPanelSettings;
    private bool warnedMissingLayout;
    private bool warnedMissingControls;

    private CanvasGroup legacyUnitPanelCanvasGroup;
    private bool cachedLegacyState;
    private float cachedLegacyAlpha = 1f;
    private bool cachedLegacyInteractable = true;
    private bool cachedLegacyBlocksRaycasts = true;
    private bool legacyPanelHidden;

    private Rect lastSafeArea = Rect.zero;
    private Vector2Int lastScreenSize = Vector2Int.zero;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
        if (layoutAsset == null)
        {
            layoutAsset = Resources.Load<VisualTreeAsset>(LayoutResourceName);
        }

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
        RestoreLegacyUnitPanel();
        ClearUiCache();
    }

    private void OnDestroy()
    {
        RestoreLegacyUnitPanel();
    }

    private void Update()
    {
        if (!enableUnitPanelUITK)
        {
            DisableOverlay();
            return;
        }

        if (!ResolveSceneReferences(force: false))
        {
            DisableOverlay();
            return;
        }

        if (!EnsureUiReady())
        {
            RestoreLegacyUnitPanel();
            return;
        }

        RefreshUiState();
        ApplySafeArea(force: false);
        HideLegacyUnitPanel();
    }

    private bool ResolveSceneReferences(bool force)
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        if (unitUIManager == null || force)
        {
            unitUIManager = UnitUIManager.Instance;
            if (unitUIManager == null)
            {
                unitUIManager = UnityEngine.Object.FindFirstObjectByType<UnitUIManager>();
            }
        }

        if (legacyUnitPanelRoot == null || force)
        {
            legacyUnitPanelRoot = ResolveLegacyUnitPanelRoot(gameObject.scene);
        }

        return uiDocument != null && unitUIManager != null;
    }

    private RectTransform ResolveLegacyUnitPanelRoot(Scene scene)
    {
        if (unitUIManager != null && unitUIManager.panelRoot != null)
        {
            RectTransform unitPanelRect = unitUIManager.panelRoot.transform as RectTransform;
            if (unitPanelRect != null)
            {
                return unitPanelRect;
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

            if (string.Equals(rect.name, LegacyUnitPanelName, StringComparison.Ordinal))
            {
                return rect;
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
                Debug.LogWarning("GameplayUnitPanelUITKView: UIDocument requires a PanelSettings asset assigned in scene.", this);
            }

            return false;
        }

        warnedMissingPanelSettings = false;

        if (uiDocument.panelSettings.themeStyleSheet == null && themeAsset != null)
        {
            uiDocument.panelSettings.themeStyleSheet = themeAsset;
        }

        if (uiDocument.visualTreeAsset == null)
        {
            if (layoutAsset == null)
            {
                layoutAsset = Resources.Load<VisualTreeAsset>(LayoutResourceName);
            }

            if (layoutAsset == null)
            {
                if (!warnedMissingLayout)
                {
                    warnedMissingLayout = true;
                    Debug.LogWarning("GameplayUnitPanelUITKView: GameplayUnitPanel_UITK layout not found in Resources.", this);
                }

                return false;
            }

            uiDocument.visualTreeAsset = layoutAsset;
        }

        warnedMissingLayout = false;
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

        UnbindButtons();

        root = currentRoot;
        hudRoot = root.Q<VisualElement>("UnitPanelHudRoot") ?? root;
        unitPanelContainer = root.Q<VisualElement>("UnitPanelContainer");
        unitNameLabel = root.Q<Label>("UnitNameLabel");
        unitHealthLabel = root.Q<Label>("UnitHealthLabel");
        unitAttackLabel = root.Q<Label>("UnitAttackLabel");
        unitDefenseLabel = root.Q<Label>("UnitDefenseLabel");
        closeButton = root.Q<Button>("UnitCloseButton");

        if (unitPanelContainer == null ||
            unitNameLabel == null ||
            unitHealthLabel == null ||
            unitAttackLabel == null ||
            unitDefenseLabel == null ||
            closeButton == null)
        {
            if (!warnedMissingControls)
            {
                warnedMissingControls = true;
                Debug.LogWarning("GameplayUnitPanelUITKView: Unit panel controls not found in UIDocument source asset.", this);
            }

            uiReady = false;
            return false;
        }

        warnedMissingControls = false;
        ApplyVisualDefaults();
        ConfigurePickingModes();
        BindButtons();
        ApplySafeArea(force: true);
        unitPanelContainer.style.display = DisplayStyle.None;
        uiReady = true;
        return true;
    }

    private void ApplyVisualDefaults()
    {
        if (root != null)
        {
            root.style.backgroundColor = Color.clear;
        }

        if (hudRoot != null)
        {
            hudRoot.style.backgroundColor = Color.clear;
        }
    }

    private void ConfigurePickingModes()
    {
        if (root != null)
        {
            root.pickingMode = PickingMode.Ignore;
        }

        if (hudRoot != null)
        {
            hudRoot.pickingMode = PickingMode.Ignore;
        }

        if (unitPanelContainer != null)
        {
            unitPanelContainer.pickingMode = PickingMode.Ignore;
        }

        if (unitNameLabel != null)
        {
            unitNameLabel.pickingMode = PickingMode.Ignore;
        }

        if (unitHealthLabel != null)
        {
            unitHealthLabel.pickingMode = PickingMode.Ignore;
        }

        if (unitAttackLabel != null)
        {
            unitAttackLabel.pickingMode = PickingMode.Ignore;
        }

        if (unitDefenseLabel != null)
        {
            unitDefenseLabel.pickingMode = PickingMode.Ignore;
        }

        if (closeButton != null)
        {
            closeButton.pickingMode = PickingMode.Position;
        }
    }

    private void BindButtons()
    {
        if (callbacksBound)
        {
            return;
        }

        if (closeButton != null)
        {
            closeButton.clicked += HandleCloseClicked;
        }

        callbacksBound = true;
    }

    private void UnbindButtons()
    {
        if (!callbacksBound)
        {
            return;
        }

        if (closeButton != null)
        {
            closeButton.clicked -= HandleCloseClicked;
        }

        callbacksBound = false;
    }

    private void HandleCloseClicked()
    {
        if (unitUIManager != null)
        {
            unitUIManager.ClosePanel();
        }
    }

    private void RefreshUiState()
    {
        if (!uiReady || unitPanelContainer == null)
        {
            return;
        }

        Unit currentUnit = unitUIManager != null ? unitUIManager.CurrentUnit : null;
        bool panelOpen = currentUnit != null;
        unitPanelContainer.style.display = panelOpen ? DisplayStyle.Flex : DisplayStyle.None;

        if (!panelOpen)
        {
            return;
        }

        if (unitNameLabel != null)
        {
            unitNameLabel.text = GetUnitNameOrFallback(currentUnit, "Unit");
        }

        if (unitHealthLabel != null)
        {
            unitHealthLabel.text = $"HP: {currentUnit.currentHealth}/{currentUnit.maxHealth}";
        }

        if (unitAttackLabel != null)
        {
            unitAttackLabel.text = $"ATK: {currentUnit.attack}";
        }

        if (unitDefenseLabel != null)
        {
            unitDefenseLabel.text = $"DEF: {currentUnit.defense}";
        }
    }

    private static string GetUnitNameOrFallback(Unit unit, string fallback)
    {
        if (unit == null || string.IsNullOrWhiteSpace(unit.name))
        {
            return fallback;
        }

        string rawName = unit.name;
        const string cloneSuffix = "(Clone)";
        if (rawName.EndsWith(cloneSuffix, StringComparison.Ordinal))
        {
            rawName = rawName.Substring(0, rawName.Length - cloneSuffix.Length).TrimEnd();
        }

        return string.IsNullOrWhiteSpace(rawName) ? fallback : rawName;
    }

    private void HideLegacyUnitPanel()
    {
        if (legacyPanelHidden || legacyUnitPanelRoot == null)
        {
            return;
        }

        legacyUnitPanelCanvasGroup = legacyUnitPanelRoot.GetComponent<CanvasGroup>();
        if (legacyUnitPanelCanvasGroup == null)
        {
            legacyUnitPanelCanvasGroup = legacyUnitPanelRoot.gameObject.AddComponent<CanvasGroup>();
        }

        cachedLegacyAlpha = legacyUnitPanelCanvasGroup.alpha;
        cachedLegacyInteractable = legacyUnitPanelCanvasGroup.interactable;
        cachedLegacyBlocksRaycasts = legacyUnitPanelCanvasGroup.blocksRaycasts;
        cachedLegacyState = true;

        legacyUnitPanelCanvasGroup.alpha = 0f;
        legacyUnitPanelCanvasGroup.interactable = false;
        legacyUnitPanelCanvasGroup.blocksRaycasts = false;
        legacyPanelHidden = true;
    }

    private void RestoreLegacyUnitPanel()
    {
        if (!legacyPanelHidden || legacyUnitPanelCanvasGroup == null)
        {
            legacyPanelHidden = false;
            return;
        }

        if (cachedLegacyState)
        {
            legacyUnitPanelCanvasGroup.alpha = cachedLegacyAlpha;
            legacyUnitPanelCanvasGroup.interactable = cachedLegacyInteractable;
            legacyUnitPanelCanvasGroup.blocksRaycasts = cachedLegacyBlocksRaycasts;
        }

        legacyPanelHidden = false;
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
        float bottomInset = safeArea.yMin;

        VisualElement safeAreaTarget = hudRoot ?? root;
        safeAreaTarget.style.paddingLeft = leftInset;
        safeAreaTarget.style.paddingRight = rightInset;
        safeAreaTarget.style.paddingTop = 0f;
        safeAreaTarget.style.paddingBottom = bottomInset;
    }

    private void DisableOverlay()
    {
        RestoreLegacyUnitPanel();

        if (uiDocument != null)
        {
            uiDocument.enabled = false;
        }

        ClearUiCache();
    }

    private void ClearUiCache()
    {
        UnbindButtons();
        root = null;
        hudRoot = null;
        unitPanelContainer = null;
        unitNameLabel = null;
        unitHealthLabel = null;
        unitAttackLabel = null;
        unitDefenseLabel = null;
        closeButton = null;
        uiReady = false;
        lastSafeArea = Rect.zero;
        lastScreenSize = Vector2Int.zero;
    }
}
