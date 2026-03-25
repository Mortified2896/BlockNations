using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class GameplayBottomHudUITKView : MonoBehaviour
{
    private const string LayoutResourceName = "GameplayBottomHud_UITK";
    private const string ThemeResourceName = "GameplayTopHud_UITK_Theme";
    private const string LegacyDefaultHudName = "Buttom HUD";

    [Header("Spike Toggle")]
    [SerializeField] private bool enableGameplayBottomHudUITK = true;

    [Header("Optional Source Overrides")]
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private GameMenuActions gameMenuActions;
    [SerializeField] private RectTransform legacyDefaultHudRoot;

    private UIDocument uiDocument;
    private VisualTreeAsset layoutAsset;
    private ThemeStyleSheet themeAsset;

    private VisualElement root;
    private VisualElement hudRoot;
    private VisualElement defaultBottomPanel;
    private UnityEngine.UIElements.Button menuButton;
    private UnityEngine.UIElements.Button nextButton;

    private bool uiReady;
    private bool callbacksBound;
    private bool warnedMissingPanelSettings;
    private bool warnedMissingLayout;
    private bool warnedMissingControls;

    private BottomStripController bottomStripController;

    private Rect lastSafeArea = Rect.zero;
    private Vector2Int lastScreenSize = Vector2Int.zero;

    private bool legacyButtonsHidden;
    private readonly List<LegacyButtonState> hiddenLegacyButtons = new List<LegacyButtonState>(2);

    private sealed class LegacyButtonState
    {
        public UnityEngine.UI.Button button;
        public CanvasGroup canvasGroup;
        public float alpha;
        public bool interactable;
        public bool blocksRaycasts;
        public bool buttonInteractable;
    }

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
        RestoreLegacyButtons();
        ClearUiCache();
    }

    private void OnDestroy()
    {
        RestoreLegacyButtons();
    }

    private void Update()
    {
        if (!enableGameplayBottomHudUITK)
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
            RestoreLegacyButtons();
            return;
        }

        RefreshUiState();
        ApplySafeArea(force: false);
        HideLegacyButtons();
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

        if (gameMenuActions == null || force)
        {
            gameMenuActions = UnityEngine.Object.FindFirstObjectByType<GameMenuActions>();
        }

        if (bottomStripController == null || force)
        {
            bottomStripController = BottomStripController.Instance;
            if (bottomStripController == null)
            {
                bottomStripController = UnityEngine.Object.FindFirstObjectByType<BottomStripController>();
            }

            if (bottomStripController == null)
            {
                BottomStripController[] controllers = UnityEngine.Object.FindObjectsByType<BottomStripController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (controllers != null && controllers.Length > 0)
                {
                    bottomStripController = controllers[0];
                }
            }
        }

        if (legacyDefaultHudRoot == null || force)
        {
            legacyDefaultHudRoot = ResolveLegacyDefaultHudRoot(gameObject.scene);
        }

        return uiDocument != null && turnManager != null && gameMenuActions != null;
    }

    private RectTransform ResolveLegacyDefaultHudRoot(UnityEngine.SceneManagement.Scene scene)
    {
        RectTransform[] rects = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];
            if (rect == null || rect.gameObject.scene != scene)
            {
                continue;
            }

            if (string.Equals(rect.name, LegacyDefaultHudName, StringComparison.Ordinal))
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
                Debug.LogWarning("GameplayBottomHudUITKView: UIDocument requires a PanelSettings asset assigned in scene.", this);
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
                    Debug.LogWarning("GameplayBottomHudUITKView: GameplayBottomHud_UITK layout not found in Resources.", this);
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
        hudRoot = root.Q<VisualElement>("GameplayBottomHudRoot") ?? root;
        defaultBottomPanel = root.Q<VisualElement>("DefaultBottomPanel");
        menuButton = root.Q<UnityEngine.UIElements.Button>("MenuButton");
        nextButton = root.Q<UnityEngine.UIElements.Button>("NextButton");

        if (defaultBottomPanel == null || menuButton == null || nextButton == null)
        {
            if (!warnedMissingControls)
            {
                warnedMissingControls = true;
                Debug.LogWarning("GameplayBottomHudUITKView: DefaultBottomPanel/MenuButton/NextButton not found in UIDocument source asset.", this);
            }

            uiReady = false;
            return false;
        }

        warnedMissingControls = false;
        ConfigurePickingModes();
        BindButtons();
        ApplySafeArea(force: true);
        uiReady = true;
        return true;
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

        if (defaultBottomPanel != null)
        {
            // Keep the strip itself non-blocking so only actual buttons consume input.
            defaultBottomPanel.pickingMode = PickingMode.Ignore;
        }

        if (menuButton != null)
        {
            menuButton.pickingMode = PickingMode.Position;
        }

        if (nextButton != null)
        {
            nextButton.pickingMode = PickingMode.Position;
        }
    }

    private void BindButtons()
    {
        if (callbacksBound)
        {
            return;
        }

        if (menuButton != null)
        {
            menuButton.clicked += HandleMenuClicked;
        }

        if (nextButton != null)
        {
            nextButton.clicked += HandleNextClicked;
        }

        callbacksBound = true;
    }

    private void UnbindButtons()
    {
        if (!callbacksBound)
        {
            return;
        }

        if (menuButton != null)
        {
            menuButton.clicked -= HandleMenuClicked;
        }

        if (nextButton != null)
        {
            nextButton.clicked -= HandleNextClicked;
        }

        callbacksBound = false;
    }

    private void HandleMenuClicked()
    {
        if (gameMenuActions != null)
        {
            gameMenuActions.QuitToMainMenu();
        }
    }

    private void HandleNextClicked()
    {
        if (turnManager != null)
        {
            turnManager.OnEndTurnButtonPressed();
        }
    }

    private void RefreshUiState()
    {
        if (!uiReady || defaultBottomPanel == null)
        {
            return;
        }

        bool showDefaultBottom = bottomStripController == null ||
                                 bottomStripController.CurrentMode == BottomStripController.BottomStripMode.DefaultHud;

        defaultBottomPanel.style.display = showDefaultBottom ? DisplayStyle.Flex : DisplayStyle.None;

        if (menuButton != null)
        {
            menuButton.SetEnabled(showDefaultBottom);
        }

        if (nextButton != null)
        {
            bool canAdvance = turnManager != null && turnManager.CanAdvanceTurn();
            nextButton.SetEnabled(showDefaultBottom && canAdvance);
        }
    }

    private void HideLegacyButtons()
    {
        if (legacyButtonsHidden)
        {
            return;
        }

        if (legacyDefaultHudRoot == null)
        {
            return;
        }

        hiddenLegacyButtons.Clear();

        UnityEngine.UI.Button[] buttons = legacyDefaultHudRoot.GetComponentsInChildren<UnityEngine.UI.Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            UnityEngine.UI.Button button = buttons[i];
            if (button == null)
            {
                continue;
            }

            string label = GetButtonLabel(button);
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            bool isMenu = string.Equals(label, "Menu", StringComparison.OrdinalIgnoreCase);
            bool isNext = string.Equals(label, "Next", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(label, "End Turn", StringComparison.OrdinalIgnoreCase);

            if (!isMenu && !isNext)
            {
                continue;
            }

            CanvasGroup group = button.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = button.gameObject.AddComponent<CanvasGroup>();
            }

            hiddenLegacyButtons.Add(new LegacyButtonState
            {
                button = button,
                canvasGroup = group,
                alpha = group.alpha,
                interactable = group.interactable,
                blocksRaycasts = group.blocksRaycasts,
                buttonInteractable = button.interactable
            });

            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            button.interactable = false;
        }

        legacyButtonsHidden = hiddenLegacyButtons.Count > 0;
    }

    private void RestoreLegacyButtons()
    {
        if (!legacyButtonsHidden)
        {
            hiddenLegacyButtons.Clear();
            return;
        }

        for (int i = 0; i < hiddenLegacyButtons.Count; i++)
        {
            LegacyButtonState state = hiddenLegacyButtons[i];
            if (state == null || state.button == null || state.canvasGroup == null)
            {
                continue;
            }

            state.canvasGroup.alpha = state.alpha;
            state.canvasGroup.interactable = state.interactable;
            state.canvasGroup.blocksRaycasts = state.blocksRaycasts;
            state.button.interactable = state.buttonInteractable;
        }

        hiddenLegacyButtons.Clear();
        legacyButtonsHidden = false;
    }

    private static string GetButtonLabel(UnityEngine.UI.Button button)
    {
        if (button == null)
        {
            return null;
        }

        TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text))
        {
            return tmp.text.Trim();
        }

        Text text = button.GetComponentInChildren<Text>(true);
        if (text != null && !string.IsNullOrWhiteSpace(text.text))
        {
            return text.text.Trim();
        }

        return null;
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
        RestoreLegacyButtons();

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
        defaultBottomPanel = null;
        menuButton = null;
        nextButton = null;
        uiReady = false;
        lastSafeArea = Rect.zero;
        lastScreenSize = Vector2Int.zero;
    }
}
