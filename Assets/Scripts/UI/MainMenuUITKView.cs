using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

public class MainMenuUITKView : MonoBehaviour
{
    private const string ThemeResourceName = "MainMenu_UITK_Theme";
    private const string SinglePlayerPrimarySaveFileName = "save_sp.json";
    private const string LegacySharedSaveFileName = "save.json";
    private const int InvalidPointerId = -1;
    private const float NonOverflowListDragLimit = 352f;
    private const float NonOverflowListDragDamping = 0.35f;
    private const float NonOverflowListDragThreshold = 10f;

    [Header("Trial Toggle")]
    [SerializeField] private bool enableUITK = true;

    [Header("References")]
    [SerializeField] private MainMenuController mainMenuController;
    [SerializeField] private Canvas legacyCanvas;

    private UIDocument uiDocument;
    private CanvasGroup legacyCanvasGroup;
    private ThemeStyleSheet themeAsset;

    private VisualElement root;
    private VisualElement mainPanel;
    private VisualElement multiplayerPanel;
    private VisualElement detailsPanel;
    private VisualElement joinPanel;
    private ScrollView activeGamesList;
    private Label detailsTitleLabel;
    private Label detailsSubtitleLabel;
    private Label statusLabel;
    private Label versionLabel;
    private Label multiplayerVersionLabel;

    private Button continueButton;
    private Button playVsAiButton;
    private Button playHotseatButton;
    private Button multiplayerButton;
    private Button quitButton;
    private Button createButton;
    private Button joinButton;
    private Button multiplayerBackButton;
    private Button detailsOpenButton;
    private Button detailsResignButton;
    private Button detailsCloseButton;
    private TextField joinGameIdInput;
    private Button joinConfirmButton;
    private Button joinCancelButton;

    private bool subscribedToMenuEvents;
    private bool uiReady;
    private bool hasSelectedGame;
    private bool suppressNextGameCardClick;
    private bool isDraggingNonOverflowGamesList;
    private int activeGamesPointerId = InvalidPointerId;
    private float activeGamesPointerStartY;
    private float activeGamesElasticOffset;
    private Rect lastSafeArea = Rect.zero;
    private Vector2Int lastScreenSize = Vector2Int.zero;
    private IVisualElementScheduledItem activeGamesElasticResetItem;
    private IVisualElementScheduledItem viewInitializationItem;

    private void Awake()
    {
        ResolveReferences();
        themeAsset = Resources.Load<ThemeStyleSheet>(ThemeResourceName);
        EnsurePanelSettingsWhenEnabled();
        ApplyUiMode();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsurePanelSettingsWhenEnabled();
        ApplyUiMode();

        if (!enableUITK)
        {
            return;
        }

        BeginViewInitialization();
    }

    private void OnDisable()
    {
        StopViewInitialization();
        UnsubscribeMainMenuEvents();
        UnbindButtons();
        UnregisterActiveGamesListCallbacks();
        ResetActiveGamesInteractionState();
        ClearCachedElements();
    }

    private void Update()
    {
        if (!enableUITK || !uiReady)
        {
            return;
        }

        ApplySafeArea(force: false);
    }

    private void ResolveReferences()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        if (mainMenuController == null)
        {
            mainMenuController = Object.FindFirstObjectByType<MainMenuController>();
        }

        if (legacyCanvas == null)
        {
            legacyCanvas = Object.FindFirstObjectByType<Canvas>();
        }

        if (legacyCanvas != null)
        {
            legacyCanvasGroup = legacyCanvas.GetComponent<CanvasGroup>();
            if (legacyCanvasGroup == null)
            {
                legacyCanvasGroup = legacyCanvas.gameObject.AddComponent<CanvasGroup>();
            }
        }
    }

    private void EnsurePanelSettingsWhenEnabled()
    {
        if (!enableUITK || uiDocument == null)
        {
            return;
        }

        if (uiDocument.panelSettings != null)
        {
            if (uiDocument.panelSettings.themeStyleSheet == null && themeAsset != null)
            {
                uiDocument.panelSettings.themeStyleSheet = themeAsset;
            }
            return;
        }

        Debug.LogWarning("MainMenuUITKView: UIDocument requires a PanelSettings asset assigned in scene.");
    }

    private void ApplyUiMode()
    {
        if (uiDocument != null)
        {
            uiDocument.enabled = enableUITK;
        }

        if (legacyCanvasGroup != null)
        {
            legacyCanvasGroup.alpha = enableUITK ? 0f : 1f;
            legacyCanvasGroup.interactable = !enableUITK;
            legacyCanvasGroup.blocksRaycasts = !enableUITK;
        }
    }

    private void BeginViewInitialization()
    {
        StopViewInitialization();
        if (TryInitializeView())
        {
            FinalizeViewInitialization();
            return;
        }

        if (uiDocument == null || uiDocument.rootVisualElement == null)
        {
            return;
        }

        viewInitializationItem = uiDocument.rootVisualElement.schedule.Execute(() =>
        {
            if (!enableUITK)
            {
                StopViewInitialization();
                return;
            }

            if (!TryInitializeView())
            {
                return;
            }

            StopViewInitialization();
            FinalizeViewInitialization();
        }).Every(16);
    }

    private void StopViewInitialization()
    {
        if (viewInitializationItem == null)
        {
            return;
        }

        viewInitializationItem.Pause();
        viewInitializationItem = null;
    }

    private bool TryInitializeView()
    {
        uiReady = false;
        hasSelectedGame = false;

        if (uiDocument == null)
        {
            return false;
        }

        root = uiDocument.rootVisualElement;
        if (root == null)
        {
            return false;
        }

        CacheElements();
        if (mainPanel == null || multiplayerPanel == null || detailsPanel == null || activeGamesList == null)
        {
            return false;
        }

        ConfigureActiveGamesList();
        RefreshVersionLabel();
        BindButtons();
        RefreshContinueButtonVisibility();

        SetVisible(mainPanel, true);
        SetVisible(multiplayerPanel, false);
        SetVisible(detailsPanel, false);
        SetStatus(mainMenuController != null ? mainMenuController.CurrentImportStatus : string.Empty);

        ApplySafeArea(force: true);
        uiReady = true;
        return true;
    }

    private void FinalizeViewInitialization()
    {
        SubscribeMainMenuEvents();
        RefreshGamesList();
        ShowMainPanel();
    }

    private void CacheElements()
    {
        mainPanel = root.Q<VisualElement>("MainPanel");
        multiplayerPanel = root.Q<VisualElement>("MultiplayerPanel");
        detailsPanel = root.Q<VisualElement>("DetailsPanel");
        joinPanel = root.Q<VisualElement>("JoinPanel");
        activeGamesList = root.Q<ScrollView>("ActiveGamesList");
        detailsTitleLabel = root.Q<Label>("DetailsTitleLabel");
        detailsSubtitleLabel = root.Q<Label>("DetailsSubtitleLabel");
        statusLabel = root.Q<Label>("StatusLabel");
        versionLabel = root.Q<Label>("VersionLabel");
        multiplayerVersionLabel = root.Q<Label>("MultiplayerVersionLabel");

        continueButton = root.Q<Button>("ContinueButton");
        playVsAiButton = root.Q<Button>("PlayVsAIButton");
        playHotseatButton = root.Q<Button>("PlayHotseatButton");
        multiplayerButton = root.Q<Button>("MultiplayerButton");
        quitButton = root.Q<Button>("QuitButton");
        createButton = root.Q<Button>("CreateButton");
        joinButton = root.Q<Button>("JoinButton");
        multiplayerBackButton = root.Q<Button>("MultiplayerBackButton");
        detailsOpenButton = root.Q<Button>("DetailsOpenButton");
        detailsResignButton = root.Q<Button>("DetailsResignButton");
        detailsCloseButton = root.Q<Button>("DetailsCloseButton");
        joinGameIdInput = root.Q<TextField>("JoinGameIdInput");
        joinConfirmButton = root.Q<Button>("JoinConfirmButton");
        joinCancelButton = root.Q<Button>("JoinCancelButton");
    }

    private void ConfigureActiveGamesList()
    {
        if (activeGamesList == null)
        {
            return;
        }

        UnregisterActiveGamesListCallbacks();
        activeGamesList.mode = ScrollViewMode.Vertical;
        activeGamesList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        activeGamesList.verticalScrollerVisibility = ScrollerVisibility.Auto;
        activeGamesList.mouseWheelScrollSize = 120f;
        activeGamesList.scrollOffset = Vector2.zero;
        activeGamesList.RegisterCallback<PointerDownEvent>(HandleActiveGamesPointerDown);
        activeGamesList.RegisterCallback<PointerMoveEvent>(HandleActiveGamesPointerMove);
        activeGamesList.RegisterCallback<PointerUpEvent>(HandleActiveGamesPointerUp);
        activeGamesList.RegisterCallback<PointerCancelEvent>(HandleActiveGamesPointerCancel);
        ResetActiveGamesElasticOffset();
    }

    private void UnregisterActiveGamesListCallbacks()
    {
        if (activeGamesList == null)
        {
            return;
        }

        activeGamesList.UnregisterCallback<PointerDownEvent>(HandleActiveGamesPointerDown);
        activeGamesList.UnregisterCallback<PointerMoveEvent>(HandleActiveGamesPointerMove);
        activeGamesList.UnregisterCallback<PointerUpEvent>(HandleActiveGamesPointerUp);
        activeGamesList.UnregisterCallback<PointerCancelEvent>(HandleActiveGamesPointerCancel);
    }

    private void HandleActiveGamesPointerDown(PointerDownEvent evt)
    {
        if (activeGamesList == null || evt.button != 0 || HasActiveGamesScrollableOverflow())
        {
            return;
        }

        StopActiveGamesElasticReset();
        activeGamesPointerId = evt.pointerId;
        activeGamesPointerStartY = evt.position.y - (activeGamesElasticOffset / NonOverflowListDragDamping);
        isDraggingNonOverflowGamesList = false;
    }

    private void HandleActiveGamesPointerMove(PointerMoveEvent evt)
    {
        if (activeGamesList == null
            || evt.pointerId != activeGamesPointerId
            || HasActiveGamesScrollableOverflow())
        {
            return;
        }

        float dragDistance = evt.position.y - activeGamesPointerStartY;
        activeGamesElasticOffset = ComputeElasticListOffset(dragDistance);
        ApplyActiveGamesElasticOffset();

        if (Mathf.Abs(dragDistance) > NonOverflowListDragThreshold)
        {
            if (!isDraggingNonOverflowGamesList)
            {
                isDraggingNonOverflowGamesList = true;
                suppressNextGameCardClick = true;
                activeGamesList.CapturePointer(evt.pointerId);
            }

            evt.StopPropagation();
        }
    }

    private void HandleActiveGamesPointerUp(PointerUpEvent evt)
    {
        if (evt.pointerId != activeGamesPointerId)
        {
            return;
        }

        ReleaseActiveGamesPointer(evt.pointerId);
        StartActiveGamesElasticReset();
    }

    private void HandleActiveGamesPointerCancel(PointerCancelEvent evt)
    {
        if (evt.pointerId != activeGamesPointerId)
        {
            return;
        }

        ReleaseActiveGamesPointer(evt.pointerId);
        ResetActiveGamesElasticOffset();
    }

    private void ReleaseActiveGamesPointer(int pointerId)
    {
        if (activeGamesList != null && activeGamesList.HasPointerCapture(pointerId))
        {
            activeGamesList.ReleasePointer(pointerId);
        }

        activeGamesPointerId = InvalidPointerId;
        if (isDraggingNonOverflowGamesList && activeGamesList != null)
        {
            activeGamesList.schedule.Execute(() => suppressNextGameCardClick = false);
        }

        isDraggingNonOverflowGamesList = false;
    }

    private static float ComputeElasticListOffset(float dragDistance)
    {
        return Mathf.Clamp(dragDistance * NonOverflowListDragDamping, -NonOverflowListDragLimit, NonOverflowListDragLimit);
    }

    private void ApplyActiveGamesElasticOffset()
    {
        if (activeGamesList == null)
        {
            return;
        }

        activeGamesList.contentContainer.transform.position = new Vector3(0f, activeGamesElasticOffset, 0f);
    }

    private void StartActiveGamesElasticReset()
    {
        StopActiveGamesElasticReset();

        if (Mathf.Abs(activeGamesElasticOffset) <= 0.01f)
        {
            ResetActiveGamesElasticOffset();
            return;
        }

        if (activeGamesList == null)
        {
            activeGamesElasticOffset = 0f;
            return;
        }

        activeGamesElasticResetItem = activeGamesList.schedule.Execute(() =>
        {
            activeGamesElasticOffset = Mathf.Lerp(activeGamesElasticOffset, 0f, 0.28f);
            if (Mathf.Abs(activeGamesElasticOffset) <= 0.5f)
            {
                ResetActiveGamesElasticOffset();
                return;
            }

            ApplyActiveGamesElasticOffset();
        }).Every(16);
    }

    private void StopActiveGamesElasticReset()
    {
        if (activeGamesElasticResetItem == null)
        {
            return;
        }

        activeGamesElasticResetItem.Pause();
        activeGamesElasticResetItem = null;
    }

    private void ResetActiveGamesElasticOffset()
    {
        StopActiveGamesElasticReset();
        activeGamesElasticOffset = 0f;
        if (activeGamesList == null)
        {
            return;
        }

        activeGamesList.contentContainer.transform.position = Vector3.zero;
    }

    private bool HasActiveGamesScrollableOverflow()
    {
        if (activeGamesList == null)
        {
            return false;
        }

        VisualElement viewport = activeGamesList.Q(className: "unity-scroll-view__content-viewport")
            ?? activeGamesList.Q(className: "unity-scroll-view__viewport");
        if (viewport == null)
        {
            return false;
        }

        return activeGamesList.contentContainer.layout.height > viewport.layout.height + 1f;
    }

    private void BindButtons()
    {
        UnbindButtons();

        if (continueButton != null)
        {
            continueButton.clicked += HandleContinueClicked;
        }

        if (playVsAiButton != null)
        {
            playVsAiButton.clicked += HandlePlayVsAiClicked;
        }

        if (playHotseatButton != null)
        {
            playHotseatButton.clicked += HandlePlayHotseatClicked;
        }

        if (multiplayerButton != null)
        {
            multiplayerButton.clicked += HandleMultiplayerClicked;
        }

        if (quitButton != null)
        {
            quitButton.clicked += HandleQuitClicked;
        }

        if (createButton != null)
        {
            createButton.clicked += HandleCreateClicked;
        }

        if (joinButton != null)
        {
            joinButton.clicked += HandleJoinClicked;
        }

        if (multiplayerBackButton != null)
        {
            multiplayerBackButton.clicked += HandleMultiplayerBackClicked;
        }

        if (detailsOpenButton != null)
        {
            detailsOpenButton.clicked += HandleDetailsOpenClicked;
        }

        if (detailsResignButton != null)
        {
            detailsResignButton.clicked += HandleDetailsResignClicked;
        }

        if (detailsCloseButton != null)
        {
            detailsCloseButton.clicked += HandleDetailsCloseClicked;
        }

        if (joinConfirmButton != null)
        {
            joinConfirmButton.clicked += HandleJoinConfirmClicked;
        }

        if (joinCancelButton != null)
        {
            joinCancelButton.clicked += HandleJoinCancelClicked;
        }
    }

    private void UnbindButtons()
    {
        if (continueButton != null)
        {
            continueButton.clicked -= HandleContinueClicked;
        }

        if (playVsAiButton != null)
        {
            playVsAiButton.clicked -= HandlePlayVsAiClicked;
        }

        if (playHotseatButton != null)
        {
            playHotseatButton.clicked -= HandlePlayHotseatClicked;
        }

        if (multiplayerButton != null)
        {
            multiplayerButton.clicked -= HandleMultiplayerClicked;
        }

        if (quitButton != null)
        {
            quitButton.clicked -= HandleQuitClicked;
        }

        if (createButton != null)
        {
            createButton.clicked -= HandleCreateClicked;
        }

        if (joinButton != null)
        {
            joinButton.clicked -= HandleJoinClicked;
        }

        if (multiplayerBackButton != null)
        {
            multiplayerBackButton.clicked -= HandleMultiplayerBackClicked;
        }

        if (detailsOpenButton != null)
        {
            detailsOpenButton.clicked -= HandleDetailsOpenClicked;
        }

        if (detailsResignButton != null)
        {
            detailsResignButton.clicked -= HandleDetailsResignClicked;
        }

        if (detailsCloseButton != null)
        {
            detailsCloseButton.clicked -= HandleDetailsCloseClicked;
        }

        if (joinConfirmButton != null)
        {
            joinConfirmButton.clicked -= HandleJoinConfirmClicked;
        }

        if (joinCancelButton != null)
        {
            joinCancelButton.clicked -= HandleJoinCancelClicked;
        }
    }

    private void SubscribeMainMenuEvents()
    {
        if (subscribedToMenuEvents || mainMenuController == null)
        {
            return;
        }

        mainMenuController.ActivePbpGamesChanged += RefreshGamesList;
        mainMenuController.ImportStatusChanged += SetStatus;
        SetStatus(mainMenuController.CurrentImportStatus);
        subscribedToMenuEvents = true;
    }

    private void UnsubscribeMainMenuEvents()
    {
        if (!subscribedToMenuEvents || mainMenuController == null)
        {
            subscribedToMenuEvents = false;
            return;
        }

        mainMenuController.ActivePbpGamesChanged -= RefreshGamesList;
        mainMenuController.ImportStatusChanged -= SetStatus;
        subscribedToMenuEvents = false;
    }

    private void HandleContinueClicked()
    {
        if (mainMenuController != null)
        {
            mainMenuController.ContinueLastSave();
        }
    }

    private void HandlePlayVsAiClicked()
    {
        if (mainMenuController != null)
        {
            mainMenuController.PlayVsAI();
        }
    }

    private void HandlePlayHotseatClicked()
    {
        if (mainMenuController != null)
        {
            mainMenuController.PlayHotseat();
        }
    }

    private void HandleMultiplayerClicked()
    {
        if (mainMenuController != null)
        {
            mainMenuController.OpenMultiplayerScreen();
        }

        ShowMultiplayerPanel();
        RefreshGamesList();
        SetStatus(mainMenuController != null ? mainMenuController.CurrentImportStatus : string.Empty);
    }

    private void HandleQuitClicked()
    {
        if (mainMenuController != null)
        {
            mainMenuController.QuitGame();
        }
    }

    private void HandleCreateClicked()
    {
        if (mainMenuController != null)
        {
            mainMenuController.Multiplayer_CreateGame();
        }
    }

    private void HandleJoinClicked()
    {
        if (joinPanel == null)
        {
            SetStatus("Join UI unavailable.");
            return;
        }

        ShowJoinPanel();
    }

    private void HandleJoinConfirmClicked()
    {
        if (mainMenuController == null)
        {
            return;
        }

        string rawGameId = joinGameIdInput != null ? joinGameIdInput.value : null;
        bool joinStarted = mainMenuController.TryJoinPlayByPost(rawGameId);
        if (joinStarted)
        {
            HideJoinPanel();
        }
    }

    private void HandleJoinCancelClicked()
    {
        HideJoinPanel();
        if (mainMenuController != null)
        {
            mainMenuController.RefreshMultiplayerList();
        }
    }

    private void HandleMultiplayerBackClicked()
    {
        if (mainMenuController != null)
        {
            mainMenuController.CloseMultiplayerScreen();
        }

        HideJoinPanel();
        HideDetailsPanel();
        ShowMainPanel();
    }

    private void HandleDetailsOpenClicked()
    {
        if (mainMenuController != null && hasSelectedGame)
        {
            mainMenuController.GameDetails_Open();
        }
    }

    private void HandleDetailsResignClicked()
    {
        if (mainMenuController != null && hasSelectedGame)
        {
            mainMenuController.GameDetails_ResignLocal();
            HideDetailsPanel();
            RefreshGamesList();
        }
    }

    private void HandleDetailsCloseClicked()
    {
        if (mainMenuController != null)
        {
            mainMenuController.CloseGameDetailsPopup();
        }

        HideDetailsPanel();
    }

    private void RefreshGamesList()
    {
        if (!enableUITK || activeGamesList == null)
        {
            return;
        }

        ResetActiveGamesElasticOffset();
        activeGamesList.Clear();

        if (mainMenuController == null)
        {
            AddInfoRow("MainMenuController not found.");
            return;
        }

        IReadOnlyList<SaveManifestService.ManifestGameSummary> games = mainMenuController.ActivePbpGames;
        bool isSingleGame = games != null && games.Count == 1;
        bool hasMultipleGames = games != null && games.Count > 1;
        activeGamesList.EnableInClassList("multiplayer-games-list--single", isSingleGame);
        activeGamesList.EnableInClassList("multiplayer-games-list--multi", hasMultipleGames);

        if (games == null || games.Count == 0)
        {
            AddInfoRow("No active games");
            return;
        }

        for (int i = 0; i < games.Count; i++)
        {
            SaveManifestService.ManifestGameSummary summary = games[i];
            bool isLastGame = i == games.Count - 1;
            activeGamesList.Add(CreateGameCard(summary, isSingleGame, isLastGame));
        }
    }

    private void HandleGameRowClicked(SaveManifestService.ManifestGameSummary summary)
    {
        if (suppressNextGameCardClick)
        {
            suppressNextGameCardClick = false;
            return;
        }

        hasSelectedGame = true;

        if (mainMenuController != null)
        {
            mainMenuController.SelectPlayByPostGameForUITK(summary);
        }

        if (detailsTitleLabel != null)
        {
            detailsTitleLabel.text = BuildGameTitle(summary.gameId);
        }

        if (detailsSubtitleLabel != null)
        {
            detailsSubtitleLabel.text = MainMenuController.BuildPlayByPostTurnSubtitle(summary);
        }

        SetVisible(detailsPanel, true);
    }

    private static string BuildGameTitle(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return "Game Unknown";
        }

        string shortId = gameId.Length <= 8 ? gameId : gameId.Substring(0, 8);
        return "Game " + shortId;
    }

    private VisualElement CreateGameCard(SaveManifestService.ManifestGameSummary summary, bool isSingleGame, bool isLastGame)
    {
        VisualElement card = new VisualElement();
        card.AddToClassList("multiplayer-game-card");
        card.EnableInClassList("multiplayer-game-card--single", isSingleGame);
        card.EnableInClassList("multiplayer-game-card--last", isLastGame);
        card.RegisterCallback<ClickEvent>(_ => HandleGameRowClicked(summary));

        Label title = new Label(BuildGameTitle(summary.gameId));
        title.AddToClassList("multiplayer-game-card-title");
        card.Add(title);

        Label status = new Label(MainMenuController.BuildPlayByPostTurnSubtitle(summary));
        status.AddToClassList("multiplayer-game-card-status");
        card.Add(status);

        return card;
    }

    private void AddInfoRow(string text)
    {
        Label row = new Label(text);
        row.AddToClassList("multiplayer-empty-state");
        activeGamesList.Add(row);
    }

    private void ShowMainPanel()
    {
        ResetActiveGamesElasticOffset();
        HideJoinPanel();
        SetVisible(mainPanel, true);
        SetVisible(multiplayerPanel, false);

        if (versionLabel != null)
        {
            versionLabel.style.display = DisplayStyle.Flex;
        }

        if (multiplayerVersionLabel != null)
        {
            multiplayerVersionLabel.style.display = DisplayStyle.None;
        }
    }

    private void ShowMultiplayerPanel()
    {
        ResetActiveGamesElasticOffset();
        HideJoinPanel();
        SetVisible(mainPanel, false);
        SetVisible(multiplayerPanel, true);

        if (versionLabel != null)
        {
            versionLabel.style.display = DisplayStyle.None;
        }

        if (multiplayerVersionLabel != null)
        {
            multiplayerVersionLabel.style.display = DisplayStyle.None;
        }
    }

    private void HideDetailsPanel()
    {
        hasSelectedGame = false;
        SetVisible(detailsPanel, false);
    }

    private void ShowJoinPanel()
    {
        SetVisible(joinPanel, true);
        if (joinGameIdInput != null)
        {
            joinGameIdInput.Focus();
        }
    }

    private void HideJoinPanel()
    {
        SetVisible(joinPanel, false);
        if (joinGameIdInput != null)
        {
            joinGameIdInput.value = string.Empty;
        }
    }

    private static void SetVisible(VisualElement element, bool visible)
    {
        if (element == null)
        {
            return;
        }

        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void SetStatus(string message)
    {
        if (statusLabel != null)
        {
            string statusText = message ?? string.Empty;
            statusLabel.text = statusText;
            statusLabel.style.display = string.IsNullOrWhiteSpace(statusText) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }

    private void RefreshVersionLabel()
    {
        string versionText = MenuVersionLabel.BuildVersionText();

        if (versionLabel != null)
        {
            versionLabel.text = versionText;
        }

        if (multiplayerVersionLabel != null)
        {
            multiplayerVersionLabel.text = versionText;
        }
    }

    private void RefreshContinueButtonVisibility()
    {
        if (continueButton == null)
        {
            return;
        }

        continueButton.style.display = HasContinueSaveFile() ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static bool HasContinueSaveFile()
    {
        string persistentRoot = Application.persistentDataPath;
        if (string.IsNullOrWhiteSpace(persistentRoot))
        {
            return false;
        }

        string singlePlayerSavePath = Path.Combine(persistentRoot, SinglePlayerPrimarySaveFileName);
        if (File.Exists(singlePlayerSavePath))
        {
            return true;
        }

        string legacySavePath = Path.Combine(persistentRoot, LegacySharedSaveFileName);
        return File.Exists(legacySavePath);
    }

    private void ApplySafeArea(bool force)
    {
        if (root == null)
        {
            return;
        }

        Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
        Rect safeArea = Screen.safeArea;
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

        float left = safeArea.xMin;
        float right = Mathf.Max(0f, screenSize.x - safeArea.xMax);
        float bottom = safeArea.yMin;
        float top = Mathf.Max(0f, screenSize.y - safeArea.yMax);

        root.style.paddingLeft = left;
        root.style.paddingRight = right;
        root.style.paddingBottom = bottom;
        root.style.paddingTop = top;
    }

    private void ResetActiveGamesInteractionState()
    {
        StopActiveGamesElasticReset();

        if (activeGamesList != null && activeGamesPointerId != InvalidPointerId && activeGamesList.HasPointerCapture(activeGamesPointerId))
        {
            activeGamesList.ReleasePointer(activeGamesPointerId);
        }

        activeGamesPointerId = InvalidPointerId;
        activeGamesPointerStartY = 0f;
        activeGamesElasticOffset = 0f;
        isDraggingNonOverflowGamesList = false;
        suppressNextGameCardClick = false;
        hasSelectedGame = false;
        uiReady = false;
    }

    private void ClearCachedElements()
    {
        root = null;
        mainPanel = null;
        multiplayerPanel = null;
        detailsPanel = null;
        joinPanel = null;
        activeGamesList = null;
        detailsTitleLabel = null;
        detailsSubtitleLabel = null;
        statusLabel = null;
        versionLabel = null;
        multiplayerVersionLabel = null;
        continueButton = null;
        playVsAiButton = null;
        playHotseatButton = null;
        multiplayerButton = null;
        quitButton = null;
        createButton = null;
        joinButton = null;
        multiplayerBackButton = null;
        detailsOpenButton = null;
        detailsResignButton = null;
        detailsCloseButton = null;
        joinGameIdInput = null;
        joinConfirmButton = null;
        joinCancelButton = null;
    }
}
