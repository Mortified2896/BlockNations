using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UnityEngine.UI;

/// <summary>
/// Hook this to your MainMenu scene Canvas/buttons to load the gameplay scene
/// with the selected mode.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    public struct PendingAIVsAISettingsReturn
    {
        public TurnManager.MapSizePreset mapSizePreset;
        public TurnManager.AIRecruitVariant recruitVariant;
        public bool storeSnapshotHistory;
        public bool enableAIVsAIDebugMode;
        public TurnManager.AIVsAIBatchSpeedPreset aiVsAiBatchSpeedPreset;
        public TurnManager.AIRecruitVariant sideARecruitVariant;
        public TurnManager.AIRecruitVariant sideBRecruitVariant;
        public AILocalDecisionFeatures sideAFeatures;
        public AILocalDecisionFeatures sideBFeatures;
        public TurnManager.AIDebugProfile sideAProfile;
        public TurnManager.AIDebugProfile sideBProfile;
        public AIVsAIBatchRunController.SimulationSettings aiVsAiSimulationSettings;
    }

    [Header("Scenes")]
    [SerializeField] private string gameplaySceneName = "SampleScene";

    [SerializeField] private GameObject modeSelectionPanel;
    [SerializeField] private GameObject aiDifficultyPanel;
    [SerializeField] private string selectedGameId;

    [Header("Layout")]
    [SerializeField] private bool autoFitMenuToScreenOnDesktop = true;
    private const string PlayByPostGameIdKeyRaw = "pbp_gameId";
    private const string PlayByPostForceNewKeyRaw = "pbp_forceNew";
    private const string PlayByPostPendingNewGameIdKeyRaw = "pbp_pendingNewGameId";
    private const string PendingCreateShareReadyGameIdKeyRaw = "ui_pbp_createShareReadyGameId";
    private const string ReturnToMultiplayerPaneKeyRaw = "ui_returnToMultiplayerPane";
    private const string SinglePlayerPrimarySaveFileName = "save_sp.json";
    private const string LegacySharedSaveFileName = "save.json";
    private const string PbpVersionVerificationFailedMessage = "Unable to verify this game's PbP version. For safety, this match cannot be opened on this build.";
    private const string PbpActiveGameUpdateRequiredCardText = "Requires matching version";
    private const string PbpJoinFullMessage = "Can't join: this game is already full.";
    private const string PbpStagingBaseUrl = "https://staging.blocknations.moneymattersmedia.com";
    private const float RemoteTurnStatusFetchCooldownSeconds = 10f;
    private const float MenuClosedRefreshIntervalSeconds = 60f;
    private const float MenuOpenRefreshIntervalSeconds = 10f;
    private const float MenuResumeImmediateRefreshStaleAfterSeconds = 15f;
    private const float MenuRefreshLoopTickSeconds = 1f;
    private const float MenuClosedFailureBackoffMaxSeconds = 180f;
    private const float MenuOpenFailureBackoffMaxSeconds = 60f;
    private static readonly Regex PbpGameIdCandidateRegex = new Regex(
        @"(?<![0-9A-Fa-f])(?:\{[0-9A-Fa-f]{8}(?:-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\}|\([0-9A-Fa-f]{8}(?:-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\)|[0-9A-Fa-f]{8}(?:-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}|[0-9A-Fa-f]{32})(?![0-9A-Fa-f])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static string PlayByPostGameIdKey => DevClientInstanceScope.ScopePlayerPrefsKey(PlayByPostGameIdKeyRaw);
    private static string PlayByPostForceNewKey => DevClientInstanceScope.ScopePlayerPrefsKey(PlayByPostForceNewKeyRaw);
    private static string PlayByPostPendingNewGameIdKey => DevClientInstanceScope.ScopePlayerPrefsKey(PlayByPostPendingNewGameIdKeyRaw);
    private static string PendingCreateShareReadyGameIdKey => DevClientInstanceScope.ScopePlayerPrefsKey(PendingCreateShareReadyGameIdKeyRaw);
    private static string ReturnToMultiplayerPaneKey => DevClientInstanceScope.ScopePlayerPrefsKey(ReturnToMultiplayerPaneKeyRaw);
    private bool isServerOnline = true;
    private bool joinProbeInProgress;
    private bool resignSubmitInFlight;
    private Coroutine serverCheckRoutine;
    private Coroutine menuRefreshLoopRoutine;
    private HttpTurnTransport cachedHttpTransport;
    private string latestServerStatusText = string.Empty;

    public event Action ActivePbpGamesChanged;
    public event Action<string> ImportStatusChanged;
    public event Action PbpBadgeChanged;
    public event Action MultiplayerScreenRequested;
    public event Action<string> MultiplayerCreateSucceeded;
    public IReadOnlyList<SaveManifestService.ManifestGameSummary> ActivePbpGames => activePbpGames;
    public IReadOnlyList<SaveManifestService.ManifestGameSummary> ArchivedPbpGames => archivedPbpGames;
    public string CurrentImportStatus { get; private set; } = string.Empty;
    public int PbpBadgeCountMyTurn { get; private set; }
    public bool IsMultiplayerScreenRequested { get; private set; }
    private List<SaveManifestService.ManifestGameSummary> activePbpGames = new List<SaveManifestService.ManifestGameSummary>();
    private List<SaveManifestService.ManifestGameSummary> archivedPbpGames = new List<SaveManifestService.ManifestGameSummary>();
    private string pendingCreateShareReadyGameId;
    private SaveManifestService.ManifestGameSummary selectedPbpGame;
    private bool hasSelectedPbpGame;
    private bool remoteTurnStatusFetchInFlight;
    private float remoteTurnStatusLastFetchRealtime = -1000f;
    private string remoteTurnStatusLastRequestSignature = string.Empty;
    private int remoteTurnStatusRequestSerial;
    private readonly Dictionary<string, RemoteTurnStatusOverlay> remoteTurnStatusByGameId =
        new Dictionary<string, RemoteTurnStatusOverlay>(StringComparer.Ordinal);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static readonly HashSet<string> PbpSnapshotReadWarningLoggedGameIds = new HashSet<string>();
    private static readonly HashSet<string> PbpMissingCanonicalSeatMetadataWarningKeys = new HashSet<string>();
#endif

    private enum MenuRefreshMode
    {
        Inactive,
        ClosedPane,
        OpenPane
    }

    private sealed class MenuRefreshRuntimeState
    {
        public MenuRefreshMode Mode = MenuRefreshMode.Inactive;
        public float LastAttemptRealtime = -1000f;
        public float LastSuccessRealtime = -1000f;
        public float NextAllowedPullRealtime;
        public int ConsecutiveFailureCount;
        public bool IsFetchInFlight;
        public string LastRequestSignature = string.Empty;
    }

    private static readonly MenuRefreshRuntimeState SharedMenuRefreshState = new MenuRefreshRuntimeState();
    private static bool hasPendingAIVsAISettingsReturn;
    private static PendingAIVsAISettingsReturn pendingAIVsAISettingsReturn;

    private readonly struct RemoteTurnStatusOverlay
    {
        public readonly bool HasNewerThanKnown;
        public readonly int TurnSeat;
        public readonly int LatestSeq;

        public RemoteTurnStatusOverlay(bool hasNewerThanKnown, int turnSeat, int latestSeq)
        {
            HasNewerThanKnown = hasNewerThanKnown;
            TurnSeat = turnSeat;
            LatestSeq = latestSeq;
        }
    }

    public enum PlayByPostMenuTurnStateKind
    {
        Unknown,
        YourTurn,
        Waiting,
        GameOver
    }

    private readonly struct PlayByPostMenuTurnStateResult
    {
        public readonly PlayByPostMenuTurnStateKind Kind;
        public readonly int CurrentTurnSeatIndex;

        public PlayByPostMenuTurnStateResult(PlayByPostMenuTurnStateKind kind, int currentTurnSeatIndex)
        {
            Kind = kind;
            CurrentTurnSeatIndex = currentTurnSeatIndex;
        }
    }

    private static bool ShouldLogRemoteTurnStatusDiagnostics()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return PbpDebugSettingsLoader.EnableSaveLoadLogs || PbpDebugSettingsLoader.EnableTransportLogs;
#else
        return false;
#endif
    }

    private static void LogRemoteTurnStatusDiagnostics(string message)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!ShouldLogRemoteTurnStatusDiagnostics())
        {
            return;
        }

        Debug.Log(message);
#endif
    }

    public static void SetPendingAIVsAISettingsReturn(PendingAIVsAISettingsReturn settings)
    {
        pendingAIVsAISettingsReturn = settings;
        hasPendingAIVsAISettingsReturn = true;
    }

    public static bool TryConsumePendingAIVsAISettingsReturn(out PendingAIVsAISettingsReturn settings)
    {
        settings = pendingAIVsAISettingsReturn;
        bool hadPending = hasPendingAIVsAISettingsReturn;
        hasPendingAIVsAISettingsReturn = false;
        return hadPending;
    }

    IEnumerator Start()
    {
        LocalPlayerProfileStore.GetOrCreateProfile();
        IosBadgePermissionAdapter.EnsureBadgeAuthorizationRequested();
        pendingCreateShareReadyGameId = PlayerPrefs.GetString(PendingCreateShareReadyGameIdKey, string.Empty);

        bool returnToMultiplayerPane = ConsumeReturnToMultiplayerPaneFlag();

        // Wait one frame so UI objects/panels are fully initialized and active state is stable.
        yield return null;
        if (autoFitMenuToScreenOnDesktop)
        {
            // One more frame so layouts are rebuilt after menus are activated.
            yield return null;
            TryAutoFitActiveMenuButtonContainer();
        }

        if (returnToMultiplayerPane)
        {
            ApplyVisibleMenuPaneState(multiplayerVisible: true, resetRefreshWindow: true);
        }
        else
        {
            ApplyVisibleMenuPaneState(multiplayerVisible: false, resetRefreshWindow: true);
        }

        RefreshMultiplayerList();
        SyncAppIconBadge(force: true);

        if (returnToMultiplayerPane)
        {
            OpenMultiplayerScreen();
        }
    }

    private void OnDisable()
    {
        StopMenuRefreshLoop();
        SharedMenuRefreshState.IsFetchInFlight = false;
    }

    private void OnApplicationPause(bool paused)
    {
        if (!paused)
        {
            HandleMenuAppResume();
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            HandleMenuAppResume();
        }
    }

    public void NewGame()
    {
        if (modeSelectionPanel != null)
        {
            modeSelectionPanel.SetActive(true);
        }
        else
        {
            // Fallback: default to Vs AI if no panel is assigned.
            PlayVsAI();
        }
    }

    public void PlayVsAI()
    {
        StartVsAIGame(TurnManager.GetDefaultMapSizePreset());
    }

    // Legacy button hooks retained so older scene wiring still launches the baseline AI.
    public void PlayVsAI_Level1()
    {
        StartVsAIGame(TurnManager.GetDefaultMapSizePreset());
    }

    public void PlayVsAI_Level2()
    {
        StartVsAIGame(TurnManager.GetDefaultMapSizePreset());
    }

    public void PlayVsAI_Level3()
    {
        StartVsAIGame(TurnManager.GetDefaultMapSizePreset());
    }

    public void PlayVsAI_Unfair()
    {
        StartVsAIGame(TurnManager.GetDefaultMapSizePreset());
    }

    public void StartVsAIGameWithSettings(
        TurnManager.MapSizePreset mapSizePreset,
        TurnManager.AIRecruitVariant recruitVariant = TurnManager.AIRecruitVariant.Default,
        bool storeSnapshotHistory = false,
        bool enableAIVsAIDebugMode = false,
        TurnManager.AIVsAIBatchSpeedPreset aiVsAiBatchSpeedPreset = TurnManager.AIVsAIBatchSpeedPreset.Normal,
        TurnManager.AIRecruitVariant sideARecruitVariant = TurnManager.AIRecruitVariant.Default,
        TurnManager.AIRecruitVariant sideBRecruitVariant = TurnManager.AIRecruitVariant.Default,
        AILocalDecisionFeatures sideAFeatures = AILocalDecisionFeatures.None,
        AILocalDecisionFeatures sideBFeatures = AILocalDecisionFeatures.None,
        TurnManager.AIDebugProfile sideAProfile = TurnManager.AIDebugProfile.Baseline,
        TurnManager.AIDebugProfile sideBProfile = TurnManager.AIDebugProfile.Baseline,
        AIVsAIBatchRunController.SimulationSettings aiVsAiSimulationSettings = default)
    {
        StartVsAIGame(
            mapSizePreset,
            recruitVariant,
            storeSnapshotHistory,
            enableAIVsAIDebugMode,
            aiVsAiBatchSpeedPreset,
            sideARecruitVariant,
            sideBRecruitVariant,
            sideAFeatures,
            sideBFeatures,
            sideAProfile,
            sideBProfile,
            aiVsAiSimulationSettings);
    }

    private void StartVsAIGame(
        TurnManager.MapSizePreset mapSizePreset,
        TurnManager.AIRecruitVariant recruitVariant = TurnManager.AIRecruitVariant.Default,
        bool storeSnapshotHistory = false,
        bool enableAIVsAIDebugMode = false,
        TurnManager.AIVsAIBatchSpeedPreset aiVsAiBatchSpeedPreset = TurnManager.AIVsAIBatchSpeedPreset.Normal,
        TurnManager.AIRecruitVariant sideARecruitVariant = TurnManager.AIRecruitVariant.Default,
        TurnManager.AIRecruitVariant sideBRecruitVariant = TurnManager.AIRecruitVariant.Default,
        AILocalDecisionFeatures sideAFeatures = AILocalDecisionFeatures.None,
        AILocalDecisionFeatures sideBFeatures = AILocalDecisionFeatures.None,
        TurnManager.AIDebugProfile sideAProfile = TurnManager.AIDebugProfile.Baseline,
        TurnManager.AIDebugProfile sideBProfile = TurnManager.AIDebugProfile.Baseline,
        AIVsAIBatchRunController.SimulationSettings aiVsAiSimulationSettings = default)
    {
        aiVsAiSimulationSettings = AIVsAIBatchRunController.SanitizeSimulationSettings(aiVsAiSimulationSettings);
        if (enableAIVsAIDebugMode &&
            aiVsAiSimulationSettings.mode == AIVsAIBatchRunController.SimulationMode.Tournament)
        {
            AIVsAIBatchRunController.TryGetInitialTournamentMatchSettings(
                aiVsAiSimulationSettings,
                out sideARecruitVariant,
                out sideBRecruitVariant,
                out sideAFeatures,
                out sideBFeatures,
                out sideAProfile,
                out sideBProfile);
        }

        GameModeSelection.SetPendingMode(TurnManager.GameMode.VsAI);
        AIRecruitVariantSelection.SetPending(recruitVariant);
        AIVsAIDebugSelection.SetPending(
            enableAIVsAIDebugMode,
            sideARecruitVariant,
            sideBRecruitVariant,
            sideAFeatures,
            sideBFeatures,
            sideAProfile,
            sideBProfile,
            aiVsAiBatchSpeedPreset);
        if (enableAIVsAIDebugMode)
        {
            AIVsAIBatchRunController.SetPendingSimulationSettings(aiVsAiSimulationSettings);
        }
        else
        {
            AIVsAIBatchRunController.ClearAll();
        }
        MapSizeSelection.SetPending(mapSizePreset);
        SnapshotHistorySelection.SetPending(storeSnapshotHistory);
        SceneManager.LoadScene(gameplaySceneName);

        if (modeSelectionPanel != null)
        {
            modeSelectionPanel.SetActive(false);
        }

        if (aiDifficultyPanel != null)
        {
            aiDifficultyPanel.SetActive(false);
        }
    }

    private void TryAutoFitActiveMenuButtonContainer()
    {
        // On mobile portrait we want large, finger-friendly buttons; don't auto-shrink.
        // On desktop/landscape, the CanvasScaler can make vertical stacks too tall.
        if (Application.isMobilePlatform)
            return;

        if (Screen.height <= 0)
            return;

        bool isLandscape = Screen.width > Screen.height;
        if (!isLandscape && Screen.height >= 1200)
            return;

        VerticalLayoutGroup[] groups = UnityEngine.Object.FindObjectsByType<VerticalLayoutGroup>(FindObjectsInactive.Include);
        VerticalLayoutGroup best = null;
        int bestActiveButtons = 0;

        foreach (VerticalLayoutGroup g in groups)
        {
            if (g == null || !g.gameObject.activeInHierarchy)
                continue;

            int count = 0;
            for (int i = 0; i < g.transform.childCount; i++)
            {
                Transform child = g.transform.GetChild(i);
                if (child == null || !child.gameObject.activeInHierarchy)
                    continue;

                if (child.GetComponent<Button>() != null)
                    count++;
            }

            if (count > bestActiveButtons)
            {
                bestActiveButtons = count;
                best = g;
            }
        }

        if (best == null || bestActiveButtons < 4)
            return;

        RectTransform rt = best.GetComponent<RectTransform>();
        if (rt == null)
            return;

        StartCoroutine(AutoFitVerticalLayoutGroupNextFrame(rt, best));
    }

    private static IEnumerator AutoFitVerticalLayoutGroupNextFrame(RectTransform container, VerticalLayoutGroup group)
    {
        // Wait until layout is calculated.
        yield return new WaitForEndOfFrame();
        Canvas.ForceUpdateCanvases();

        if (container == null || group == null)
            yield break;

        // Keep the background/panel at full size. We only shrink spacing/button heights as needed.
        container.localScale = Vector3.one;

        float available = Screen.safeArea.height * 0.95f;
        if (available <= 0f)
            yield break;

        // Consider only visible top-level menu buttons within this container.
        List<RectTransform> buttonRects = new List<RectTransform>();
        List<LayoutElement> buttonLayoutElements = new List<LayoutElement>();
        List<TextMeshProUGUI> buttonLabels = new List<TextMeshProUGUI>();

        for (int i = 0; i < container.childCount; i++)
        {
            Transform child = container.GetChild(i);
            if (child == null || !child.gameObject.activeInHierarchy)
                continue;

            Button b = child.GetComponent<Button>();
            if (b == null)
                continue;

            RectTransform rt = child as RectTransform;
            if (rt == null)
                continue;

            buttonRects.Add(rt);

            LayoutElement le = child.GetComponent<LayoutElement>();
            buttonLayoutElements.Add(le);

            TextMeshProUGUI label = child.GetComponentInChildren<TextMeshProUGUI>(true);
            buttonLabels.Add(label);
        }

        int buttonCount = buttonRects.Count;
        if (buttonCount <= 1)
            yield break;

        float sumHeights = 0f;
        for (int i = 0; i < buttonCount; i++)
        {
            RectTransform rt = buttonRects[i];
            float h = Mathf.Max(0f, rt.rect.height);
            if (h <= 0.01f)
            {
                h = Mathf.Abs(rt.sizeDelta.y);
            }
            sumHeights += h;
        }

        float padding = group.padding.top + group.padding.bottom;
        float requiredWithCurrentSpacing = sumHeights + padding + group.spacing * (buttonCount - 1);
        if (requiredWithCurrentSpacing <= available)
            yield break;

        // 1) Reduce spacing first (cheap win).
        const float minSpacing = 12f;
        float requiredWithMinSpacing = sumHeights + padding + minSpacing * (buttonCount - 1);
        group.spacing = minSpacing;

        if (requiredWithMinSpacing <= available)
            yield break;

        // 2) Still too tall: reduce button heights (and font sizes proportionally).
        float availableForButtons = available - padding - minSpacing * (buttonCount - 1);
        if (availableForButtons <= 0f)
            yield break;

        float targetHeight = Mathf.Floor(availableForButtons / buttonCount);
        if (targetHeight <= 0f)
            yield break;

        // Determine baseline from the first button that has a reasonable height.
        float baselineHeight = 0f;
        float baselineFontSize = 0f;
        for (int i = 0; i < buttonCount; i++)
        {
            float h = Mathf.Max(0f, buttonRects[i].rect.height);
            if (h <= 0.01f) h = Mathf.Abs(buttonRects[i].sizeDelta.y);
            if (h <= 0.01f) continue;

            baselineHeight = h;
            if (buttonLabels[i] != null)
            {
                baselineFontSize = buttonLabels[i].fontSize;
            }
            break;
        }

        float scale = (baselineHeight > 0.01f) ? Mathf.Clamp01(targetHeight / baselineHeight) : 1f;

        for (int i = 0; i < buttonCount; i++)
        {
            RectTransform rt = buttonRects[i];
            Vector2 sd = rt.sizeDelta;
            sd.y = targetHeight;
            rt.sizeDelta = sd;

            LayoutElement le = buttonLayoutElements[i];
            if (le != null)
            {
                le.preferredHeight = targetHeight;
            }

            TextMeshProUGUI label = buttonLabels[i];
            if (label != null && baselineFontSize > 0.01f)
            {
                // Keep readable while shrinking on desktop.
                label.enableAutoSizing = false;
                label.fontSize = Mathf.Max(28f, baselineFontSize * scale);
            }
        }

        Canvas.ForceUpdateCanvases();
    }

    public void CloseAIDifficultyPanel()
    {
        if (aiDifficultyPanel != null)
        {
            aiDifficultyPanel.SetActive(false);
        }

        if (modeSelectionPanel != null)
        {
            modeSelectionPanel.SetActive(true);
        }
    }

    public void PlayByPost()
    {
        StartPlayByPostGameWithSettings(TurnManager.GetDefaultMapSizePreset());
    }

    public bool StartPlayByPostGameWithSettings(
        TurnManager.MapSizePreset mapSizePreset,
        bool storeSnapshotHistory = false,
        int playerCount = PlayByPostSeatUtility.MinSeatCount)
    {
        AIVsAIBatchRunController.ClearAll();

        if (!isServerOnline)
        {
            SetImportStatus(BuildConnectivityWarningStatus());
            return false;
        }

        string gameId = System.Guid.NewGuid().ToString();
        LocalPlayerSeatStore.SetSeat(gameId, 0);
        PlayerPrefs.SetInt(PlayByPostForceNewKey, 1);
        PlayerPrefs.SetString(PlayByPostPendingNewGameIdKey, gameId);
        PlayerPrefs.Save();

        GameModeSelection.SetPendingMode(TurnManager.GameMode.PlayByPost);
        MapSizeSelection.SetPending(mapSizePreset);
        SnapshotHistorySelection.SetPending(storeSnapshotHistory);
        PlayByPostSeatCountSelection.SetPending(playerCount);
        SceneManager.LoadScene(gameplaySceneName);

        if (modeSelectionPanel != null)
        {
            modeSelectionPanel.SetActive(false);
        }

        return true;
    }

    public bool TryJoinPlayByPost(string rawGameId)
    {
        return TryJoinPlayByPostInternal(rawGameId, out _);
    }

    public bool TryResolvePlayByPostJoinGameId(string rawGameId, out string normalizedGameId)
    {
        return TryNormalizeJoinGameId(rawGameId, out normalizedGameId, out _);
    }

    private bool TryJoinPlayByPostInternal(string rawGameId, out string normalizedGameId)
    {
        normalizedGameId = null;

        if (joinProbeInProgress)
        {
            SetImportStatus("Checking game compatibility...");
            return false;
        }

        if (!TryNormalizeJoinGameId(rawGameId, out normalizedGameId, out string validationError))
        {
            SetImportStatus(validationError);
            return false;
        }

        SetImportStatus("Checking game compatibility...");
        joinProbeInProgress = true;
        StartCoroutine(JoinPlayByPostAfterServerProbe(normalizedGameId));
        return true;
    }

    public void OpenMultiplayerScreen()
    {
        ApplyVisibleMenuPaneState(multiplayerVisible: true, resetRefreshWindow: true);
        MultiplayerScreenRequested?.Invoke();
        TryEmitPendingCreateSuccess();

        if (serverCheckRoutine != null)
        {
            StopCoroutine(serverCheckRoutine);
        }

        ResolveServerCheckSources();
        isServerOnline = false;
        latestServerStatusText = string.Empty;
        SetImportStatus("Checking server...");
        UpdateMultiplayerButtonStates();
        serverCheckRoutine = StartCoroutine(CheckServerOnlineCoroutine());
    }

    public void CloseMultiplayerScreen()
    {
        ApplyVisibleMenuPaneState(multiplayerVisible: false, resetRefreshWindow: true);
        // UITK handles panel visibility locally.
    }

    public void NotifyVisibleMenuPaneChanged(bool multiplayerVisible)
    {
        ApplyVisibleMenuPaneState(multiplayerVisible, resetRefreshWindow: true);
    }

    public void OpenJoinPopup()
    {
        // Legacy popup retired; joins are initiated from UITK.
    }

    public void CloseJoinPopup()
    {
        // Legacy popup retired; joins are initiated from UITK.
    }

    public void RefreshMultiplayerList()
    {
        RefreshMultiplayerListInternal(bypassRemoteCooldown: false);
    }

    public bool TryManualRefreshMultiplayerList()
    {
        if (IsMenuRefreshInFlight())
        {
            return false;
        }

        RefreshMultiplayerListInternal(bypassRemoteCooldown: true);
        return true;
    }

    private void RefreshMultiplayerListInternal(bool bypassRemoteCooldown)
    {
        ResolveServerCheckSources();
        activePbpGames = SaveManifestService.GetActivePlayByPostGames();
        archivedPbpGames = SaveManifestService.GetArchivedPlayByPostGames();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (PbpDebugSettingsLoader.EnableSaveLoadLogs)
        {
            Debug.Log($"[MP] Active PBp games={activePbpGames.Count}");
            for (int i = 0; i < activePbpGames.Count; i++)
            {
                SaveManifestService.ManifestGameSummary entry = activePbpGames[i];
                bool computed = TryGetIsYourTurnFromManifest(entry, out bool isYourTurn, out int computedTransportSeq, out string reason);
                Debug.Log(
                    $"[MP] Active[{i}] gameId={entry.gameId} entryKey={entry.entryKey} lastPlayedUtc={entry.lastPlayedUtc} isFinished={entry.isFinished} " +
                    $"lastKnownRoundTurn={entry.lastKnownRoundTurn} lastKnownIsPlayerTurn={entry.lastKnownIsPlayerTurn} computedTransportSeq={computedTransportSeq} isYourTurn={isYourTurn} computed={computed} reason={reason}");
            }

            Debug.Log($"[MP] Archived PBp games={archivedPbpGames.Count}");
            for (int i = 0; i < archivedPbpGames.Count; i++)
            {
                SaveManifestService.ManifestGameSummary entry = archivedPbpGames[i];
                Debug.Log(
                    $"[MP] Archived[{i}] gameId={entry.gameId} entryKey={entry.entryKey} lastPlayedUtc={entry.lastPlayedUtc} isFinished={entry.isFinished} isArchivedLocally={entry.isArchivedLocally}");
            }
        }
#endif
        RecomputePbpBadge();
        IosPbpBackgroundNotificationExperiment.SyncState(activePbpGames, cachedHttpTransport);
        ActivePbpGamesChanged?.Invoke();

        PbpConnectivityState connectivityState = ResolveSharedConnectivityState();
        if (connectivityState == PbpConnectivityState.Normal)
        {
            SetImportStatus(GetDefaultMultiplayerStatusText());
        }
        else
        {
            SetImportStatus(BuildConnectivityWarningStatus());
        }

        TryRefreshRemoteTurnStatusesForMenu(bypassRemoteCooldown);
        UpdateMenuRefreshLoopState();
    }

    private void TryEmitPendingCreateSuccess()
    {
        if (MultiplayerCreateSucceeded == null
            || string.IsNullOrWhiteSpace(pendingCreateShareReadyGameId))
        {
            return;
        }

        string createdGameId = pendingCreateShareReadyGameId;
        pendingCreateShareReadyGameId = string.Empty;
        PlayerPrefs.DeleteKey(PendingCreateShareReadyGameIdKey);
        PlayerPrefs.Save();
        MultiplayerCreateSucceeded?.Invoke(createdGameId);
    }

    public void RecomputePbpBadge()
    {
        int countMyTurn = 0;
        for (int i = 0; i < activePbpGames.Count; i++)
        {
            SaveManifestService.ManifestGameSummary summary = activePbpGames[i];
            if (GetPlayByPostTurnStateKindForMenu(summary) == PlayByPostMenuTurnStateKind.YourTurn)
            {
                countMyTurn++;
            }
        }

        if (PbpBadgeCountMyTurn == countMyTurn)
            return;

        PbpBadgeCountMyTurn = countMyTurn;
        SyncAppIconBadge(force: false);
        PbpBadgeChanged?.Invoke();
    }

    public void ResumePlayByPostGame(string gameId, bool returnToMultiplayerPane = false)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return;

        if (!TryReadLocalPbpSnapshotHeader(gameId, out MinimalSaveHeader localHeader))
        {
            SetImportStatus(PbpVersionVerificationFailedMessage);
            return;
        }

        if (TryGetPbpPreflightBlockWarningFromHeader(localHeader, out string versionWarning))
        {
            SetImportStatus(versionWarning);
            return;
        }

        PlayByPostSeatCountSelection.SetPending(localHeader.seatCount);
        GameModeSelection.SetPendingMode(TurnManager.GameMode.PlayByPost);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"Resuming PlayByPost game. pendingMode={TurnManager.GameMode.PlayByPost}, gameId={gameId}");
#endif
        PersistPlayByPostSelection(gameId, returnToMultiplayerPane);
        SceneManager.LoadScene(gameplaySceneName);
    }

    private void SelectPlayByPostGame(SaveManifestService.ManifestGameSummary summary)
    {
        selectedPbpGame = summary;
        hasSelectedPbpGame = true;
        selectedGameId = summary.gameId;
    }

    public void OpenSelectedGameDetails(SaveManifestService.ManifestGameSummary summary)
    {
        SelectPlayByPostGame(summary);
    }

    public void SelectPlayByPostGameForUITK(SaveManifestService.ManifestGameSummary summary)
    {
        SelectPlayByPostGame(summary);
    }

    public void CloseGameDetailsPopup()
    {
        // UITK owns details visibility; keep only data refresh behavior.
        RefreshMultiplayerList();
    }

    public void GameDetails_Open()
    {
        string gameId = hasSelectedPbpGame ? selectedPbpGame.gameId : selectedGameId;
        ResumePlayByPostGame(gameId, returnToMultiplayerPane: true);
    }

    public bool CanSendReminderForGame(SaveManifestService.ManifestGameSummary summary)
    {
        if (!PlayByPostReminderShareAdapter.ShouldShowReminderShareUi())
        {
            return false;
        }

        if (summary.isFinished || summary.isArchivedLocally || string.IsNullOrWhiteSpace(summary.gameId))
        {
            return false;
        }

        return GetPlayByPostTurnStateKindForMenu(summary) == PlayByPostMenuTurnStateKind.Waiting;
    }

    public void GameDetails_SendReminder()
    {
        if (!TryGetSelectedPbpGameSummary(out SaveManifestService.ManifestGameSummary summary) ||
            !CanSendReminderForGame(summary))
        {
            SetImportStatus("Reminder unavailable for this game.");
            return;
        }

        if (!PlayByPostReminderShareAdapter.TryPresentDefaultReminderShareSheet())
        {
            SetImportStatus("Reminder sharing is unavailable on this platform.");
            return;
        }

#if UNITY_EDITOR
        SetImportStatus("Editor preview: reminder text copied.");
#endif
    }

    public void GameDetails_ArchiveFinishedLocal()
    {
        string gameId = hasSelectedPbpGame ? selectedPbpGame.gameId : selectedGameId;
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return;
        }

        ArchiveLocalPlayByPostGame(gameId, clearActiveGameSelection: true, markFinishedLocally: true);
        RefreshMultiplayerList();
    }

    public void GameDetails_ResignLocal()
    {
        if (resignSubmitInFlight)
        {
            return;
        }

        if (!TryGetSelectedPbpGameSummary(out SaveManifestService.ManifestGameSummary summary) ||
            summary.isFinished ||
            summary.isArchivedLocally ||
            string.IsNullOrWhiteSpace(summary.gameId))
        {
            SetImportStatus("Resign unavailable for this game.");
            return;
        }

        if (GetPlayByPostTurnStateKindForMenu(summary) != PlayByPostMenuTurnStateKind.YourTurn)
        {
            SetImportStatus("Resign is only available on your turn.");
            return;
        }

        if (!TryReadLocalPbpSnapshotJson(summary.gameId, out string snapshotJson, out MinimalSaveHeader localHeader))
        {
            SetImportStatus("Resign requires the latest local game snapshot. Open the match and try again.");
            return;
        }

        if (TryGetRemoteTurnStatusOverlay(summary, out RemoteTurnStatusOverlay remoteOverlay) &&
            remoteOverlay.LatestSeq > Mathf.Max(0, localHeader.transportSeq))
        {
            SetImportStatus("A newer turn is available. Open the match and try again.");
            return;
        }

        if (!LocalPlayerSeatStore.TryGetSeat(summary.gameId, out int localSeat))
        {
            SetImportStatus("Resign unavailable: missing local seat assignment.");
            return;
        }

        ResolveServerCheckSources();
        HttpTurnTransport httpTransport = cachedHttpTransport;
        if (httpTransport == null)
        {
            SetImportStatus("Server unreachable. Can't submit resignation.");
            return;
        }

        httpTransport.Initialize();
        if (!httpTransport.IsAvailable)
        {
            SetImportStatus("Server unreachable. Can't submit resignation.");
            return;
        }

        LocalPlayerProfileStore.ProfileData profile = LocalPlayerProfileStore.GetOrCreateProfile();
        if (!TurnManager.TryBuildPlayByPostResignationJson(
                snapshotJson,
                summary.gameId,
                localSeat,
                profile.PlayerId,
                profile.TypedDisplayName,
                out string resignedJson,
                out int exportTurnNumber,
                out bool exportIsPlayerTurn,
                out int exportCurrentTurnSeatIndex,
                out int exportTransportSeq,
                out int exportSeatCount,
                out bool exportGameOver))
        {
            SetImportStatus("Resign requires the latest local turn state. Open the match and try again.");
            return;
        }

        resignSubmitInFlight = true;
        SetImportStatus("Submitting resignation...");
        StartCoroutine(SubmitPlayByPostResignation(
            summary.gameId,
            httpTransport,
            resignedJson,
            exportTurnNumber,
            exportIsPlayerTurn,
            exportCurrentTurnSeatIndex,
            exportTransportSeq,
            exportSeatCount,
            exportGameOver));
    }

    public void GameDetails_DeleteLocalCopy()
    {
        string gameId = hasSelectedPbpGame ? selectedPbpGame.gameId : selectedGameId;
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return;
        }

        DeleteLocalPlayByPostGameData(gameId, clearActiveGameSelection: true);

        selectedPbpGame = default;
        hasSelectedPbpGame = false;
        selectedGameId = string.Empty;
        CloseGameDetailsPopup();
        RefreshMultiplayerList();
    }

    public int ClearAllLocalPlayByPostGames()
    {
        HashSet<string> gameIds = new HashSet<string>(StringComparer.Ordinal);
        CollectLocalPlayByPostGameIds(SaveManifestService.GetActivePlayByPostGames(), gameIds);
        CollectLocalPlayByPostGameIds(SaveManifestService.GetArchivedPlayByPostGames(), gameIds);

        if (gameIds.Count <= 0)
        {
            ClearSelectedPlayByPostGameSelection();
            RefreshMultiplayerList();
            return 0;
        }

        int clearedCount = 0;
        foreach (string gameId in gameIds)
        {
            DeleteLocalPlayByPostGameData(gameId, clearActiveGameSelection: true);
            clearedCount++;
        }

        ClearSelectedPlayByPostGameSelection();
        RefreshMultiplayerList();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[MP] Cleared all local PBp games clearedCount={clearedCount}");
#endif
        return clearedCount;
    }

    private static void CollectLocalPlayByPostGameIds(
        IReadOnlyList<SaveManifestService.ManifestGameSummary> summaries,
        HashSet<string> gameIds)
    {
        if (summaries == null || gameIds == null)
        {
            return;
        }

        for (int i = 0; i < summaries.Count; i++)
        {
            SaveManifestService.ManifestGameSummary summary = summaries[i];
            if (string.IsNullOrWhiteSpace(summary.gameId))
            {
                continue;
            }

            gameIds.Add(summary.gameId);
        }
    }

    private void ClearSelectedPlayByPostGameSelection()
    {
        selectedPbpGame = default;
        hasSelectedPbpGame = false;
        selectedGameId = string.Empty;
    }

    private IEnumerator SubmitPlayByPostResignation(
        string gameId,
        HttpTurnTransport httpTransport,
        string resignedJson,
        int exportTurnNumber,
        bool exportIsPlayerTurn,
        int exportCurrentTurnSeatIndex,
        int exportTransportSeq,
        int exportSeatCount,
        bool exportGameOver)
    {
        bool submitOk = false;
        string submitError = null;
        yield return StartCoroutine(httpTransport.SubmitTurn(gameId, exportTransportSeq, resignedJson, (ok, err) =>
        {
            submitOk = ok;
            submitError = err;
        }));

        resignSubmitInFlight = false;

        if (!submitOk)
        {
            SetImportStatus(BuildResignationFailureStatus(submitError));
            yield break;
        }

        TryWriteLocalPbpSnapshot(gameId, resignedJson);
        SaveManifestService.RecordPlayByPostExport(
            gameId,
            httpTransport.TransportName,
            lastKnownRoundTurn: exportTurnNumber,
            lastKnownIsPlayerTurn: exportIsPlayerTurn,
            lastKnownCurrentTurnSeatIndex: exportCurrentTurnSeatIndex,
            lastKnownTransportSeq: exportTransportSeq,
            lastKnownSeatCount: exportSeatCount);
        ArchiveLocalPlayByPostGame(
            gameId,
            clearActiveGameSelection: true,
            markFinishedLocally: exportGameOver);

        selectedPbpGame = default;
        hasSelectedPbpGame = false;
        selectedGameId = string.Empty;
        CloseGameDetailsPopup();
        RefreshMultiplayerList();
        SetImportStatus(exportGameOver
            ? "Resignation submitted. Game archived."
            : "Resignation submitted. Game archived locally.");
    }

    public void ResumePlayByPostGame_FromSelected()
    {
        ResumePlayByPostGame(selectedGameId, returnToMultiplayerPane: true);
    }

    public void Multiplayer_CreateGame()
    {
        PlayByPost();
    }

    public void Multiplayer_JoinGame()
    {
        TryJoinPlayByPostInternal(GUIUtility.systemCopyBuffer, out _);
    }

    public bool ShouldShowIosDebugNotificationTrigger()
    {
        return IosDebugNotificationAdapter.IsAvailable();
    }

    public bool TriggerIosDebugNotification()
    {
        return IosDebugNotificationAdapter.TryScheduleTestNotification();
    }

    public void CopyCurrentPbpGameIdToClipboard()
    {
        string gameId = hasSelectedPbpGame ? selectedPbpGame.gameId : selectedGameId;
        if (string.IsNullOrWhiteSpace(gameId))
        {
            gameId = PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty);
        }

        if (string.IsNullOrWhiteSpace(gameId))
        {
            SetImportStatus("No game code selected.");
            return;
        }

        CopyPbpGameIdToClipboard(gameId);
    }

    public bool CopyPbpGameIdToClipboard(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            SetImportStatus("No game code selected.");
            return false;
        }

        if (!ClipboardUtility.TryCopy(gameId))
        {
            GUIUtility.systemCopyBuffer = gameId;
            Debug.LogWarning($"ClipboardUtility copy failed; fallback copy buffer used for gameId={gameId}.");
        }

        SetImportStatus($"Game code copied: {gameId}");
        return true;
    }

    public void NotifyMultiplayerUiReady()
    {
        if (!IsMultiplayerScreenRequested)
        {
            return;
        }

        TryEmitPendingCreateSuccess();
    }

    private bool TryGetSelectedPbpGameSummary(out SaveManifestService.ManifestGameSummary summary)
    {
        string gameId = hasSelectedPbpGame ? selectedPbpGame.gameId : selectedGameId;
        if (!string.IsNullOrWhiteSpace(gameId))
        {
            for (int i = 0; i < activePbpGames.Count; i++)
            {
                if (string.Equals(activePbpGames[i].gameId, gameId, StringComparison.Ordinal))
                {
                    summary = activePbpGames[i];
                    return true;
                }
            }

            for (int i = 0; i < archivedPbpGames.Count; i++)
            {
                if (string.Equals(archivedPbpGames[i].gameId, gameId, StringComparison.Ordinal))
                {
                    summary = archivedPbpGames[i];
                    return true;
                }
            }
        }

        if (hasSelectedPbpGame && !string.IsNullOrWhiteSpace(selectedPbpGame.gameId))
        {
            summary = selectedPbpGame;
            return true;
        }

        summary = default;
        return false;
    }

    public void ContinueLastSave()
    {
        string path = ResolveContinueSavePath();
        if (!System.IO.File.Exists(path))
        {
            Debug.LogWarning("No save file found at " + path + ". Continue canceled; staying in menu.");
            return;
        }

        // Peek at the save header so we can skip PlayByPost saves.
        try
        {
            string json = File.ReadAllText(path);
            MinimalSaveHeader header = JsonUtility.FromJson<MinimalSaveHeader>(json);
            if (header != null && !string.IsNullOrEmpty(header.mode))
            {
                if (header.mode == TurnManager.GameMode.PlayByPost.ToString())
                {
                    Debug.LogWarning("Last save is a Play-by-Post game. Use Import JSON instead of Continue.");
                    return;
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("Failed to inspect save header; attempting to continue anyway. " + ex.Message);
        }

        Debug.Log("Continue requested. Loading save at " + path);
        SaveLoadRequest.RequestLoad(path);
        SceneManager.LoadScene(gameplaySceneName);
    }

    private static string ResolveContinueSavePath()
    {
        string spPath = Path.Combine(GetPersistentRootPath(), SinglePlayerPrimarySaveFileName);
        if (File.Exists(spPath))
        {
            return spPath;
        }

        return Path.Combine(GetPersistentRootPath(), LegacySharedSaveFileName);
    }

    // === JSON import (paste-based) ===
    public void OpenImportPanel()
    {
        // For now we skip a dedicated panel and import directly
        // from the clipboard when the user clicks "Import JSON".
        ImportFromPastedJson();
    }

    public void CloseImportPanel()
    {
        // No-op: kept for compatibility with any existing buttons.
    }

    [System.Serializable]
    private class MinimalSaveHeader
    {
        public string gameId;
        public string mode;
        public int protocolVersion = 0;
        public string appVersion = string.Empty;
        public string mapSizePreset = string.Empty;
        public int boardWidth;
        public int boardHeight;
        public bool isPlayerTurn;
        public int turnNumber;
        public bool gameOver;
        public bool hasWinnerSeatIndex;
        public int winnerSeatIndex = -1;
        public int seatCount = PlayByPostSeatUtility.MinSeatCount;
        public int currentTurnSeatIndex;
        public int transportSeq;
        public string playerOneTypedDisplayName = string.Empty;
        public string playerTwoTypedDisplayName = string.Empty;
        public List<PlayByPostSeatMetadata> seats = new List<PlayByPostSeatMetadata>();
    }

    public void ImportFromPastedJson()
    {
        Debug.Log("ImportFromPastedJson clicked");
        // New behavior: read JSON directly from the system clipboard so players
        // can just copy from their friend and click "Import JSON" on the main menu.
        string json = GUIUtility.systemCopyBuffer;
        if (string.IsNullOrWhiteSpace(json))
        {
            SetImportStatus("Clipboard is empty. Copy a JSON save first.");
            return;
        }

        // Quick validation before writing to disk
        MinimalSaveHeader header = null;
        try
        {
            header = JsonUtility.FromJson<MinimalSaveHeader>(json);
        }
        catch (System.Exception ex)
        {
            SetImportStatus("Invalid JSON: " + ex.Message);
            return;
        }

        if (header == null || string.IsNullOrEmpty(header.mode))
        {
            SetImportStatus("JSON does not look like a save file.");
            return;
        }

        if (string.Equals(header.mode, TurnManager.GameMode.PlayByPost.ToString(), StringComparison.Ordinal) &&
            TryGetPbpPreflightBlockWarningFromHeader(header, out string importVersionWarning))
        {
            SetImportStatus(importVersionWarning);
            return;
        }

        string path = Path.Combine(GetPersistentRootPath(), "imported.json");
        try
        {
            File.WriteAllText(path, json);
        }
        catch (IOException ioEx)
        {
            SetImportStatus("Failed to write import file: " + ioEx.Message);
            return;
        }

        Debug.Log($"Importing pasted save to {path} (mode: {header.mode}, turn {header.turnNumber})");
        SaveManifestService.RecordImportedSave(header.gameId, header.mode, header.gameOver, path);
        SaveLoadRequest.RequestLoad(path);
        SceneManager.LoadScene(gameplaySceneName);
    }

    private void SetImportStatus(string message)
    {
        CurrentImportStatus = message ?? string.Empty;

        ImportStatusChanged?.Invoke(CurrentImportStatus);
    }

    // Allows runtime-built UI to register a status text field.
    public void ConfigureImportUI(GameObject panel, TMP_InputField input, TMP_Text status)
    {
        // Deprecated legacy hook kept for backwards compatibility.
    }

    public void QuitGame()
    {
        Application.Quit();
    }

    private string GetOrCreatePlayByPostGameId()
    {
        string gameId = PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty);
        if (string.IsNullOrWhiteSpace(gameId))
        {
            gameId = System.Guid.NewGuid().ToString();
            PlayerPrefs.SetString(PlayByPostGameIdKey, gameId);
        }

        return gameId;
    }

    private static bool IsPlaceholderGameId(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        string trimmed = gameId.Trim();
        return trimmed.Equals("code", System.StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("enter game code", System.StringComparison.OrdinalIgnoreCase);
    }

    private static void PersistPlayByPostSelection(string gameId, bool returnToMultiplayerPane)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return;
        }

        PlayerPrefs.DeleteKey(PlayByPostForceNewKey);
        PlayerPrefs.DeleteKey(PlayByPostPendingNewGameIdKey);
        PlayerPrefs.SetString(PlayByPostGameIdKey, gameId);
        if (returnToMultiplayerPane)
        {
            PlayerPrefs.SetInt(ReturnToMultiplayerPaneKey, 1);
        }
        PlayerPrefs.Save();
    }

    private static bool ConsumeReturnToMultiplayerPaneFlag()
    {
        if (PlayerPrefs.GetInt(ReturnToMultiplayerPaneKey, 0) != 1)
        {
            return false;
        }

        PlayerPrefs.DeleteKey(ReturnToMultiplayerPaneKey);
        PlayerPrefs.Save();
        return true;
    }

    private IEnumerator CheckServerOnlineCoroutine()
    {
        if (cachedHttpTransport == null)
        {
            ResolveServerCheckSources();
        }

        bool online;
        bool hasCheck = true;

        HttpTurnTransport httpTransport = cachedHttpTransport;
        if (httpTransport != null)
        {
            HttpTurnTransport.ServerStatusProbeResult probeResult = default;
            yield return StartCoroutine(httpTransport.CheckServerStatus(result => probeResult = result));
            online = PbpServerStatusText.IsHealthy(probeResult.Classification);
            latestServerStatusText = PbpServerStatusText.GetStatusText(probeResult.Classification);
            PbpConnectivityStateModel.ObserveServerProbeResult(probeResult.Classification);
        }
        else
        {
            online = false;
            latestServerStatusText = PbpServerStatusText.GetStatusText(
                HttpTurnTransport.ServerStatusProbeClassification.Unreachable);
            PbpConnectivityStateModel.ObserveServerProbeResult(
                HttpTurnTransport.ServerStatusProbeClassification.Unreachable);
        }

        isServerOnline = online;
        UpdateMultiplayerButtonStates();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (PbpDebugSettingsLoader.EnableTransportLogs)
        {
            Debug.Log(
                $"Multiplayer server check complete. online={isServerOnline}, checkedTransport={hasCheck}, " +
                $"statusText={latestServerStatusText}");
        }
#endif

        SetImportStatus(latestServerStatusText);
        RefreshMultiplayerList();
        serverCheckRoutine = null;
    }

    private IEnumerator JoinPlayByPostAfterServerProbe(string gameId)
    {
        try
        {
            if (cachedHttpTransport == null)
            {
                ResolveServerCheckSources();
            }

            HttpTurnTransport httpTransport = cachedHttpTransport;
            if (httpTransport == null)
            {
                SetImportStatus("Server unreachable. Can't verify game.");
                yield break;
            }

            httpTransport.Initialize();

            bool probeOk = false;
            string probeError = null;
            string probeJson = null;
            yield return StartCoroutine(httpTransport.TryFetchNextTurn(gameId, 0, (ok, err, fetchedTurn, fetchedJson) =>
            {
                probeOk = ok;
                probeError = err;
                probeJson = fetchedJson;
            }));

            HttpTurnTransport.ServerStatusProbeClassification probeClassification =
                PbpServerStatusText.ClassifyTransportResult(probeOk, probeError);
            isServerOnline = PbpServerStatusText.IsHealthy(probeClassification);
            latestServerStatusText = PbpServerStatusText.GetStatusText(probeClassification);
            PbpConnectivityStateModel.ObserveServerProbeResult(probeClassification);

            if (probeOk)
            {
                if (TryGetPbpPreflightBlockWarningFromJson(probeJson, out string joinVersionWarning))
                {
                    SetImportStatus(joinVersionWarning);
                    yield break;
                }

                LocalPlayerProfileStore.ProfileData profile = LocalPlayerProfileStore.GetOrCreateProfile();
                bool claimOk = false;
                string claimError = null;
                int claimedSeatIndex = 0;
                yield return StartCoroutine(httpTransport.ClaimSeat(
                    gameId,
                    profile.PlayerId,
                    profile.TypedDisplayName,
                    (ok, error, seatIndex, alreadyClaimed) =>
                    {
                        claimOk = ok;
                        claimError = error;
                        claimedSeatIndex = seatIndex;
                    }));

                HttpTurnTransport.ServerStatusProbeClassification claimClassification =
                    PbpServerStatusText.ClassifyTransportResult(claimOk, claimError);
                isServerOnline = PbpServerStatusText.IsHealthy(claimClassification);
                latestServerStatusText = PbpServerStatusText.GetStatusText(claimClassification);
                PbpConnectivityStateModel.ObserveServerProbeResult(claimClassification);

                if (!claimOk)
                {
                    if (string.Equals(claimError, "GAME_FULL", StringComparison.Ordinal))
                    {
                        SetImportStatus(PbpJoinFullMessage);
                        yield break;
                    }

                    SetImportStatus(BuildVerificationFailureStatus(claimError));
                    yield break;
                }

                SetImportStatus("Game found. Joining...");
                string claimedProbeJson = probeJson;
                if (!TurnManager.TryPatchClaimedPlayByPostSeatMetadataJson(
                        probeJson,
                        gameId,
                        claimedSeatIndex,
                        profile.PlayerId,
                        profile.TypedDisplayName,
                        out claimedProbeJson))
                {
                    claimedProbeJson = probeJson;
                }

                LocalPlayerSeatStore.SetSeat(gameId, claimedSeatIndex);
                TryWriteLocalPbpSnapshot(gameId, claimedProbeJson);
                PersistPlayByPostSelection(gameId, returnToMultiplayerPane: true);
                SeedPendingPlayByPostSeatCountFromJson(claimedProbeJson);
                GameModeSelection.SetPendingMode(TurnManager.GameMode.PlayByPost);
                SceneManager.LoadScene(gameplaySceneName);

                if (modeSelectionPanel != null)
                {
                    modeSelectionPanel.SetActive(false);
                }

                yield break;
            }

            if (string.Equals(probeError, TurnTelemetryConstants.NoTurn, StringComparison.Ordinal))
            {
                SetImportStatus("Game found, but no turn is available yet. Ask the host to submit the first turn.");
                yield break;
            }

            SetImportStatus(BuildVerificationFailureStatus(probeError));
        }
        finally
        {
            joinProbeInProgress = false;
        }
    }

    private void ResolveServerCheckSources()
    {
        if (cachedHttpTransport == null)
        {
            cachedHttpTransport = UnityEngine.Object.FindAnyObjectByType<HttpTurnTransport>();
        }
    }

    private void UpdateMultiplayerButtonStates()
    {
        // Legacy uGUI button state updates retired with the hidden menu canvas path.
    }

    private static bool TryValidateJoinGameId(string rawGameId, out string normalizedGameId, out string error)
    {
        normalizedGameId = string.IsNullOrWhiteSpace(rawGameId) ? null : rawGameId.Trim();
        error = null;

        if (string.IsNullOrWhiteSpace(normalizedGameId) || IsPlaceholderGameId(normalizedGameId))
        {
            error = "Enter a game code.";
            return false;
        }

        if (!Guid.TryParse(normalizedGameId, out _))
        {
            error = "Invalid game code.";
            return false;
        }

        return true;
    }

    private static bool TryNormalizeJoinGameId(string rawGameId, out string normalizedGameId, out string error)
    {
        if (TryValidateJoinGameId(rawGameId, out normalizedGameId, out error))
        {
            return true;
        }

        if (TryExtractPbpGameIdFromNoisyText(rawGameId, out string extractedGameId) &&
            TryValidateJoinGameId(extractedGameId, out normalizedGameId, out error))
        {
            return true;
        }

        normalizedGameId = null;
        return false;
    }

    private static bool TryExtractPbpGameIdFromNoisyText(string text, out string gameId)
    {
        gameId = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int bestScore = int.MinValue;
        int bestIndex = int.MaxValue;
        string bestCandidate = null;

        foreach (Match match in PbpGameIdCandidateRegex.Matches(text))
        {
            string candidate = TrimPbpGameIdDecorations(match.Value);
            if (!Guid.TryParse(candidate, out _))
            {
                continue;
            }

            int score = candidate.Length == 36 ? 100 : 90;
            score += ScorePbpGameIdContext(text, match.Index, match.Length);

            if (score > bestScore || (score == bestScore && match.Index < bestIndex))
            {
                bestScore = score;
                bestIndex = match.Index;
                bestCandidate = candidate;
            }
        }

        gameId = bestCandidate;
        return !string.IsNullOrWhiteSpace(gameId);
    }

    private static string TrimPbpGameIdDecorations(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }

        return candidate.Trim(' ', '\t', '\r', '\n', '{', '}', '(', ')', '[', ']', '<', '>', '"', '\'', '`');
    }

    private static int ScorePbpGameIdContext(string text, int index, int length)
    {
        int windowStart = Math.Max(0, index - 24);
        int windowLength = Math.Min(text.Length, index + length + 24) - windowStart;
        if (windowLength <= 0)
        {
            return 0;
        }

        string window = text.Substring(windowStart, windowLength);
        int score = 0;

        if (window.IndexOf("game code", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 12;
        }
        else if (window.IndexOf("code", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 6;
        }

        if (window.IndexOf("join", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 4;
        }

        if (window.IndexOf("invite", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 4;
        }

        return score;
    }

    private static PbpConnectivityState ResolveSharedConnectivityState()
    {
        return PbpConnectivityStateModel.Resolve(Application.internetReachability).State;
    }

    private static string BuildConnectivityWarningStatus()
    {
        return ResolveSharedConnectivityState() == PbpConnectivityState.Offline
            ? "Offline"
            : PbpServerStatusText.GetStatusText(HttpTurnTransport.ServerStatusProbeClassification.Unreachable);
    }

    private string GetDefaultMultiplayerStatusText()
    {
        if (!string.IsNullOrWhiteSpace(latestServerStatusText))
        {
            return latestServerStatusText;
        }

        if (activePbpGames.Count <= 0)
        {
            return "No active games";
        }

        if (activePbpGames.Count == 1)
        {
            return "1 active game";
        }

        return $"{activePbpGames.Count} active games";
    }

    private static string BuildVerificationFailureStatus(string transportError)
    {
        return PbpServerStatusText.BuildStatusWithContext(
            PbpServerStatusText.ClassifyTransportResult(false, transportError),
            "Can't verify game.");
    }

    private static string BuildResignationFailureStatus(string transportError)
    {
        if (string.Equals(transportError, TurnTelemetryConstants.Conflict, StringComparison.Ordinal))
        {
            return "A newer turn is available. Open the match and try again.";
        }

        return PbpServerStatusText.BuildStatusWithContext(
            PbpServerStatusText.ClassifyTransportResult(false, transportError),
            "Can't submit resignation.");
    }

    public string GetPlayByPostServerIndicatorText()
    {
        HttpTurnTransport httpTransport = cachedHttpTransport;
        if (httpTransport == null)
        {
            cachedHttpTransport = httpTransport = UnityEngine.Object.FindAnyObjectByType<HttpTurnTransport>();
        }

        if (httpTransport == null)
        {
            return string.Empty;
        }

        string configuredBaseUrl = httpTransport.EffectiveBaseUrl;
        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            return string.Empty;
        }

        string normalizedStagingBaseUrl = HttpTurnTransport.NormalizeConfiguredBaseUrl(PbpStagingBaseUrl);

        return string.Equals(configuredBaseUrl, normalizedStagingBaseUrl, StringComparison.OrdinalIgnoreCase)
            ? "PBp server: Staging"
            : "PBp server: Live";
    }

    private static string BuildGameTitle(SaveManifestService.ManifestGameSummary summary)
    {
        if (!string.IsNullOrWhiteSpace(summary.displayName))
        {
            return summary.displayName;
        }

        string generated = PbpGameDisplayNameGenerator.BuildForGameId(summary.gameId);
        if (!string.IsNullOrWhiteSpace(generated))
        {
            return generated;
        }

        return BuildLegacyGameTitle(summary.gameId);
    }

    private static string BuildLegacyGameTitle(string rawGameId)
    {
        if (string.IsNullOrWhiteSpace(rawGameId))
        {
            return "Game Unknown";
        }

        string shortId = rawGameId.Length <= 8 ? rawGameId : rawGameId.Substring(0, 8);
        return $"Game {shortId}";
    }

    public static string BuildPlayByPostTurnSubtitle(SaveManifestService.ManifestGameSummary summary)
    {
        return ResolveFallbackPlayByPostMenuTurnState(summary).Kind switch
        {
            PlayByPostMenuTurnStateKind.GameOver => "Game Over",
            PlayByPostMenuTurnStateKind.YourTurn => "Your turn",
            _ => "Waiting for opponent"
        };
    }

    public string BuildPlayByPostTurnSubtitleForMenu(SaveManifestService.ManifestGameSummary summary)
    {
        MinimalSaveHeader localHeader = null;
        if (TryReadLocalPbpSnapshotHeader(summary.gameId, out localHeader) &&
            TryGetPbpPreflightBlockWarningFromHeader(localHeader, out _))
        {
            return PbpActiveGameUpdateRequiredCardText;
        }

        PlayByPostMenuTurnStateResult turnState = ResolvePlayByPostMenuTurnStateForMenu(summary);
        if (turnState.Kind == PlayByPostMenuTurnStateKind.GameOver)
        {
            return "Game Over";
        }

        int seatCount = GetMenuSeatCount(summary, localHeader);
        if (seatCount > 2)
        {
            int waitingSeatIndex = ResolveEffectiveWaitingSeatIndexForMenu(localHeader, turnState, seatCount);
            if (waitingSeatIndex < 0)
            {
                return turnState.Kind == PlayByPostMenuTurnStateKind.YourTurn
                    ? "Your turn"
                    : "Waiting";
            }

            string waitingSeatLabel = GetSeatDisplayNameOrFallbackForMenu(localHeader, waitingSeatIndex);
            if (TryResolveSeatStateForMenu(localHeader, summary, turnState, waitingSeatIndex, out string waitingSeatState))
            {
                if (string.Equals(waitingSeatState, PlayByPostSeatUtility.SeatStateUnclaimed, StringComparison.Ordinal))
                {
                    return $"Waiting for {PlayByPostSeatUtility.BuildPlayerLabel(waitingSeatIndex)} to join";
                }
            }

            return turnState.Kind == PlayByPostMenuTurnStateKind.YourTurn
                ? "Your turn"
                : $"Waiting for {waitingSeatLabel}";
        }

        string opponentDisplayName = GetTwoPlayerOpponentDisplayNameOrFallbackForMenu(summary.gameId, localHeader, turnState);

        return turnState.Kind == PlayByPostMenuTurnStateKind.YourTurn
            ? $"Your turn against {opponentDisplayName}"
            : $"Waiting for {opponentDisplayName}";
    }

    public PlayByPostMenuTurnStateKind GetPlayByPostTurnStateKindForMenu(SaveManifestService.ManifestGameSummary summary)
    {
        return ResolvePlayByPostMenuTurnStateForMenu(summary).Kind;
    }

    public string BuildPlayByPostTurnStateForMenu(SaveManifestService.ManifestGameSummary summary)
    {
        return ResolvePlayByPostMenuTurnStateForMenu(summary).Kind switch
        {
            PlayByPostMenuTurnStateKind.GameOver => "Game Over",
            PlayByPostMenuTurnStateKind.YourTurn => "Your turn",
            _ => "Waiting for opponent"
        };
    }

    private PlayByPostMenuTurnStateResult ResolvePlayByPostMenuTurnStateForMenu(SaveManifestService.ManifestGameSummary summary)
    {
        PlayByPostMenuTurnStateResult fallback = ResolveFallbackPlayByPostMenuTurnState(summary);
        if (fallback.Kind == PlayByPostMenuTurnStateKind.GameOver)
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] subtitle gameId={summary.gameId} reason=FALLBACK_GAME_OVER fallback={fallback.Kind}");
            return fallback;
        }

        if (!IsHttpPbpGame(summary))
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] subtitle gameId={summary.gameId} reason=FALLBACK_NOT_HTTP fallback={fallback.Kind} transportType={summary.transportType ?? "<null>"} slotType={summary.slotType ?? "<null>"}");
            return fallback;
        }

        if (!remoteTurnStatusByGameId.TryGetValue(summary.gameId, out RemoteTurnStatusOverlay remote))
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] subtitle gameId={summary.gameId} reason=FALLBACK_NO_REMOTE_STATUS fallback={fallback.Kind}");
            return fallback;
        }

        if (!remote.HasNewerThanKnown)
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] subtitle gameId={summary.gameId} reason=FALLBACK_NOT_NEWER fallback={fallback.Kind} turnSeat={remote.TurnSeat}");
            return fallback;
        }

        if (remote.TurnSeat < 0 || remote.TurnSeat >= PlayByPostSeatUtility.MaxSeatCount)
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] subtitle gameId={summary.gameId} reason=FALLBACK_INVALID_TURN_SEAT fallback={fallback.Kind} turnSeat={remote.TurnSeat}");
            return fallback;
        }

        if (!LocalPlayerSeatStore.TryGetSeat(summary.gameId, out int localSeat))
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] subtitle gameId={summary.gameId} reason=FALLBACK_SEAT_MISSING fallback={fallback.Kind} turnSeat={remote.TurnSeat}");
            return fallback;
        }

        PlayByPostMenuTurnStateResult overlay = new PlayByPostMenuTurnStateResult(
            localSeat == remote.TurnSeat ? PlayByPostMenuTurnStateKind.YourTurn : PlayByPostMenuTurnStateKind.Waiting,
            remote.TurnSeat);
        string reason = overlay.Kind == PlayByPostMenuTurnStateKind.YourTurn ? "OVERLAY_YOUR_TURN" : "OVERLAY_WAITING";
        LogRemoteTurnStatusDiagnostics(
            $"[MPRemoteStatus] subtitle gameId={summary.gameId} reason={reason} overlay={overlay.Kind} localSeat={localSeat} turnSeat={remote.TurnSeat}");
        return overlay;
    }

    private static PlayByPostMenuTurnStateResult ResolveFallbackPlayByPostMenuTurnState(SaveManifestService.ManifestGameSummary summary)
    {
        if (TryGetLocalPbpSnapshotGameOver(summary.gameId, out bool snapshotGameOver) && snapshotGameOver)
        {
            return new PlayByPostMenuTurnStateResult(PlayByPostMenuTurnStateKind.GameOver, -1);
        }

        if (TryGetIsYourTurnFromManifest(summary, out bool isYourTurn, out _, out _))
        {
            int currentTurnSeatIndex = summary.lastKnownCurrentTurnSeatIndex;
            if (summary.lastKnownSeatCount > 0)
            {
                currentTurnSeatIndex = PlayByPostSeatUtility.NormalizeSeatIndex(
                    currentTurnSeatIndex,
                    summary.lastKnownSeatCount);
            }

            return new PlayByPostMenuTurnStateResult(
                isYourTurn ? PlayByPostMenuTurnStateKind.YourTurn : PlayByPostMenuTurnStateKind.Waiting,
                currentTurnSeatIndex);
        }

        return new PlayByPostMenuTurnStateResult(PlayByPostMenuTurnStateKind.Waiting, -1);
    }

    public string BuildPlayByPostDetailsSubtitleForMenu(SaveManifestService.ManifestGameSummary summary)
    {
        if (!TryReadLocalPbpSnapshotHeader(summary.gameId, out MinimalSaveHeader header))
        {
            PlayByPostMenuTurnStateResult fallbackTurnState = ResolvePlayByPostMenuTurnStateForMenu(summary);
            int fallbackSeatCount = GetMenuSeatCount(summary, null);
            string subtitle;
            if (fallbackTurnState.Kind == PlayByPostMenuTurnStateKind.GameOver)
            {
                subtitle = "Game Over";
            }
            else if (fallbackTurnState.Kind == PlayByPostMenuTurnStateKind.YourTurn)
            {
                subtitle = "Your turn";
            }
            else if (fallbackSeatCount > 2 && fallbackTurnState.CurrentTurnSeatIndex >= 0)
            {
                subtitle = $"Waiting for {PlayByPostSeatUtility.BuildPlayerLabel(fallbackTurnState.CurrentTurnSeatIndex)}";
            }
            else
            {
                subtitle = BuildPlayByPostTurnStateForMenu(summary);
            }

            return TryGetLocalPbpSnapshotProtocolVersion(summary.gameId, out _)
                ? $"{subtitle}\n{BuildPbpVersionText(summary.gameId)}"
                : subtitle;
        }

        List<string> lines = new List<string>();
        PlayByPostMenuTurnStateResult turnState = ResolvePlayByPostMenuTurnStateForMenu(summary);
        string statusLine = BuildPlayByPostDetailsStatusLineForMenu(summary, turnState, header);
        if (!string.IsNullOrWhiteSpace(statusLine))
        {
            lines.Add(statusLine);
        }

        if (TryGetMenuBoardDimensions(header, out int boardWidth, out int boardHeight))
        {
            lines.Add($"Map: {boardWidth}x{boardHeight}");
        }

        lines.Add(BuildMenuTurnLine(summary, header, turnState));

        int seatCount = GetMenuSeatCount(summary, header);
        lines.Add($"Players: {seatCount}-player");
        for (int seatIndex = 0; seatIndex < seatCount; seatIndex++)
        {
            lines.Add(BuildSeatDetailsLineForMenu(header, summary, turnState, seatIndex));
        }

        lines.Add(BuildPbpVersionText(summary.gameId));
        return string.Join("\n", lines);
    }

    private string BuildPlayByPostDetailsStatusLineForMenu(
        SaveManifestService.ManifestGameSummary summary,
        PlayByPostMenuTurnStateResult turnState,
        MinimalSaveHeader header)
    {
        if (TryGetPbpPreflightBlockWarningFromHeader(header, out _))
        {
            return PbpActiveGameUpdateRequiredCardText;
        }

        switch (turnState.Kind)
        {
            case PlayByPostMenuTurnStateKind.GameOver:
                return "Game Over";

            case PlayByPostMenuTurnStateKind.YourTurn:
                return "Your turn";

            default:
                int waitingSeatIndex = ResolveEffectiveWaitingSeatIndexForMenu(header, turnState, GetMenuSeatCount(summary, header));
                if (waitingSeatIndex < 0)
                {
                    return "Waiting";
                }

                if (TryResolveSeatStateForMenu(header, summary, turnState, waitingSeatIndex, out string waitingSeatState))
                {
                    if (string.Equals(waitingSeatState, PlayByPostSeatUtility.SeatStateUnclaimed, StringComparison.Ordinal))
                    {
                        return $"Waiting for {PlayByPostSeatUtility.BuildPlayerLabel(waitingSeatIndex)} to join";
                    }
                }

                return $"Waiting for {GetSeatDisplayNameOrFallbackForMenu(header, waitingSeatIndex)}";
        }
    }

    private static int GetMenuSeatCount(SaveManifestService.ManifestGameSummary summary, MinimalSaveHeader header)
    {
        int headerSeatCount = header != null && header.seatCount > 0
            ? PlayByPostSeatUtility.NormalizeSeatCount(header.seatCount)
            : 0;
        int summarySeatCount = summary.lastKnownSeatCount > 0
            ? PlayByPostSeatUtility.NormalizeSeatCount(summary.lastKnownSeatCount)
            : 0;
        int rawSeatCount = Mathf.Max(headerSeatCount, summarySeatCount);
        return PlayByPostSeatUtility.NormalizeSeatCount(rawSeatCount);
    }

    private string BuildMenuTurnLine(
        SaveManifestService.ManifestGameSummary summary,
        MinimalSaveHeader header,
        PlayByPostMenuTurnStateResult turnState)
    {
        return $"Turn: {Math.Max(0, GetMenuRoundTurnNumber(summary, header, turnState))}";
    }

    private int GetMenuRoundTurnNumber(
        SaveManifestService.ManifestGameSummary summary,
        MinimalSaveHeader header,
        PlayByPostMenuTurnStateResult turnState)
    {
        int roundTurnNumber = 0;
        if (summary.hasLastKnownTurnState)
        {
            roundTurnNumber = summary.lastKnownRoundTurn;
        }
        else if (header != null)
        {
            roundTurnNumber = header.turnNumber;
        }

        int effectiveTransportSeq = GetMenuKnownTransportSeq(summary, header);
        int seatCount = GetMenuSeatCount(summary, header);
        if (effectiveTransportSeq > 0 && seatCount > 0)
        {
            int derivedRoundTurn = effectiveTransportSeq / seatCount;
            roundTurnNumber = Mathf.Max(roundTurnNumber, derivedRoundTurn);
        }

        return Mathf.Max(0, roundTurnNumber);
    }

    private static bool TryGetMenuBoardDimensions(MinimalSaveHeader header, out int boardWidth, out int boardHeight)
    {
        boardWidth = 0;
        boardHeight = 0;
        if (header == null)
        {
            return false;
        }

        if (header.boardWidth > 0 && header.boardHeight > 0)
        {
            boardWidth = header.boardWidth;
            boardHeight = header.boardHeight;
            return true;
        }

        TurnManager.MapSizePreset preset = TurnManager.ParseMapSizePresetOrDefault(header.mapSizePreset);
        TurnManager.GetBoardDimensionsForPreset(preset, out boardWidth, out boardHeight);
        return boardWidth > 0 && boardHeight > 0;
    }

    private string BuildSeatDetailsLineForMenu(
        MinimalSaveHeader header,
        SaveManifestService.ManifestGameSummary summary,
        PlayByPostMenuTurnStateResult turnState,
        int seatIndex)
    {
        string seatLabel = PlayByPostSeatUtility.BuildPlayerLabel(seatIndex);
        if (!TryResolveSeatStateForMenu(header, summary, turnState, seatIndex, out string seatState))
        {
            return seatLabel;
        }

        if (string.Equals(seatState, PlayByPostSeatUtility.SeatStateResigned, StringComparison.Ordinal))
        {
            string resignedDisplayName = GetSeatDisplayNameOrFallbackForMenu(header, seatIndex);
            string resignedBaseLine = string.Equals(resignedDisplayName, seatLabel, StringComparison.Ordinal)
                ? seatLabel
                : $"{seatLabel}: {resignedDisplayName}";
            if (TryGetFinishedSeatOutcomeSuffixForMenu(header, summary, seatIndex, seatState, out string resignedFinishedOutcomeSuffix))
            {
                return $"{resignedBaseLine} {resignedFinishedOutcomeSuffix}";
            }

            return string.Equals(resignedDisplayName, seatLabel, StringComparison.Ordinal)
                ? $"{seatLabel}: Resigned"
                : $"{seatLabel}: {resignedDisplayName} (Resigned)";
        }

        if (string.Equals(seatState, PlayByPostSeatUtility.SeatStateUnclaimed, StringComparison.Ordinal))
        {
            return $"{seatLabel}: Waiting to join";
        }

        string displayName = GetSeatDisplayNameOrFallbackForMenu(header, seatIndex);
        string baseLine = string.Equals(displayName, seatLabel, StringComparison.Ordinal)
            ? seatLabel
            : $"{seatLabel}: {displayName}";

        if (TryGetFinishedSeatOutcomeSuffixForMenu(header, summary, seatIndex, seatState, out string finishedOutcomeSuffix))
        {
            return $"{baseLine} {finishedOutcomeSuffix}";
        }

        return baseLine;
    }

    private bool TryResolveSeatStateForMenu(
        MinimalSaveHeader header,
        SaveManifestService.ManifestGameSummary summary,
        PlayByPostMenuTurnStateResult turnState,
        int seatIndex,
        out string seatState)
    {
        seatState = PlayByPostSeatUtility.SeatStateUnclaimed;
        if (TryGetSeatMetadataForMenu(header, seatIndex, out PlayByPostSeatMetadata seat))
        {
            string normalizedState = PlayByPostSeatUtility.NormalizeSeatState(seat.state);
            bool hasClaimSignal =
                !string.IsNullOrWhiteSpace(seat.claimedPlayerId) ||
                !string.IsNullOrWhiteSpace(LocalPlayerProfileStore.NormalizeTypedDisplayName(seat.typedDisplayName));

            if (string.Equals(normalizedState, PlayByPostSeatUtility.SeatStateResigned, StringComparison.Ordinal))
            {
                seatState = PlayByPostSeatUtility.SeatStateResigned;
                return true;
            }

            if (string.Equals(normalizedState, PlayByPostSeatUtility.SeatStateUnclaimed, StringComparison.Ordinal) &&
                hasClaimSignal)
            {
                seatState = PlayByPostSeatUtility.SeatStateActive;
                return true;
            }

            if (!string.Equals(normalizedState, PlayByPostSeatUtility.SeatStateUnclaimed, StringComparison.Ordinal))
            {
                seatState = normalizedState;
                return true;
            }

            if (CanInferSeatClaimedFromTurnState(
                    GetMenuRoundTurnNumber(summary, header, turnState),
                    GetMenuKnownTransportSeq(summary, header),
                    GetMenuSeatCount(summary, header),
                    turnState,
                    seatIndex))
            {
                seatState = PlayByPostSeatUtility.SeatStateActive;
                return true;
            }

            seatState = PlayByPostSeatUtility.SeatStateUnclaimed;
            return true;
        }

        bool isMultiSeat = GetMenuSeatCount(summary, header) > PlayByPostSeatUtility.MinSeatCount;
        if (isMultiSeat)
        {
            LogMissingCanonicalSeatMetadataForMenu(summary.gameId, seatIndex);
            return false;
        }

        if (CanInferSeatClaimedFromTurnState(
                GetMenuRoundTurnNumber(summary, header, turnState),
                GetMenuKnownTransportSeq(summary, header),
                GetMenuSeatCount(summary, header),
                turnState,
                seatIndex))
        {
            seatState = PlayByPostSeatUtility.SeatStateActive;
            return true;
        }

        seatState = TryGetSeatTypedDisplayNameForMenu(header, seatIndex, out _)
            ? PlayByPostSeatUtility.SeatStateActive
            : PlayByPostSeatUtility.SeatStateUnclaimed;
        return true;
    }

    private static int ResolveEffectiveWaitingSeatIndexForMenu(
        MinimalSaveHeader header,
        PlayByPostMenuTurnStateResult turnState,
        int seatCount)
    {
        return PlayByPostSeatUtility.ResolveEffectiveWaitingSeatIndex(
            header != null ? header.seats : null,
            turnState.CurrentTurnSeatIndex,
            seatCount);
    }

    private int GetMenuKnownTransportSeq(
        SaveManifestService.ManifestGameSummary summary,
        MinimalSaveHeader header)
    {
        int headerTransportSeq = header != null ? Mathf.Max(0, header.transportSeq) : 0;
        int summaryTransportSeq = Mathf.Max(0, summary.lastKnownTransportSeq);
        int effectiveTransportSeq = Mathf.Max(headerTransportSeq, summaryTransportSeq);
        if (TryGetRemoteTurnStatusOverlay(summary, out RemoteTurnStatusOverlay remoteOverlay) &&
            remoteOverlay.LatestSeq > effectiveTransportSeq)
        {
            effectiveTransportSeq = remoteOverlay.LatestSeq;
        }

        return effectiveTransportSeq;
    }

    private bool TryGetRemoteTurnStatusOverlay(
        SaveManifestService.ManifestGameSummary summary,
        out RemoteTurnStatusOverlay remoteOverlay)
    {
        remoteOverlay = default;
        if (!IsHttpPbpGame(summary))
        {
            return false;
        }

        return remoteTurnStatusByGameId.TryGetValue(summary.gameId, out remoteOverlay);
    }

    private static bool CanInferSeatClaimedFromTurnState(
        int roundTurnNumber,
        int knownTransportSeq,
        int seatCount,
        PlayByPostMenuTurnStateResult turnState,
        int seatIndex)
    {
        if (turnState.Kind == PlayByPostMenuTurnStateKind.GameOver ||
            turnState.CurrentTurnSeatIndex < 0)
        {
            return false;
        }

        if (roundTurnNumber > 1)
        {
            return true;
        }

        int normalizedSeatCount = PlayByPostSeatUtility.NormalizeSeatCount(seatCount);
        if (turnState.CurrentTurnSeatIndex == 0 &&
            knownTransportSeq >= normalizedSeatCount * 2)
        {
            return true;
        }

        if (seatIndex < turnState.CurrentTurnSeatIndex)
        {
            return true;
        }

        return false;
    }

    private static bool TryGetSeatMetadataForMenu(MinimalSaveHeader header, int seatIndex, out PlayByPostSeatMetadata seatMetadata)
    {
        seatMetadata = null;
        if (header == null || header.seats == null)
        {
            return false;
        }

        for (int i = 0; i < header.seats.Count; i++)
        {
            PlayByPostSeatMetadata seat = header.seats[i];
            if (seat == null || seat.seatIndex != seatIndex)
            {
                continue;
            }

            seatMetadata = seat;
            return true;
        }

        return false;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private static void LogMissingCanonicalSeatMetadataForMenu(string gameId, int seatIndex)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        string key = $"{gameId ?? "<missing>"}:{seatIndex}";
        if (!PbpMissingCanonicalSeatMetadataWarningKeys.Add(key))
        {
            return;
        }

        Debug.LogWarning(
            $"[MP] Missing canonical PBp seat metadata for multi-seat details. gameId={(string.IsNullOrWhiteSpace(gameId) ? "<missing>" : gameId)} seatIndex={seatIndex}");
#endif
    }

    private static bool TryGetLocalPbpSnapshotGameOver(string gameId, out bool gameOver)
    {
        gameOver = false;
        if (!TryReadLocalPbpSnapshotHeader(gameId, out MinimalSaveHeader header))
        {
            return false;
        }

        gameOver = header.gameOver;
        return true;
    }

    private static string GetTwoPlayerOpponentDisplayNameOrFallbackForMenu(
        string gameId,
        MinimalSaveHeader header,
        PlayByPostMenuTurnStateResult turnState)
    {
        if (LocalPlayerSeatStore.TryGetSeat(gameId, out int localSeat) && (localSeat == 0 || localSeat == 1))
        {
            int localSeatIndex = PlayByPostSeatUtility.NormalizeSeatIndex(localSeat, PlayByPostSeatUtility.MinSeatCount);
            int opponentSeatIndex = localSeatIndex == 0 ? 1 : 0;
            return GetSeatDisplayNameOrFallbackForMenu(header, opponentSeatIndex);
        }

        int inferredOpponentSeatIndex =
            turnState.Kind == PlayByPostMenuTurnStateKind.YourTurn
                ? 1 - PlayByPostSeatUtility.NormalizeSeatIndex(turnState.CurrentTurnSeatIndex, PlayByPostSeatUtility.MinSeatCount)
                : PlayByPostSeatUtility.NormalizeSeatIndex(turnState.CurrentTurnSeatIndex, PlayByPostSeatUtility.MinSeatCount);
        int fallbackOpponentSeatIndex = Mathf.Clamp(inferredOpponentSeatIndex, 0, PlayByPostSeatUtility.MinSeatCount - 1);
        return GetSeatDisplayNameOrFallbackForMenu(header, fallbackOpponentSeatIndex);
    }

    private static bool TryGetSeatTypedDisplayNameForMenu(MinimalSaveHeader header, int seatIndex, out string typedDisplayName)
    {
        typedDisplayName = null;
        if (!TryGetSeatMetadataForMenu(header, seatIndex, out PlayByPostSeatMetadata seat))
        {
            return false;
        }

        typedDisplayName = LocalPlayerProfileStore.NormalizeTypedDisplayName(seat.typedDisplayName);
        return !string.IsNullOrWhiteSpace(typedDisplayName);
    }

    private static string GetSeatDisplayNameOrFallbackForMenu(MinimalSaveHeader header, int seatIndex)
    {
        return TryGetSeatTypedDisplayNameForMenu(header, seatIndex, out string typedDisplayName)
            ? PlayByPostSeatUtility.ResolveSeatDisplayNameOrFallback(seatIndex, typedDisplayName)
            : PlayByPostSeatUtility.BuildPlayerLabel(seatIndex);
    }

    private static bool TryGetFinishedSeatOutcomeSuffixForMenu(
        MinimalSaveHeader header,
        SaveManifestService.ManifestGameSummary summary,
        int seatIndex,
        string resolvedSeatState,
        out string suffix)
    {
        suffix = null;
        if (!summary.isFinished && (header == null || !header.gameOver))
        {
            return false;
        }

        bool hasAuthoritativeWinner =
            header != null &&
            header.hasWinnerSeatIndex &&
            header.winnerSeatIndex >= 0;
        int normalizedWinnerSeatIndex = hasAuthoritativeWinner
            ? PlayByPostSeatUtility.NormalizeSeatIndex(header.winnerSeatIndex, GetMenuSeatCount(summary, header))
            : -1;

        if (hasAuthoritativeWinner && seatIndex == normalizedWinnerSeatIndex)
        {
            suffix = "(Won)";
            return true;
        }

        if (string.Equals(resolvedSeatState, PlayByPostSeatUtility.SeatStateResigned, StringComparison.Ordinal))
        {
            suffix = "(Resigned)";
            return true;
        }

        if (string.Equals(resolvedSeatState, PlayByPostSeatUtility.SeatStateEliminated, StringComparison.Ordinal))
        {
            suffix = "(Eliminated)";
            return true;
        }

        if (hasAuthoritativeWinner &&
            !string.Equals(resolvedSeatState, PlayByPostSeatUtility.SeatStateUnclaimed, StringComparison.Ordinal))
        {
            suffix = "(Eliminated)";
            return true;
        }

        return false;
    }

    private static bool TryReadLocalPbpSnapshotJson(string gameId, out string json, out MinimalSaveHeader header)
    {
        json = null;
        header = null;
        string snapshotPath = GetPbpPerGameSnapshotPath(gameId);
        if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
        {
            return false;
        }

        try
        {
            json = File.ReadAllText(snapshotPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            header = JsonUtility.FromJson<MinimalSaveHeader>(json);
            if (header == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(header.gameId) &&
                !string.Equals(header.gameId, gameId, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string gameIdKeyForLog = string.IsNullOrWhiteSpace(gameId) ? "<missing>" : gameId;
            if (PbpSnapshotReadWarningLoggedGameIds.Add(gameIdKeyForLog))
            {
                Debug.LogWarning($"[MP] Failed to read PBp snapshot header for gameId={gameIdKeyForLog}: {ex.Message}");
            }
#endif
            return false;
        }
    }

    private static bool TryReadLocalPbpSnapshotHeader(string gameId, out MinimalSaveHeader header)
    {
        return TryReadLocalPbpSnapshotJson(gameId, out _, out header);
    }

    private static bool TryGetLocalPbpSnapshotProtocolVersion(string gameId, out int protocolVersion)
    {
        protocolVersion = 0;
        if (!TryReadLocalPbpSnapshotHeader(gameId, out MinimalSaveHeader header))
        {
            return false;
        }

        return TryGetVerifiedPbpProtocolVersion(header, out protocolVersion);
    }

    private static bool TryGetVerifiedPbpProtocolVersion(MinimalSaveHeader header, out int protocolVersion)
    {
        protocolVersion = 0;
        if (header == null)
        {
            return false;
        }

        if (!string.Equals(header.mode, TurnManager.GameMode.PlayByPost.ToString(), StringComparison.Ordinal))
        {
            return false;
        }

        if (header.protocolVersion <= 0)
        {
            return false;
        }

        protocolVersion = header.protocolVersion;
        return true;
    }

    // Temporary migration bridge: legacy PbP headers created before appVersion existed
    // are allowed while their protocolVersion remains supported.
    private static bool TryGetLegacyBridgedPbpAppVersion(MinimalSaveHeader header, out string appVersion)
    {
        appVersion = null;
        if (header == null)
        {
            return false;
        }

        if (!string.Equals(header.mode, TurnManager.GameMode.PlayByPost.ToString(), StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(header.appVersion))
        {
            return true;
        }

        appVersion = header.appVersion.Trim();
        return true;
    }

    private static string BuildPbpVersionText(string gameId)
    {
        if (TryGetLocalPbpSnapshotProtocolVersion(gameId, out int protocolVersion))
        {
            return $"PbP Version: {protocolVersion}";
        }

        return "PbP Version: Unverified";
    }

    private static bool TryGetPbpVersionMismatchWarning(int gameProtocolVersion, out string warning)
    {
        warning = null;
        if (gameProtocolVersion <= 0)
        {
            return false;
        }

        int supportedVersion = TurnManager.PbpProtocolVersion;
        if (TurnManager.IsSupportedPbpLoadProtocolVersion(gameProtocolVersion))
        {
            return false;
        }

        warning = $"This game uses PbP {gameProtocolVersion}. Your app supports PbP {supportedVersion}. This match cannot be opened on this build.";
        return true;
    }

    private static bool TryGetPbpAppVersionMismatchWarning(string gameAppVersion, out string warning)
    {
        warning = null;
        if (string.IsNullOrWhiteSpace(gameAppVersion))
        {
            return false;
        }

        string normalizedGameAppVersion = gameAppVersion.Trim();
        if (TurnManager.IsSupportedPbpAppVersion(normalizedGameAppVersion))
        {
            return false;
        }

        warning =
            $"This game uses Block Nations v{normalizedGameAppVersion}. Your app is v{TurnManager.CurrentAppVersion}. This match cannot be opened on this build.";
        return true;
    }

    private static bool TryGetPbpPreflightBlockWarningFromHeader(MinimalSaveHeader header, out string warning)
    {
        warning = null;
        if (!TryGetVerifiedPbpProtocolVersion(header, out int gameProtocolVersion))
        {
            warning = PbpVersionVerificationFailedMessage;
            return true;
        }

        if (TryGetPbpVersionMismatchWarning(gameProtocolVersion, out warning))
        {
            return true;
        }

        if (!TryGetLegacyBridgedPbpAppVersion(header, out string gameAppVersion))
        {
            return true;
        }

        return TryGetPbpAppVersionMismatchWarning(gameAppVersion, out warning);
    }

    private static bool TryGetPbpPreflightBlockWarningFromJson(string json, out string warning)
    {
        warning = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            warning = PbpVersionVerificationFailedMessage;
            return true;
        }

        MinimalSaveHeader header;
        try
        {
            header = JsonUtility.FromJson<MinimalSaveHeader>(json);
        }
        catch
        {
            warning = PbpVersionVerificationFailedMessage;
            return true;
        }

        if (header == null)
        {
            warning = PbpVersionVerificationFailedMessage;
            return true;
        }

        return TryGetPbpPreflightBlockWarningFromHeader(header, out warning);
    }

    private static void SeedPendingPlayByPostSeatCountFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            MinimalSaveHeader header = JsonUtility.FromJson<MinimalSaveHeader>(json);
            if (header == null ||
                !string.Equals(header.mode, TurnManager.GameMode.PlayByPost.ToString(), StringComparison.Ordinal))
            {
                return;
            }

            PlayByPostSeatCountSelection.SetPending(header.seatCount);
        }
        catch
        {
            // Leave the default seat count in place if the probe payload cannot be parsed here.
        }
    }

    private static void TryWriteLocalPbpSnapshot(string gameId, string json)
    {
        if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            MinimalSaveHeader header = JsonUtility.FromJson<MinimalSaveHeader>(json);
            if (header == null ||
                !string.Equals(header.mode, TurnManager.GameMode.PlayByPost.ToString(), StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(header.gameId) &&
                !string.Equals(header.gameId, gameId, StringComparison.Ordinal))
            {
                return;
            }

            string snapshotPath = GetPbpPerGameSnapshotPath(gameId);
            if (string.IsNullOrWhiteSpace(snapshotPath))
            {
                return;
            }

            string snapshotDirectory = Path.GetDirectoryName(snapshotPath);
            if (!string.IsNullOrWhiteSpace(snapshotDirectory))
            {
                Directory.CreateDirectory(snapshotDirectory);
            }

            File.WriteAllText(snapshotPath, json);
        }
        catch (Exception ex)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning($"[MP] Failed to seed local PBp snapshot for gameId={gameId}: {ex.Message}");
#endif
        }
    }

    private static string GetPbpPerGameSnapshotPath(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return null;
        }

        string safeGameId = SanitizeGameIdForFileName(gameId);
        if (string.IsNullOrWhiteSpace(safeGameId))
        {
            return null;
        }

        return Path.Combine(GetPersistentRootPath(), "pbp", $"pbp_{safeGameId}.json");
    }

    private static string GetPersistentRootPath()
    {
        return DevClientInstanceScope.GetScopedPersistentDataPath();
    }

    private static string SanitizeGameIdForFileName(string gameId)
    {
        if (string.IsNullOrEmpty(gameId))
        {
            return string.Empty;
        }

        char[] chars = gameId.ToCharArray();
        char[] invalidChars = Path.GetInvalidFileNameChars();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalidChars, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private static string BuildGameSubtitle(SaveManifestService.ManifestGameSummary summary)
    {
        return BuildPlayByPostTurnSubtitle(summary);
    }

    public static bool TryGetIsYourTurnFromManifest(
        SaveManifestService.ManifestGameSummary summary,
        out bool isYourTurn,
        out int computedTransportSeq,
        out string reason)
    {
        isYourTurn = false;
        computedTransportSeq = 0;
        reason = null;

        if (string.IsNullOrWhiteSpace(summary.gameId))
        {
            reason = "GAME_ID_MISSING";
            return false;
        }

        if (!summary.hasLastKnownTurnState)
        {
            reason = "STATE_UNKNOWN";
            return false;
        }

        computedTransportSeq = summary.lastKnownTransportSeq > 0
            ? summary.lastKnownTransportSeq
            : SaveManifestService.ComputePlayByPostTransportSeq(
                summary.lastKnownRoundTurn,
                summary.lastKnownCurrentTurnSeatIndex,
                summary.lastKnownSeatCount);

        if (!LocalPlayerSeatStore.TryGetSeat(summary.gameId, out int seatOrPlayerIndex))
        {
            reason = "SEAT_UNKNOWN";
            return false;
        }

        int currentTurnSeatIndex = summary.lastKnownCurrentTurnSeatIndex;
        if (summary.lastKnownSeatCount > 0)
        {
            currentTurnSeatIndex = PlayByPostSeatUtility.NormalizeSeatIndex(
                currentTurnSeatIndex,
                summary.lastKnownSeatCount);
        }
        else
        {
            currentTurnSeatIndex = summary.lastKnownIsPlayerTurn ? 0 : 1;
        }

        isYourTurn = seatOrPlayerIndex == currentTurnSeatIndex;
        reason = "OK";
        return true;
    }

    private void TryRefreshRemoteTurnStatusesForMenu(bool bypassCooldown = false)
    {
        if (SharedMenuRefreshState.Mode == MenuRefreshMode.Inactive)
        {
            LogRemoteTurnStatusDiagnostics(
                "[MPRemoteStatus] refresh skip=MENU_INACTIVE");
            return;
        }

        if (!isServerOnline)
        {
            LogRemoteTurnStatusDiagnostics(
                "[MPRemoteStatus] refresh skip=SERVER_OFFLINE");
            SharedMenuRefreshState.NextAllowedPullRealtime = Time.realtimeSinceStartup + GetCurrentMenuRefreshIntervalSeconds();
            ClearRemoteTurnStatusOverlay();
            return;
        }

        ResolveServerCheckSources();
        HttpTurnTransport httpTransport = cachedHttpTransport;
        if (httpTransport == null)
        {
            LogRemoteTurnStatusDiagnostics(
                "[MPRemoteStatus] refresh skip=HTTP_TRANSPORT_MISSING");
            SharedMenuRefreshState.NextAllowedPullRealtime = Time.realtimeSinceStartup + GetCurrentMenuRefreshIntervalSeconds();
            ClearRemoteTurnStatusOverlay();
            return;
        }

        httpTransport.Initialize();
        if (!httpTransport.IsAvailable)
        {
            LogRemoteTurnStatusDiagnostics(
                "[MPRemoteStatus] refresh skip=HTTP_TRANSPORT_UNAVAILABLE");
            SharedMenuRefreshState.NextAllowedPullRealtime = Time.realtimeSinceStartup + GetCurrentMenuRefreshIntervalSeconds();
            ClearRemoteTurnStatusOverlay();
            return;
        }

        if (!TryBuildRemoteTurnStatusQuery(out HttpTurnTransport.TurnStatusQuery[] queries, out string requestSignature))
        {
            LogRemoteTurnStatusDiagnostics(
                "[MPRemoteStatus] refresh skip=NO_HTTP_ELIGIBLE_GAMES");
            SharedMenuRefreshState.NextAllowedPullRealtime = Time.realtimeSinceStartup + GetCurrentMenuRefreshIntervalSeconds();
            ClearRemoteTurnStatusOverlay();
            return;
        }

        if (remoteTurnStatusFetchInFlight || SharedMenuRefreshState.IsFetchInFlight)
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] refresh skip=IN_FLIGHT signature={requestSignature}");
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (!bypassCooldown &&
            string.Equals(remoteTurnStatusLastRequestSignature, requestSignature, StringComparison.Ordinal) &&
            (now - remoteTurnStatusLastFetchRealtime) < RemoteTurnStatusFetchCooldownSeconds)
        {
            float remaining = RemoteTurnStatusFetchCooldownSeconds - (now - remoteTurnStatusLastFetchRealtime);
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] refresh skip=COOLDOWN signature={requestSignature} remainingSec={Mathf.Max(0f, remaining):F2}");
            return;
        }

        List<string> debugKnownSeq = new List<string>(queries.Length);
        for (int i = 0; i < queries.Length; i++)
        {
            debugKnownSeq.Add($"{queries[i].GameId}:{queries[i].KnownSeq}");
        }
        LogRemoteTurnStatusDiagnostics(
            $"[MPRemoteStatus] refresh start signature={requestSignature} queries=[{string.Join(", ", debugKnownSeq)}]");

        SharedMenuRefreshState.LastAttemptRealtime = Time.realtimeSinceStartup;
        SharedMenuRefreshState.IsFetchInFlight = true;
        SharedMenuRefreshState.LastRequestSignature = requestSignature;
        remoteTurnStatusFetchInFlight = true;
        int requestSerial = ++remoteTurnStatusRequestSerial;
        StartCoroutine(FetchRemoteTurnStatusesCoroutine(httpTransport, queries, requestSignature, requestSerial));
    }

    private IEnumerator FetchRemoteTurnStatusesCoroutine(
        HttpTurnTransport httpTransport,
        HttpTurnTransport.TurnStatusQuery[] queries,
        string requestSignature,
        int requestSerial)
    {
        bool ok = false;
        HttpTurnTransport.TurnStatusItem[] items = null;

        yield return StartCoroutine(httpTransport.FetchTurnStatuses(queries, (success, error, resultItems) =>
        {
            ok = success;
            items = resultItems;
        }));

        remoteTurnStatusFetchInFlight = false;
        SharedMenuRefreshState.IsFetchInFlight = false;
        remoteTurnStatusLastFetchRealtime = Time.realtimeSinceStartup;
        remoteTurnStatusLastRequestSignature = requestSignature;
        LogRemoteTurnStatusDiagnostics(
            $"[MPRemoteStatus] fetch complete ok={ok} items={(items != null ? items.Length : 0)} requestSerial={requestSerial} currentSerial={remoteTurnStatusRequestSerial} signature={requestSignature}");

        UpdateMenuRefreshBackoff(ok && items != null, requestSignature);

        if (requestSerial != remoteTurnStatusRequestSerial)
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] fetch discard=STALE_SERIAL requestSerial={requestSerial} currentSerial={remoteTurnStatusRequestSerial}");
            yield break;
        }

        if (SharedMenuRefreshState.Mode == MenuRefreshMode.Inactive)
        {
            LogRemoteTurnStatusDiagnostics(
                "[MPRemoteStatus] fetch discard=MENU_INACTIVE");
            yield break;
        }

        if (!ok || items == null)
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] fetch apply=FAILED_CLEAR ok={ok} itemsNull={(items == null)}");
            ClearRemoteTurnStatusOverlay();
            yield break;
        }

        string currentSignature = BuildRemoteTurnStatusRequestSignature();
        if (!string.Equals(currentSignature, requestSignature, StringComparison.Ordinal))
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] fetch discard=SIGNATURE_CHANGED requestSignature={requestSignature} currentSignature={currentSignature}");
            TryRefreshRemoteTurnStatusesForMenu(bypassCooldown: true);
            yield break;
        }

        Dictionary<string, RemoteTurnStatusOverlay> next = new Dictionary<string, RemoteTurnStatusOverlay>(StringComparer.Ordinal);
        for (int i = 0; i < items.Length; i++)
        {
            HttpTurnTransport.TurnStatusItem item = items[i];
            if (string.IsNullOrWhiteSpace(item.GameId))
            {
                LogRemoteTurnStatusDiagnostics(
                    $"[MPRemoteStatus] fetch item skip=EMPTY_GAME_ID index={i}");
                continue;
            }

            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] fetch item gameId={item.GameId} hasNewerThanKnown={item.HasNewerThanKnown} turnSeat={item.TurnSeat} knownSeq={item.KnownSeq} latestSeq={item.LatestSeq} nextSeqAfterKnown={item.NextSeqAfterKnown}");
            next[item.GameId] = new RemoteTurnStatusOverlay(item.HasNewerThanKnown, item.TurnSeat, item.LatestSeq);
        }

        bool refreshedLocalSnapshots = false;
        yield return StartCoroutine(FetchAndApplyNewerSnapshotsForMenuCoroutine(
            httpTransport,
            items,
            requestSerial,
            result => refreshedLocalSnapshots = result));

        if (refreshedLocalSnapshots)
        {
            LogRemoteTurnStatusDiagnostics(
                "[MPRemoteStatus] fetch applied local_snapshot_updates=true");
            RefreshMultiplayerListInternal(bypassRemoteCooldown: true);
            yield break;
        }

        bool changed = !AreRemoteTurnStatusOverlaysEqual(next);
        remoteTurnStatusByGameId.Clear();
        foreach (KeyValuePair<string, RemoteTurnStatusOverlay> kv in next)
        {
            remoteTurnStatusByGameId[kv.Key] = kv.Value;
        }

        LogRemoteTurnStatusDiagnostics(
            $"[MPRemoteStatus] fetch applied changed={changed} overlayCount={remoteTurnStatusByGameId.Count}");

        if (changed)
        {
            RecomputePbpBadge();
            ActivePbpGamesChanged?.Invoke();
        }
    }

    private bool TryBuildRemoteTurnStatusQuery(
        out HttpTurnTransport.TurnStatusQuery[] queries,
        out string signature)
    {
        List<HttpTurnTransport.TurnStatusQuery> requestList = new List<HttpTurnTransport.TurnStatusQuery>();
        List<string> signatureParts = new List<string>();
        HashSet<string> seenGameIds = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < activePbpGames.Count; i++)
        {
            SaveManifestService.ManifestGameSummary summary = activePbpGames[i];
            if (!IsHttpPbpGame(summary))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(summary.gameId))
            {
                continue;
            }

            if (!seenGameIds.Add(summary.gameId))
            {
                continue;
            }

            int knownSeq = GetKnownTransportSeqOrUnknown(summary);
            requestList.Add(new HttpTurnTransport.TurnStatusQuery(summary.gameId, knownSeq));
            signatureParts.Add($"{summary.gameId}|{knownSeq}");
        }

        if (requestList.Count <= 0)
        {
            queries = null;
            signature = string.Empty;
            return false;
        }

        queries = requestList.ToArray();
        signature = string.Join(";", signatureParts);
        return true;
    }

    private IEnumerator FetchAndApplyNewerSnapshotsForMenuCoroutine(
        HttpTurnTransport httpTransport,
        HttpTurnTransport.TurnStatusItem[] items,
        int requestSerial,
        Action<bool> done)
    {
        bool anyUpdated = false;

        if (httpTransport == null || items == null || items.Length <= 0)
        {
            done?.Invoke(false);
            yield break;
        }

        for (int i = 0; i < items.Length; i++)
        {
            if (requestSerial != remoteTurnStatusRequestSerial ||
                SharedMenuRefreshState.Mode == MenuRefreshMode.Inactive)
            {
                done?.Invoke(anyUpdated);
                yield break;
            }

            HttpTurnTransport.TurnStatusItem item = items[i];
            if (!item.HasNewerThanKnown || string.IsNullOrWhiteSpace(item.GameId))
            {
                continue;
            }

            if (!TryGetHttpActivePbpGameSummary(item.GameId, out SaveManifestService.ManifestGameSummary summary))
            {
                continue;
            }

            int knownSeq = Mathf.Max(-1, GetKnownTransportSeqOrUnknown(summary));
            int targetSeq = Mathf.Max(knownSeq, item.LatestSeq);
            while (knownSeq < targetSeq)
            {
                bool fetchOk = false;
                string fetchError = null;
                int fetchedSeq = 0;
                string fetchedJson = null;
                yield return StartCoroutine(httpTransport.TryFetchNextTurn(item.GameId, knownSeq, (ok, err, seq, json) =>
                {
                    fetchOk = ok;
                    fetchError = err;
                    fetchedSeq = seq;
                    fetchedJson = json;
                }));

                if (!fetchOk || string.IsNullOrWhiteSpace(fetchedJson) || fetchedSeq <= knownSeq)
                {
                    LogRemoteTurnStatusDiagnostics(
                        $"[MPRemoteStatus] snapshot fetch stop gameId={item.GameId} ok={fetchOk} err={(fetchError ?? "<null>")} fetchedSeq={fetchedSeq} knownSeq={knownSeq} targetSeq={targetSeq}");
                    break;
                }

                if (!TryApplyFetchedPbpSnapshotForMenu(summary, item.GameId, fetchedSeq, fetchedJson))
                {
                    LogRemoteTurnStatusDiagnostics(
                        $"[MPRemoteStatus] snapshot apply failed gameId={item.GameId} fetchedSeq={fetchedSeq}");
                    break;
                }

                anyUpdated = true;
                knownSeq = fetchedSeq;
            }
        }

        done?.Invoke(anyUpdated);
    }

    private bool TryGetHttpActivePbpGameSummary(string gameId, out SaveManifestService.ManifestGameSummary summary)
    {
        summary = default;
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        for (int i = 0; i < activePbpGames.Count; i++)
        {
            SaveManifestService.ManifestGameSummary candidate = activePbpGames[i];
            if (!string.Equals(candidate.gameId, gameId, StringComparison.Ordinal) ||
                !IsHttpPbpGame(candidate))
            {
                continue;
            }

            summary = candidate;
            return true;
        }

        return false;
    }

    private static bool TryApplyFetchedPbpSnapshotForMenu(
        SaveManifestService.ManifestGameSummary summary,
        string expectedGameId,
        int fetchedSeq,
        string fetchedJson)
    {
        if (string.IsNullOrWhiteSpace(expectedGameId) || string.IsNullOrWhiteSpace(fetchedJson))
        {
            return false;
        }

        if (!TurnManager.TryIngestFetchedPlayByPostSnapshotJson(
                fetchedJson,
                expectedGameId,
                fetchedSeq,
                summary.lastKnownSeatCount,
                "menu_refresh_fetch",
                out _,
                out _,
                out _,
                out _,
                out _,
                out _))
        {
            return false;
        }

        return true;
    }

    private string BuildRemoteTurnStatusRequestSignature()
    {
        return TryBuildRemoteTurnStatusQuery(out _, out string signature)
            ? signature
            : string.Empty;
    }

    private static int GetKnownTransportSeqOrUnknown(SaveManifestService.ManifestGameSummary summary)
    {
        if (summary.lastKnownTransportSeq > 0)
        {
            return summary.lastKnownTransportSeq;
        }

        if (summary.hasLastKnownTurnState)
        {
            return SaveManifestService.ComputePlayByPostTransportSeq(
                summary.lastKnownRoundTurn,
                summary.lastKnownCurrentTurnSeatIndex,
                summary.lastKnownSeatCount);
        }

        return -1;
    }

    private static bool IsHttpPbpGame(SaveManifestService.ManifestGameSummary summary)
    {
        if (!string.Equals(summary.slotType, "PlayByPost", StringComparison.Ordinal))
        {
            return false;
        }

        // File transport should never use server status overlay.
        if (string.Equals(summary.transportType, "File", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private bool AreRemoteTurnStatusOverlaysEqual(Dictionary<string, RemoteTurnStatusOverlay> next)
    {
        if (next.Count != remoteTurnStatusByGameId.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, RemoteTurnStatusOverlay> kv in next)
        {
            if (!remoteTurnStatusByGameId.TryGetValue(kv.Key, out RemoteTurnStatusOverlay existing))
            {
                return false;
            }

            if (existing.HasNewerThanKnown != kv.Value.HasNewerThanKnown ||
                existing.TurnSeat != kv.Value.TurnSeat ||
                existing.LatestSeq != kv.Value.LatestSeq)
            {
                return false;
            }
        }

        return true;
    }

    private void ClearRemoteTurnStatusOverlay()
    {
        if (remoteTurnStatusByGameId.Count <= 0)
        {
            return;
        }

        remoteTurnStatusByGameId.Clear();
        RecomputePbpBadge();
        ActivePbpGamesChanged?.Invoke();
    }

    public bool HasHttpEligiblePbpGamesForMenuRefresh()
    {
        for (int i = 0; i < activePbpGames.Count; i++)
        {
            if (IsHttpPbpGame(activePbpGames[i]))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsMenuRefreshInFlight()
    {
        return SharedMenuRefreshState.IsFetchInFlight || remoteTurnStatusFetchInFlight;
    }

    public int GetMenuRefreshCountdownSeconds()
    {
        if (SharedMenuRefreshState.Mode != MenuRefreshMode.OpenPane || !HasHttpEligiblePbpGamesForMenuRefresh())
        {
            return -1;
        }

        if (IsMenuRefreshInFlight())
        {
            return 0;
        }

        float remaining = SharedMenuRefreshState.NextAllowedPullRealtime - Time.realtimeSinceStartup;
        return Mathf.Max(0, Mathf.CeilToInt(remaining));
    }

    private void HandleMenuAppResume()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        SyncAppIconBadge(force: true);
        UpdateMenuRefreshLoopState();
        if (!HasHttpEligiblePbpGamesForMenuRefresh())
        {
            return;
        }

        float now = Time.realtimeSinceStartup;
        if ((now - SharedMenuRefreshState.LastSuccessRealtime) >= MenuResumeImmediateRefreshStaleAfterSeconds)
        {
            SharedMenuRefreshState.NextAllowedPullRealtime = now;
        }
    }

    private void ApplyVisibleMenuPaneState(bool multiplayerVisible, bool resetRefreshWindow)
    {
        IsMultiplayerScreenRequested = multiplayerVisible;
        UpdateMenuRefreshMode(
            multiplayerVisible ? MenuRefreshMode.OpenPane : MenuRefreshMode.ClosedPane,
            resetRefreshWindow);
    }

    private void UpdateMenuRefreshMode(MenuRefreshMode mode, bool resetRefreshWindow)
    {
        MenuRefreshMode previousMode = SharedMenuRefreshState.Mode;
        SharedMenuRefreshState.Mode = mode;

        if (mode != MenuRefreshMode.Inactive &&
            !SharedMenuRefreshState.IsFetchInFlight &&
            SharedMenuRefreshState.ConsecutiveFailureCount <= 0 &&
            (resetRefreshWindow || previousMode != mode))
        {
            float now = Time.realtimeSinceStartup;
            float intervalSeconds = mode == MenuRefreshMode.OpenPane
                ? MenuOpenRefreshIntervalSeconds
                : MenuClosedRefreshIntervalSeconds;
            float secondsSinceSuccess = SharedMenuRefreshState.LastSuccessRealtime < 0f
                ? float.PositiveInfinity
                : now - SharedMenuRefreshState.LastSuccessRealtime;

            SharedMenuRefreshState.NextAllowedPullRealtime = secondsSinceSuccess >= intervalSeconds
                ? now
                : now + intervalSeconds;
        }

        UpdateMenuRefreshLoopState();
    }

    private void UpdateMenuRefreshLoopState()
    {
        bool shouldRun = isActiveAndEnabled &&
            SharedMenuRefreshState.Mode != MenuRefreshMode.Inactive &&
            HasHttpEligiblePbpGamesForMenuRefresh();

        if (!shouldRun)
        {
            StopMenuRefreshLoop();
            return;
        }

        if (menuRefreshLoopRoutine == null)
        {
            menuRefreshLoopRoutine = StartCoroutine(MenuRefreshLoop());
        }
    }

    private void StopMenuRefreshLoop()
    {
        if (menuRefreshLoopRoutine == null)
        {
            return;
        }

        StopCoroutine(menuRefreshLoopRoutine);
        menuRefreshLoopRoutine = null;
    }

    private IEnumerator MenuRefreshLoop()
    {
        while (isActiveAndEnabled)
        {
            if (SharedMenuRefreshState.Mode == MenuRefreshMode.Inactive || !HasHttpEligiblePbpGamesForMenuRefresh())
            {
                break;
            }

            float now = Time.realtimeSinceStartup;
            if (!IsMenuRefreshInFlight() && now >= SharedMenuRefreshState.NextAllowedPullRealtime)
            {
                RefreshMultiplayerList();
            }

            yield return new WaitForSecondsRealtime(MenuRefreshLoopTickSeconds);
        }

        menuRefreshLoopRoutine = null;
    }

    private void UpdateMenuRefreshBackoff(bool success, string requestSignature)
    {
        float now = Time.realtimeSinceStartup;
        SharedMenuRefreshState.LastRequestSignature = requestSignature ?? string.Empty;

        if (success)
        {
            SharedMenuRefreshState.LastSuccessRealtime = now;
            SharedMenuRefreshState.ConsecutiveFailureCount = 0;
            SharedMenuRefreshState.NextAllowedPullRealtime = now + GetCurrentMenuRefreshIntervalSeconds();
            return;
        }

        SharedMenuRefreshState.ConsecutiveFailureCount++;
        SharedMenuRefreshState.NextAllowedPullRealtime = now + GetMenuRefreshFailureDelaySeconds();
    }

    private float GetCurrentMenuRefreshIntervalSeconds()
    {
        return SharedMenuRefreshState.Mode == MenuRefreshMode.OpenPane
            ? MenuOpenRefreshIntervalSeconds
            : MenuClosedRefreshIntervalSeconds;
    }

    private float GetMenuRefreshFailureDelaySeconds()
    {
        float baseDelay = GetCurrentMenuRefreshIntervalSeconds();
        int backoffStep = Mathf.Max(0, SharedMenuRefreshState.ConsecutiveFailureCount - 1);
        float delay = baseDelay * Mathf.Pow(2f, backoffStep);
        float maxDelay = SharedMenuRefreshState.Mode == MenuRefreshMode.OpenPane
            ? MenuOpenFailureBackoffMaxSeconds
            : MenuClosedFailureBackoffMaxSeconds;
        return Mathf.Min(delay, maxDelay);
    }

    private void SyncAppIconBadge(bool force)
    {
        AppIconBadgeAdapter.SetBadgeCount(PbpBadgeCountMyTurn, force);
    }

    public static void ArchiveLocalPlayByPostGame(
        string gameId,
        bool clearActiveGameSelection = true,
        bool markFinishedLocally = true)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return;
        }

        bool finishedUpdated = markFinishedLocally && SaveManifestService.MarkPlayByPostGameFinished(gameId);
        bool archivedUpdated = SaveManifestService.SetPlayByPostGameArchivedLocally(gameId, isArchivedLocally: true);
        bool prefsChanged = false;
        if (clearActiveGameSelection)
        {
            string activeGameId = PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty);
            if (string.Equals(activeGameId, gameId, StringComparison.Ordinal))
            {
                PlayerPrefs.DeleteKey(PlayByPostGameIdKey);
                prefsChanged = true;
            }
        }

        if (prefsChanged)
        {
            PlayerPrefs.Save();
        }

        IosPbpBackgroundNotificationExperiment.RemoveGame(gameId);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[MP] Archived local PBp game gameId={gameId} markFinishedLocally={markFinishedLocally} finishedUpdated={finishedUpdated} archivedUpdated={archivedUpdated} prefsChanged={prefsChanged}");
#endif
    }

    public static void DeleteLocalPlayByPostGameData(string gameId, bool clearActiveGameSelection = true)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return;
        }

        bool manifestUpdated = SaveManifestService.RemovePlayByPostGame(gameId);

        string turnsFolder = Path.Combine(
            GetPersistentRootPath(),
            "PlayByPost",
            "Turns",
            Hash128.Compute(gameId).ToString());
        bool deletedTurnsFolder = false;
        if (Directory.Exists(turnsFolder))
        {
            try
            {
                Directory.Delete(turnsFolder, true);
                deletedTurnsFolder = true;
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"[MP] Failed deleting PBp folder for {gameId}: {turnsFolder} ({ex.Message})");
#endif
            }
        }

        string savePath = Path.Combine(GetPersistentRootPath(), "save.json");
        bool deletedSaveJson = SaveManifestService.TryDeleteMatchingPlayByPostSaveFile(savePath, gameId);
        string importedPath = Path.Combine(GetPersistentRootPath(), "imported.json");
        bool deletedImportedJson = SaveManifestService.TryDeleteMatchingPlayByPostSaveFile(importedPath, gameId);

        bool prefsChanged = LocalPlayerSeatStore.ClearSeat(gameId);
        if (clearActiveGameSelection)
        {
            string activeGameId = PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty);
            if (string.Equals(activeGameId, gameId, StringComparison.Ordinal))
            {
                PlayerPrefs.DeleteKey(PlayByPostGameIdKey);
                prefsChanged = true;
            }
        }

        if (prefsChanged)
        {
            PlayerPrefs.Save();
        }

        IosPbpBackgroundNotificationExperiment.RemoveGame(gameId);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[MP] Local cleanup gameId={gameId} manifestUpdated={manifestUpdated} deletedTurnsFolder={deletedTurnsFolder} deletedSaveJson={deletedSaveJson} deletedImportedJson={deletedImportedJson} prefsChanged={prefsChanged}");
#endif
    }
}

internal static class AppIconBadgeAdapter
{
    private static bool hasAppliedCount;
    private static int lastAppliedCount;

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void BNSetApplicationIconBadgeNumber(int count);
#endif

    public static void SetBadgeCount(int count, bool force = false)
    {
        int clampedCount = Mathf.Max(0, count);
        if (!force && hasAppliedCount && lastAppliedCount == clampedCount)
        {
            return;
        }

        ApplyPlatformBadgeCount(clampedCount);
        lastAppliedCount = clampedCount;
        hasAppliedCount = true;
    }

    private static void ApplyPlatformBadgeCount(int count)
    {
#if UNITY_IOS && !UNITY_EDITOR
        BNSetApplicationIconBadgeNumber(count);
#endif
    }
}

internal static class IosBadgePermissionAdapter
{
    private static bool hasRequestedAuthorization;

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void BNRequestBadgeAuthorization();
#endif

    public static void EnsureBadgeAuthorizationRequested()
    {
        if (hasRequestedAuthorization)
        {
            return;
        }

        hasRequestedAuthorization = true;
        RequestPlatformAuthorization();
    }

    private static void RequestPlatformAuthorization()
    {
#if UNITY_IOS && !UNITY_EDITOR
        BNRequestBadgeAuthorization();
#endif
    }
}

internal static class IosDebugNotificationAdapter
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void BNTriggerDebugLocalNotification();
#endif

    public static bool IsAvailable()
    {
#if UNITY_IOS && !UNITY_EDITOR && DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
    }

    public static bool TryScheduleTestNotification()
    {
        if (!IsAvailable())
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[iOS Debug Notification] Test notification unavailable on this build/platform.");
#endif
            return false;
        }

#if UNITY_IOS && !UNITY_EDITOR
#if DEVELOPMENT_BUILD
        Debug.Log("[iOS Debug Notification] Calling native test notification bridge.");
#endif
        BNTriggerDebugLocalNotification();
#endif
        return true;
    }
}
