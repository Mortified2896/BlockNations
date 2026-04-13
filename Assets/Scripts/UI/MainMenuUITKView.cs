using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

public class MainMenuUITKView : MonoBehaviour
{
    private const bool EnableProfileResponsiveDebugLogs = true;
    private const string ProfileTypedDisplayNameHelperErrorClass = "profile-typed-display-name-label--error";
    private const string ThemeResourceName = "MainMenu_UITK_Theme";
    private const string SinglePlayerPrimarySaveFileName = "save_sp.json";
    private const string LegacySharedSaveFileName = "save.json";
    private const int VisiblePlayerIdPrefixLength = 8;
    private const int VisiblePlayerIdSuffixLength = 5;
    private const int ProfileStatusHideDelayMs = 1800;
    private const int InvalidPointerId = -1;
    private const string ProfileContinueButtonLabel = "Continue";
    private const string ProfileScreenClass = "profile-screen";
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
    private static readonly Color ProfileScreenBackgroundColor = Color.black;

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
    private VisualElement generalSettingsAiStyleSection;
    private VisualElement generalSettingsPbpSection;
    private VisualElement generalSettingsDevSection;
    private VisualElement generalSettingsAIVsAIOptionsSection;
    private VisualElement generalSettingsAIVsAiHeadToHeadSection;
    private VisualElement generalSettingsAIVsAiTournamentSection;
    private VisualElement generalSettingsAIVsAiTournamentParticipantPool;
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
    private Label profileTitleValueLabel;
    private Label profilePlayerIdValueLabel;
    private Label profileTypedDisplayNameHelperLabel;
    private Label profileStatusLabel;
    private Label createSuccessGameCodeLabel;
    private Label generalSettingsTitleLabel;
    private Label generalSettingsSubtitleLabel;
    private Label generalSettingsPbpPlayerCountHelperLabel;
    private Label generalSettingsStoreSnapshotHistoryHelperLabel;
    private Label generalSettingsAIVsAiTournamentEstimateLabel;
    private Label generalSettingsAIVsAiTournamentMatchesPerPairingLabel;

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
    private Button generalSettingsPbpPlayerCount2Button;
    private Button generalSettingsPbpPlayerCount3Button;
    private Button generalSettingsPbpPlayerCount4Button;
    private Button generalSettingsAiStyleDefaultButton;
    private Button generalSettingsAiStyleRiderFocusButton;
    private Button generalSettingsStoreSnapshotHistoryButton;
    private Button generalSettingsWatchAIVsAIButton;
    private Button generalSettingsAIVsAiModeHeadToHeadButton;
    private Button generalSettingsAIVsAiModeTournamentButton;
    private Button generalSettingsSideAAiStyleDefaultButton;
    private Button generalSettingsSideAAiStyleRiderFocusButton;
    private Button generalSettingsSideAFeatureOffenseButton;
    private Button generalSettingsSideAFeatureExchangeButton;
    private Button generalSettingsSideAFeatureDefenseButton;
    private Button generalSettingsSideBAiStyleDefaultButton;
    private Button generalSettingsSideBAiStyleRiderFocusButton;
    private Button generalSettingsSideBFeatureOffenseButton;
    private Button generalSettingsSideBFeatureExchangeButton;
    private Button generalSettingsSideBFeatureDefenseButton;
    private Button generalSettingsAIVsAiPresetQuickButton;
    private Button generalSettingsAIVsAiPresetStandardButton;
    private Button generalSettingsAIVsAiPresetStrictButton;
    private Button generalSettingsAIVsAiEvaluationMethodBayesianButton;
    private Button generalSettingsAIVsAiBatchSpeedNormalButton;
    private Button generalSettingsAIVsAiBatchSpeedFastButton;
    private Button generalSettingsAIVsAiBatchSpeedVeryFastButton;
    private Button generalSettingsAIVsAiBatchSpeedUltraFastButton;
    private Button generalSettingsAIVsAiTournamentTypeRoundRobinButton;
    private Button generalSettingsAIVsAiTournamentRunContinuouslyButton;
    private Button generalSettingsAIVsAiTournamentSeatSwapButton;
    private Button generalSettingsAIVsAiTournamentSelectFullPoolButton;
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
    private Button profileNewTitleButton;
    private Button profileCopyPlayerIdButton;
    private Button profileBackButton;
    private TextField profileTypedDisplayNameInput;
    private TextField generalSettingsAIVsAiCertaintyThresholdInput;
    private TextField generalSettingsAIVsAiMinimumGamesInput;
    private TextField generalSettingsAIVsAiTimeBudgetMinutesInput;
    private TextField generalSettingsAIVsAiBatchSizeInput;
    private TextField generalSettingsAIVsAiEmergencyHardMaxGamesInput;
    private TextField generalSettingsAIVsAiTournamentGamesPerPairingInput;
    private VisualElement multiplayerBadge;
    private readonly List<Button> generalSettingsAIVsAiTournamentParticipantButtons = new List<Button>();

    private bool subscribedToMenuEvents;
    private bool uiReady;
    private bool hasSelectedGame;
    private bool profileOpenedFromMultiplayerRedirect;
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
    private string profileTypedDisplayNameDraft = string.Empty;
    private bool profileTypedDisplayNameOverflowAttempted;
    private string pendingCreateSuccessGameId;
    private string selectedDetailsGameId = string.Empty;
    private PendingGeneralSettingsMode pendingGeneralSettingsMode = PendingGeneralSettingsMode.None;
    private GeneralSettingsBackgroundPane generalSettingsBackgroundPane = GeneralSettingsBackgroundPane.None;
    private TurnManager.MapSizePreset selectedMapSizePreset = TurnManager.GetDefaultMapSizePreset();
    private int selectedPlayByPostPlayerCount = PlayByPostSeatUtility.MinSeatCount;
    private TurnManager.AIRecruitVariant selectedAIRecruitVariant = TurnManager.AIRecruitVariant.Default;
    private bool selectedStoreSnapshotHistory;
    private bool selectedEnableAIVsAIDebugMode;
    private TurnManager.AIRecruitVariant selectedSideAAIRecruitVariant = TurnManager.AIRecruitVariant.Default;
    private bool hasCachedPanelClearState;
    private bool defaultPanelClearColorEnabled;
    private Color defaultPanelClearColorValue = Color.clear;
    private TurnManager.AIRecruitVariant selectedSideBAIRecruitVariant = TurnManager.AIRecruitVariant.Default;
    private AILocalDecisionFeatures selectedSideAFeatures = AILocalDecisionFeatures.None;
    private AILocalDecisionFeatures selectedSideBFeatures = AILocalDecisionFeatures.None;
    private AIVsAIBatchRunController.SimulationSettings selectedAIVsAISimulationSettings =
        AIVsAIBatchRunController.GetDefaultSimulationSettings();
    private TurnManager.AIVsAIBatchSpeedPreset selectedAIVsAIBatchSpeedPreset = TurnManager.AIVsAIBatchSpeedPreset.UltraFast;

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
        UpdateRootScreenClass(profileVisible: false);
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

        if (TryOpenPendingAIVsAISettingsReturn())
        {
            return;
        }

        if (mainMenuController != null && mainMenuController.IsMultiplayerScreenRequested)
        {
            OpenMultiplayerPanelOrRedirect(requestControllerOpen: false);
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
        generalSettingsAiStyleSection = root.Q<VisualElement>("GeneralSettingsAiStyleSection");
        generalSettingsPbpSection = root.Q<VisualElement>("GeneralSettingsPbpSection");
        generalSettingsDevSection = root.Q<VisualElement>("GeneralSettingsDevSection");
        generalSettingsAIVsAIOptionsSection = root.Q<VisualElement>("GeneralSettingsAIVsAIOptionsSection");
        generalSettingsAIVsAiHeadToHeadSection = root.Q<VisualElement>("GeneralSettingsAIVsAiHeadToHeadSection");
        generalSettingsAIVsAiTournamentSection = root.Q<VisualElement>("GeneralSettingsAIVsAiTournamentSection");
        generalSettingsAIVsAiTournamentParticipantPool = root.Q<VisualElement>("GeneralSettingsAIVsAiTournamentParticipantPool");
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
        profileTitleValueLabel = root.Q<Label>("ProfileTitleValueLabel");
        profilePlayerIdValueLabel = root.Q<Label>("ProfilePlayerIdValueLabel");
        profileTypedDisplayNameHelperLabel = root.Q<Label>("ProfileTypedDisplayNameHelperLabel");
        profileStatusLabel = root.Q<Label>("ProfileStatusLabel");
        profileTypedDisplayNameInput = root.Q<TextField>("ProfileTypedDisplayNameInput");
        createSuccessGameCodeLabel = root.Q<Label>("CreateSuccessGameCodeLabel");
        generalSettingsTitleLabel = root.Q<Label>("GeneralSettingsTitleLabel");
        generalSettingsSubtitleLabel = root.Q<Label>("GeneralSettingsSubtitleLabel");
        generalSettingsPbpPlayerCountHelperLabel = root.Q<Label>("GeneralSettingsPbpPlayerCountHelperLabel");
        generalSettingsStoreSnapshotHistoryHelperLabel = root.Q<Label>("GeneralSettingsStoreSnapshotHistoryHelperLabel");
        generalSettingsAIVsAiTournamentEstimateLabel = root.Q<Label>("GeneralSettingsAIVsAiTournamentEstimateLabel");
        generalSettingsAIVsAiTournamentMatchesPerPairingLabel = root.Q<Label>("GeneralSettingsAIVsAiTournamentMatchesPerPairingLabel");

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
        generalSettingsPbpPlayerCount2Button = root.Q<Button>("GeneralSettingsPbpPlayerCount2Button");
        generalSettingsPbpPlayerCount3Button = root.Q<Button>("GeneralSettingsPbpPlayerCount3Button");
        generalSettingsPbpPlayerCount4Button = root.Q<Button>("GeneralSettingsPbpPlayerCount4Button");
        generalSettingsAiStyleDefaultButton = root.Q<Button>("GeneralSettingsAiStyleDefaultButton");
        generalSettingsAiStyleRiderFocusButton = root.Q<Button>("GeneralSettingsAiStyleRiderFocusButton");
        generalSettingsStoreSnapshotHistoryButton = root.Q<Button>("GeneralSettingsStoreSnapshotHistoryButton");
        generalSettingsWatchAIVsAIButton = root.Q<Button>("GeneralSettingsWatchAIVsAIButton");
        generalSettingsAIVsAiModeHeadToHeadButton = root.Q<Button>("GeneralSettingsAIVsAiModeHeadToHeadButton");
        generalSettingsAIVsAiModeTournamentButton = root.Q<Button>("GeneralSettingsAIVsAiModeTournamentButton");
        generalSettingsSideAAiStyleDefaultButton = root.Q<Button>("GeneralSettingsSideAAiStyleDefaultButton");
        generalSettingsSideAAiStyleRiderFocusButton = root.Q<Button>("GeneralSettingsSideAAiStyleRiderFocusButton");
        generalSettingsSideAFeatureOffenseButton = root.Q<Button>("GeneralSettingsSideAFeatureOffenseButton");
        generalSettingsSideAFeatureExchangeButton = root.Q<Button>("GeneralSettingsSideAFeatureExchangeButton");
        generalSettingsSideAFeatureDefenseButton = root.Q<Button>("GeneralSettingsSideAFeatureDefenseButton");
        generalSettingsSideBAiStyleDefaultButton = root.Q<Button>("GeneralSettingsSideBAiStyleDefaultButton");
        generalSettingsSideBAiStyleRiderFocusButton = root.Q<Button>("GeneralSettingsSideBAiStyleRiderFocusButton");
        generalSettingsSideBFeatureOffenseButton = root.Q<Button>("GeneralSettingsSideBFeatureOffenseButton");
        generalSettingsSideBFeatureExchangeButton = root.Q<Button>("GeneralSettingsSideBFeatureExchangeButton");
        generalSettingsSideBFeatureDefenseButton = root.Q<Button>("GeneralSettingsSideBFeatureDefenseButton");
        generalSettingsAIVsAiPresetQuickButton = root.Q<Button>("GeneralSettingsAIVsAiPresetQuickButton");
        generalSettingsAIVsAiPresetStandardButton = root.Q<Button>("GeneralSettingsAIVsAiPresetStandardButton");
        generalSettingsAIVsAiPresetStrictButton = root.Q<Button>("GeneralSettingsAIVsAiPresetStrictButton");
        generalSettingsAIVsAiEvaluationMethodBayesianButton = root.Q<Button>("GeneralSettingsAIVsAiEvaluationMethodBayesianButton");
        generalSettingsAIVsAiBatchSpeedNormalButton = root.Q<Button>("GeneralSettingsAIVsAiBatchSpeedNormalButton");
        generalSettingsAIVsAiBatchSpeedFastButton = root.Q<Button>("GeneralSettingsAIVsAiBatchSpeedFastButton");
        generalSettingsAIVsAiBatchSpeedVeryFastButton = root.Q<Button>("GeneralSettingsAIVsAiBatchSpeedVeryFastButton");
        generalSettingsAIVsAiBatchSpeedUltraFastButton = root.Q<Button>("GeneralSettingsAIVsAiBatchSpeedUltraFastButton");
        generalSettingsAIVsAiTournamentTypeRoundRobinButton = root.Q<Button>("GeneralSettingsAIVsAiTournamentTypeRoundRobinButton");
        generalSettingsAIVsAiTournamentRunContinuouslyButton = root.Q<Button>("GeneralSettingsAIVsAiTournamentRunContinuouslyButton");
        generalSettingsAIVsAiTournamentSeatSwapButton = root.Q<Button>("GeneralSettingsAIVsAiTournamentSeatSwapButton");
        generalSettingsAIVsAiTournamentSelectFullPoolButton = root.Q<Button>("GeneralSettingsAIVsAiTournamentSelectFullPoolButton");
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
        profileNewTitleButton = root.Q<Button>("ProfileNewTitleButton");
        profileCopyPlayerIdButton = root.Q<Button>("ProfileCopyPlayerIdButton");
        profileBackButton = root.Q<Button>("ProfileBackButton");
        generalSettingsAIVsAiCertaintyThresholdInput = root.Q<TextField>("GeneralSettingsAIVsAiCertaintyThresholdInput");
        generalSettingsAIVsAiMinimumGamesInput = root.Q<TextField>("GeneralSettingsAIVsAiMinimumGamesInput");
        generalSettingsAIVsAiTimeBudgetMinutesInput = root.Q<TextField>("GeneralSettingsAIVsAiTimeBudgetMinutesInput");
        generalSettingsAIVsAiBatchSizeInput = root.Q<TextField>("GeneralSettingsAIVsAiBatchSizeInput");
        generalSettingsAIVsAiEmergencyHardMaxGamesInput = root.Q<TextField>("GeneralSettingsAIVsAiEmergencyHardMaxGamesInput");
        generalSettingsAIVsAiTournamentGamesPerPairingInput = root.Q<TextField>("GeneralSettingsAIVsAiTournamentGamesPerPairingInput");

        if (profileTypedDisplayNameInput != null)
        {
            profileTypedDisplayNameInput.maxLength = ProfileUsernameGenerator.MaxUsernameLength;
            profileTypedDisplayNameInput.isDelayed = false;
        }

        ConfigureDelayedTextField(generalSettingsAIVsAiCertaintyThresholdInput);
        ConfigureDelayedTextField(generalSettingsAIVsAiMinimumGamesInput);
        ConfigureDelayedTextField(generalSettingsAIVsAiTimeBudgetMinutesInput);
        ConfigureDelayedTextField(generalSettingsAIVsAiBatchSizeInput);
        ConfigureDelayedTextField(generalSettingsAIVsAiEmergencyHardMaxGamesInput);
        ConfigureDelayedTextField(generalSettingsAIVsAiTournamentGamesPerPairingInput);
        EnsureTournamentParticipantButtons();
    }

    private static void ConfigureDelayedTextField(TextField textField)
    {
        if (textField == null)
        {
            return;
        }

        textField.isDelayed = true;
    }

    private void EnsureTournamentParticipantButtons()
    {
        if (generalSettingsAIVsAiTournamentParticipantPool == null)
        {
            generalSettingsAIVsAiTournamentParticipantButtons.Clear();
            return;
        }

        if (generalSettingsAIVsAiTournamentParticipantButtons.Count == AIVsAIBatchRunController.GetGeneratedVariantCount())
        {
            return;
        }

        generalSettingsAIVsAiTournamentParticipantPool.Clear();
        generalSettingsAIVsAiTournamentParticipantButtons.Clear();

        for (int i = 0; i < AIVsAIBatchRunController.GetGeneratedVariantCount(); i++)
        {
            int capturedIndex = i;
            Button button = new Button(() => ToggleTournamentParticipant(capturedIndex))
            {
                text = AIVsAIBatchRunController.GetGeneratedVariantLabel(capturedIndex)
            };
            button.AddToClassList("menu-button");
            button.AddToClassList("general-settings-option-button");
            generalSettingsAIVsAiTournamentParticipantPool.Add(button);
            generalSettingsAIVsAiTournamentParticipantButtons.Add(button);
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

        if (generalSettingsPbpPlayerCount2Button != null)
        {
            generalSettingsPbpPlayerCount2Button.clicked += HandleGeneralSettingsPbpPlayerCount2Clicked;
        }

        if (generalSettingsAiStyleDefaultButton != null)
        {
            generalSettingsAiStyleDefaultButton.clicked += HandleGeneralSettingsAiStyleDefaultClicked;
        }

        if (generalSettingsAiStyleRiderFocusButton != null)
        {
            generalSettingsAiStyleRiderFocusButton.clicked += HandleGeneralSettingsAiStyleRiderFocusClicked;
        }

        if (generalSettingsSideAAiStyleDefaultButton != null)
        {
            generalSettingsSideAAiStyleDefaultButton.clicked += HandleGeneralSettingsSideAAiStyleDefaultClicked;
        }

        if (generalSettingsSideAAiStyleRiderFocusButton != null)
        {
            generalSettingsSideAAiStyleRiderFocusButton.clicked += HandleGeneralSettingsSideAAiStyleRiderFocusClicked;
        }

        if (generalSettingsSideAFeatureOffenseButton != null)
        {
            generalSettingsSideAFeatureOffenseButton.clicked += HandleGeneralSettingsSideAFeatureOffenseClicked;
        }

        if (generalSettingsSideAFeatureExchangeButton != null)
        {
            generalSettingsSideAFeatureExchangeButton.clicked += HandleGeneralSettingsSideAFeatureExchangeClicked;
        }

        if (generalSettingsSideAFeatureDefenseButton != null)
        {
            generalSettingsSideAFeatureDefenseButton.clicked += HandleGeneralSettingsSideAFeatureDefenseClicked;
        }

        if (generalSettingsSideBAiStyleDefaultButton != null)
        {
            generalSettingsSideBAiStyleDefaultButton.clicked += HandleGeneralSettingsSideBAiStyleDefaultClicked;
        }

        if (generalSettingsSideBAiStyleRiderFocusButton != null)
        {
            generalSettingsSideBAiStyleRiderFocusButton.clicked += HandleGeneralSettingsSideBAiStyleRiderFocusClicked;
        }

        if (generalSettingsSideBFeatureOffenseButton != null)
        {
            generalSettingsSideBFeatureOffenseButton.clicked += HandleGeneralSettingsSideBFeatureOffenseClicked;
        }

        if (generalSettingsSideBFeatureExchangeButton != null)
        {
            generalSettingsSideBFeatureExchangeButton.clicked += HandleGeneralSettingsSideBFeatureExchangeClicked;
        }

        if (generalSettingsSideBFeatureDefenseButton != null)
        {
            generalSettingsSideBFeatureDefenseButton.clicked += HandleGeneralSettingsSideBFeatureDefenseClicked;
        }

        if (generalSettingsAIVsAiPresetQuickButton != null)
        {
            generalSettingsAIVsAiPresetQuickButton.clicked += HandleGeneralSettingsAIVsAiPresetQuickClicked;
        }

        if (generalSettingsAIVsAiPresetStandardButton != null)
        {
            generalSettingsAIVsAiPresetStandardButton.clicked += HandleGeneralSettingsAIVsAiPresetStandardClicked;
        }

        if (generalSettingsAIVsAiPresetStrictButton != null)
        {
            generalSettingsAIVsAiPresetStrictButton.clicked += HandleGeneralSettingsAIVsAiPresetStrictClicked;
        }

        if (generalSettingsAIVsAiEvaluationMethodBayesianButton != null)
        {
            generalSettingsAIVsAiEvaluationMethodBayesianButton.clicked += HandleGeneralSettingsAIVsAiEvaluationMethodBayesianClicked;
        }

        if (generalSettingsAIVsAiBatchSpeedNormalButton != null)
        {
            generalSettingsAIVsAiBatchSpeedNormalButton.clicked += HandleGeneralSettingsAIVsAiBatchSpeedNormalClicked;
        }

        if (generalSettingsAIVsAiBatchSpeedFastButton != null)
        {
            generalSettingsAIVsAiBatchSpeedFastButton.clicked += HandleGeneralSettingsAIVsAiBatchSpeedFastClicked;
        }

        if (generalSettingsAIVsAiBatchSpeedVeryFastButton != null)
        {
            generalSettingsAIVsAiBatchSpeedVeryFastButton.clicked += HandleGeneralSettingsAIVsAiBatchSpeedVeryFastClicked;
        }

        if (generalSettingsAIVsAiBatchSpeedUltraFastButton != null)
        {
            generalSettingsAIVsAiBatchSpeedUltraFastButton.clicked += HandleGeneralSettingsAIVsAiBatchSpeedUltraFastClicked;
        }

        if (generalSettingsStoreSnapshotHistoryButton != null)
        {
            generalSettingsStoreSnapshotHistoryButton.clicked += HandleGeneralSettingsStoreSnapshotHistoryClicked;
        }

        if (generalSettingsWatchAIVsAIButton != null)
        {
            generalSettingsWatchAIVsAIButton.clicked += HandleGeneralSettingsWatchAIVsAIClicked;
        }

        if (generalSettingsAIVsAiModeHeadToHeadButton != null)
        {
            generalSettingsAIVsAiModeHeadToHeadButton.clicked += HandleGeneralSettingsAIVsAiModeHeadToHeadClicked;
        }

        if (generalSettingsAIVsAiModeTournamentButton != null)
        {
            generalSettingsAIVsAiModeTournamentButton.clicked += HandleGeneralSettingsAIVsAiModeTournamentClicked;
        }

        if (generalSettingsAIVsAiCertaintyThresholdInput != null)
        {
            generalSettingsAIVsAiCertaintyThresholdInput.RegisterValueChangedCallback(HandleGeneralSettingsAIVsAiCertaintyThresholdChanged);
        }

        if (generalSettingsAIVsAiMinimumGamesInput != null)
        {
            generalSettingsAIVsAiMinimumGamesInput.RegisterValueChangedCallback(HandleGeneralSettingsAIVsAiMinimumGamesChanged);
        }

        if (generalSettingsAIVsAiTimeBudgetMinutesInput != null)
        {
            generalSettingsAIVsAiTimeBudgetMinutesInput.RegisterValueChangedCallback(HandleGeneralSettingsAIVsAiTimeBudgetMinutesChanged);
        }

        if (generalSettingsAIVsAiBatchSizeInput != null)
        {
            generalSettingsAIVsAiBatchSizeInput.RegisterValueChangedCallback(HandleGeneralSettingsAIVsAiBatchSizeChanged);
        }

        if (generalSettingsAIVsAiEmergencyHardMaxGamesInput != null)
        {
            generalSettingsAIVsAiEmergencyHardMaxGamesInput.RegisterValueChangedCallback(HandleGeneralSettingsAIVsAiEmergencyHardMaxGamesChanged);
        }

        if (generalSettingsAIVsAiTournamentTypeRoundRobinButton != null)
        {
            generalSettingsAIVsAiTournamentTypeRoundRobinButton.clicked += HandleGeneralSettingsAIVsAiTournamentTypeRoundRobinClicked;
        }

        if (generalSettingsAIVsAiTournamentRunContinuouslyButton != null)
        {
            generalSettingsAIVsAiTournamentRunContinuouslyButton.clicked += HandleGeneralSettingsAIVsAiTournamentRunContinuouslyClicked;
        }

        if (generalSettingsAIVsAiTournamentSeatSwapButton != null)
        {
            generalSettingsAIVsAiTournamentSeatSwapButton.clicked += HandleGeneralSettingsAIVsAiTournamentSeatSwapClicked;
        }

        if (generalSettingsAIVsAiTournamentSelectFullPoolButton != null)
        {
            generalSettingsAIVsAiTournamentSelectFullPoolButton.clicked += HandleGeneralSettingsAIVsAiTournamentSelectFullPoolClicked;
        }

        if (generalSettingsAIVsAiTournamentGamesPerPairingInput != null)
        {
            generalSettingsAIVsAiTournamentGamesPerPairingInput.RegisterValueChangedCallback(HandleGeneralSettingsAIVsAiTournamentGamesPerPairingChanged);
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

        if (profileNewTitleButton != null)
        {
            profileNewTitleButton.clicked += HandleProfileNewTitleClicked;
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
            profileTypedDisplayNameInput.RegisterCallback<KeyDownEvent>(HandleProfileTypedDisplayNameKeyDown);
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

        if (generalSettingsPbpPlayerCount2Button != null)
        {
            generalSettingsPbpPlayerCount2Button.clicked -= HandleGeneralSettingsPbpPlayerCount2Clicked;
        }

        if (generalSettingsAiStyleDefaultButton != null)
        {
            generalSettingsAiStyleDefaultButton.clicked -= HandleGeneralSettingsAiStyleDefaultClicked;
        }

        if (generalSettingsAiStyleRiderFocusButton != null)
        {
            generalSettingsAiStyleRiderFocusButton.clicked -= HandleGeneralSettingsAiStyleRiderFocusClicked;
        }

        if (generalSettingsSideAAiStyleDefaultButton != null)
        {
            generalSettingsSideAAiStyleDefaultButton.clicked -= HandleGeneralSettingsSideAAiStyleDefaultClicked;
        }

        if (generalSettingsSideAAiStyleRiderFocusButton != null)
        {
            generalSettingsSideAAiStyleRiderFocusButton.clicked -= HandleGeneralSettingsSideAAiStyleRiderFocusClicked;
        }

        if (generalSettingsSideAFeatureOffenseButton != null)
        {
            generalSettingsSideAFeatureOffenseButton.clicked -= HandleGeneralSettingsSideAFeatureOffenseClicked;
        }

        if (generalSettingsSideAFeatureExchangeButton != null)
        {
            generalSettingsSideAFeatureExchangeButton.clicked -= HandleGeneralSettingsSideAFeatureExchangeClicked;
        }

        if (generalSettingsSideAFeatureDefenseButton != null)
        {
            generalSettingsSideAFeatureDefenseButton.clicked -= HandleGeneralSettingsSideAFeatureDefenseClicked;
        }

        if (generalSettingsSideBAiStyleDefaultButton != null)
        {
            generalSettingsSideBAiStyleDefaultButton.clicked -= HandleGeneralSettingsSideBAiStyleDefaultClicked;
        }

        if (generalSettingsSideBAiStyleRiderFocusButton != null)
        {
            generalSettingsSideBAiStyleRiderFocusButton.clicked -= HandleGeneralSettingsSideBAiStyleRiderFocusClicked;
        }

        if (generalSettingsSideBFeatureOffenseButton != null)
        {
            generalSettingsSideBFeatureOffenseButton.clicked -= HandleGeneralSettingsSideBFeatureOffenseClicked;
        }

        if (generalSettingsSideBFeatureExchangeButton != null)
        {
            generalSettingsSideBFeatureExchangeButton.clicked -= HandleGeneralSettingsSideBFeatureExchangeClicked;
        }

        if (generalSettingsSideBFeatureDefenseButton != null)
        {
            generalSettingsSideBFeatureDefenseButton.clicked -= HandleGeneralSettingsSideBFeatureDefenseClicked;
        }

        if (generalSettingsAIVsAiPresetQuickButton != null)
        {
            generalSettingsAIVsAiPresetQuickButton.clicked -= HandleGeneralSettingsAIVsAiPresetQuickClicked;
        }

        if (generalSettingsAIVsAiPresetStandardButton != null)
        {
            generalSettingsAIVsAiPresetStandardButton.clicked -= HandleGeneralSettingsAIVsAiPresetStandardClicked;
        }

        if (generalSettingsAIVsAiPresetStrictButton != null)
        {
            generalSettingsAIVsAiPresetStrictButton.clicked -= HandleGeneralSettingsAIVsAiPresetStrictClicked;
        }

        if (generalSettingsAIVsAiEvaluationMethodBayesianButton != null)
        {
            generalSettingsAIVsAiEvaluationMethodBayesianButton.clicked -= HandleGeneralSettingsAIVsAiEvaluationMethodBayesianClicked;
        }

        if (generalSettingsAIVsAiBatchSpeedNormalButton != null)
        {
            generalSettingsAIVsAiBatchSpeedNormalButton.clicked -= HandleGeneralSettingsAIVsAiBatchSpeedNormalClicked;
        }

        if (generalSettingsAIVsAiBatchSpeedFastButton != null)
        {
            generalSettingsAIVsAiBatchSpeedFastButton.clicked -= HandleGeneralSettingsAIVsAiBatchSpeedFastClicked;
        }

        if (generalSettingsAIVsAiBatchSpeedVeryFastButton != null)
        {
            generalSettingsAIVsAiBatchSpeedVeryFastButton.clicked -= HandleGeneralSettingsAIVsAiBatchSpeedVeryFastClicked;
        }

        if (generalSettingsAIVsAiBatchSpeedUltraFastButton != null)
        {
            generalSettingsAIVsAiBatchSpeedUltraFastButton.clicked -= HandleGeneralSettingsAIVsAiBatchSpeedUltraFastClicked;
        }

        if (generalSettingsStoreSnapshotHistoryButton != null)
        {
            generalSettingsStoreSnapshotHistoryButton.clicked -= HandleGeneralSettingsStoreSnapshotHistoryClicked;
        }

        if (generalSettingsWatchAIVsAIButton != null)
        {
            generalSettingsWatchAIVsAIButton.clicked -= HandleGeneralSettingsWatchAIVsAIClicked;
        }

        if (generalSettingsAIVsAiModeHeadToHeadButton != null)
        {
            generalSettingsAIVsAiModeHeadToHeadButton.clicked -= HandleGeneralSettingsAIVsAiModeHeadToHeadClicked;
        }

        if (generalSettingsAIVsAiModeTournamentButton != null)
        {
            generalSettingsAIVsAiModeTournamentButton.clicked -= HandleGeneralSettingsAIVsAiModeTournamentClicked;
        }

        if (generalSettingsAIVsAiCertaintyThresholdInput != null)
        {
            generalSettingsAIVsAiCertaintyThresholdInput.UnregisterValueChangedCallback(HandleGeneralSettingsAIVsAiCertaintyThresholdChanged);
        }

        if (generalSettingsAIVsAiMinimumGamesInput != null)
        {
            generalSettingsAIVsAiMinimumGamesInput.UnregisterValueChangedCallback(HandleGeneralSettingsAIVsAiMinimumGamesChanged);
        }

        if (generalSettingsAIVsAiTimeBudgetMinutesInput != null)
        {
            generalSettingsAIVsAiTimeBudgetMinutesInput.UnregisterValueChangedCallback(HandleGeneralSettingsAIVsAiTimeBudgetMinutesChanged);
        }

        if (generalSettingsAIVsAiBatchSizeInput != null)
        {
            generalSettingsAIVsAiBatchSizeInput.UnregisterValueChangedCallback(HandleGeneralSettingsAIVsAiBatchSizeChanged);
        }

        if (generalSettingsAIVsAiEmergencyHardMaxGamesInput != null)
        {
            generalSettingsAIVsAiEmergencyHardMaxGamesInput.UnregisterValueChangedCallback(HandleGeneralSettingsAIVsAiEmergencyHardMaxGamesChanged);
        }

        if (generalSettingsAIVsAiTournamentTypeRoundRobinButton != null)
        {
            generalSettingsAIVsAiTournamentTypeRoundRobinButton.clicked -= HandleGeneralSettingsAIVsAiTournamentTypeRoundRobinClicked;
        }

        if (generalSettingsAIVsAiTournamentRunContinuouslyButton != null)
        {
            generalSettingsAIVsAiTournamentRunContinuouslyButton.clicked -= HandleGeneralSettingsAIVsAiTournamentRunContinuouslyClicked;
        }

        if (generalSettingsAIVsAiTournamentSeatSwapButton != null)
        {
            generalSettingsAIVsAiTournamentSeatSwapButton.clicked -= HandleGeneralSettingsAIVsAiTournamentSeatSwapClicked;
        }

        if (generalSettingsAIVsAiTournamentSelectFullPoolButton != null)
        {
            generalSettingsAIVsAiTournamentSelectFullPoolButton.clicked -= HandleGeneralSettingsAIVsAiTournamentSelectFullPoolClicked;
        }

        if (generalSettingsAIVsAiTournamentGamesPerPairingInput != null)
        {
            generalSettingsAIVsAiTournamentGamesPerPairingInput.UnregisterValueChangedCallback(HandleGeneralSettingsAIVsAiTournamentGamesPerPairingChanged);
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

        if (profileNewTitleButton != null)
        {
            profileNewTitleButton.clicked -= HandleProfileNewTitleClicked;
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
            profileTypedDisplayNameInput.UnregisterCallback<KeyDownEvent>(HandleProfileTypedDisplayNameKeyDown);
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

        OpenMultiplayerPanelOrRedirect(requestControllerOpen: false);
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

    private bool TryOpenPendingAIVsAISettingsReturn()
    {
        if (!MainMenuController.TryConsumePendingAIVsAISettingsReturn(out MainMenuController.PendingAIVsAISettingsReturn pendingSettings))
        {
            return false;
        }

        ShowGeneralSettingsPanel(PendingGeneralSettingsMode.VsAI);
        selectedMapSizePreset = pendingSettings.mapSizePreset;
        selectedAIRecruitVariant = pendingSettings.recruitVariant;
        selectedStoreSnapshotHistory = pendingSettings.storeSnapshotHistory;
        selectedEnableAIVsAIDebugMode = pendingSettings.enableAIVsAIDebugMode;
        selectedSideAAIRecruitVariant = pendingSettings.sideARecruitVariant;
        selectedSideBAIRecruitVariant = pendingSettings.sideBRecruitVariant;
        selectedSideAFeatures = pendingSettings.sideAFeatures;
        selectedSideBFeatures = pendingSettings.sideBFeatures;
        selectedAIVsAISimulationSettings =
            AIVsAIBatchRunController.SanitizeSimulationSettings(pendingSettings.aiVsAiSimulationSettings);
        selectedAIVsAIBatchSpeedPreset = pendingSettings.aiVsAiBatchSpeedPreset;
        RefreshGeneralSettingsSelectionState();
        return true;
    }

    private void HandleMultiplayerClicked()
    {
        OpenMultiplayerPanelOrRedirect(requestControllerOpen: true);
    }

    private void HandleProfileClicked()
    {
        profileOpenedFromMultiplayerRedirect = false;
        RefreshProfileBackButtonLabel();
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

    private void HandleGeneralSettingsPbpPlayerCount2Clicked()
    {
        selectedPlayByPostPlayerCount = PlayByPostSeatUtility.MinSeatCount;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAiStyleDefaultClicked()
    {
        selectedAIRecruitVariant = TurnManager.AIRecruitVariant.Default;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAiStyleRiderFocusClicked()
    {
        selectedAIRecruitVariant = TurnManager.AIRecruitVariant.RiderFocus;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsSideAAiStyleDefaultClicked()
    {
        selectedSideAAIRecruitVariant = TurnManager.AIRecruitVariant.Default;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsSideAAiStyleRiderFocusClicked()
    {
        selectedSideAAIRecruitVariant = TurnManager.AIRecruitVariant.RiderFocus;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsSideAFeatureOffenseClicked()
    {
        ToggleSideAFeature(AILocalDecisionFeatures.OffensiveObviousWin);
    }

    private void HandleGeneralSettingsSideAFeatureExchangeClicked()
    {
        ToggleSideAFeature(AILocalDecisionFeatures.ExchangeScoring);
    }

    private void HandleGeneralSettingsSideAFeatureDefenseClicked()
    {
        ToggleSideAFeature(AILocalDecisionFeatures.DefensiveVeto);
    }

    private void HandleGeneralSettingsSideBAiStyleDefaultClicked()
    {
        selectedSideBAIRecruitVariant = TurnManager.AIRecruitVariant.Default;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsSideBAiStyleRiderFocusClicked()
    {
        selectedSideBAIRecruitVariant = TurnManager.AIRecruitVariant.RiderFocus;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsSideBFeatureOffenseClicked()
    {
        ToggleSideBFeature(AILocalDecisionFeatures.OffensiveObviousWin);
    }

    private void HandleGeneralSettingsSideBFeatureExchangeClicked()
    {
        ToggleSideBFeature(AILocalDecisionFeatures.ExchangeScoring);
    }

    private void HandleGeneralSettingsSideBFeatureDefenseClicked()
    {
        ToggleSideBFeature(AILocalDecisionFeatures.DefensiveVeto);
    }

    private void HandleGeneralSettingsAIVsAiPresetQuickClicked()
    {
        ApplyAIVsAiSimulationPreset(AIVsAIBatchRunController.SimulationPreset.QuickExploration);
    }

    private void HandleGeneralSettingsAIVsAiPresetStandardClicked()
    {
        ApplyAIVsAiSimulationPreset(AIVsAIBatchRunController.SimulationPreset.StandardComparison);
    }

    private void HandleGeneralSettingsAIVsAiPresetStrictClicked()
    {
        ApplyAIVsAiSimulationPreset(AIVsAIBatchRunController.SimulationPreset.StrictComparison);
    }

    private void HandleGeneralSettingsAIVsAiEvaluationMethodBayesianClicked()
    {
        selectedAIVsAISimulationSettings.evaluationMethod = AIVsAIBatchRunController.EvaluationMethod.Bayesian;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAIVsAiCertaintyThresholdChanged(ChangeEvent<string> evt)
    {
        if (TryParseFloat(evt != null ? evt.newValue : null, out float parsedValue))
        {
            if (!Mathf.Approximately(selectedAIVsAISimulationSettings.certaintyThreshold, parsedValue))
            {
                selectedAIVsAISimulationSettings.certaintyThreshold = parsedValue;
                MarkAIVsAiSimulationSettingsAsManual();
            }
        }

        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAIVsAiMinimumGamesChanged(ChangeEvent<string> evt)
    {
        if (TryParseInt(evt != null ? evt.newValue : null, out int parsedValue))
        {
            if (selectedAIVsAISimulationSettings.minimumGames != parsedValue)
            {
                selectedAIVsAISimulationSettings.minimumGames = parsedValue;
                MarkAIVsAiSimulationSettingsAsManual();
            }
        }

        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAIVsAiTimeBudgetMinutesChanged(ChangeEvent<string> evt)
    {
        if (TryParseFloat(evt != null ? evt.newValue : null, out float parsedMinutes))
        {
            float parsedSeconds = Mathf.Max(0f, parsedMinutes) * 60f;
            if (!Mathf.Approximately(selectedAIVsAISimulationSettings.timeBudgetSeconds, parsedSeconds))
            {
                selectedAIVsAISimulationSettings.timeBudgetSeconds = parsedSeconds;
                MarkAIVsAiSimulationSettingsAsManual();
            }
        }

        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAIVsAiBatchSizeChanged(ChangeEvent<string> evt)
    {
        if (TryParseInt(evt != null ? evt.newValue : null, out int parsedValue))
        {
            if (selectedAIVsAISimulationSettings.batchSize != parsedValue)
            {
                selectedAIVsAISimulationSettings.batchSize = parsedValue;
                MarkAIVsAiSimulationSettingsAsManual();
            }
        }

        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAIVsAiEmergencyHardMaxGamesChanged(ChangeEvent<string> evt)
    {
        if (TryParseInt(evt != null ? evt.newValue : null, out int parsedValue))
        {
            if (selectedAIVsAISimulationSettings.emergencyHardMaxGames != parsedValue)
            {
                selectedAIVsAISimulationSettings.emergencyHardMaxGames = parsedValue;
                MarkAIVsAiSimulationSettingsAsManual();
            }
        }

        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAIVsAiBatchSpeedNormalClicked()
    {
        selectedAIVsAIBatchSpeedPreset = TurnManager.AIVsAIBatchSpeedPreset.Normal;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAIVsAiBatchSpeedFastClicked()
    {
        selectedAIVsAIBatchSpeedPreset = TurnManager.AIVsAIBatchSpeedPreset.Fast;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAIVsAiBatchSpeedVeryFastClicked()
    {
        selectedAIVsAIBatchSpeedPreset = TurnManager.AIVsAIBatchSpeedPreset.VeryFast;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAIVsAiBatchSpeedUltraFastClicked()
    {
        selectedAIVsAIBatchSpeedPreset = TurnManager.AIVsAIBatchSpeedPreset.UltraFast;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsStoreSnapshotHistoryClicked()
    {
        if (selectedEnableAIVsAIDebugMode)
        {
            return;
        }

        selectedStoreSnapshotHistory = !selectedStoreSnapshotHistory;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsWatchAIVsAIClicked()
    {
        selectedEnableAIVsAIDebugMode = !selectedEnableAIVsAIDebugMode;
        if (selectedEnableAIVsAIDebugMode)
        {
            selectedStoreSnapshotHistory = false;
        }

        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAIVsAiModeHeadToHeadClicked()
    {
        selectedAIVsAISimulationSettings.mode = AIVsAIBatchRunController.SimulationMode.HeadToHead;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAIVsAiModeTournamentClicked()
    {
        selectedAIVsAISimulationSettings.mode = AIVsAIBatchRunController.SimulationMode.Tournament;
        RefreshGeneralSettingsSelectionState();
    }

    private void ToggleSideAFeature(AILocalDecisionFeatures feature)
    {
        selectedSideAFeatures ^= feature;
        RefreshGeneralSettingsSelectionState();
    }

    private void ToggleSideBFeature(AILocalDecisionFeatures feature)
    {
        selectedSideBFeatures ^= feature;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAIVsAiTournamentTypeRoundRobinClicked()
    {
        selectedAIVsAISimulationSettings.tournamentType = AIVsAIBatchRunController.TournamentType.RoundRobin;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAIVsAiTournamentRunContinuouslyClicked()
    {
        selectedAIVsAISimulationSettings.tournamentRunContinuously = !selectedAIVsAISimulationSettings.tournamentRunContinuously;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAIVsAiTournamentSeatSwapClicked()
    {
        selectedAIVsAISimulationSettings.tournamentSeatSwap = !selectedAIVsAISimulationSettings.tournamentSeatSwap;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAIVsAiTournamentSelectFullPoolClicked()
    {
        selectedAIVsAISimulationSettings.tournamentParticipantMask =
            AIVsAIBatchRunController.GetDefaultTournamentParticipantMask();
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsAIVsAiTournamentGamesPerPairingChanged(ChangeEvent<string> evt)
    {
        if (TryParseInt(evt != null ? evt.newValue : null, out int parsedValue))
        {
            if (selectedAIVsAISimulationSettings.tournamentGamesPerPairing != parsedValue)
            {
                selectedAIVsAISimulationSettings.tournamentGamesPerPairing = parsedValue;
            }
        }

        RefreshGeneralSettingsSelectionState();
    }

    private void ToggleTournamentParticipant(int generatedVariantIndex)
    {
        if (generatedVariantIndex < 0 || generatedVariantIndex >= AIVsAIBatchRunController.GetGeneratedVariantCount())
        {
            return;
        }

        selectedAIVsAISimulationSettings.tournamentParticipantMask ^= 1 << generatedVariantIndex;
        RefreshGeneralSettingsSelectionState();
    }

    private void HandleGeneralSettingsConfirmClicked()
    {
        if (mainMenuController == null)
        {
            return;
        }

        bool started = false;
        if (pendingGeneralSettingsMode == PendingGeneralSettingsMode.VsAI)
        {
            CommitAIVsAiSimulationTextInputs();
            mainMenuController.StartVsAIGameWithSettings(
                selectedMapSizePreset,
                selectedAIRecruitVariant,
                selectedStoreSnapshotHistory,
                selectedEnableAIVsAIDebugMode,
                selectedAIVsAIBatchSpeedPreset,
                selectedSideAAIRecruitVariant,
                selectedSideBAIRecruitVariant,
                selectedSideAFeatures,
                selectedSideBFeatures,
                TurnManager.AIDebugProfile.Baseline,
                TurnManager.AIDebugProfile.Baseline,
                selectedAIVsAISimulationSettings);
            started = true;
        }
        else if (pendingGeneralSettingsMode == PendingGeneralSettingsMode.PlayByPost)
        {
            started = mainMenuController.StartPlayByPostGameWithSettings(
                selectedMapSizePreset,
                selectedStoreSnapshotHistory,
                selectedPlayByPostPlayerCount);
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

    private void HandleProfileNewTitleClicked()
    {
        profileData = LocalPlayerProfileStore.RegenerateTitle();
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
        LocalPlayerProfileStore.TypedDisplayNameValidationResult validationResult =
            CommitOrRestoreTypedDisplayNameInput(showValidationFeedback: true);
        PlayerPrefs.Save();

        if (validationResult != LocalPlayerProfileStore.TypedDisplayNameValidationResult.Valid)
        {
            RefreshProfileBackButtonLabel();
            return;
        }

        bool returnToMultiplayer = profileOpenedFromMultiplayerRedirect &&
            LocalPlayerProfileStore.HasRecognizableTypedDisplayName(profileData.TypedDisplayName);
        profileOpenedFromMultiplayerRedirect = false;
        RefreshProfileBackButtonLabel();

        if (returnToMultiplayer)
        {
            OpenMultiplayerPanelOrRedirect(requestControllerOpen: true);
            return;
        }

        ShowMainPanel();
    }

    private void HandleProfileTypedDisplayNameChanged(ChangeEvent<string> evt)
    {
        profileTypedDisplayNameDraft = LocalPlayerProfileStore.NormalizeTypedDisplayName(evt.newValue);
        if (profileTypedDisplayNameInput != null &&
            !string.Equals(profileTypedDisplayNameInput.value, profileTypedDisplayNameDraft, System.StringComparison.Ordinal))
        {
            profileTypedDisplayNameInput.SetValueWithoutNotify(profileTypedDisplayNameDraft);
        }

        if (profileTypedDisplayNameOverflowAttempted &&
            profileTypedDisplayNameDraft.Length >= ProfileUsernameGenerator.MaxUsernameLength)
        {
            ShowTypedDisplayNameValidation(LocalPlayerProfileStore.TypedDisplayNameValidationResult.TooLong);
            return;
        }

        profileTypedDisplayNameOverflowAttempted = false;
        ResetTypedDisplayNameHelperLabel();
        RefreshProfilePublicUsernamePreview();
    }

    private void HandleProfileTypedDisplayNameKeyDown(KeyDownEvent evt)
    {
        if (evt == null || profileTypedDisplayNameInput == null)
        {
            return;
        }

        if (!IsProfileTypedDisplayNameOverflowAttempt(evt))
        {
            return;
        }

        profileTypedDisplayNameOverflowAttempted = true;
        ShowTypedDisplayNameValidation(LocalPlayerProfileStore.TypedDisplayNameValidationResult.TooLong);
    }

    private void HandleProfileTypedDisplayNameFocusOut(FocusOutEvent evt)
    {
        // Keep profile name edits as draft-only until the player explicitly
        // confirms with Continue/Back.
    }

    private LocalPlayerProfileStore.TypedDisplayNameValidationResult CommitOrRestoreTypedDisplayNameInput(bool showValidationFeedback)
    {
        if (profileTypedDisplayNameInput == null)
        {
            return LocalPlayerProfileStore.TypedDisplayNameValidationResult.Valid;
        }

        profileTypedDisplayNameDraft = LocalPlayerProfileStore.NormalizeTypedDisplayName(profileTypedDisplayNameInput.value);
        LocalPlayerProfileStore.TypedDisplayNameValidationResult validationResult =
            LocalPlayerProfileStore.GetTypedDisplayNameValidationResult(profileTypedDisplayNameInput.value);

        if (validationResult == LocalPlayerProfileStore.TypedDisplayNameValidationResult.Valid)
        {
            string committedTypedDisplayName = LocalPlayerProfileStore.SetTypedDisplayName(profileTypedDisplayNameDraft);
            if (!string.Equals(profileData.TypedDisplayName, committedTypedDisplayName, System.StringComparison.Ordinal))
            {
                profileData.TypedDisplayName = committedTypedDisplayName;
            }

            profileTypedDisplayNameOverflowAttempted = false;
            ResetTypedDisplayNameHelperLabel();
            RefreshProfilePublicUsernamePreview();
            ClearProfileStatus();
        }
        else
        {
            if (showValidationFeedback)
            {
                ShowTypedDisplayNameValidation(validationResult);
            }
        }

        profileTypedDisplayNameInput.SetValueWithoutNotify(profileTypedDisplayNameDraft);
        return validationResult;
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
                              mainMenuController.GetPlayByPostTurnStateKindForMenu(summary) ==
                              MainMenuController.PlayByPostMenuTurnStateKind.YourTurn;
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
        profileOpenedFromMultiplayerRedirect = false;
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
        UpdateRootScreenClass(profileVisible: false);
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
        profileOpenedFromMultiplayerRedirect = false;
        ResetActiveGamesElasticOffset();
        HideGeneralSettingsPanel();
        SetDetailsConfirmState(false);
        HideJoinPanel();
        ClearProfileStatus();
        SetVisible(mainPanel, false);
        SetVisible(multiplayerPanel, true);
        SetVisible(profilePanel, false);
        UpdateRootScreenClass(profileVisible: false);
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

    private void OpenMultiplayerPanelOrRedirect(bool requestControllerOpen)
    {
        profileData = LocalPlayerProfileStore.GetOrCreateProfile();
        // Current PBp playtest rule: Multiplayer requires a manually entered recognizable
        // typed name, while the separate generated username feature stays hidden for now.
        if (!LocalPlayerProfileStore.HasRecognizableTypedDisplayName(profileData.TypedDisplayName))
        {
            profileOpenedFromMultiplayerRedirect = true;
            RefreshProfileBackButtonLabel();
            ShowProfilePanel();
            return;
        }

        if (requestControllerOpen && mainMenuController != null)
        {
            mainMenuController.OpenMultiplayerScreen();
            return;
        }

        ShowMultiplayerPanel();
        RefreshGamesList();
        RefreshMultiplayerRefreshCountdown();
        SetStatus(mainMenuController != null ? mainMenuController.CurrentImportStatus : string.Empty);
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
        profileTypedDisplayNameOverflowAttempted = false;
        ResetTypedDisplayNameHelperLabel();
        RefreshProfileBackButtonLabel();
        ClearProfileStatus();
        HideGeneralSettingsPanel();
        HideJoinPanel();
        HideDetailsPanel();
        SetVisible(mainPanel, false);
        SetVisible(multiplayerPanel, false);
        SetVisible(profilePanel, true);
        UpdateRootScreenClass(profileVisible: true);
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

    private void RefreshProfileBackButtonLabel()
    {
        if (profileBackButton == null)
        {
            return;
        }

        profileBackButton.text = ProfileContinueButtonLabel;
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
        VisualElement newTitleButtonText = profileNewTitleButton?.Q(className: "unity-button__text");
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
            DescribeElement("newTitleButtonText", newTitleButtonText) +
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
        selectedPlayByPostPlayerCount = PlayByPostSeatUtility.MinSeatCount;
        selectedAIRecruitVariant = TurnManager.AIRecruitVariant.Default;
        selectedStoreSnapshotHistory = false;
        selectedEnableAIVsAIDebugMode = false;
        selectedSideAAIRecruitVariant = TurnManager.AIRecruitVariant.Default;
        selectedSideBAIRecruitVariant = TurnManager.AIRecruitVariant.Default;
        selectedSideAFeatures = AILocalDecisionFeatures.None;
        selectedSideBFeatures = AILocalDecisionFeatures.None;
        selectedAIVsAISimulationSettings = AIVsAIBatchRunController.GetDefaultSimulationSettings();
        selectedAIVsAIBatchSpeedPreset = TurnManager.AIVsAIBatchSpeedPreset.UltraFast;
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
                : "Choose your map size and player count for this new play-by-post match.";
        }

        if (generalSettingsAiSection != null)
        {
            generalSettingsAiSection.style.display = isVsAi ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (generalSettingsPbpSection != null)
        {
            generalSettingsPbpSection.style.display = isVsAi ? DisplayStyle.None : DisplayStyle.Flex;
        }

        if (generalSettingsDevSection != null)
        {
            generalSettingsDevSection.style.display = IsDevBuild() ? DisplayStyle.Flex : DisplayStyle.None;
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
        bool isVsAi = pendingGeneralSettingsMode == PendingGeneralSettingsMode.VsAI;
        bool disableSnapshotHistory = isVsAi && selectedEnableAIVsAIDebugMode;
        bool hideGlobalAiStyle = isVsAi && selectedEnableAIVsAIDebugMode;

        if (generalSettingsAiStyleSection != null)
        {
            generalSettingsAiStyleSection.style.display = hideGlobalAiStyle
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        if (generalSettingsStoreSnapshotHistoryButton != null)
        {
            UpdateGeneralSettingsSelectionButton(
                generalSettingsStoreSnapshotHistoryButton,
                selectedStoreSnapshotHistory);
            UpdateGeneralSettingsDisabledButton(
                generalSettingsStoreSnapshotHistoryButton,
                disableSnapshotHistory);
        }

        if (generalSettingsWatchAIVsAIButton != null)
        {
            generalSettingsWatchAIVsAIButton.style.display =
                IsDevBuild() && isVsAi ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateGeneralSettingsSelectionButton(
                generalSettingsWatchAIVsAIButton,
                selectedEnableAIVsAIDebugMode);
        }

        if (generalSettingsStoreSnapshotHistoryHelperLabel != null)
        {
            generalSettingsStoreSnapshotHistoryHelperLabel.style.display =
                IsDevBuild() && disableSnapshotHistory ? DisplayStyle.Flex : DisplayStyle.None;
        }

        bool isPlayByPost = pendingGeneralSettingsMode == PendingGeneralSettingsMode.PlayByPost;
        UpdateGeneralSettingsSelectionButton(
            generalSettingsPbpPlayerCount2Button,
            isPlayByPost && selectedPlayByPostPlayerCount == 2);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsPbpPlayerCount3Button,
            isPlayByPost && selectedPlayByPostPlayerCount == 3);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsPbpPlayerCount4Button,
            isPlayByPost && selectedPlayByPostPlayerCount == 4);
        UpdateGeneralSettingsDisabledButton(
            generalSettingsPbpPlayerCount2Button,
            !isPlayByPost);
        UpdateGeneralSettingsDisabledButton(
            generalSettingsPbpPlayerCount3Button,
            true);
        UpdateGeneralSettingsDisabledButton(
            generalSettingsPbpPlayerCount4Button,
            true);

        if (generalSettingsPbpPlayerCountHelperLabel != null)
        {
            generalSettingsPbpPlayerCountHelperLabel.text =
                "2-player PBp is available now. 3- and 4-player matches stay disabled until the seat-based gameplay phase lands.";
            generalSettingsPbpPlayerCountHelperLabel.style.display =
                isPlayByPost ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (generalSettingsAIVsAIOptionsSection != null)
        {
            generalSettingsAIVsAIOptionsSection.style.display =
                IsDevBuild() && isVsAi && selectedEnableAIVsAIDebugMode
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
        }

        selectedAIVsAISimulationSettings =
            AIVsAIBatchRunController.SanitizeSimulationSettings(selectedAIVsAISimulationSettings);
        bool isTournamentMode =
            selectedAIVsAISimulationSettings.mode == AIVsAIBatchRunController.SimulationMode.Tournament;

        UpdateGeneralSettingsSelectionButton(
            generalSettingsMapSmallButton,
            selectedMapSizePreset == TurnManager.MapSizePreset.Small);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsMapLargeButton,
            selectedMapSizePreset == TurnManager.MapSizePreset.Large);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAiStyleDefaultButton,
            selectedAIRecruitVariant == TurnManager.AIRecruitVariant.Default);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAiStyleRiderFocusButton,
            selectedAIRecruitVariant == TurnManager.AIRecruitVariant.RiderFocus);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAIVsAiModeHeadToHeadButton,
            !isTournamentMode);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAIVsAiModeTournamentButton,
            isTournamentMode);
        if (generalSettingsAIVsAiHeadToHeadSection != null)
        {
            generalSettingsAIVsAiHeadToHeadSection.style.display = isTournamentMode
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        if (generalSettingsAIVsAiTournamentSection != null)
        {
            generalSettingsAIVsAiTournamentSection.style.display = isTournamentMode
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        UpdateGeneralSettingsSelectionButton(
            generalSettingsSideAAiStyleDefaultButton,
            selectedSideAAIRecruitVariant == TurnManager.AIRecruitVariant.Default);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsSideAAiStyleRiderFocusButton,
            selectedSideAAIRecruitVariant == TurnManager.AIRecruitVariant.RiderFocus);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsSideAFeatureOffenseButton,
            (selectedSideAFeatures & AILocalDecisionFeatures.OffensiveObviousWin) != 0);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsSideAFeatureExchangeButton,
            (selectedSideAFeatures & AILocalDecisionFeatures.ExchangeScoring) != 0);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsSideAFeatureDefenseButton,
            (selectedSideAFeatures & AILocalDecisionFeatures.DefensiveVeto) != 0);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsSideBAiStyleDefaultButton,
            selectedSideBAIRecruitVariant == TurnManager.AIRecruitVariant.Default);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsSideBAiStyleRiderFocusButton,
            selectedSideBAIRecruitVariant == TurnManager.AIRecruitVariant.RiderFocus);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsSideBFeatureOffenseButton,
            (selectedSideBFeatures & AILocalDecisionFeatures.OffensiveObviousWin) != 0);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsSideBFeatureExchangeButton,
            (selectedSideBFeatures & AILocalDecisionFeatures.ExchangeScoring) != 0);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsSideBFeatureDefenseButton,
            (selectedSideBFeatures & AILocalDecisionFeatures.DefensiveVeto) != 0);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAIVsAiPresetQuickButton,
            selectedAIVsAISimulationSettings.preset == AIVsAIBatchRunController.SimulationPreset.QuickExploration);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAIVsAiPresetStandardButton,
            selectedAIVsAISimulationSettings.preset == AIVsAIBatchRunController.SimulationPreset.StandardComparison);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAIVsAiPresetStrictButton,
            selectedAIVsAISimulationSettings.preset == AIVsAIBatchRunController.SimulationPreset.StrictComparison);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAIVsAiEvaluationMethodBayesianButton,
            selectedAIVsAISimulationSettings.evaluationMethod == AIVsAIBatchRunController.EvaluationMethod.Bayesian);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAIVsAiBatchSpeedNormalButton,
            selectedAIVsAIBatchSpeedPreset == TurnManager.AIVsAIBatchSpeedPreset.Normal);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAIVsAiBatchSpeedFastButton,
            selectedAIVsAIBatchSpeedPreset == TurnManager.AIVsAIBatchSpeedPreset.Fast);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAIVsAiBatchSpeedVeryFastButton,
            selectedAIVsAIBatchSpeedPreset == TurnManager.AIVsAIBatchSpeedPreset.VeryFast);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAIVsAiBatchSpeedUltraFastButton,
            selectedAIVsAIBatchSpeedPreset == TurnManager.AIVsAIBatchSpeedPreset.UltraFast);
        SetTextFieldValue(
            generalSettingsAIVsAiCertaintyThresholdInput,
            FormatAIVsAiFloat(selectedAIVsAISimulationSettings.certaintyThreshold));
        SetTextFieldValue(
            generalSettingsAIVsAiMinimumGamesInput,
            selectedAIVsAISimulationSettings.minimumGames.ToString(CultureInfo.InvariantCulture));
        SetTextFieldValue(
            generalSettingsAIVsAiTimeBudgetMinutesInput,
            FormatAIVsAiFloat(selectedAIVsAISimulationSettings.timeBudgetSeconds / 60f));
        SetTextFieldValue(
            generalSettingsAIVsAiBatchSizeInput,
            selectedAIVsAISimulationSettings.batchSize.ToString(CultureInfo.InvariantCulture));
        SetTextFieldValue(
            generalSettingsAIVsAiEmergencyHardMaxGamesInput,
            selectedAIVsAISimulationSettings.emergencyHardMaxGames.ToString(CultureInfo.InvariantCulture));
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAIVsAiTournamentTypeRoundRobinButton,
            selectedAIVsAISimulationSettings.tournamentType == AIVsAIBatchRunController.TournamentType.RoundRobin);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAIVsAiTournamentRunContinuouslyButton,
            selectedAIVsAISimulationSettings.tournamentRunContinuously);
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAIVsAiTournamentSeatSwapButton,
            selectedAIVsAISimulationSettings.tournamentSeatSwap);
        SetTextFieldValue(
            generalSettingsAIVsAiTournamentGamesPerPairingInput,
            selectedAIVsAISimulationSettings.tournamentGamesPerPairing.ToString(CultureInfo.InvariantCulture));

        bool usingFullTournamentPool =
            selectedAIVsAISimulationSettings.tournamentParticipantMask ==
            AIVsAIBatchRunController.GetDefaultTournamentParticipantMask();
        UpdateGeneralSettingsSelectionButton(
            generalSettingsAIVsAiTournamentSelectFullPoolButton,
            usingFullTournamentPool);

        int selectedTournamentParticipants = AIVsAIBatchRunController.CountTournamentParticipants(
            selectedAIVsAISimulationSettings.tournamentParticipantMask);
        for (int i = 0; i < generalSettingsAIVsAiTournamentParticipantButtons.Count; i++)
        {
            bool selected = (selectedAIVsAISimulationSettings.tournamentParticipantMask & (1 << i)) != 0;
            UpdateGeneralSettingsSelectionButton(generalSettingsAIVsAiTournamentParticipantButtons[i], selected);
        }

        if (generalSettingsAIVsAiTournamentEstimateLabel != null)
        {
            int actualMatchesPerPairing = selectedAIVsAISimulationSettings.tournamentGamesPerPairing *
                                          (selectedAIVsAISimulationSettings.tournamentSeatSwap ? 2 : 1);
            AIVsAIBatchRunController.TournamentEstimate estimate =
                AIVsAIBatchRunController.EstimateTournament(
                    selectedAIVsAISimulationSettings,
                    selectedAIVsAIBatchSpeedPreset);
            generalSettingsAIVsAiTournamentEstimateLabel.text =
                $"Estimate only: {estimate.participantCount} variants | {estimate.totalPairings} pairings | {actualMatchesPerPairing} matches/pairing | {estimate.totalGames} games | approx {FormatDurationEstimate(estimate.estimatedRuntimeSeconds)}";
        }

        if (generalSettingsAIVsAiTournamentMatchesPerPairingLabel != null)
        {
            int scheduledGamesPerPairing = selectedAIVsAISimulationSettings.tournamentGamesPerPairing;
            int actualMatchesPerPairing = scheduledGamesPerPairing *
                                          (selectedAIVsAISimulationSettings.tournamentSeatSwap ? 2 : 1);
            string seatSwapState = selectedAIVsAISimulationSettings.tournamentSeatSwap ? "ON" : "OFF";
            generalSettingsAIVsAiTournamentMatchesPerPairingLabel.text =
                $"Actual Matches Per Pairing: {actualMatchesPerPairing} (Seat Swap {seatSwapState}; {scheduledGamesPerPairing} scheduled game{(scheduledGamesPerPairing == 1 ? string.Empty : "s")} before mirroring)";
        }

        bool tournamentReady = !isTournamentMode || selectedTournamentParticipants >= 2;
        if (generalSettingsConfirmButton != null)
        {
            generalSettingsConfirmButton.SetEnabled(tournamentReady);
        }
    }

    private void ApplyAIVsAiSimulationPreset(AIVsAIBatchRunController.SimulationPreset preset)
    {
        selectedAIVsAISimulationSettings = AIVsAIBatchRunController.GetPresetSettings(preset);
        RefreshGeneralSettingsSelectionState();
    }

    private void CommitAIVsAiSimulationTextInputs()
    {
        bool changed = false;

        if (TryParseFloat(generalSettingsAIVsAiCertaintyThresholdInput != null ? generalSettingsAIVsAiCertaintyThresholdInput.value : null, out float certaintyThreshold))
        {
            if (!Mathf.Approximately(selectedAIVsAISimulationSettings.certaintyThreshold, certaintyThreshold))
            {
                selectedAIVsAISimulationSettings.certaintyThreshold = certaintyThreshold;
                changed = true;
            }
        }

        if (TryParseInt(generalSettingsAIVsAiMinimumGamesInput != null ? generalSettingsAIVsAiMinimumGamesInput.value : null, out int minimumGames))
        {
            if (selectedAIVsAISimulationSettings.minimumGames != minimumGames)
            {
                selectedAIVsAISimulationSettings.minimumGames = minimumGames;
                changed = true;
            }
        }

        if (TryParseFloat(generalSettingsAIVsAiTimeBudgetMinutesInput != null ? generalSettingsAIVsAiTimeBudgetMinutesInput.value : null, out float timeBudgetMinutes))
        {
            float timeBudgetSeconds = Mathf.Max(0f, timeBudgetMinutes) * 60f;
            if (!Mathf.Approximately(selectedAIVsAISimulationSettings.timeBudgetSeconds, timeBudgetSeconds))
            {
                selectedAIVsAISimulationSettings.timeBudgetSeconds = timeBudgetSeconds;
                changed = true;
            }
        }

        if (TryParseInt(generalSettingsAIVsAiBatchSizeInput != null ? generalSettingsAIVsAiBatchSizeInput.value : null, out int batchSize))
        {
            if (selectedAIVsAISimulationSettings.batchSize != batchSize)
            {
                selectedAIVsAISimulationSettings.batchSize = batchSize;
                changed = true;
            }
        }

        if (TryParseInt(generalSettingsAIVsAiEmergencyHardMaxGamesInput != null ? generalSettingsAIVsAiEmergencyHardMaxGamesInput.value : null, out int emergencyHardMaxGames))
        {
            if (selectedAIVsAISimulationSettings.emergencyHardMaxGames != emergencyHardMaxGames)
            {
                selectedAIVsAISimulationSettings.emergencyHardMaxGames = emergencyHardMaxGames;
                changed = true;
            }
        }

        if (TryParseInt(generalSettingsAIVsAiTournamentGamesPerPairingInput != null ? generalSettingsAIVsAiTournamentGamesPerPairingInput.value : null, out int tournamentGamesPerPairing))
        {
            if (selectedAIVsAISimulationSettings.tournamentGamesPerPairing != tournamentGamesPerPairing)
            {
                selectedAIVsAISimulationSettings.tournamentGamesPerPairing = tournamentGamesPerPairing;
                changed = true;
            }
        }

        if (changed)
        {
            MarkAIVsAiSimulationSettingsAsManual();
        }

        selectedAIVsAISimulationSettings =
            AIVsAIBatchRunController.SanitizeSimulationSettings(selectedAIVsAISimulationSettings);
    }

    private void MarkAIVsAiSimulationSettingsAsManual()
    {
        selectedAIVsAISimulationSettings.preset = AIVsAIBatchRunController.SimulationPreset.Manual;
        selectedAIVsAISimulationSettings = AIVsAIBatchRunController.SanitizeSimulationSettings(selectedAIVsAISimulationSettings);
        selectedAIVsAISimulationSettings.preset = AIVsAIBatchRunController.SimulationPreset.Manual;
    }

    private static bool TryParseInt(string rawValue, out int parsedValue)
    {
        parsedValue = 0;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        string trimmed = rawValue.Trim();
        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue) ||
               int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsedValue);
    }

    private static bool TryParseFloat(string rawValue, out float parsedValue)
    {
        parsedValue = 0f;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        string trimmed = rawValue.Trim();
        return float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedValue) ||
               float.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out parsedValue);
    }

    private static string FormatAIVsAiFloat(float value)
    {
        if (Mathf.Abs(value - Mathf.Round(value)) <= 0.0001f)
        {
            return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatDurationEstimate(float totalSeconds)
    {
        totalSeconds = Mathf.Max(0f, totalSeconds);
        if (totalSeconds >= 3600f)
        {
            int hours = Mathf.FloorToInt(totalSeconds / 3600f);
            int minutes = Mathf.FloorToInt((totalSeconds % 3600f) / 60f);
            return $"{hours}h {minutes}m";
        }

        if (totalSeconds >= 60f)
        {
            int minutes = Mathf.FloorToInt(totalSeconds / 60f);
            int seconds = Mathf.FloorToInt(totalSeconds % 60f);
            return $"{minutes}m {seconds}s";
        }

        return $"{totalSeconds:0.0}s";
    }

    private static void SetTextFieldValue(TextField textField, string value)
    {
        if (textField == null)
        {
            return;
        }

        string safeValue = value ?? string.Empty;
        if (textField.value != safeValue)
        {
            textField.SetValueWithoutNotify(safeValue);
        }
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

    private static void UpdateGeneralSettingsDisabledButton(Button button, bool disabled)
    {
        if (button == null)
        {
            return;
        }

        const string DisabledClass = "general-settings-option--disabled";
        button.SetEnabled(!disabled);

        if (disabled)
        {
            button.AddToClassList(DisabledClass);
        }
        else
        {
            button.RemoveFromClassList(DisabledClass);
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
            string serverIndicatorText = mainMenuController != null
                ? mainMenuController.GetPlayByPostServerIndicatorText()
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(serverIndicatorText))
            {
                statusText = string.IsNullOrWhiteSpace(statusText)
                    ? serverIndicatorText
                    : $"{statusText}\n{serverIndicatorText}";
            }

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
        profileTypedDisplayNameDraft = profileData.TypedDisplayName ?? string.Empty;

        if (profileUsernameValueLabel != null)
        {
            profileUsernameValueLabel.text = BuildProfilePublicUsernamePreview();
        }

        if (profileTitleValueLabel != null)
        {
            profileTitleValueLabel.text = profileData.Title ?? string.Empty;
        }

        if (profilePlayerIdValueLabel != null)
        {
            profilePlayerIdValueLabel.text = BuildVisiblePlayerId(profileData.PlayerId);
        }

        if (profileTypedDisplayNameInput != null)
        {
            profileTypedDisplayNameInput.SetValueWithoutNotify(profileTypedDisplayNameDraft);
        }
    }

    private string GetTypedDisplayNameDefaultHelperText()
    {
        return $"{LocalPlayerProfileStore.GetTypedDisplayNameLengthRangeText()} characters";
    }

    private string GetTypedDisplayNameValidationMessage(LocalPlayerProfileStore.TypedDisplayNameValidationResult validationResult)
    {
        string lengthRange = LocalPlayerProfileStore.GetTypedDisplayNameLengthRangeText();
        switch (validationResult)
        {
            case LocalPlayerProfileStore.TypedDisplayNameValidationResult.TooShort:
                return $"Too short! ({lengthRange} characters)";
            case LocalPlayerProfileStore.TypedDisplayNameValidationResult.TooLong:
                return $"Too long! ({lengthRange} characters)";
            case LocalPlayerProfileStore.TypedDisplayNameValidationResult.InvalidCharacters:
                return "Use letters and numbers only.";
            case LocalPlayerProfileStore.TypedDisplayNameValidationResult.NotRecognizable:
                return GetTypedDisplayNameDefaultHelperText();
            default:
                return GetTypedDisplayNameDefaultHelperText();
        }
    }

    private void ShowTypedDisplayNameValidation(LocalPlayerProfileStore.TypedDisplayNameValidationResult validationResult)
    {
        if (validationResult == LocalPlayerProfileStore.TypedDisplayNameValidationResult.Valid)
        {
            ResetTypedDisplayNameHelperLabel();
            return;
        }

        if (profileTypedDisplayNameHelperLabel == null)
        {
            return;
        }

        profileTypedDisplayNameHelperLabel.text = GetTypedDisplayNameValidationMessage(validationResult);
        profileTypedDisplayNameHelperLabel.EnableInClassList(ProfileTypedDisplayNameHelperErrorClass, true);
    }

    private void ResetTypedDisplayNameHelperLabel()
    {
        if (profileTypedDisplayNameHelperLabel == null)
        {
            return;
        }

        profileTypedDisplayNameHelperLabel.text = GetTypedDisplayNameDefaultHelperText();
        profileTypedDisplayNameHelperLabel.EnableInClassList(ProfileTypedDisplayNameHelperErrorClass, false);
    }

    private string BuildProfilePublicUsernamePreview()
    {
        return LocalPlayerProfileStore.FormatPublicUsername(profileTypedDisplayNameDraft, profileData.Title);
    }

    private void RefreshProfilePublicUsernamePreview()
    {
        if (profileUsernameValueLabel == null)
        {
            return;
        }

        profileUsernameValueLabel.text = BuildProfilePublicUsernamePreview();
    }

    private bool IsProfileTypedDisplayNameOverflowAttempt(KeyDownEvent evt)
    {
        if (evt.altKey || evt.ctrlKey || evt.commandKey)
        {
            return false;
        }

        if (profileTypedDisplayNameDraft.Length < ProfileUsernameGenerator.MaxUsernameLength)
        {
            return false;
        }

        if (evt.keyCode == KeyCode.Backspace ||
            evt.keyCode == KeyCode.Delete ||
            evt.keyCode == KeyCode.LeftArrow ||
            evt.keyCode == KeyCode.RightArrow ||
            evt.keyCode == KeyCode.UpArrow ||
            evt.keyCode == KeyCode.DownArrow ||
            evt.keyCode == KeyCode.Home ||
            evt.keyCode == KeyCode.End ||
            evt.keyCode == KeyCode.Tab ||
            evt.keyCode == KeyCode.Return ||
            evt.keyCode == KeyCode.KeypadEnter ||
            evt.keyCode == KeyCode.Escape)
        {
            return false;
        }

        return evt.character != 0 && !char.IsControl(evt.character);
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
        string persistentRoot = DevClientInstanceScope.GetScopedPersistentDataPath();
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

        Vector2 responsiveSize = responsiveSizeTierController.LastResponsiveSize;
        if (responsiveSize.x <= 0f || responsiveSize.y <= 0f)
        {
            Rect safeArea = Screen.safeArea;
            Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
            if (safeArea.width <= 0f || safeArea.height <= 0f)
            {
                safeArea = new Rect(0f, 0f, screenSize.x, screenSize.y);
            }

            responsiveSize = UITKResponsiveSizeTierController.ComputeResponsiveSize(safeArea.size);
        }

        root.EnableInClassList(WidePhoneMenuClass, ShouldUseWidePhoneMenuLayout(responsiveSize));
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

    private void UpdateRootScreenClass(bool profileVisible)
    {
        if (root == null)
        {
            UpdatePanelClearColor(profileVisible);
            return;
        }

        root.EnableInClassList(ProfileScreenClass, profileVisible);
        UpdatePanelClearColor(profileVisible);
    }

    private void UpdatePanelClearColor(bool profileVisible)
    {
        object panelSettings = uiDocument != null ? uiDocument.panelSettings : null;
        if (panelSettings == null)
        {
            return;
        }

        if (!hasCachedPanelClearState)
        {
            defaultPanelClearColorEnabled = ReadPanelSettingsBool(panelSettings, "clearColor", "m_ClearColor");
            defaultPanelClearColorValue = ReadPanelSettingsColor(panelSettings, "colorClearValue", "m_ColorClearValue", Color.clear);
            hasCachedPanelClearState = true;
        }

        bool clearColorEnabled = profileVisible || defaultPanelClearColorEnabled;
        Color clearColorValue = profileVisible ? ProfileScreenBackgroundColor : defaultPanelClearColorValue;

        WritePanelSettingsBool(panelSettings, clearColorEnabled, "clearColor", "m_ClearColor");
        WritePanelSettingsColor(panelSettings, clearColorValue, "colorClearValue", "m_ColorClearValue");
    }

    private static bool ReadPanelSettingsBool(object panelSettings, string propertyName, string fieldName)
    {
        if (TryGetPanelSettingsProperty(panelSettings, propertyName, out PropertyInfo propertyInfo) &&
            propertyInfo.PropertyType == typeof(bool) &&
            propertyInfo.CanRead)
        {
            return (bool)propertyInfo.GetValue(panelSettings);
        }

        if (TryGetPanelSettingsField(panelSettings, fieldName, out FieldInfo fieldInfo) &&
            fieldInfo.FieldType == typeof(bool))
        {
            return (bool)fieldInfo.GetValue(panelSettings);
        }

        return false;
    }

    private static Color ReadPanelSettingsColor(object panelSettings, string propertyName, string fieldName, Color fallback)
    {
        if (TryGetPanelSettingsProperty(panelSettings, propertyName, out PropertyInfo propertyInfo) &&
            propertyInfo.PropertyType == typeof(Color) &&
            propertyInfo.CanRead)
        {
            return (Color)propertyInfo.GetValue(panelSettings);
        }

        if (TryGetPanelSettingsField(panelSettings, fieldName, out FieldInfo fieldInfo) &&
            fieldInfo.FieldType == typeof(Color))
        {
            return (Color)fieldInfo.GetValue(panelSettings);
        }

        return fallback;
    }

    private static void WritePanelSettingsBool(object panelSettings, bool value, string propertyName, string fieldName)
    {
        if (TryGetPanelSettingsProperty(panelSettings, propertyName, out PropertyInfo propertyInfo) &&
            propertyInfo.PropertyType == typeof(bool) &&
            propertyInfo.CanWrite)
        {
            propertyInfo.SetValue(panelSettings, value);
            return;
        }

        if (TryGetPanelSettingsField(panelSettings, fieldName, out FieldInfo fieldInfo) &&
            fieldInfo.FieldType == typeof(bool))
        {
            fieldInfo.SetValue(panelSettings, value);
        }
    }

    private static void WritePanelSettingsColor(object panelSettings, Color value, string propertyName, string fieldName)
    {
        if (TryGetPanelSettingsProperty(panelSettings, propertyName, out PropertyInfo propertyInfo) &&
            propertyInfo.PropertyType == typeof(Color) &&
            propertyInfo.CanWrite)
        {
            propertyInfo.SetValue(panelSettings, value);
            return;
        }

        if (TryGetPanelSettingsField(panelSettings, fieldName, out FieldInfo fieldInfo) &&
            fieldInfo.FieldType == typeof(Color))
        {
            fieldInfo.SetValue(panelSettings, value);
        }
    }

    private static bool TryGetPanelSettingsProperty(object panelSettings, string propertyName, out PropertyInfo propertyInfo)
    {
        propertyInfo = panelSettings.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return propertyInfo != null;
    }

    private static bool TryGetPanelSettingsField(object panelSettings, string fieldName, out FieldInfo fieldInfo)
    {
        fieldInfo = panelSettings.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return fieldInfo != null;
    }

    private float ResolveMainMenuTitleBaseFontSize()
    {
        if (root.ClassListContains("ui-large"))
        {
            return MainMenuTitleLargeBaseFontSize;
        }

        // Keep smaller phone-class title sizing even when the shared responsive tier lands on regular.
        if (root.ClassListContains(WidePhoneMenuClass))
        {
            return MainMenuTitleWidePhoneBaseFontSize;
        }

        if (root.ClassListContains("ui-regular"))
        {
            return MainMenuTitleRegularBaseFontSize;
        }

        return MainMenuTitleCompactBaseFontSize;
    }

    private static bool ShouldUseWidePhoneMenuLayout(Vector2 responsiveSize)
    {
        float shortestSide = Mathf.Min(responsiveSize.x, responsiveSize.y);
        float usableHeight = Mathf.Max(responsiveSize.x, responsiveSize.y);
        return shortestSide >= WidePhoneShortestSideMin && usableHeight >= WidePhoneHeightMin;
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
        generalSettingsAiStyleSection = null;
        generalSettingsPbpSection = null;
        generalSettingsDevSection = null;
        generalSettingsAIVsAIOptionsSection = null;
        generalSettingsAIVsAiHeadToHeadSection = null;
        generalSettingsAIVsAiTournamentSection = null;
        generalSettingsAIVsAiTournamentParticipantPool = null;
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
        profileTitleValueLabel = null;
        profilePlayerIdValueLabel = null;
        profileTypedDisplayNameHelperLabel = null;
        profileStatusLabel = null;
        profileTypedDisplayNameInput = null;
        createSuccessGameCodeLabel = null;
        generalSettingsTitleLabel = null;
        generalSettingsSubtitleLabel = null;
        generalSettingsPbpPlayerCountHelperLabel = null;
        generalSettingsStoreSnapshotHistoryHelperLabel = null;
        generalSettingsAIVsAiTournamentEstimateLabel = null;
        generalSettingsAIVsAiTournamentMatchesPerPairingLabel = null;
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
        generalSettingsPbpPlayerCount2Button = null;
        generalSettingsPbpPlayerCount3Button = null;
        generalSettingsPbpPlayerCount4Button = null;
        generalSettingsAiStyleDefaultButton = null;
        generalSettingsAiStyleRiderFocusButton = null;
        generalSettingsStoreSnapshotHistoryButton = null;
        generalSettingsWatchAIVsAIButton = null;
        generalSettingsAIVsAiModeHeadToHeadButton = null;
        generalSettingsAIVsAiModeTournamentButton = null;
        generalSettingsSideAAiStyleDefaultButton = null;
        generalSettingsSideAAiStyleRiderFocusButton = null;
        generalSettingsSideAFeatureOffenseButton = null;
        generalSettingsSideAFeatureExchangeButton = null;
        generalSettingsSideAFeatureDefenseButton = null;
        generalSettingsSideBAiStyleDefaultButton = null;
        generalSettingsSideBAiStyleRiderFocusButton = null;
        generalSettingsSideBFeatureOffenseButton = null;
        generalSettingsSideBFeatureExchangeButton = null;
        generalSettingsSideBFeatureDefenseButton = null;
        generalSettingsAIVsAiPresetQuickButton = null;
        generalSettingsAIVsAiPresetStandardButton = null;
        generalSettingsAIVsAiPresetStrictButton = null;
        generalSettingsAIVsAiEvaluationMethodBayesianButton = null;
        generalSettingsAIVsAiBatchSpeedNormalButton = null;
        generalSettingsAIVsAiBatchSpeedFastButton = null;
        generalSettingsAIVsAiBatchSpeedVeryFastButton = null;
        generalSettingsAIVsAiBatchSpeedUltraFastButton = null;
        generalSettingsAIVsAiTournamentTypeRoundRobinButton = null;
        generalSettingsAIVsAiTournamentRunContinuouslyButton = null;
        generalSettingsAIVsAiTournamentSeatSwapButton = null;
        generalSettingsAIVsAiTournamentSelectFullPoolButton = null;
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
        profileNewTitleButton = null;
        profileCopyPlayerIdButton = null;
        profileBackButton = null;
        generalSettingsAIVsAiCertaintyThresholdInput = null;
        generalSettingsAIVsAiMinimumGamesInput = null;
        generalSettingsAIVsAiTimeBudgetMinutesInput = null;
        generalSettingsAIVsAiBatchSizeInput = null;
        generalSettingsAIVsAiEmergencyHardMaxGamesInput = null;
        generalSettingsAIVsAiTournamentGamesPerPairingInput = null;
        generalSettingsAIVsAiTournamentParticipantButtons.Clear();
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
