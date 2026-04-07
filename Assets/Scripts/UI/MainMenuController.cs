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
    [Header("Scenes")]
    [SerializeField] private string gameplaySceneName = "SampleScene";

    [SerializeField] private GameObject modeSelectionPanel;
    [SerializeField] private GameObject aiDifficultyPanel;
    [SerializeField] private string selectedGameId;

    [Header("Layout")]
    [SerializeField] private bool autoFitMenuToScreenOnDesktop = true;
    private const string PlayByPostGameIdKey = "pbp_gameId";
    private const string PlayByPostForceNewKey = "pbp_forceNew";
    private const string PlayByPostPendingNewGameIdKey = "pbp_pendingNewGameId";
    private const string PendingCreateShareReadyGameIdKey = "ui_pbp_createShareReadyGameId";
    private const string ReturnToMultiplayerPaneKey = "ui_returnToMultiplayerPane";
    private const string SinglePlayerPrimarySaveFileName = "save_sp.json";
    private const string LegacySharedSaveFileName = "save.json";
    private const string PbpVersionVerificationFailedMessage = "Unable to verify this game's PbP version. For safety, this match cannot be opened on this build.";
    private const string PbpActiveGameUpdateRequiredCardText = "Requires matching version";
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
    private bool isServerOnline = true;
    private bool joinProbeInProgress;
    private Coroutine serverCheckRoutine;
    private Coroutine menuRefreshLoopRoutine;
    private HttpTurnTransport cachedHttpTransport;

    public event Action ActivePbpGamesChanged;
    public event Action<string> ImportStatusChanged;
    public event Action PbpBadgeChanged;
    public event Action MultiplayerScreenRequested;
    public event Action<string> MultiplayerCreateSucceeded;
    public IReadOnlyList<SaveManifestService.ManifestGameSummary> ActivePbpGames => activePbpGames;
    public string CurrentImportStatus { get; private set; } = string.Empty;
    public int PbpBadgeCountMyTurn { get; private set; }
    public bool IsMultiplayerScreenRequested { get; private set; }
    private List<SaveManifestService.ManifestGameSummary> activePbpGames = new List<SaveManifestService.ManifestGameSummary>();
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

    private readonly struct RemoteTurnStatusOverlay
    {
        public readonly bool HasNewerThanKnown;
        public readonly int TurnSeat;

        public RemoteTurnStatusOverlay(bool hasNewerThanKnown, int turnSeat)
        {
            HasNewerThanKnown = hasNewerThanKnown;
            TurnSeat = turnSeat;
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
        // Open difficulty selection instead of starting immediately.
        if (aiDifficultyPanel != null)
        {
            aiDifficultyPanel.SetActive(true);

            if (modeSelectionPanel != null)
            {
                modeSelectionPanel.SetActive(false);
            }
        }
        else
        {
            // Fallback if no difficulty panel is wired.
            StartVsAIGame(TurnManager.AIDifficulty.Level1, TurnManager.GetDefaultMapSizePreset());
        }
    }

    // Optional: hook dedicated buttons to these for different difficulties.
    public void PlayVsAI_Level1()
    {
        StartVsAIGame(TurnManager.AIDifficulty.Level1, TurnManager.GetDefaultMapSizePreset());
    }

    public void PlayVsAI_Level2()
    {
        StartVsAIGame(TurnManager.AIDifficulty.Level2, TurnManager.GetDefaultMapSizePreset());
    }

    public void PlayVsAI_Level3()
    {
        StartVsAIGame(TurnManager.AIDifficulty.Level3, TurnManager.GetDefaultMapSizePreset());
    }

    public void PlayVsAI_Unfair()
    {
        StartVsAIGame(TurnManager.AIDifficulty.Level3, TurnManager.GetDefaultMapSizePreset());
    }

    public void StartVsAIGameWithSettings(
        TurnManager.AIDifficulty difficulty,
        TurnManager.MapSizePreset mapSizePreset,
        TurnManager.AIRecruitVariant recruitVariant = TurnManager.AIRecruitVariant.Default,
        bool storeSnapshotHistory = false,
        bool enableAIVsAIDebugMode = false,
        TurnManager.AIVsAIBatchSpeedPreset aiVsAiBatchSpeedPreset = TurnManager.AIVsAIBatchSpeedPreset.Normal,
        TurnManager.AIRecruitVariant sideARecruitVariant = TurnManager.AIRecruitVariant.Default,
        TurnManager.AIRecruitVariant sideBRecruitVariant = TurnManager.AIRecruitVariant.Default,
        TurnManager.AIDebugProfile sideAProfile = TurnManager.AIDebugProfile.Baseline,
        TurnManager.AIDebugProfile sideBProfile = TurnManager.AIDebugProfile.Baseline,
        AIVsAIBatchRunController.SimulationSettings aiVsAiSimulationSettings = default)
    {
        StartVsAIGame(
            difficulty,
            mapSizePreset,
            recruitVariant,
            storeSnapshotHistory,
            enableAIVsAIDebugMode,
            aiVsAiBatchSpeedPreset,
            sideARecruitVariant,
            sideBRecruitVariant,
            sideAProfile,
            sideBProfile,
            aiVsAiSimulationSettings);
    }

    private void StartVsAIGame(
        TurnManager.AIDifficulty difficulty,
        TurnManager.MapSizePreset mapSizePreset,
        TurnManager.AIRecruitVariant recruitVariant = TurnManager.AIRecruitVariant.Default,
        bool storeSnapshotHistory = false,
        bool enableAIVsAIDebugMode = false,
        TurnManager.AIVsAIBatchSpeedPreset aiVsAiBatchSpeedPreset = TurnManager.AIVsAIBatchSpeedPreset.Normal,
        TurnManager.AIRecruitVariant sideARecruitVariant = TurnManager.AIRecruitVariant.Default,
        TurnManager.AIRecruitVariant sideBRecruitVariant = TurnManager.AIRecruitVariant.Default,
        TurnManager.AIDebugProfile sideAProfile = TurnManager.AIDebugProfile.Baseline,
        TurnManager.AIDebugProfile sideBProfile = TurnManager.AIDebugProfile.Baseline,
        AIVsAIBatchRunController.SimulationSettings aiVsAiSimulationSettings = default)
    {
        GameModeSelection.SetPendingMode(TurnManager.GameMode.VsAI);
        AIDifficultySelection.SetPending(difficulty);
        AIRecruitVariantSelection.SetPending(recruitVariant);
        AIVsAIDebugSelection.SetPending(
            enableAIVsAIDebugMode,
            sideARecruitVariant,
            sideBRecruitVariant,
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

        VerticalLayoutGroup[] groups = UnityEngine.Object.FindObjectsByType<VerticalLayoutGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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

    public bool StartPlayByPostGameWithSettings(TurnManager.MapSizePreset mapSizePreset, bool storeSnapshotHistory = false)
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

        if (!isServerOnline)
        {
            SetImportStatus(BuildConnectivityWarningStatus());
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
        }
#endif
        RecomputePbpBadge();
        IosPbpBackgroundNotificationExperiment.SyncState(activePbpGames, cachedHttpTransport);
        ActivePbpGamesChanged?.Invoke();

        PbpConnectivityState connectivityState = ResolveSharedConnectivityState();
        if (connectivityState == PbpConnectivityState.Normal)
        {
            if (activePbpGames.Count <= 0)
            {
                SetImportStatus("No active games");
            }
            else if (activePbpGames.Count == 1)
            {
                SetImportStatus("1 active game");
            }
            else
            {
                SetImportStatus($"{activePbpGames.Count} active games");
            }
        }
        else
        {
            SetImportStatus(connectivityState == PbpConnectivityState.Offline ? "Offline" : "Can't reach server");
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
            if (string.Equals(BuildPlayByPostTurnStateForMenu(summary), "Your turn", StringComparison.Ordinal))
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

        if (summary.isFinished || string.IsNullOrWhiteSpace(summary.gameId))
        {
            return false;
        }

        string subtitle = BuildPlayByPostTurnStateForMenu(summary);
        return string.Equals(subtitle, "Waiting for opponent", StringComparison.Ordinal);
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

    public void GameDetails_ResignLocal()
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
        string spPath = Path.Combine(Application.persistentDataPath, SinglePlayerPrimarySaveFileName);
        if (File.Exists(spPath))
        {
            return spPath;
        }

        return Path.Combine(Application.persistentDataPath, LegacySharedSaveFileName);
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
        public string playerOneTypedDisplayName = string.Empty;
        public string playerTwoTypedDisplayName = string.Empty;
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

        string path = Path.Combine(Application.persistentDataPath, "imported.json");
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
            bool probeResult = false;
            yield return StartCoroutine(httpTransport.CheckServerReachable(result => probeResult = result));
            online = probeResult;
        }
        else
        {
            online = false;
        }

        isServerOnline = online;
        PbpConnectivityStateModel.ObserveServerProbeResult(online);
        UpdateMultiplayerButtonStates();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (PbpDebugSettingsLoader.EnableTransportLogs)
        {
            Debug.Log($"Multiplayer server check complete. online={isServerOnline}, checkedTransport={hasCheck}");
        }
#endif

        SetImportStatus(isServerOnline ? "Server online" : BuildConnectivityWarningStatus());
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
                SetImportStatus($"{BuildConnectivityWarningStatus()}. Can't verify game.");
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

            if (probeOk)
            {
                if (TryGetPbpPreflightBlockWarningFromJson(probeJson, out string joinVersionWarning))
                {
                    SetImportStatus(joinVersionWarning);
                    yield break;
                }

                SetImportStatus("Game found. Joining...");
                LocalPlayerSeatStore.SetSeat(gameId, 1);
                PersistPlayByPostSelection(gameId, returnToMultiplayerPane: true);
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

            SetImportStatus($"{BuildConnectivityWarningStatus()}. Can't verify game.");
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
            cachedHttpTransport = UnityEngine.Object.FindFirstObjectByType<HttpTurnTransport>();
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
            : "Can't reach server";
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
        if (TryGetLocalPbpSnapshotGameOver(summary.gameId, out bool snapshotGameOver) && snapshotGameOver)
        {
            return "Game Over";
        }

        if (TryGetIsYourTurnFromManifest(summary, out bool isYourTurn, out _, out _) && isYourTurn)
        {
            return "Your turn";
        }

        return "Waiting for opponent";
    }

    public string BuildPlayByPostTurnSubtitleForMenu(SaveManifestService.ManifestGameSummary summary)
    {
        if (TryReadLocalPbpSnapshotHeader(summary.gameId, out MinimalSaveHeader localHeader) &&
            TryGetPbpPreflightBlockWarningFromHeader(localHeader, out _))
        {
            return PbpActiveGameUpdateRequiredCardText;
        }

        string turnState = BuildPlayByPostTurnStateForMenu(summary);
        if (string.Equals(turnState, "Game Over", StringComparison.Ordinal))
        {
            return turnState;
        }

        string opponentTypedDisplayName = TryGetOpponentTypedDisplayNameForMenu(summary.gameId, out string foundOpponentTypedDisplayName)
            ? foundOpponentTypedDisplayName
            : "Opponent";

        return string.Equals(turnState, "Your turn", StringComparison.Ordinal)
            ? $"Your turn against {opponentTypedDisplayName}"
            : $"Waiting for {opponentTypedDisplayName}";
    }

    public string BuildPlayByPostTurnStateForMenu(SaveManifestService.ManifestGameSummary summary)
    {
        string fallback = BuildPlayByPostTurnSubtitle(summary);
        if (string.Equals(fallback, "Game Over", StringComparison.Ordinal))
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] subtitle gameId={summary.gameId} reason=FALLBACK_GAME_OVER fallback={fallback}");
            return fallback;
        }

        if (!IsHttpPbpGame(summary))
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] subtitle gameId={summary.gameId} reason=FALLBACK_NOT_HTTP fallback={fallback} transportType={summary.transportType ?? "<null>"} slotType={summary.slotType ?? "<null>"}");
            return fallback;
        }

        if (!remoteTurnStatusByGameId.TryGetValue(summary.gameId, out RemoteTurnStatusOverlay remote))
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] subtitle gameId={summary.gameId} reason=FALLBACK_NO_REMOTE_STATUS fallback={fallback}");
            return fallback;
        }

        if (!remote.HasNewerThanKnown)
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] subtitle gameId={summary.gameId} reason=FALLBACK_NOT_NEWER fallback={fallback} turnSeat={remote.TurnSeat}");
            return fallback;
        }

        if (remote.TurnSeat != 0 && remote.TurnSeat != 1)
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] subtitle gameId={summary.gameId} reason=FALLBACK_INVALID_TURN_SEAT fallback={fallback} turnSeat={remote.TurnSeat}");
            return fallback;
        }

        if (!LocalPlayerSeatStore.TryGetSeat(summary.gameId, out int localSeat))
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] subtitle gameId={summary.gameId} reason=FALLBACK_SEAT_MISSING fallback={fallback} turnSeat={remote.TurnSeat}");
            return fallback;
        }

        if (localSeat != 0 && localSeat != 1)
        {
            LogRemoteTurnStatusDiagnostics(
                $"[MPRemoteStatus] subtitle gameId={summary.gameId} reason=FALLBACK_INVALID_LOCAL_SEAT fallback={fallback} localSeat={localSeat} turnSeat={remote.TurnSeat}");
            return fallback;
        }

        string overlayText = localSeat == remote.TurnSeat ? "Your turn" : "Waiting for opponent";
        string reason = localSeat == remote.TurnSeat ? "OVERLAY_YOUR_TURN" : "OVERLAY_WAITING";
        LogRemoteTurnStatusDiagnostics(
            $"[MPRemoteStatus] subtitle gameId={summary.gameId} reason={reason} overlay={overlayText} localSeat={localSeat} turnSeat={remote.TurnSeat}");
        return overlayText;
    }

    public string BuildPlayByPostDetailsSubtitleForMenu(SaveManifestService.ManifestGameSummary summary)
    {
        string subtitle = BuildPlayByPostTurnStateForMenu(summary);
        if (!TryGetOpponentTypedDisplayNameForMenu(summary.gameId, out string opponentTypedDisplayName))
        {
            return TryGetLocalPbpSnapshotProtocolVersion(summary.gameId, out _)
                ? $"{subtitle}\n{BuildPbpVersionText(summary.gameId)}"
                : subtitle;
        }

        string details = $"{subtitle}\nOpponent: {opponentTypedDisplayName}";
        if (TryGetLocalPbpSnapshotProtocolVersion(summary.gameId, out _))
        {
            details = $"{details}\n{BuildPbpVersionText(summary.gameId)}";
        }

        return details;
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

    private static bool TryGetOpponentTypedDisplayNameForMenu(string gameId, out string opponentTypedDisplayName)
    {
        opponentTypedDisplayName = null;
        if (!TryReadLocalPbpSnapshotHeader(gameId, out MinimalSaveHeader header))
        {
            return false;
        }

        if (!LocalPlayerSeatStore.TryGetSeat(gameId, out int localSeat) || (localSeat != 0 && localSeat != 1))
        {
            return false;
        }

        string playerOneTypedDisplayName = LocalPlayerProfileStore.NormalizeTypedDisplayName(header.playerOneTypedDisplayName);
        string playerTwoTypedDisplayName = LocalPlayerProfileStore.NormalizeTypedDisplayName(header.playerTwoTypedDisplayName);
        opponentTypedDisplayName = localSeat == 0 ? playerTwoTypedDisplayName : playerOneTypedDisplayName;
        return !string.IsNullOrWhiteSpace(opponentTypedDisplayName);
    }

    private static bool TryReadLocalPbpSnapshotHeader(string gameId, out MinimalSaveHeader header)
    {
        header = null;
        string snapshotPath = GetPbpPerGameSnapshotPath(gameId);
        if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(snapshotPath);
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

        return Path.Combine(Application.persistentDataPath, "pbp", $"pbp_{safeGameId}.json");
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
            : SaveManifestService.ComputePlayByPostTransportSeq(summary.lastKnownRoundTurn, summary.lastKnownIsPlayerTurn);

        if (!LocalPlayerSeatStore.TryGetSeat(summary.gameId, out int seatOrPlayerIndex))
        {
            reason = "SEAT_UNKNOWN";
            return false;
        }

        bool localIsPlayerOwned = seatOrPlayerIndex == 0;
        isYourTurn = summary.lastKnownIsPlayerTurn == localIsPlayerOwned;
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
            next[item.GameId] = new RemoteTurnStatusOverlay(item.HasNewerThanKnown, item.TurnSeat);
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
            return SaveManifestService.ComputePlayByPostTransportSeq(summary.lastKnownRoundTurn, summary.lastKnownIsPlayerTurn);
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
                existing.TurnSeat != kv.Value.TurnSeat)
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

    public static void DeleteLocalPlayByPostGameData(string gameId, bool clearActiveGameSelection = true)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return;
        }

        bool manifestUpdated = SaveManifestService.MarkPlayByPostGameFinished(gameId);

        string turnsFolder = Path.Combine(
            Application.persistentDataPath,
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

        string savePath = Path.Combine(Application.persistentDataPath, "save.json");
        bool deletedSaveJson = SaveManifestService.TryDeleteMatchingPlayByPostSaveFile(savePath, gameId);
        string importedPath = Path.Combine(Application.persistentDataPath, "imported.json");
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
