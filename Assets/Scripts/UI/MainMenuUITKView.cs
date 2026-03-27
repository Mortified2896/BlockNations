using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

public class MainMenuUITKView : MonoBehaviour
{
    private const string ThemeResourceName = "MainMenu_UITK_Theme";
    private const string SinglePlayerPrimarySaveFileName = "save_sp.json";
    private const string LegacySharedSaveFileName = "save.json";
    private const int VisiblePlayerIdPrefixLength = 8;
    private const int VisiblePlayerIdSuffixLength = 5;
    private const int ProfileStatusHideDelayMs = 1800;
    private const int InvalidPointerId = -1;
    private const float NonOverflowListDragLimit = 352f;
    private const float NonOverflowListDragDamping = 0.35f;
    private const float NonOverflowListDragThreshold = 10f;
    private const float DetailsGameIdFontSize = 30f;

    [Header("Trial Toggle")]
    [SerializeField] private bool enableUITK = true;

    [Header("References")]
    [SerializeField] private MainMenuController mainMenuController;

    private UIDocument uiDocument;
    private ThemeStyleSheet themeAsset;

    private VisualElement root;
    private VisualElement mainPanel;
    private VisualElement multiplayerPanel;
    private VisualElement profilePanel;
    private VisualElement detailsPanel;
    private VisualElement joinPanel;
    private VisualElement createSuccessPanel;
    private ScrollView activeGamesList;
    private Label detailsTitleLabel;
    private Label detailsSubtitleLabel;
    private Label detailsGameIdLabel;
    private Label statusLabel;
    private Label versionLabel;
    private Label multiplayerVersionLabel;
    private Label profileUsernameValueLabel;
    private Label profilePlayerIdValueLabel;
    private Label profileStatusLabel;
    private Label createSuccessGameCodeLabel;

    private Button continueButton;
    private Button playVsAiButton;
    private Button multiplayerButton;
    private Button profileButton;
    private Button quitButton;
    private Button createButton;
    private Button joinButton;
    private Button multiplayerBackButton;
    private Button createSuccessCopyButton;
    private Button createSuccessCloseButton;
    private Button detailsOpenButton;
    private Button detailsResignButton;
    private Button detailsCloseButton;
    private TextField joinGameIdInput;
    private Button joinConfirmButton;
    private Button joinCancelButton;
    private Button profileRegenerateButton;
    private Button profileCopyPlayerIdButton;
    private Button profileBackButton;

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
    private IVisualElementScheduledItem profileStatusClearItem;
    private IVisualElementScheduledItem viewInitializationItem;
    private LocalPlayerProfileStore.ProfileData profileData;
    private string pendingCreateSuccessGameId;
    private string selectedDetailsGameId = string.Empty;

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
        StopProfileStatusClearTimer();
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
        if (mainPanel == null
            || multiplayerPanel == null
            || profilePanel == null
            || detailsPanel == null
            || activeGamesList == null)
        {
            return false;
        }

        profileData = LocalPlayerProfileStore.GetOrCreateProfile();
        ConfigureActiveGamesList();
        RefreshVersionLabel();
        BindButtons();
        RefreshContinueButtonVisibility();
        RefreshProfileLabels();
        ClearProfileStatus();

        SetVisible(mainPanel, true);
        SetVisible(multiplayerPanel, false);
        SetVisible(profilePanel, false);
        SetVisible(detailsPanel, false);
        SetVisible(createSuccessPanel, false);
        SetStatus(mainMenuController != null ? mainMenuController.CurrentImportStatus : string.Empty);

        ApplySafeArea(force: true);
        uiReady = true;
        return true;
    }

    private void FinalizeViewInitialization()
    {
        SubscribeMainMenuEvents();
        RefreshGamesList();

        if (mainMenuController != null && mainMenuController.IsMultiplayerScreenRequested)
        {
            ShowMultiplayerPanel();
            SetStatus(mainMenuController.CurrentImportStatus);
        }
        else
        {
            ShowMainPanel();
        }
    }

    private void CacheElements()
    {
        mainPanel = root.Q<VisualElement>("MainPanel");
        multiplayerPanel = root.Q<VisualElement>("MultiplayerPanel");
        profilePanel = root.Q<VisualElement>("ProfilePanel");
        detailsPanel = root.Q<VisualElement>("DetailsPanel");
        joinPanel = root.Q<VisualElement>("JoinPanel");
        createSuccessPanel = root.Q<VisualElement>("CreateSuccessPanel");
        activeGamesList = root.Q<ScrollView>("ActiveGamesList");
        detailsTitleLabel = root.Q<Label>("DetailsTitleLabel");
        detailsSubtitleLabel = root.Q<Label>("DetailsSubtitleLabel");
        detailsGameIdLabel = root.Q<Label>("DetailsGameIdLabel");
        if (detailsGameIdLabel != null)
        {
            detailsGameIdLabel.style.fontSize = DetailsGameIdFontSize;
            detailsGameIdLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            detailsGameIdLabel.pickingMode = PickingMode.Position;
        }
        statusLabel = root.Q<Label>("StatusLabel");
        versionLabel = root.Q<Label>("VersionLabel");
        multiplayerVersionLabel = root.Q<Label>("MultiplayerVersionLabel");
        profileUsernameValueLabel = root.Q<Label>("ProfileUsernameValueLabel");
        profilePlayerIdValueLabel = root.Q<Label>("ProfilePlayerIdValueLabel");
        profileStatusLabel = root.Q<Label>("ProfileStatusLabel");
        createSuccessGameCodeLabel = root.Q<Label>("CreateSuccessGameCodeLabel");

        continueButton = root.Q<Button>("ContinueButton");
        playVsAiButton = root.Q<Button>("PlayVsAIButton");
        multiplayerButton = root.Q<Button>("MultiplayerButton");
        profileButton = root.Q<Button>("ProfileButton");
        quitButton = root.Q<Button>("QuitButton");
        createButton = root.Q<Button>("CreateButton");
        joinButton = root.Q<Button>("JoinButton");
        multiplayerBackButton = root.Q<Button>("MultiplayerBackButton");
        createSuccessCopyButton = root.Q<Button>("CreateSuccessCopyButton");
        createSuccessCloseButton = root.Q<Button>("CreateSuccessCloseButton");
        detailsOpenButton = root.Q<Button>("DetailsOpenButton");
        detailsResignButton = root.Q<Button>("DetailsResignButton");
        detailsCloseButton = root.Q<Button>("DetailsCloseButton");
        joinGameIdInput = root.Q<TextField>("JoinGameIdInput");
        joinConfirmButton = root.Q<Button>("JoinConfirmButton");
        joinCancelButton = root.Q<Button>("JoinCancelButton");
        profileRegenerateButton = root.Q<Button>("ProfileRegenerateButton");
        profileCopyPlayerIdButton = root.Q<Button>("ProfileCopyPlayerIdButton");
        profileBackButton = root.Q<Button>("ProfileBackButton");
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

        if (multiplayerButton != null)
        {
            multiplayerButton.clicked += HandleMultiplayerClicked;
        }

        if (profileButton != null)
        {
            profileButton.clicked += HandleProfileClicked;
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

        if (createSuccessCopyButton != null)
        {
            createSuccessCopyButton.clicked += HandleCreateSuccessCopyClicked;
        }

        if (createSuccessCloseButton != null)
        {
            createSuccessCloseButton.clicked += HandleCreateSuccessCloseClicked;
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

        if (detailsGameIdLabel != null)
        {
            detailsGameIdLabel.RegisterCallback<ClickEvent>(HandleDetailsGameIdClicked);
        }

        if (joinConfirmButton != null)
        {
            joinConfirmButton.clicked += HandleJoinConfirmClicked;
        }

        if (joinCancelButton != null)
        {
            joinCancelButton.clicked += HandleJoinCancelClicked;
        }

        if (profileRegenerateButton != null)
        {
            profileRegenerateButton.clicked += HandleProfileRegenerateClicked;
        }

        if (profileCopyPlayerIdButton != null)
        {
            profileCopyPlayerIdButton.clicked += HandleProfileCopyPlayerIdClicked;
        }

        if (profileBackButton != null)
        {
            profileBackButton.clicked += HandleProfileBackClicked;
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

        if (multiplayerButton != null)
        {
            multiplayerButton.clicked -= HandleMultiplayerClicked;
        }

        if (profileButton != null)
        {
            profileButton.clicked -= HandleProfileClicked;
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

        if (createSuccessCopyButton != null)
        {
            createSuccessCopyButton.clicked -= HandleCreateSuccessCopyClicked;
        }

        if (createSuccessCloseButton != null)
        {
            createSuccessCloseButton.clicked -= HandleCreateSuccessCloseClicked;
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

        if (detailsGameIdLabel != null)
        {
            detailsGameIdLabel.UnregisterCallback<ClickEvent>(HandleDetailsGameIdClicked);
        }

        if (joinConfirmButton != null)
        {
            joinConfirmButton.clicked -= HandleJoinConfirmClicked;
        }

        if (joinCancelButton != null)
        {
            joinCancelButton.clicked -= HandleJoinCancelClicked;
        }

        if (profileRegenerateButton != null)
        {
            profileRegenerateButton.clicked -= HandleProfileRegenerateClicked;
        }

        if (profileCopyPlayerIdButton != null)
        {
            profileCopyPlayerIdButton.clicked -= HandleProfileCopyPlayerIdClicked;
        }

        if (profileBackButton != null)
        {
            profileBackButton.clicked -= HandleProfileBackClicked;
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
        mainMenuController.MultiplayerScreenRequested += HandleMultiplayerScreenRequested;
        mainMenuController.MultiplayerCreateSucceeded += HandleMultiplayerCreateSucceeded;
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
        mainMenuController.MultiplayerScreenRequested -= HandleMultiplayerScreenRequested;
        mainMenuController.MultiplayerCreateSucceeded -= HandleMultiplayerCreateSucceeded;
        subscribedToMenuEvents = false;
    }

    private void HandleMultiplayerScreenRequested()
    {
        if (!enableUITK || !uiReady)
        {
            return;
        }

        ShowMultiplayerPanel();
        RefreshGamesList();
        SetStatus(mainMenuController != null ? mainMenuController.CurrentImportStatus : string.Empty);
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

    private void HandleProfileClicked()
    {
        ShowProfilePanel();
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

    private void HandleProfileRegenerateClicked()
    {
        profileData = LocalPlayerProfileStore.RegenerateUsername();
        RefreshProfileLabels();
        ClearProfileStatus();
    }

    private void HandleProfileCopyPlayerIdClicked()
    {
        if (ClipboardUtility.TryCopy(profileData.PlayerId))
        {
            ShowTransientProfileStatus("Copied!");
            return;
        }

        ShowTransientProfileStatus("Copy failed.");
    }

    private void HandleProfileBackClicked()
    {
        ShowMainPanel();
    }

    private void HandleMultiplayerBackClicked()
    {
        if (mainMenuController != null)
        {
            mainMenuController.CloseMultiplayerScreen();
        }

        HideCreateSuccessPanel();
        HideJoinPanel();
        HideDetailsPanel();
        ShowMainPanel();
    }

    private void HandleMultiplayerCreateSucceeded(string gameId)
    {
        pendingCreateSuccessGameId = gameId;
        TryShowPendingCreateSuccessPanel();
    }

    private void HandleCreateSuccessCopyClicked()
    {
        if (mainMenuController == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(pendingCreateSuccessGameId))
        {
            SetStatus("No game code selected.");
            return;
        }

        mainMenuController.CopyPbpGameIdToClipboard(pendingCreateSuccessGameId);
    }

    private void HandleCreateSuccessCloseClicked()
    {
        HideCreateSuccessPanel();
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

    private void HandleDetailsGameIdClicked(ClickEvent evt)
    {
        if (mainMenuController == null || string.IsNullOrWhiteSpace(selectedDetailsGameId))
        {
            return;
        }

        mainMenuController.CopyPbpGameIdToClipboard(selectedDetailsGameId);
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
        selectedDetailsGameId = summary.gameId;

        if (mainMenuController != null)
        {
            mainMenuController.SelectPlayByPostGameForUITK(summary);
        }

        if (detailsTitleLabel != null)
        {
            detailsTitleLabel.text = BuildGameTitle(summary);
        }

        if (detailsSubtitleLabel != null)
        {
            detailsSubtitleLabel.text = MainMenuController.BuildPlayByPostTurnSubtitle(summary);
        }

        if (detailsGameIdLabel != null)
        {
            string gameIdText = BuildGameIdLine(summary.gameId);
            detailsGameIdLabel.text = gameIdText;
            detailsGameIdLabel.style.display = string.IsNullOrWhiteSpace(gameIdText) ? DisplayStyle.None : DisplayStyle.Flex;
        }
        else if (detailsSubtitleLabel != null)
        {
            string gameIdText = BuildGameIdLine(summary.gameId);
            if (!string.IsNullOrWhiteSpace(gameIdText))
            {
                detailsSubtitleLabel.text = $"{detailsSubtitleLabel.text}\n{gameIdText}";
            }
        }

        SetVisible(detailsPanel, true);
    }

    private static string BuildGameTitle(SaveManifestService.ManifestGameSummary summary)
    {
        if (!string.IsNullOrWhiteSpace(summary.displayName))
        {
            return summary.displayName;
        }

        string generatedName = PbpGameDisplayNameGenerator.BuildForGameId(summary.gameId);
        if (!string.IsNullOrWhiteSpace(generatedName))
        {
            return generatedName;
        }

        return BuildLegacyGameTitle(summary.gameId);
    }

    private static string BuildLegacyGameTitle(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return "Game Unknown";
        }

        string shortId = gameId.Length <= 8 ? gameId : gameId.Substring(0, 8);
        return "Game " + shortId;
    }

    private static string BuildGameIdLine(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return string.Empty;
        }

        return $"Game ID: {gameId}";
    }

    private VisualElement CreateGameCard(SaveManifestService.ManifestGameSummary summary, bool isSingleGame, bool isLastGame)
    {
        VisualElement card = new VisualElement();
        card.AddToClassList("multiplayer-game-card");
        card.EnableInClassList("multiplayer-game-card--single", isSingleGame);
        card.EnableInClassList("multiplayer-game-card--last", isLastGame);
        card.RegisterCallback<ClickEvent>(_ => HandleGameRowClicked(summary));

        Label title = new Label(BuildGameTitle(summary));
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
        HideCreateSuccessPanel();
        HideJoinPanel();
        HideDetailsPanel();
        ClearProfileStatus();
        SetVisible(mainPanel, true);
        SetVisible(multiplayerPanel, false);
        SetVisible(profilePanel, false);

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
        ClearProfileStatus();
        SetVisible(mainPanel, false);
        SetVisible(multiplayerPanel, true);
        SetVisible(profilePanel, false);

        if (versionLabel != null)
        {
            versionLabel.style.display = DisplayStyle.None;
        }

        if (multiplayerVersionLabel != null)
        {
            multiplayerVersionLabel.style.display = DisplayStyle.None;
        }

        TryShowPendingCreateSuccessPanel();
    }

    private void HideDetailsPanel()
    {
        hasSelectedGame = false;
        selectedDetailsGameId = string.Empty;
        SetVisible(detailsPanel, false);
    }

    private void ShowProfilePanel()
    {
        profileData = LocalPlayerProfileStore.GetOrCreateProfile();
        RefreshProfileLabels();
        ClearProfileStatus();
        HideJoinPanel();
        HideDetailsPanel();
        SetVisible(mainPanel, false);
        SetVisible(multiplayerPanel, false);
        SetVisible(profilePanel, true);

        if (versionLabel != null)
        {
            versionLabel.style.display = DisplayStyle.None;
        }

        if (multiplayerVersionLabel != null)
        {
            multiplayerVersionLabel.style.display = DisplayStyle.None;
        }
    }

    private void ShowJoinPanel()
    {
        HideCreateSuccessPanel();
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

    private void RefreshProfileLabels()
    {
        if (profileUsernameValueLabel != null)
        {
            profileUsernameValueLabel.text = profileData.Username ?? string.Empty;
        }

        if (profilePlayerIdValueLabel != null)
        {
            profilePlayerIdValueLabel.text = BuildVisiblePlayerId(profileData.PlayerId);
        }
    }

    private void SetProfileStatus(string message)
    {
        if (profileStatusLabel == null)
        {
            return;
        }

        string statusText = message ?? string.Empty;
        profileStatusLabel.text = statusText;
        profileStatusLabel.style.display = string.IsNullOrWhiteSpace(statusText) ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private void ShowTransientProfileStatus(string message)
    {
        SetProfileStatus(message);
        StopProfileStatusClearTimer();

        if (profilePanel == null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        profileStatusClearItem = profilePanel.schedule.Execute(ClearProfileStatus).StartingIn(ProfileStatusHideDelayMs);
    }

    private void ClearProfileStatus()
    {
        StopProfileStatusClearTimer();
        SetProfileStatus(string.Empty);
    }

    private void StopProfileStatusClearTimer()
    {
        if (profileStatusClearItem == null)
        {
            return;
        }

        profileStatusClearItem.Pause();
        profileStatusClearItem = null;
    }

    private static string BuildVisiblePlayerId(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return string.Empty;
        }

        int minimumLengthForTruncation = VisiblePlayerIdPrefixLength + VisiblePlayerIdSuffixLength + 3;
        if (playerId.Length <= minimumLengthForTruncation)
        {
            return playerId;
        }

        string prefix = playerId.Substring(0, VisiblePlayerIdPrefixLength);
        string suffix = playerId.Substring(playerId.Length - VisiblePlayerIdSuffixLength, VisiblePlayerIdSuffixLength);
        return $"{prefix}...{suffix}";
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
        selectedDetailsGameId = string.Empty;
        uiReady = false;
    }

    private void ClearCachedElements()
    {
        root = null;
        mainPanel = null;
        multiplayerPanel = null;
        profilePanel = null;
        detailsPanel = null;
        joinPanel = null;
        createSuccessPanel = null;
        activeGamesList = null;
        detailsTitleLabel = null;
        detailsSubtitleLabel = null;
        detailsGameIdLabel = null;
        statusLabel = null;
        versionLabel = null;
        multiplayerVersionLabel = null;
        profileUsernameValueLabel = null;
        profilePlayerIdValueLabel = null;
        profileStatusLabel = null;
        createSuccessGameCodeLabel = null;
        continueButton = null;
        playVsAiButton = null;
        multiplayerButton = null;
        profileButton = null;
        quitButton = null;
        createButton = null;
        joinButton = null;
        multiplayerBackButton = null;
        createSuccessCopyButton = null;
        createSuccessCloseButton = null;
        detailsOpenButton = null;
        detailsResignButton = null;
        detailsCloseButton = null;
        joinGameIdInput = null;
        joinConfirmButton = null;
        joinCancelButton = null;
        profileRegenerateButton = null;
        profileCopyPlayerIdButton = null;
        profileBackButton = null;
    }

    private void TryShowPendingCreateSuccessPanel()
    {
        if (string.IsNullOrWhiteSpace(pendingCreateSuccessGameId)
            || multiplayerPanel == null
            || multiplayerPanel.resolvedStyle.display == DisplayStyle.None)
        {
            return;
        }

        if (createSuccessGameCodeLabel != null)
        {
            createSuccessGameCodeLabel.text = pendingCreateSuccessGameId;
        }

        SetVisible(createSuccessPanel, true);
    }

    private void HideCreateSuccessPanel()
    {
        SetVisible(createSuccessPanel, false);
    }
}
