using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class GameplayTopHudUITKView : MonoBehaviour
{
    private const string ThemeResourceName = "GameplayTopHud_UITK_Theme";

    [Header("Spike Toggle")]
    [SerializeField] private bool enableGameplayTopHudUITK = true;

    [Header("Optional Source Overrides")]
    [SerializeField] private TurnManager turnManager;

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
        ClearUiCache();
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
            return;
        }

        RefreshLabels();
        ApplySafeArea(force: false);
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

        return uiDocument != null && turnManager != null;
    }

    private static bool ShouldShowForMode(TurnManager.GameMode mode)
    {
        return mode == TurnManager.GameMode.None ||
               mode == TurnManager.GameMode.VsAI ||
               mode == TurnManager.GameMode.PlayByPost;
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

        turnLabel.text = BuildTurnLabel();
        goldLabel.text = BuildGoldLabel();

        if (statusLabel == null)
        {
            return;
        }

        PbpTopHudStatusProvider.StatusResult pbpStatus =
            PbpTopHudStatusProvider.Build(turnManager);

        statusLabel.text = pbpStatus.Visible ? pbpStatus.Message : string.Empty;
        statusLabel.style.display = pbpStatus.Visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private string BuildTurnLabel()
    {
        if (turnManager == null)
        {
            return string.Empty;
        }

        return $"Turn {turnManager.turnNumber} - {turnManager.GetCurrentSideName()}";
    }

    private string BuildGoldLabel()
    {
        if (turnManager == null)
        {
            return string.Empty;
        }

        int displayGold = turnManager.playerGold;
        if ((turnManager.currentMode == TurnManager.GameMode.Hotseat ||
             turnManager.currentMode == TurnManager.GameMode.PlayByPost) &&
            !turnManager.isPlayerTurn)
        {
            displayGold = turnManager.aiGold;
        }

        return $"Gold {displayGold}";
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
