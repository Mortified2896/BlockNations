using System;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class GameplayBottomHudUITKView : MonoBehaviour
{
    private const string LayoutResourceName = "GameplayBottomHud_UITK";
    private const string ThemeResourceName = "GameplayTopHud_UITK_Theme";
    private const string PlayByPostGameIdKey = "pbp_gameId";
    private const string PlayByPostShareShownKeyPrefix = "pbp_shareShown_";
    private const string ShareOverlayTitleText = "Share Game Code";
    private const string ShareOverlayInstructionText = "Send this code to your friend";
    private const string ShareOverlayCopyButtonText = "Copy Code";
    private const string ShareOverlayCloseButtonText = "Close";
    private const string PlayByPostFetchOkResult = "OK";
    private const string PlayByPostEndgameShareText = "Well played! Want to play again?";
    private const string GameOverOverlayDefaultTitleText = "Game Over";

    [Header("Spike Toggle")]
    [SerializeField] private bool enableGameplayBottomHudUITK = true;

    [Header("Optional Source Overrides")]
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private GameMenuActions gameMenuActions;
    [SerializeField] private UnitUIManager unitUIManager;
    [SerializeField] private CityUIManager cityUIManager;

    private UIDocument uiDocument;
    private VisualTreeAsset layoutAsset;
    private ThemeStyleSheet themeAsset;
    private readonly UITKResponsiveSizeTierController responsiveSizeTierController = new UITKResponsiveSizeTierController();

    private VisualElement root;
    private VisualElement topGutterMask;
    private VisualElement bottomGutterMask;
    private VisualElement leftGutterMask;
    private VisualElement rightGutterMask;
    private VisualElement hudRoot;
    private VisualElement defaultBottomPanel;
    private UnityEngine.UIElements.Button menuButton;
    private UnityEngine.UIElements.Button nextButton;
    private VisualElement pbpShareOverlay;
    private Label pbpShareCodeLabel;
    private UnityEngine.UIElements.Button pbpShareCopyButton;
    private UnityEngine.UIElements.Button pbpShareCloseButton;
    private VisualElement gameOverOverlay;
    private Label gameOverTitleLabel;
    private Label gameOverMessageLabel;
    private UnityEngine.UIElements.Button gameOverPrimaryButton;

    private bool uiReady;
    private bool callbacksBound;
    private bool warnedMissingPanelSettings;
    private bool warnedMissingLayout;
    private bool warnedMissingControls;
    private TurnManager subscribedTurnManager;
    private string visibleSharePromptGameId = string.Empty;

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
        RefreshTurnManagerSubscription();
    }

    private void OnDisable()
    {
        RefreshTurnManagerSubscription(forceClear: true);
        HidePlayByPostShareOverlay();
        ClearUiCache();
    }

    private void OnDestroy()
    {
        RefreshTurnManagerSubscription(forceClear: true);
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
            RefreshTurnManagerSubscription(forceClear: true);
            DisableOverlay();
            return;
        }

        RefreshTurnManagerSubscription();

        if (!EnsureUiReady())
        {
            return;
        }

        RefreshUiState();
        ApplySafeArea(force: false);
        responsiveSizeTierController.Apply(root);
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
                turnManager = UnityEngine.Object.FindAnyObjectByType<TurnManager>();
            }
        }

        if (gameMenuActions == null || force)
        {
            gameMenuActions = UnityEngine.Object.FindAnyObjectByType<GameMenuActions>();
        }

        if (unitUIManager == null || force)
        {
            unitUIManager = UnitUIManager.Instance;
            if (unitUIManager == null)
            {
                unitUIManager = UnityEngine.Object.FindAnyObjectByType<UnitUIManager>();
            }
        }

        if (cityUIManager == null || force)
        {
            cityUIManager = CityUIManager.Instance;
            if (cityUIManager == null)
            {
                cityUIManager = UnityEngine.Object.FindAnyObjectByType<CityUIManager>();
            }
        }

        return uiDocument != null && turnManager != null && gameMenuActions != null;
    }

    private void RefreshTurnManagerSubscription(bool forceClear = false)
    {
        TurnManager target = forceClear ? null : turnManager;
        if (subscribedTurnManager == target)
        {
            return;
        }

        if (subscribedTurnManager != null)
        {
            subscribedTurnManager.PlayByPostSubmitResult -= HandlePlayByPostSubmitResult;
            subscribedTurnManager.PlayByPostFetchResult -= HandlePlayByPostFetchResult;
        }

        subscribedTurnManager = target;
        if (subscribedTurnManager != null)
        {
            subscribedTurnManager.PlayByPostSubmitResult += HandlePlayByPostSubmitResult;
            subscribedTurnManager.PlayByPostFetchResult += HandlePlayByPostFetchResult;
        }
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
        EnsureSafeAreaGutterMask();
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
        EnsurePlayByPostShareOverlay();
        EnsureGameOverOverlay();
        ConfigurePickingModes();
        BindButtons();
        ApplySafeArea(force: true);
        responsiveSizeTierController.Apply(root);
        uiReady = true;
        return true;
    }

    private void EnsureSafeAreaGutterMask()
    {
        if (root == null)
        {
            return;
        }

        topGutterMask = EnsureGutterMaskBar("TopSafeAreaGutterMask");
        bottomGutterMask = EnsureGutterMaskBar("BottomSafeAreaGutterMask");
        leftGutterMask = EnsureGutterMaskBar("LeftSafeAreaGutterMask");
        rightGutterMask = EnsureGutterMaskBar("RightSafeAreaGutterMask");
    }

    private VisualElement EnsureGutterMaskBar(string elementName)
    {
        VisualElement gutterMask = root.Q<VisualElement>(elementName);
        if (gutterMask == null)
        {
            gutterMask = new VisualElement
            {
                name = elementName,
                pickingMode = PickingMode.Ignore
            };
            root.Insert(0, gutterMask);
        }

        gutterMask.style.position = Position.Absolute;
        gutterMask.style.backgroundColor = Color.black;
        gutterMask.style.display = DisplayStyle.None;
        return gutterMask;
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

        bool overlayVisible = pbpShareOverlay != null &&
                              pbpShareOverlay.resolvedStyle.display != DisplayStyle.None;
        SetShareOverlayInteractionEnabled(overlayVisible);
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

        if (pbpShareCopyButton != null)
        {
            pbpShareCopyButton.clicked += HandlePbpShareCopyClicked;
        }

        if (pbpShareCloseButton != null)
        {
            pbpShareCloseButton.clicked += HandlePbpShareCloseClicked;
        }

        if (gameOverPrimaryButton != null)
        {
            gameOverPrimaryButton.clicked += HandleGameOverPrimaryClicked;
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

        if (pbpShareCopyButton != null)
        {
            pbpShareCopyButton.clicked -= HandlePbpShareCopyClicked;
        }

        if (pbpShareCloseButton != null)
        {
            pbpShareCloseButton.clicked -= HandlePbpShareCloseClicked;
        }

        if (gameOverPrimaryButton != null)
        {
            gameOverPrimaryButton.clicked -= HandleGameOverPrimaryClicked;
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

    private void HandlePbpShareCopyClicked()
    {
        string gameId = visibleSharePromptGameId;
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return;
        }

        if (!ClipboardUtility.TryCopy(gameId))
        {
            GUIUtility.systemCopyBuffer = gameId;
        }
    }

    private void HandlePbpShareCloseClicked()
    {
        HidePlayByPostShareOverlay();
    }

    private void HandleGameOverPrimaryClicked()
    {
        if (turnManager != null)
        {
            turnManager.OnPlayAgainButtonPressed();
        }
    }

    private void HandlePlayByPostSubmitResult(bool ok, string err)
    {
        if (!ok || turnManager == null || turnManager.currentMode != TurnManager.GameMode.PlayByPost)
        {
            return;
        }

        if (turnManager.IsGameOverUiVisible)
        {
            PlayByPostReminderShareAdapter.TryPresentReminderShareSheet(PlayByPostEndgameShareText);
            return;
        }

        string gameId = GetCurrentPlayByPostGameId();
        bool isLocalHost = !string.IsNullOrWhiteSpace(gameId) && IsLocalHostForGame(gameId);
        bool shouldShowSharePrompt = isLocalHost &&
                                     PlayerPrefs.GetInt(GetShareShownKey(gameId), 0) == 0;
        if (shouldShowSharePrompt)
        {
            TryPresentPlayByPostSharePrompt(gameId);
            return;
        }

        int reminderTurnNumber = turnManager.turnNumber;
        if (!turnManager.isPlayerTurn)
        {
            reminderTurnNumber++;
        }

        PlayByPostReminderShareAdapter.TryPresentReminderShareSheetForTurn(reminderTurnNumber);
    }

    private void HandlePlayByPostFetchResult(bool reachable, string resultOrError)
    {
        if (!reachable || !string.Equals(resultOrError, PlayByPostFetchOkResult, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        HidePlayByPostShareOverlay();
    }

    private void RefreshUiState()
    {
        if (!uiReady || defaultBottomPanel == null)
        {
            return;
        }

        if (unitUIManager == null)
        {
            unitUIManager = UnitUIManager.Instance;
        }

        if (cityUIManager == null)
        {
            cityUIManager = CityUIManager.Instance;
        }

        bool unitPanelOpen = unitUIManager != null && unitUIManager.IsPanelOpen;
        bool cityPanelOpen = cityUIManager != null && cityUIManager.IsPanelOpen;
        bool showDefaultBottom = !unitPanelOpen && !cityPanelOpen;

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

        RefreshGameOverOverlayState();
    }

    private void EnsurePlayByPostShareOverlay()
    {
        if (hudRoot == null)
        {
            return;
        }

        pbpShareOverlay = root.Q<VisualElement>("PbpShareOverlay");
        pbpShareCodeLabel = root.Q<Label>("PbpShareCodeLabel");
        pbpShareCopyButton = root.Q<UnityEngine.UIElements.Button>("PbpShareCopyButton");
        pbpShareCloseButton = root.Q<UnityEngine.UIElements.Button>("PbpShareCloseButton");
        if (pbpShareOverlay != null && pbpShareCodeLabel != null && pbpShareCopyButton != null && pbpShareCloseButton != null)
        {
            return;
        }

        pbpShareOverlay = new VisualElement
        {
            name = "PbpShareOverlay",
            pickingMode = PickingMode.Position
        };
        pbpShareOverlay.style.position = Position.Absolute;
        pbpShareOverlay.style.left = 0f;
        pbpShareOverlay.style.right = 0f;
        pbpShareOverlay.style.top = 0f;
        pbpShareOverlay.style.bottom = 0f;
        pbpShareOverlay.style.alignItems = Align.Center;
        pbpShareOverlay.style.justifyContent = Justify.Center;
        pbpShareOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.62f);
        pbpShareOverlay.style.display = DisplayStyle.None;
        pbpShareOverlay.style.visibility = Visibility.Hidden;

        VisualElement card = new VisualElement
        {
            name = "PbpShareCard",
            pickingMode = PickingMode.Ignore
        };
        card.style.width = 700f;
        card.style.maxWidth = new Length(92f, LengthUnit.Percent);
        card.style.minHeight = 320f;
        card.style.paddingLeft = 28f;
        card.style.paddingRight = 28f;
        card.style.paddingTop = 28f;
        card.style.paddingBottom = 28f;
        card.style.backgroundColor = new Color(0.08f, 0.10f, 0.14f, 0.95f);
        card.style.borderTopLeftRadius = 16f;
        card.style.borderTopRightRadius = 16f;
        card.style.borderBottomLeftRadius = 16f;
        card.style.borderBottomRightRadius = 16f;

        Label title = new Label(ShareOverlayTitleText)
        {
            name = "PbpShareTitleLabel",
            pickingMode = PickingMode.Ignore
        };
        title.style.fontSize = 40f;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;
        title.style.unityTextAlign = TextAnchor.MiddleCenter;

        Label instruction = new Label(ShareOverlayInstructionText)
        {
            name = "PbpShareInstructionLabel",
            pickingMode = PickingMode.Ignore
        };
        instruction.style.marginTop = 10f;
        instruction.style.fontSize = 26f;
        instruction.style.color = new Color(0.90f, 0.93f, 0.98f, 1f);
        instruction.style.unityTextAlign = TextAnchor.MiddleCenter;

        pbpShareCodeLabel = new Label(string.Empty)
        {
            name = "PbpShareCodeLabel",
            pickingMode = PickingMode.Ignore
        };
        pbpShareCodeLabel.style.marginTop = 16f;
        pbpShareCodeLabel.style.marginBottom = 24f;
        pbpShareCodeLabel.style.paddingLeft = 16f;
        pbpShareCodeLabel.style.paddingRight = 16f;
        pbpShareCodeLabel.style.paddingTop = 14f;
        pbpShareCodeLabel.style.paddingBottom = 14f;
        pbpShareCodeLabel.style.alignSelf = Align.Stretch;
        pbpShareCodeLabel.style.width = new Length(100f, LengthUnit.Percent);
        pbpShareCodeLabel.style.maxWidth = new Length(100f, LengthUnit.Percent);
        pbpShareCodeLabel.style.minHeight = 96f;
        pbpShareCodeLabel.style.whiteSpace = WhiteSpace.Normal;
        pbpShareCodeLabel.style.fontSize = 28f;
        pbpShareCodeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        pbpShareCodeLabel.style.color = new Color(0.96f, 0.97f, 1f, 1f);
        pbpShareCodeLabel.style.backgroundColor = new Color(0.16f, 0.24f, 0.41f, 0.88f);
        pbpShareCodeLabel.style.borderTopLeftRadius = 10f;
        pbpShareCodeLabel.style.borderTopRightRadius = 10f;
        pbpShareCodeLabel.style.borderBottomLeftRadius = 10f;
        pbpShareCodeLabel.style.borderBottomRightRadius = 10f;
        pbpShareCodeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;

        VisualElement actions = new VisualElement
        {
            name = "PbpShareActions",
            pickingMode = PickingMode.Ignore
        };
        actions.style.flexDirection = FlexDirection.Row;
        actions.style.alignItems = Align.Center;

        pbpShareCopyButton = new UnityEngine.UIElements.Button
        {
            name = "PbpShareCopyButton",
            text = ShareOverlayCopyButtonText,
            pickingMode = PickingMode.Position
        };
        pbpShareCopyButton.style.flexGrow = 1f;
        pbpShareCopyButton.style.height = 84f;
        pbpShareCopyButton.style.fontSize = 32f;
        pbpShareCopyButton.style.marginRight = 8f;
        pbpShareCopyButton.style.color = Color.white;
        pbpShareCopyButton.style.backgroundColor = new Color(0.18f, 0.52f, 0.82f, 0.95f);

        pbpShareCloseButton = new UnityEngine.UIElements.Button
        {
            name = "PbpShareCloseButton",
            text = ShareOverlayCloseButtonText,
            pickingMode = PickingMode.Position
        };
        pbpShareCloseButton.style.flexGrow = 1f;
        pbpShareCloseButton.style.height = 84f;
        pbpShareCloseButton.style.fontSize = 32f;
        pbpShareCloseButton.style.marginLeft = 8f;
        pbpShareCloseButton.style.color = Color.white;
        pbpShareCloseButton.style.backgroundColor = new Color(0.28f, 0.32f, 0.40f, 0.95f);

        actions.Add(pbpShareCopyButton);
        actions.Add(pbpShareCloseButton);

        card.Add(title);
        card.Add(instruction);
        card.Add(pbpShareCodeLabel);
        card.Add(actions);
        pbpShareOverlay.Add(card);
        hudRoot.Add(pbpShareOverlay);
        SetShareOverlayInteractionEnabled(false);
    }

    private void EnsureGameOverOverlay()
    {
        if (hudRoot == null || root == null)
        {
            return;
        }

        gameOverOverlay = root.Q<VisualElement>("GameOverOverlay");
        gameOverTitleLabel = root.Q<Label>("GameOverTitleLabel");
        gameOverMessageLabel = root.Q<Label>("GameOverMessageLabel");
        gameOverPrimaryButton = root.Q<UnityEngine.UIElements.Button>("GameOverPrimaryButton");
        if (gameOverOverlay != null && gameOverTitleLabel != null && gameOverMessageLabel != null && gameOverPrimaryButton != null)
        {
            return;
        }

        if (gameOverOverlay == null)
        {
            gameOverOverlay = new VisualElement
            {
                name = "GameOverOverlay",
                pickingMode = PickingMode.Position
            };
            hudRoot.Add(gameOverOverlay);
        }
        else
        {
            gameOverOverlay.Clear();
        }

        gameOverOverlay.style.position = Position.Absolute;
        gameOverOverlay.style.left = 0f;
        gameOverOverlay.style.right = 0f;
        gameOverOverlay.style.top = 0f;
        gameOverOverlay.style.bottom = 0f;
        gameOverOverlay.style.alignItems = Align.Center;
        gameOverOverlay.style.justifyContent = Justify.Center;
        gameOverOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.72f);
        gameOverOverlay.style.display = DisplayStyle.None;
        gameOverOverlay.style.visibility = Visibility.Hidden;

        VisualElement card = new VisualElement
        {
            name = "GameOverCard",
            pickingMode = PickingMode.Ignore
        };
        card.style.width = 760f;
        card.style.maxWidth = new Length(92f, LengthUnit.Percent);
        card.style.minHeight = 360f;
        card.style.paddingLeft = 30f;
        card.style.paddingRight = 30f;
        card.style.paddingTop = 30f;
        card.style.paddingBottom = 30f;
        card.style.backgroundColor = new Color(0.08f, 0.10f, 0.14f, 0.98f);
        card.style.borderTopLeftRadius = 16f;
        card.style.borderTopRightRadius = 16f;
        card.style.borderBottomLeftRadius = 16f;
        card.style.borderBottomRightRadius = 16f;

        gameOverTitleLabel = new Label(GameOverOverlayDefaultTitleText)
        {
            name = "GameOverTitleLabel",
            pickingMode = PickingMode.Ignore
        };
        gameOverTitleLabel.style.fontSize = 44f;
        gameOverTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        gameOverTitleLabel.style.color = Color.white;
        gameOverTitleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;

        gameOverMessageLabel = new Label(string.Empty)
        {
            name = "GameOverMessageLabel",
            pickingMode = PickingMode.Ignore
        };
        gameOverMessageLabel.style.marginTop = 16f;
        gameOverMessageLabel.style.marginBottom = 28f;
        gameOverMessageLabel.style.fontSize = 30f;
        gameOverMessageLabel.style.color = new Color(0.94f, 0.95f, 0.98f, 1f);
        gameOverMessageLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        gameOverMessageLabel.style.whiteSpace = WhiteSpace.Normal;

        gameOverPrimaryButton = new UnityEngine.UIElements.Button
        {
            name = "GameOverPrimaryButton",
            text = "Play Again",
            pickingMode = PickingMode.Position
        };
        gameOverPrimaryButton.style.height = 84f;
        gameOverPrimaryButton.style.fontSize = 30f;
        gameOverPrimaryButton.style.color = Color.white;
        gameOverPrimaryButton.style.backgroundColor = new Color(0.18f, 0.52f, 0.82f, 0.95f);

        card.Add(gameOverTitleLabel);
        card.Add(gameOverMessageLabel);
        card.Add(gameOverPrimaryButton);
        gameOverOverlay.Add(card);
        SetGameOverOverlayInteractionEnabled(false);
    }

    private void TryPresentPlayByPostSharePrompt(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return;
        }

        if (!EnsureUiReady())
        {
            return;
        }

        EnsurePlayByPostShareOverlay();
        if (pbpShareOverlay == null || pbpShareCodeLabel == null)
        {
            return;
        }

        visibleSharePromptGameId = gameId;
        pbpShareCodeLabel.text = gameId;
        pbpShareOverlay.style.display = DisplayStyle.Flex;
        pbpShareOverlay.style.visibility = Visibility.Visible;
        SetShareOverlayInteractionEnabled(true);
        MarkSharePromptAsShown(gameId);
    }

    private void HidePlayByPostShareOverlay()
    {
        if (pbpShareOverlay != null)
        {
            pbpShareOverlay.style.display = DisplayStyle.None;
            pbpShareOverlay.style.visibility = Visibility.Hidden;
        }

        SetShareOverlayInteractionEnabled(false);

        visibleSharePromptGameId = string.Empty;
    }

    private void SetShareOverlayInteractionEnabled(bool enabled)
    {
        if (pbpShareOverlay != null)
        {
            pbpShareOverlay.pickingMode = enabled ? PickingMode.Position : PickingMode.Ignore;
            pbpShareOverlay.SetEnabled(enabled);
        }

        if (pbpShareCopyButton != null)
        {
            pbpShareCopyButton.pickingMode = enabled ? PickingMode.Position : PickingMode.Ignore;
            pbpShareCopyButton.SetEnabled(enabled);
        }

        if (pbpShareCloseButton != null)
        {
            pbpShareCloseButton.pickingMode = enabled ? PickingMode.Position : PickingMode.Ignore;
            pbpShareCloseButton.SetEnabled(enabled);
        }
    }

    private void RefreshGameOverOverlayState()
    {
        if (gameOverOverlay == null || gameOverMessageLabel == null || gameOverPrimaryButton == null)
        {
            return;
        }

        bool visible = turnManager != null && turnManager.IsGameOverUiVisible;
        gameOverOverlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        gameOverOverlay.style.visibility = visible ? Visibility.Visible : Visibility.Hidden;

        if (!visible)
        {
            SetGameOverOverlayInteractionEnabled(false);
            return;
        }

        string message = turnManager.GameOverUiMessage;
        gameOverMessageLabel.text = string.IsNullOrWhiteSpace(message) ? GameOverOverlayDefaultTitleText : message;

        string primaryLabel = turnManager.GameOverUiPrimaryButtonLabel;
        gameOverPrimaryButton.text = string.IsNullOrWhiteSpace(primaryLabel) ? "Play Again" : primaryLabel;
        gameOverPrimaryButton.SetEnabled(turnManager.GameOverUiPrimaryButtonInteractable);

        SetGameOverOverlayInteractionEnabled(true);
    }

    private void SetGameOverOverlayInteractionEnabled(bool enabled)
    {
        if (gameOverOverlay != null)
        {
            gameOverOverlay.pickingMode = enabled ? PickingMode.Position : PickingMode.Ignore;
            gameOverOverlay.SetEnabled(enabled);
        }

        if (gameOverPrimaryButton != null)
        {
            gameOverPrimaryButton.pickingMode = enabled ? PickingMode.Position : PickingMode.Ignore;
        }
    }

    private static string GetCurrentPlayByPostGameId()
    {
        string gameId = PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty);
        return string.IsNullOrWhiteSpace(gameId) ? string.Empty : gameId.Trim();
    }

    private static bool IsLocalHostForGame(string gameId)
    {
        return !string.IsNullOrWhiteSpace(gameId) &&
               LocalPlayerSeatStore.TryGetSeat(gameId, out int seat) &&
               seat == 0;
    }

    private static string GetShareShownKey(string gameId)
    {
        return PlayByPostShareShownKeyPrefix + gameId;
    }

    private static void MarkSharePromptAsShown(string gameId)
    {
        PlayerPrefs.SetInt(GetShareShownKey(gameId), 1);
        PlayerPrefs.Save();
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
        float rightInset = Mathf.Max(0f, screenSize.x - safeArea.xMax);
        float bottomInset = Mathf.Max(0f, safeArea.yMin);
        float topInset = Mathf.Max(0f, screenSize.y - safeArea.yMax);

        VisualElement safeAreaTarget = hudRoot ?? root;
        safeAreaTarget.style.paddingLeft = leftInset;
        safeAreaTarget.style.paddingRight = rightInset;
        safeAreaTarget.style.paddingTop = 0f;
        safeAreaTarget.style.paddingBottom = bottomInset;

        UpdateHorizontalGutterMask(topGutterMask, 0f, topInset);
        UpdateHorizontalGutterMask(bottomGutterMask, screenSize.y - bottomInset, bottomInset);
        UpdateLeftGutterMask(leftGutterMask, topInset, bottomInset, leftInset);
        UpdateRightGutterMask(rightGutterMask, topInset, bottomInset, rightInset);
    }

    private static void UpdateHorizontalGutterMask(
        VisualElement gutterMask,
        float top,
        float height)
    {
        if (gutterMask == null)
        {
            return;
        }

        gutterMask.style.left = 0f;
        gutterMask.style.top = top;
        gutterMask.style.right = 0f;
        gutterMask.style.bottom = StyleKeyword.Auto;
        gutterMask.style.width = StyleKeyword.Auto;
        gutterMask.style.height = StyleKeyword.Auto;

        if (Mathf.Approximately(height, 0f))
        {
            gutterMask.style.display = DisplayStyle.None;
            return;
        }

        gutterMask.style.height = height;
        gutterMask.style.display = DisplayStyle.Flex;
    }

    private static void UpdateLeftGutterMask(
        VisualElement gutterMask,
        float top,
        float bottom,
        float width)
    {
        if (gutterMask == null)
        {
            return;
        }

        gutterMask.style.left = 0f;
        gutterMask.style.top = top;
        gutterMask.style.right = StyleKeyword.Auto;
        gutterMask.style.bottom = bottom;
        gutterMask.style.width = StyleKeyword.Auto;
        gutterMask.style.height = StyleKeyword.Auto;

        if (Mathf.Approximately(width, 0f))
        {
            gutterMask.style.display = DisplayStyle.None;
            return;
        }

        gutterMask.style.width = width;
        gutterMask.style.display = DisplayStyle.Flex;
    }

    private static void UpdateRightGutterMask(
        VisualElement gutterMask,
        float top,
        float bottom,
        float width)
    {
        if (gutterMask == null)
        {
            return;
        }

        gutterMask.style.left = StyleKeyword.Auto;
        gutterMask.style.top = top;
        gutterMask.style.right = 0f;
        gutterMask.style.bottom = bottom;
        gutterMask.style.width = StyleKeyword.Auto;
        gutterMask.style.height = StyleKeyword.Auto;

        if (Mathf.Approximately(width, 0f))
        {
            gutterMask.style.display = DisplayStyle.None;
            return;
        }

        gutterMask.style.width = width;
        gutterMask.style.display = DisplayStyle.Flex;
    }

    private void DisableOverlay()
    {
        RefreshTurnManagerSubscription(forceClear: true);
        HidePlayByPostShareOverlay();

        if (uiDocument != null)
        {
            uiDocument.enabled = false;
        }

        ClearUiCache();
    }

    private void ClearUiCache()
    {
        UnbindButtons();
        responsiveSizeTierController.Reset(root);
        root = null;
        topGutterMask = null;
        bottomGutterMask = null;
        leftGutterMask = null;
        rightGutterMask = null;
        hudRoot = null;
        defaultBottomPanel = null;
        menuButton = null;
        nextButton = null;
        pbpShareOverlay = null;
        pbpShareCodeLabel = null;
        pbpShareCopyButton = null;
        pbpShareCloseButton = null;
        gameOverOverlay = null;
        gameOverTitleLabel = null;
        gameOverMessageLabel = null;
        gameOverPrimaryButton = null;
        visibleSharePromptGameId = string.Empty;
        uiReady = false;
        lastSafeArea = Rect.zero;
        lastScreenSize = Vector2Int.zero;
    }
}
