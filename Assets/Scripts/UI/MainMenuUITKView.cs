using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

public class MainMenuUITKView : MonoBehaviour
{
    private const bool EnableProfileResponsiveDebugLogs = true;
    private const string ThemeResourceName = "MainMenu_UITK_Theme";
    private const string SinglePlayerPrimarySaveFileName = "save_sp.json";
    private const string LegacySharedSaveFileName = "save.json";
    private const int VisiblePlayerIdPrefixLength = 8;
    private const int VisiblePlayerIdSuffixLength = 5;
    private const int ProfileStatusHideDelayMs = 1800;
    private const int InvalidPointerId = -1;
    private const string WidePhoneMenuClass = "menu-phone-wide";
    private const float WidePhoneShortestSideMin = 428f;
    private const float WidePhoneHeightMin = 900f;
    private const float MainMenuTitleCompactBaseFontSize = 68f;
    private const float MainMenuTitleWidePhoneBaseFontSize = 104f;
    private const float MainMenuTitleRegularBaseFontSize = 148f;
    private const float MainMenuTitleLargeBaseFontSize = 172f;
    private const float MainMenuTitleMinimumFontSize = 48f;
    private const float MainMenuTitleFitPadding = 8f;
    private const float NonOverflowListDragLimit = 352f;
    private const float NonOverflowListDragDamping = 0.35f;
    private const float NonOverflowListDragThreshold = 10f;
    private const float PullToRefreshTriggerOffset = 56f;
    private const float PullToRefreshTopScrollTolerance = 1f;
    private const float DetailsGameIdFontSize = 30f;
    private const int RefreshCountdownTickMs = 1000;

    [Header("Trial Toggle")]
    [SerializeField] private bool enableUITK = true;

    [Header("References")]
    [SerializeField] private MainMenuController mainMenuController;

    private UIDocument uiDocument;
    private ThemeStyleSheet themeAsset;
    private readonly UITKResponsiveSizeTierController responsiveSizeTierController = new UITKResponsiveSizeTierController();

    private VisualElement root;
    private VisualElement mainPanel;
    private VisualElement multiplayerPanel;
    private VisualElement profilePanel;
    private VisualElement detailsPanel;
    private VisualElement detailsContent;
    private VisualElement detailsResignConfirmContent;
    private VisualElement joinPanel;
    private VisualElement createSuccessPanel;
    private VisualElement generalSettingsPanel;
    private VisualElement generalSettingsCard;
    private VisualElement generalSettingsAiSection;
    private VisualElement generalSettingsDevSection;
    private ScrollView activeGamesList;
    private Label detailsTitleLabel;
    private Label detailsSubtitleLabel;
    private Label detailsGameIdLabel;
    private Label statusLabel;
    private Label multiplayerRefreshCountdownLabel;
    private Label versionLabel;
    private Label multiplayerVersionLabel;
    private Label titleLabel;
    private Label profileUsernameValueLabel;
    private Label profilePlayerIdValueLabel;
    private Label profileStatusLabel;
    private Label createSuccessGameCodeLabel;
    private Label generalSettingsTitleLabel;
    private Label generalSettingsSubtitleLabel;
    private Toggle generalSettingsStoreSnapshotHistoryToggle;

    private Button continueButton;
    private Button playVsAiButton;
    private Button multiplayerButton;
    private Button profileButton;
    private Button quitButton;
    private Button createButton;
    private Button joinButton;
    private Button multiplayerBackButton;
    private Button debugNotificationButton;
    private Button createSuccessCopyButton;
    private Button createSuccessCloseButton;
    private Button generalSettingsMapSmallButton;
    private Button generalSettingsMapLargeButton;
    private Button generalSettingsAiLevel1Button;
    private Button generalSettingsAiLevel2Button;
    private Button generalSettingsAiLevel3Button;
    private Button generalSettingsConfirmButton;
    private Button generalSettingsBackButton;
    private Button detailsOpenButton;
    private Button detailsSendReminderButton;
    private Button detailsResignButton;
    private Button detailsCloseButton;
    private Button resignConfirmCancelButton;
    private Button resignConfirmAcceptButton;
    private TextField joinGameIdInput;
    private Button joinConfirmButton;
    private Button joinCancelButton;
    private Button profileRegenerateButton;
    private Button profileCopyPlayerIdButton;
    private Button profileBackButton;
    private TextField profileTypedDisplayNameInput;
    private VisualElement multiplayerBadge;

    private bool subscribedToMenuEvents;
    private bool uiReady;
    private bool hasSelectedGame;
    private bool activeGamesPullStartedFromRest;
    private bool activeGamesPullRefreshArmed;
    private bool suppressNextGameCardClick;
    private bool isDraggingNonOverflowGamesList;
    private int activeGamesPointerId = InvalidPointerId;
    private float activeGamesPointerStartY;
    private float activeGamesElasticOffset;
    private Rect lastSafeArea = Rect.zero;
    private Vector2Int lastScreenSize = Vector2Int.zero;
    private IVisualElementScheduledItem activeGamesElasticResetItem;
    private IVisualElementScheduledItem refreshCountdownItem;
    private IVisualElementScheduledItem profileStatusClearItem;
    private IVisualElementScheduledItem viewInitializationItem;
    private LocalPlayerProfileStore.ProfileData profileData;
    private string pendingCreateSuccessGameId;
    private string selectedDetailsGameId = string.Empty;
    private PendingGeneralSettingsMode pendingGeneralSettingsMode = PendingGeneralSettingsMode.None;
    private GeneralSettingsBackgroundPane generalSettingsBackgroundPane = GeneralSettingsBackgroundPane.None;
    private TurnManager.MapSizePreset selectedMapSizePreset = TurnManager.GetDefaultMapSizePreset();
    private TurnManager.AIDifficulty selectedAIDifficulty = TurnManager.AIDifficulty.Level1;
    private bool selectedStoreSnapshotHistory;

    private enum PendingGeneralSettingsMode
    {
        None,
        VsAI,
        PlayByPost
    }

    private enum GeneralSettingsBackgroundPane
    {
        None,
        Main,
        Multiplayer
    }

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
        StopRefreshCountdownTimer();
        StopProfileStatusClearTimer();
        UnsubscribeMainMenuEvents();
        UnbindButtons();
        UnregisterActiveGamesListCallbacks();
        ResetActiveGamesInteractionState();
        ClearCachedElements();
    }

    private void OnApplicationPause(bool paused)
    {
        if (!paused)
        {
            HandleVisiblePaneResume();
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            HandleVisiblePaneResume();
        }
    }

    private void Update()
    {
        if (!enableUITK || !uiReady)
        {
            return;
        }

        ApplySafeArea(force: false);
        responsiveSizeTierController.Apply(root);
        ApplyMenuPhoneLayoutClasses();
        FitMainMenuTitleToWidth();
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
        RefreshMultiplayerBadge();
        RefreshMultiplayerRefreshCountdown();

        SetVisible(mainPanel, true);
        SetVisible(multiplayerPanel, false);
        SetVisible(profilePanel, false);
        SetVisible(detailsPanel, false);
        SetVisible(createSuccessPanel, false);
        SetVisible(generalSettingsPanel, false);
        SetStatus(mainMenuController != null ? mainMenuController.CurrentImportStatus : string.Empty);

        ApplySafeArea(force: true);
        responsiveSizeTierController.Apply(root);
        ApplyMenuPhoneLayoutClasses();
        FitMainMenuTitleToWidth();
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
        detailsContent = root.Q<VisualElement>("DetailsContent");
        detailsResignConfirmContent = root.Q<VisualElement>("DetailsResignConfirmContent");
        joinPanel = root.Q<VisualElement>("JoinPanel");
        createSuccessPanel = root.Q<VisualElement>("CreateSuccessPanel");
        generalSettingsPanel = root.Q<VisualElement>("GeneralSettingsPanel");
        generalSettingsCard = root.Q<VisualElement>("GeneralSettingsCard");
        generalSettingsAiSection = root.Q<VisualElement>("GeneralSettingsAiSection");
        generalSettingsDevSection = root.Q<VisualElement>("GeneralSettingsDevSection");
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
        multiplayerRefreshCountdownLabel = root.Q<Label>("MultiplayerRefreshCountdownLabel");
        versionLabel = root.Q<Label>("VersionLabel");
        multiplayerVersionLabel = root.Q<Label>("MultiplayerVersionLabel");
        titleLabel = root.Q<Label>("TitleLabel");
        profileUsernameValueLabel = root.Q<Label>("ProfileUsernameValueLabel");
        profilePlayerIdValueLabel = root.Q<Label>("ProfilePlayerIdValueLabel");
        profileStatusLabel = root.Q<Label>("ProfileStatusLabel");
        profileTypedDisplayNameInput = root.Q<TextField>("ProfileTypedDisplayNameInput");
        createSuccessGameCodeLabel = root.Q<Label>("CreateSuccessGameCodeLabel");
        generalSettingsTitleLabel = root.Q<Label>("GeneralSettingsTitleLabel");
        generalSettingsSubtitleLabel = root.Q<Label>("GeneralSettingsSubtitleLabel");
        generalSettingsStoreSnapshotHistoryToggle = root.Q<Toggle>("GeneralSettingsStoreSnapshotHistoryToggle");

        continueButton = root.Q<Button>("ContinueButton");
        playVsAiButton = root.Q<Button>("PlayVsAIButton");
        multiplayerButton = root.Q<Button>("MultiplayerButton");
        multiplayerBadge = root.Q<VisualElement>("MultiplayerBadge");
        profileButton = root.Q<Button>("ProfileButton");
        quitButton = root.Q<Button>("QuitButton");
        createButton = root.Q<Button>("CreateButton");
        joinButton = root.Q<Button>("JoinButton");
        multiplayerBackButton = root.Q<Button>("MultiplayerBackButton");
        EnsureDebugNotificationButton();
        debugNotificationButton = root.Q<Button>("DebugNotificationButton");
        createSuccessCopyButton = root.Q<Button>("CreateSuccessCopyButton");
        createSuccessCloseButton = root.Q<Button>("CreateSuccessCloseButton");
        generalSettingsMapSmallButton = root.Q<Button>("GeneralSettingsMapSmallButton");
        generalSettingsMapLargeButton = root.Q<Button>("GeneralSettingsMapLargeButton");
        generalSettingsAiLevel1Button = root.Q<Button>("GeneralSettingsAiLevel1Button");
        generalSettingsAiLevel2Button = root.Q<Button>("GeneralSettingsAiLevel2Button");
        generalSettingsAiLevel3Button = root.Q<Button>("GeneralSettingsAiLevel3Button");
        generalSettingsConfirmButton = root.Q<Button>("GeneralSettingsConfirmButton");
        generalSettingsBackButton = root.Q<Button>("GeneralSettingsBackButton");
        detailsOpenButton = root.Q<Button>("DetailsOpenButton");
        detailsSendReminderButton = root.Q<Button>("DetailsSendReminderButton");
        detailsResignButton = root.Q<Button>("DetailsResignButton");
        detailsCloseButton = root.Q<Button>("DetailsCloseButton");
        resignConfirmCancelButton = root.Q<Button>("ResignConfirmCancelButton");
        resignConfirmAcceptButton = root.Q<Button>("ResignConfirmAcceptButton");
        joinGameIdInput = root.Q<TextField>("JoinGameIdInput");
        joinConfirmButton = root.Q<Button>("JoinConfirmButton");
        joinCancelButton = root.Q<Button>("JoinCancelButton");
        profileRegenerateButton = root.Q<Button>("ProfileRegenerateButton");
        profileCopyPlayerIdButton = root.Q<Button>("ProfileCopyPlayerIdButton");
        profileBackButton = root.Q<Button>("ProfileBackButton");

        if (profileTypedDisplayNameInput != null)
        {
            profileTypedDisplayNameInput.maxLength = ProfileUsernameGenerator.MaxUsernameLength;
            profileTypedDisplayNameInput.isDelayed = false;
        }
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
        activeGamesList.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
        activeGamesList.mouseWheelScrollSize = 120f;
        activeGamesList.scrollOffset = Vector2.zero;
        HideActiveGamesScrollbars();
        activeGamesList.RegisterCallback<PointerDownEvent>(HandleActiveGamesPointerDown);
        activeGamesList.RegisterCallback<PointerMoveEvent>(HandleActiveGamesPointerMove);
        activeGamesList.RegisterCallback<PointerUpEvent>(HandleActiveGamesPointerUp);
        activeGamesList.RegisterCallback<PointerCancelEvent>(HandleActiveGamesPointerCancel);
        ResetActiveGamesElasticOffset();
    }

    private void HideActiveGamesScrollbars()
    {
        if (activeGamesList == null)
        {
            return;
        }

        Scroller verticalScroller = activeGamesList.verticalScroller;
        if (verticalScroller != null)
        {
            verticalScroller.pickingMode = PickingMode.Ignore;
            verticalScroller.style.opacity = 0f;
            verticalScroller.style.width = 0f;
            verticalScroller.style.minWidth = 0f;
            verticalScroller.style.marginLeft = 0f;
            verticalScroller.style.marginRight = 0f;
        }

        Scroller horizontalScroller = activeGamesList.horizontalScroller;
        if (horizontalScroller != null)
        {
            horizontalScroller.pickingMode = PickingMode.Ignore;
            horizontalScroller.style.opacity = 0f;
            horizontalScroller.style.height = 0f;
            horizontalScroller.style.minHeight = 0f;
            horizontalScroller.style.marginTop = 0f;
            horizontalScroller.style.marginBottom = 0f;
        }
    }

    private void EnsureDebugNotificationButton()
    {
        VisualElement actionBar = root.Q(className: "multiplayer-action-bar");
        if (actionBar == null)
        {
            return;
        }

        Button existingButton = actionBar.Q<Button>("DebugNotificationButton");
        bool shouldShow = mainMenuController != null && mainMenuController.ShouldShowIosDebugNotificationTrigger();
        if (!shouldShow)
        {
            if (existingButton != null)
            {
                actionBar.Remove(existingButton);
            }

            return;
        }

        if (existingButton != null)
        {
            return;
        }

        Button button = new Button
        {
            name = "DebugNotificationButton",
            text = "Test iOS Notification"
        };
        button.AddToClassList("menu-button");
        button.AddToClassList("multiplayer-action-button");
        actionBar.Insert(0, button);
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
        if (activeGamesList == null || evt.button != 0)
        {
            return;
        }

        StopActiveGamesElasticReset();
        activeGamesPointerId = evt.pointerId;
        activeGamesPointerStartY = evt.position.y - (activeGamesElasticOffset / NonOverflowListDragDamping);
        activeGamesPullStartedFromRest = IsActiveGamesListAtRest();
        activeGamesPullRefreshArmed = false;
        isDraggingNonOverflowGamesList = false;
    }

    private void HandleActiveGamesPointerMove(PointerMoveEvent evt)
    {
        if (activeGamesList == null || evt.pointerId != activeGamesPointerId)
        {
            return;
        }

        float dragDistance = evt.position.y - activeGamesPointerStartY;
        if (HasActiveGamesScrollableOverflow())
        {
            HandleScrollableActiveGamesPullMove(evt, dragDistance);
            return;
        }

        activeGamesElasticOffset = ComputeElasticListOffset(dragDistance);
        activeGamesPullRefreshArmed = activeGamesPullStartedFromRest &&
                                      CanTriggerActiveGamesPullRefresh() &&
                                      activeGamesElasticOffset >= PullToRefreshTriggerOffset;
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

    private void HandleScrollableActiveGamesPullMove(PointerMoveEvent evt, float dragDistance)
    {
        if (!isDraggingNonOverflowGamesList)
        {
            if (dragDistance <= NonOverflowListDragThreshold || !IsActiveGamesListAtTop())
            {
                return;
            }

            isDraggingNonOverflowGamesList = true;
            suppressNextGameCardClick = true;
            activeGamesList.CapturePointer(evt.pointerId);
            activeGamesPointerStartY = evt.position.y;
            dragDistance = 0f;
        }

        activeGamesElasticOffset = ComputeElasticListOffset(Mathf.Max(0f, dragDistance));
        activeGamesPullRefreshArmed = CanTriggerActiveGamesPullRefresh() &&
                                      activeGamesElasticOffset >= PullToRefreshTriggerOffset;
        ApplyActiveGamesElasticOffset();
        evt.StopPropagation();
    }

    private void HandleActiveGamesPointerUp(PointerUpEvent evt)
    {
        if (evt.pointerId != activeGamesPointerId)
        {
            return;
        }

        bool shouldTriggerPullRefresh = activeGamesPullRefreshArmed;
        ReleaseActiveGamesPointer(evt.pointerId);
        StartActiveGamesElasticReset();

        if (shouldTriggerPullRefresh)
        {
            TriggerActiveGamesPullRefresh();
        }
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
        activeGamesPullStartedFromRest = false;
        activeGamesPullRefreshArmed = false;
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
        activeGamesPullStartedFromRest = false;
        activeGamesPullRefreshArmed = false;
        if (activeGamesList == null)
        {
            return;
        }

        activeGamesList.contentContainer.transform.position = Vector3.zero;
    }

    private bool IsActiveGamesListAtTop()
    {
        return activeGamesList != null && activeGamesList.scrollOffset.y <= PullToRefreshTopScrollTolerance;
    }

    private bool IsActiveGamesListAtRest()
    {
        return Mathf.Abs(activeGamesElasticOffset) <= 0.01f;
    }

    private bool CanTriggerActiveGamesPullRefresh()
    {
        return mainMenuController != null &&
               mainMenuController.HasHttpEligiblePbpGamesForMenuRefresh() &&
               !mainMenuController.IsMenuRefreshInFlight();
    }

    private void TriggerActiveGamesPullRefresh()
    {
        if (mainMenuController == null)
        {
            return;
        }

        if (!mainMenuController.TryManualRefreshMultiplayerList())
        {
            return;
        }

        RefreshMultiplayerRefreshCountdown();
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

        if (debugNotificationButton != null)
        {
            debugNotificationButton.clicked += HandleDebugNotificationClicked;
        }

        if (createSuccessCopyButton != null)
        {
            createSuccessCopyButton.clicked += HandleCreateSuccessCopyClicked;
        }

        if (createSuccessCloseButton != null)
        {
            createSuccessCloseButton.clicked += HandleCreateSuccessCloseClicked;
        }

        if (generalSettingsMapSmallButton != null)
        {
            generalSettingsMapSmallButton.clicked += HandleGeneralSettingsMapSmallClicked;
        }

        if (generalSettingsMapLargeButton != null)
        {
            generalSettingsMapLargeButton.clicked += HandleGeneralSettingsMapLargeClicked;
        }

        if (generalSettingsAiLevel1Button != null)
        {
            generalSettingsAiLevel1Button.clicked += HandleGeneralSettingsAiLevel1Clicked;
        }

        if (generalSettingsAiLevel2Button != null)
        {
            generalSettingsAiLevel2Button.clicked += HandleGeneralSettingsAiLevel2Clicked;
        }

        if (generalSettingsAiLevel3Button != null)
        {
            generalSettingsAiLevel3Button.clicked += HandleGeneralSettingsAiLevel3Clicked;
        }

        if (generalSettingsConfirmButton != null)
        {
            generalSettingsConfirmButton.clicked += HandleGeneralSettingsConfirmClicked;
        }

        if (generalSettingsBackButton != null)
        {
            generalSettingsBackButton.clicked += HandleGeneralSettingsBackClicked;
        }

        if (detailsOpenButton != null)
        {
            detailsOpenButton.clicked += HandleDetailsOpenClicked;
        }

        if (detailsSendReminderButton != null)
        {
            detailsSendReminderButton.clicked += HandleDetailsSendReminderClicked;
        }

        if (detailsResignButton != null)
        {
            detailsResignButton.clicked += HandleDetailsResignClicked;
        }

        if (detailsCloseButton != null)
        {
            detailsCloseButton.clicked += HandleDetailsCloseClicked;
        }

        if (resignConfirmCancelButton != null)
        {
            resignConfirmCancelButton.clicked += HandleResignConfirmCancelClicked;
        }

        if (resignConfirmAcceptButton != null)
        {
            resignConfirmAcceptButton.clicked += HandleResignConfirmAcceptClicked;
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

        if (profileTypedDisplayNameInput != null)
        {
            profileTypedDisplayNameInput.RegisterValueChangedCallback(HandleProfileTypedDisplayNameChanged);
            profileTypedDisplayNameInput.RegisterCallback<FocusOutEvent>(HandleProfileTypedDisplayNameFocusOut);
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

        if (debugNotificationButton != null)
        {
            debugNotificationButton.clicked -= HandleDebugNotificationClicked;
        }

        if (createSuccessCopyButton != null)
        {
            createSuccessCopyButton.clicked -= HandleCreateSuccessCopyClicked;
        }

        if (createSuccessCloseButton != null)
        {
            createSuccessCloseButton.clicked -= HandleCreateSuccessCloseClicked;
        }

        if (generalSettingsMapSmallButton != null)
        {
            generalSettingsMapSmallButton.clicked -= HandleGeneralSettingsMapSmallClicked;
        }

        if (generalSettingsMapLargeButton != null)
        {
            generalSettingsMapLargeButton.clicked -= HandleGeneralSettingsMapLargeClicked;
        }

        if (generalSettingsAiLevel1Button != null)
        {
            generalSettingsAiLevel1Button.clicked -= HandleGeneralSettingsAiLevel1Clicked;
        }

        if (generalSettingsAiLevel2Button != null)
        {
            generalSettingsAiLevel2Button.clicked -= HandleGeneralSettingsAiLevel2Clicked;
        }

        if (generalSettingsAiLevel3Button != null)
        {
            generalSettingsAiLevel3Button.clicked -= HandleGeneralSettingsAiLevel3Clicked;
        }

        if (generalSettingsConfirmButton != null)
        {
            generalSettingsConfirmButton.clicked -= HandleGeneralSettingsConfirmClicked;
        }

        if (generalSettingsBackButton != null)
        {
            generalSettingsBackButton.clicked -= HandleGeneralSettingsBackClicked;
        }

        if (detailsOpenButton != null)
        {
            detailsOpenButton.clicked -= HandleDetailsOpenClicked;
        }

        if (detailsSendReminderButton != null)
        {
            detailsSendReminderButton.clicked -= HandleDetailsSendReminderClicked;
        }

        if (detailsResignButton != null)
        {
            detailsResignButton.clicked -= HandleDetailsResignClicked;
        }

        if (detailsCloseButton != null)
        {
            detailsCloseButton.clicked -= HandleDetailsCloseClicked;
        }

        if (resignConfirmCancelButton != null)
        {
            resignConfirmCancelButton.clicked -= HandleResignConfirmCancelClicked;
        }

        if (resignConfirmAcceptButton != null)
        {
            resignConfirmAcceptButton.clicked -= HandleResignConfirmAcceptClicked;
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

        if (profileTypedDisplayNameInput != null)
        {
            profileTypedDisplayNameInput.UnregisterValueChangedCallback(HandleProfileTypedDisplayNameChanged);
            profileTypedDisplayNameInput.UnregisterCallback<FocusOutEvent>(HandleProfileTypedDisplayNameFocusOut);
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
        mainMenuController.PbpBadgeChanged += RefreshMultiplayerBadge;
        mainMenuController.MultiplayerScreenRequested += HandleMultiplayerScreenRequested;
        mainMenuController.MultiplayerCreateSucceeded += HandleMultiplayerCreateSucceeded;
        SetStatus(mainMenuController.CurrentImportStatus);
        RefreshMultiplayerBadge();
        RefreshMultiplayerRefreshCountdown();
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
        mainMenuController.PbpBadgeChanged -= RefreshMultiplayerBadge;
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
        RefreshMultiplayerRefreshCountdown();
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
        ShowGeneralSettingsPanel(PendingGeneralSettingsMode.VsAI);
    }

    private void HandleMultiplayerClicked()
    {
        if (mainMenuController != null)
        {
            mainMenuController.OpenMultiplayerScreen();
        }

        ShowMultiplayerPanel();
        RefreshGamesList();
        RefreshMultiplayerRefreshCountdown();
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
        ShowGeneralSettingsPanel(PendingGeneralSettingsMode.PlayByPost);
    }

    private void HandleGeneralSettingsMapSmallClicked()
    {
        selectedMapSizePreset = TurnManager.MapSizePreset.Small;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsMapLargeClicked()
    {
        selectedMapSizePreset = TurnManager.MapSizePreset.Large;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAiLevel1Clicked()
    {
        selectedAIDifficulty = TurnManager.AIDifficulty.Level1;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAiLevel2Clicked()
    {
        selectedAIDifficulty = TurnManager.AIDifficulty.Level2;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAiLevel3Clicked()
    {
        selectedAIDifficulty = TurnManager.AIDifficulty.Level3;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsConfirmClicked()
    {
        if (mainMenuController == null)
        {
            return;
        }

        if (generalSettingsStoreSnapshotHistoryToggle != null)
        {
            selectedStoreSnapshotHistory = generalSettingsStoreSnapshotHistoryToggle.value;
        }

        bool started = false;
        if (pendingGeneralSettingsMode == PendingGeneralSettingsMode.VsAI)
        {
            mainMenuController.StartVsAIGameWithSettings(
                selectedAIDifficulty,
                selectedMapSizePreset,
                selectedStoreSnapshotHistory);
            started = true;
        }
        else if (pendingGeneralSettingsMode == PendingGeneralSettingsMode.PlayByPost)
        {
            started = mainMenuController.StartPlayByPostGameWithSettings(
                selectedMapSizePreset,
                selectedStoreSnapshotHistory);
            if (!started)
            {
                SetStatus(mainMenuController.CurrentImportStatus);
            }
        }

        if (started)
        {
            HideGeneralSettingsPanel();
        }
    }

    private void HandleGeneralSettingsBackClicked()
    {
        HideGeneralSettingsPanel();
    }

    private void HandleJoinClicked()
    {
        if (joinPanel == null)
        {
            SetStatus("Join UI unavailable.");
            return;
        }

        if (!TryStartClipboardFirstJoin(allowTypedFallback: false))
        {
            SetStatus("No valid copied code found. Enter or paste a game code.");
            ShowJoinPanel();
        }
    }

    private void HandleJoinConfirmClicked()
    {
        if (mainMenuController == null)
        {
            return;
        }

        bool joinStarted = TryStartClipboardFirstJoin(allowTypedFallback: true);

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
        CommitOrRestoreTypedDisplayNameInput();
        PlayerPrefs.Save();
        ShowMainPanel();
    }

    private void HandleProfileTypedDisplayNameChanged(ChangeEvent<string> evt)
    {
        string normalized = LocalPlayerProfileStore.NormalizeTypedDisplayName(evt.newValue);
        if (profileTypedDisplayNameInput != null &&
            !string.Equals(profileTypedDisplayNameInput.value, normalized, System.StringComparison.Ordinal))
        {
            profileTypedDisplayNameInput.SetValueWithoutNotify(normalized);
        }

        if (!LocalPlayerProfileStore.IsValidTypedDisplayName(normalized) ||
            string.Equals(profileData.TypedDisplayName, normalized, System.StringComparison.Ordinal))
        {
            return;
        }

        profileData.TypedDisplayName = LocalPlayerProfileStore.SetTypedDisplayName(normalized);
        ClearProfileStatus();
    }

    private void HandleProfileTypedDisplayNameFocusOut(FocusOutEvent evt)
    {
        CommitOrRestoreTypedDisplayNameInput();
        PlayerPrefs.Save();
    }

    private void CommitOrRestoreTypedDisplayNameInput()
    {
        if (profileTypedDisplayNameInput == null)
        {
            return;
        }

        string normalized = LocalPlayerProfileStore.NormalizeTypedDisplayName(profileTypedDisplayNameInput.value);
        if (LocalPlayerProfileStore.IsValidTypedDisplayName(normalized))
        {
            if (!string.Equals(profileData.TypedDisplayName, normalized, System.StringComparison.Ordinal))
            {
                profileData.TypedDisplayName = LocalPlayerProfileStore.SetTypedDisplayName(normalized);
                ClearProfileStatus();
            }

            profileTypedDisplayNameInput.SetValueWithoutNotify(profileData.TypedDisplayName ?? string.Empty);
            return;
        }

        if (!LocalPlayerProfileStore.IsValidTypedDisplayName(profileData.TypedDisplayName))
        {
            profileData.TypedDisplayName = LocalPlayerProfileStore.SetTypedDisplayName(
                LocalPlayerProfileStore.GenerateValidTypedDisplayNameFallback());
            ClearProfileStatus();
        }

        profileTypedDisplayNameInput.SetValueWithoutNotify(profileData.TypedDisplayName ?? string.Empty);
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

    private void HandleDebugNotificationClicked()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log("[iOS Debug Notification] Test button tapped in Multiplayer pane.");
#endif
        if (mainMenuController == null)
        {
            return;
        }

        bool scheduled = mainMenuController.TriggerIosDebugNotification();
        SetStatus(scheduled
            ? "Dev iOS test notification scheduled."
            : string.Empty);
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

    private void HandleDetailsSendReminderClicked()
    {
        if (mainMenuController != null && hasSelectedGame)
        {
            mainMenuController.GameDetails_SendReminder();
        }
    }

    private void HandleDetailsResignClicked()
    {
        if (hasSelectedGame)
        {
            ShowResignConfirmPanel();
        }
    }

    private void HandleDetailsCloseClicked()
    {
        SetDetailsConfirmState(false);

        if (mainMenuController != null)
        {
            mainMenuController.CloseGameDetailsPopup();
        }

        HideDetailsPanel();
    }

    private void HandleResignConfirmCancelClicked()
    {
        SetDetailsConfirmState(false);
    }

    private void HandleResignConfirmAcceptClicked()
    {
        if (mainMenuController != null && hasSelectedGame)
        {
            mainMenuController.GameDetails_ResignLocal();
            SetDetailsConfirmState(false);
            HideDetailsPanel();
            RefreshGamesList();
        }
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
            RefreshMultiplayerRefreshCountdown();
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
            RefreshMultiplayerRefreshCountdown();
            return;
        }

        List<SaveManifestService.ManifestGameSummary> yourTurnGames = new List<SaveManifestService.ManifestGameSummary>();
        List<string> yourTurnSubtitles = new List<string>();
        List<SaveManifestService.ManifestGameSummary> waitingGames = new List<SaveManifestService.ManifestGameSummary>();
        List<string> waitingSubtitles = new List<string>();

        for (int i = 0; i < games.Count; i++)
        {
            SaveManifestService.ManifestGameSummary summary = games[i];
            string subtitle = BuildActiveGameSubtitle(summary);
            bool isYourTurn = mainMenuController != null &&
                              string.Equals(
                                  mainMenuController.BuildPlayByPostTurnStateForMenu(summary),
                                  "Your turn",
                                  System.StringComparison.Ordinal);
            if (isYourTurn)
            {
                yourTurnGames.Add(summary);
                yourTurnSubtitles.Add(subtitle);
            }
            else
            {
                waitingGames.Add(summary);
                waitingSubtitles.Add(subtitle);
            }
        }

        int totalGameCount = games.Count;
        int renderedGameCount = 0;

        for (int i = 0; i < yourTurnGames.Count; i++)
        {
            bool isLastGame = renderedGameCount == totalGameCount - 1;
            activeGamesList.Add(CreateGameCard(yourTurnGames[i], yourTurnSubtitles[i], isSingleGame, isLastGame, waitingStyle: false));
            renderedGameCount++;
        }

        bool showWaitingHeader = waitingGames.Count > 0;
        if (showWaitingHeader)
        {
            AddSectionHeader("Waiting for opponent");
        }

        for (int i = 0; i < waitingGames.Count; i++)
        {
            bool isLastGame = renderedGameCount == totalGameCount - 1;
            activeGamesList.Add(CreateGameCard(waitingGames[i], waitingSubtitles[i], isSingleGame, isLastGame, waitingStyle: true));
            renderedGameCount++;
        }

        RefreshMultiplayerRefreshCountdown();
    }

    private string BuildActiveGameSubtitle(SaveManifestService.ManifestGameSummary summary)
    {
        return mainMenuController != null
            ? mainMenuController.BuildPlayByPostTurnSubtitleForMenu(summary)
            : MainMenuController.BuildPlayByPostTurnSubtitle(summary);
    }

    private void AddSectionHeader(string text)
    {
        Label row = new Label(text);
        row.AddToClassList("multiplayer-games-section-header");
        activeGamesList.Add(row);
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
            detailsSubtitleLabel.text = mainMenuController != null
                ? mainMenuController.BuildPlayByPostDetailsSubtitleForMenu(summary)
                : MainMenuController.BuildPlayByPostTurnSubtitle(summary);
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

        RefreshDetailsReminderButton(summary);
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

    private void RefreshDetailsReminderButton(SaveManifestService.ManifestGameSummary summary)
    {
        if (detailsSendReminderButton == null)
        {
            return;
        }

        bool visible = mainMenuController != null && mainMenuController.CanSendReminderForGame(summary);
        detailsSendReminderButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private VisualElement CreateGameCard(
        SaveManifestService.ManifestGameSummary summary,
        string subtitle,
        bool isSingleGame,
        bool isLastGame,
        bool waitingStyle)
    {
        VisualElement card = new VisualElement();
        card.AddToClassList("multiplayer-game-card");
        card.EnableInClassList("multiplayer-game-card--single", isSingleGame);
        card.EnableInClassList("multiplayer-game-card--last", isLastGame);
        card.EnableInClassList("multiplayer-game-card--waiting", waitingStyle);
        card.RegisterCallback<ClickEvent>(_ => HandleGameRowClicked(summary));

        Label title = new Label(BuildGameTitle(summary));
        title.AddToClassList("multiplayer-game-card-title");
        title.EnableInClassList("multiplayer-game-card-title--waiting", waitingStyle);
        card.Add(title);

        Label status = new Label(subtitle);
        status.AddToClassList("multiplayer-game-card-status");
        status.EnableInClassList("multiplayer-game-card-status--waiting", waitingStyle);
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
        StopRefreshCountdownTimer();
        HideGeneralSettingsPanel();
        HideCreateSuccessPanel();
        HideJoinPanel();
        SetDetailsConfirmState(false);
        HideDetailsPanel();
        ClearProfileStatus();
        SetVisible(mainPanel, true);
        SetVisible(multiplayerPanel, false);
        SetVisible(profilePanel, false);
        NotifyControllerVisiblePaneChanged(multiplayerVisible: false);

        if (versionLabel != null)
        {
            versionLabel.style.display = DisplayStyle.Flex;
        }

        if (multiplayerVersionLabel != null)
        {
            multiplayerVersionLabel.style.display = DisplayStyle.None;
        }

        FitMainMenuTitleToWidth();
    }

    private void ShowMultiplayerPanel()
    {
        ResetActiveGamesElasticOffset();
        HideGeneralSettingsPanel();
        SetDetailsConfirmState(false);
        HideJoinPanel();
        ClearProfileStatus();
        SetVisible(mainPanel, false);
        SetVisible(multiplayerPanel, true);
        SetVisible(profilePanel, false);
        NotifyControllerVisiblePaneChanged(multiplayerVisible: true);

        if (versionLabel != null)
        {
            versionLabel.style.display = DisplayStyle.None;
        }

        if (multiplayerVersionLabel != null)
        {
            multiplayerVersionLabel.style.display = DisplayStyle.None;
        }

        StartRefreshCountdownTimer();
        RefreshMultiplayerRefreshCountdown();
        TryShowPendingCreateSuccessPanel();
    }

    private void HideDetailsPanel()
    {
        SetDetailsConfirmState(false);
        hasSelectedGame = false;
        selectedDetailsGameId = string.Empty;
        SetVisible(detailsPanel, false);
    }

    private void ShowProfilePanel()
    {
        StopRefreshCountdownTimer();
        profileData = LocalPlayerProfileStore.GetOrCreateProfile();
        RefreshProfileLabels();
        ClearProfileStatus();
        HideGeneralSettingsPanel();
        HideJoinPanel();
        HideDetailsPanel();
        SetVisible(mainPanel, false);
        SetVisible(multiplayerPanel, false);
        SetVisible(profilePanel, true);
        NotifyControllerVisiblePaneChanged(multiplayerVisible: false);

        if (versionLabel != null)
        {
            versionLabel.style.display = DisplayStyle.None;
        }

        if (multiplayerVersionLabel != null)
        {
            multiplayerVersionLabel.style.display = DisplayStyle.None;
        }

        if (EnableProfileResponsiveDebugLogs && profilePanel != null)
        {
            profilePanel.schedule.Execute(LogProfileResponsiveDebugInfo).ExecuteLater(0);
        }
    }

    private void LogProfileResponsiveDebugInfo()
    {
        if (!EnableProfileResponsiveDebugLogs || root == null || profilePanel == null)
        {
            return;
        }

        responsiveSizeTierController.Apply(root);

        Label profileTitle = root.Q<Label>(className: "profile-title");
        Label helperLabel = root.Q<Label>(className: "profile-typed-display-name-label");
        Label playerIdLabel = root.Q<Label>(className: "profile-field-label");
        Label playerIdValue = profilePlayerIdValueLabel;
        VisualElement typedDisplayInput = profileTypedDisplayNameInput?.Q(className: "unity-base-text-field__input")
            ?? profileTypedDisplayNameInput?.Q(className: "unity-text-field__input");
        VisualElement regenerateButtonText = profileRegenerateButton?.Q(className: "unity-button__text");
        VisualElement copyButtonText = profileCopyPlayerIdButton?.Q(className: "unity-button__text");
        VisualElement backButtonText = profileBackButton?.Q(className: "unity-button__text");

        Debug.Log(
            "MainMenuUITKView profile responsive debug\n" +
            $"rootClasses=[{string.Join(", ", root.GetClasses())}]\n" +
            $"tier={responsiveSizeTierController.CurrentTierClass}\n" +
            $"sharedResponsiveStyleAttached={responsiveSizeTierController.IsSharedStyleSheetAttached(root)}\n" +
            $"screen={Screen.width}x{Screen.height} dpi={Screen.dpi:F1} safeArea={Screen.safeArea} responsiveSize={responsiveSizeTierController.LastResponsiveSize}\n" +
            DescribeElement("profileTitle", profileTitle) +
            DescribeElement("helperLabel", helperLabel) +
            DescribeElement("typedDisplayInput", typedDisplayInput) +
            DescribeElement("playerIdLabel", playerIdLabel) +
            DescribeElement("playerIdValue", playerIdValue) +
            DescribeElement("regenerateButtonText", regenerateButtonText) +
            DescribeElement("copyButtonText", copyButtonText) +
            DescribeElement("backButtonText", backButtonText),
            this);
    }

    private static string DescribeElement(string label, VisualElement element)
    {
        if (element == null)
        {
            return $"{label}=missing\n";
        }

        return
            $"{label}: type={element.GetType().Name} name={element.name} classes=[{string.Join(", ", element.GetClasses())}] " +
            $"display={element.resolvedStyle.display} fontSize={element.resolvedStyle.fontSize} size={element.resolvedStyle.width}x{element.resolvedStyle.height}\n";
    }

    private void ShowJoinPanel()
    {
        HideGeneralSettingsPanel();
        HideCreateSuccessPanel();
        SetDetailsConfirmState(false);
        SetVisible(joinPanel, true);
        if (joinGameIdInput != null)
        {
            joinGameIdInput.Focus();
        }
    }

    private void ShowResignConfirmPanel()
    {
        SetDetailsConfirmState(true);
    }

    private void SetDetailsConfirmState(bool confirmVisible)
    {
        SetVisible(detailsContent, !confirmVisible);
        SetVisible(detailsResignConfirmContent, confirmVisible);
    }

    private void HideJoinPanel()
    {
        SetVisible(joinPanel, false);
        if (joinGameIdInput != null)
        {
            joinGameIdInput.value = string.Empty;
        }
    }

    private void ShowGeneralSettingsPanel(PendingGeneralSettingsMode mode)
    {
        pendingGeneralSettingsMode = mode;
        generalSettingsBackgroundPane = IsVisible(multiplayerPanel)
            ? GeneralSettingsBackgroundPane.Multiplayer
            : GeneralSettingsBackgroundPane.Main;
        selectedMapSizePreset = TurnManager.GetDefaultMapSizePreset();
        selectedAIDifficulty = TurnManager.AIDifficulty.Level1;
        selectedStoreSnapshotHistory = false;
        HideCreateSuccessPanel();
        HideJoinPanel();
        SetDetailsConfirmState(false);
        HideDetailsPanel();
        SetVisible(mainPanel, false);
        SetVisible(multiplayerPanel, false);

        if (versionLabel != null)
        {
            versionLabel.style.display = DisplayStyle.None;
        }

        if (multiplayerVersionLabel != null)
        {
            multiplayerVersionLabel.style.display = DisplayStyle.None;
        }

        bool isVsAi = mode == PendingGeneralSettingsMode.VsAI;

        if (generalSettingsTitleLabel != null)
        {
            generalSettingsTitleLabel.text = "General Settings";
        }

        if (generalSettingsSubtitleLabel != null)
        {
            generalSettingsSubtitleLabel.text = isVsAi
                ? "Choose your map size and AI level."
                : "Choose your map size for this new play-by-post match.";
        }

        if (generalSettingsAiSection != null)
        {
            generalSettingsAiSection.style.display = isVsAi ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (generalSettingsDevSection != null)
        {
            generalSettingsDevSection.style.display = IsDevBuild() ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (generalSettingsStoreSnapshotHistoryToggle != null)
        {
            generalSettingsStoreSnapshotHistoryToggle.value = selectedStoreSnapshotHistory;
        }

        if (generalSettingsConfirmButton != null)
        {
            generalSettingsConfirmButton.text = isVsAi ? "Start Game" : "Create Match";
        }

        RefreshGeneralSettingsSelectionState();
        SetVisible(generalSettingsPanel, true);
    }

    private void HideGeneralSettingsPanel()
    {
        pendingGeneralSettingsMode = PendingGeneralSettingsMode.None;
        SetVisible(generalSettingsPanel, false);

        switch (generalSettingsBackgroundPane)
        {
            case GeneralSettingsBackgroundPane.Multiplayer:
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
                RefreshMultiplayerRefreshCountdown();
                break;

            case GeneralSettingsBackgroundPane.Main:
            default:
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
                FitMainMenuTitleToWidth();
                break;
        }

        generalSettingsBackgroundPane = GeneralSettingsBackgroundPane.None;
    }

    private void RefreshGeneralSettingsSelectionState()
    {
        UpdateGeneralSettingsSelectionButton(
            generalSettingsMapSmallButton,
            selectedMapSizePreset == TurnManager.MapSizePreset.Small);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsMapLargeButton,
            selectedMapSizePreset == TurnManager.MapSizePreset.Large);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAiLevel1Button,
            selectedAIDifficulty == TurnManager.AIDifficulty.Level1);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAiLevel2Button,
            selectedAIDifficulty == TurnManager.AIDifficulty.Level2);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAiLevel3Button,
            selectedAIDifficulty == TurnManager.AIDifficulty.Level3);
    }

    private static void UpdateGeneralSettingsSelectionButton(Button button, bool selected)
    {
        if (button == null)
        {
            return;
        }

        const string SelectedClass = "general-settings-option--selected";
        if (selected)
        {
            button.AddToClassList(SelectedClass);
        }
        else
        {
            button.RemoveFromClassList(SelectedClass);
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

    private static bool IsDevBuild()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
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

    private bool TryStartClipboardFirstJoin(bool allowTypedFallback)
    {
        if (mainMenuController == null)
        {
            return false;
        }

        SetStatus("Trying copied game code...");
        if (mainMenuController.TryResolvePlayByPostJoinGameId(GUIUtility.systemCopyBuffer, out string clipboardGameId))
        {
            SetStatus("Joining copied game code...");
            return mainMenuController.TryJoinPlayByPost(clipboardGameId);
        }

        if (!allowTypedFallback)
        {
            return false;
        }

        SetStatus("No valid copied code found. Trying typed code...");
        string rawGameId = joinGameIdInput != null ? joinGameIdInput.value : null;
        return mainMenuController.TryJoinPlayByPost(rawGameId);
    }

    private void RefreshMultiplayerBadge()
    {
        if (multiplayerBadge == null)
        {
            return;
        }

        int badgeCount = mainMenuController != null ? mainMenuController.PbpBadgeCountMyTurn : 0;
        multiplayerBadge.style.display = badgeCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void RefreshMultiplayerRefreshCountdown()
    {
        if (multiplayerRefreshCountdownLabel == null)
        {
            return;
        }

        if (mainMenuController == null || multiplayerPanel == null || multiplayerPanel.resolvedStyle.display == DisplayStyle.None)
        {
            multiplayerRefreshCountdownLabel.text = string.Empty;
            multiplayerRefreshCountdownLabel.style.display = DisplayStyle.None;
            return;
        }

        if (!mainMenuController.HasHttpEligiblePbpGamesForMenuRefresh())
        {
            multiplayerRefreshCountdownLabel.text = string.Empty;
            multiplayerRefreshCountdownLabel.style.display = DisplayStyle.None;
            return;
        }

        if (mainMenuController.IsMenuRefreshInFlight())
        {
            multiplayerRefreshCountdownLabel.text = "Refreshing...";
            multiplayerRefreshCountdownLabel.style.display = DisplayStyle.Flex;
            return;
        }

        int remainingSeconds = mainMenuController.GetMenuRefreshCountdownSeconds();
        if (remainingSeconds < 0)
        {
            multiplayerRefreshCountdownLabel.text = string.Empty;
            multiplayerRefreshCountdownLabel.style.display = DisplayStyle.None;
            return;
        }

        multiplayerRefreshCountdownLabel.text = $"Next check in {FormatCountdownMinutesSeconds(remainingSeconds)}";
        multiplayerRefreshCountdownLabel.style.display = DisplayStyle.Flex;
    }

    private static string FormatCountdownMinutesSeconds(int totalSeconds)
    {
        int safeSeconds = Mathf.Max(0, totalSeconds);
        int minutes = safeSeconds / 60;
        int seconds = safeSeconds % 60;
        return $"{minutes}:{seconds:00}";
    }

    private void StartRefreshCountdownTimer()
    {
        StopRefreshCountdownTimer();
        if (multiplayerPanel == null)
        {
            return;
        }

        refreshCountdownItem = multiplayerPanel.schedule.Execute(RefreshMultiplayerRefreshCountdown).Every(RefreshCountdownTickMs);
    }

    private void StopRefreshCountdownTimer()
    {
        if (refreshCountdownItem == null)
        {
            return;
        }

        refreshCountdownItem.Pause();
        refreshCountdownItem = null;
    }

    private void NotifyControllerVisiblePaneChanged(bool multiplayerVisible)
    {
        if (mainMenuController == null)
        {
            return;
        }

        mainMenuController.NotifyVisibleMenuPaneChanged(multiplayerVisible);
    }

    private void HandleVisiblePaneResume()
    {
        if (!enableUITK || !uiReady)
        {
            return;
        }

        if (IsVisible(multiplayerPanel))
        {
            NotifyControllerVisiblePaneChanged(multiplayerVisible: true);
            StartRefreshCountdownTimer();
        }
        else
        {
            NotifyControllerVisiblePaneChanged(multiplayerVisible: false);
            StopRefreshCountdownTimer();
        }

        RefreshMultiplayerRefreshCountdown();
    }

    private static bool IsVisible(VisualElement element)
    {
        return element != null && element.style.display != DisplayStyle.None;
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

        if (profileTypedDisplayNameInput != null)
        {
            profileTypedDisplayNameInput.SetValueWithoutNotify(profileData.TypedDisplayName ?? string.Empty);
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

    private void ApplyMenuPhoneLayoutClasses()
    {
        if (root == null)
        {
            return;
        }

        Rect safeArea = Screen.safeArea;
        Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
        if (safeArea.width <= 0f || safeArea.height <= 0f)
        {
            safeArea = new Rect(0f, 0f, screenSize.x, screenSize.y);
        }

        float shortestSide = Mathf.Min(safeArea.width, safeArea.height);
        float usableHeight = Mathf.Max(safeArea.width, safeArea.height);
        bool isWidePhone = shortestSide >= WidePhoneShortestSideMin && usableHeight >= WidePhoneHeightMin;
        root.EnableInClassList(WidePhoneMenuClass, isWidePhone);
    }

    private void FitMainMenuTitleToWidth()
    {
        if (root == null || mainPanel == null || titleLabel == null)
        {
            return;
        }

        if (mainPanel.resolvedStyle.display == DisplayStyle.None)
        {
            return;
        }

        float availableWidth = mainPanel.contentRect.width - MainMenuTitleFitPadding;
        if (availableWidth <= 0f)
        {
            return;
        }

        float fontSize = ResolveMainMenuTitleBaseFontSize();
        titleLabel.style.fontSize = fontSize;

        // Shrink only when needed so the title never collides with the side margins.
        Vector2 measuredSize = titleLabel.MeasureTextSize(
            titleLabel.text,
            0f,
            VisualElement.MeasureMode.Undefined,
            0f,
            VisualElement.MeasureMode.Undefined);

        while (measuredSize.x > availableWidth && fontSize > MainMenuTitleMinimumFontSize)
        {
            fontSize -= 1f;
            titleLabel.style.fontSize = fontSize;
            measuredSize = titleLabel.MeasureTextSize(
                titleLabel.text,
                0f,
                VisualElement.MeasureMode.Undefined,
                0f,
                VisualElement.MeasureMode.Undefined);
        }
    }

    private float ResolveMainMenuTitleBaseFontSize()
    {
        if (root.ClassListContains("ui-large"))
        {
            return MainMenuTitleLargeBaseFontSize;
        }

        if (root.ClassListContains("ui-regular"))
        {
            return MainMenuTitleRegularBaseFontSize;
        }

        if (root.ClassListContains(WidePhoneMenuClass))
        {
            return MainMenuTitleWidePhoneBaseFontSize;
        }

        return MainMenuTitleCompactBaseFontSize;
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
        activeGamesPullStartedFromRest = false;
        activeGamesPullRefreshArmed = false;
        isDraggingNonOverflowGamesList = false;
        suppressNextGameCardClick = false;
        hasSelectedGame = false;
        selectedDetailsGameId = string.Empty;
        uiReady = false;
    }

    private void ClearCachedElements()
    {
        responsiveSizeTierController.Reset(root);
        generalSettingsBackgroundPane = GeneralSettingsBackgroundPane.None;
        root = null;
        mainPanel = null;
        multiplayerPanel = null;
        profilePanel = null;
        detailsPanel = null;
        detailsContent = null;
        detailsResignConfirmContent = null;
        joinPanel = null;
        createSuccessPanel = null;
        generalSettingsPanel = null;
        generalSettingsCard = null;
        generalSettingsAiSection = null;
        generalSettingsDevSection = null;
        activeGamesList = null;
        detailsTitleLabel = null;
        detailsSubtitleLabel = null;
        titleLabel = null;
        detailsGameIdLabel = null;
        statusLabel = null;
        multiplayerRefreshCountdownLabel = null;
        versionLabel = null;
        multiplayerVersionLabel = null;
        profileUsernameValueLabel = null;
        profilePlayerIdValueLabel = null;
        profileStatusLabel = null;
        profileTypedDisplayNameInput = null;
        createSuccessGameCodeLabel = null;
        generalSettingsTitleLabel = null;
        generalSettingsSubtitleLabel = null;
        generalSettingsStoreSnapshotHistoryToggle = null;
        continueButton = null;
        playVsAiButton = null;
        multiplayerButton = null;
        multiplayerBadge = null;
        profileButton = null;
        quitButton = null;
        createButton = null;
        joinButton = null;
        multiplayerBackButton = null;
        createSuccessCopyButton = null;
        createSuccessCloseButton = null;
        generalSettingsMapSmallButton = null;
        generalSettingsMapLargeButton = null;
        generalSettingsAiLevel1Button = null;
        generalSettingsAiLevel2Button = null;
        generalSettingsAiLevel3Button = null;
        generalSettingsConfirmButton = null;
        generalSettingsBackButton = null;
        detailsOpenButton = null;
        detailsSendReminderButton = null;
        detailsResignButton = null;
        detailsCloseButton = null;
        resignConfirmCancelButton = null;
        resignConfirmAcceptButton = null;
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
