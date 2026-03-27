using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
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
    private bool tutorialLaunchQueued;
    private const string PlayByPostGameIdKey = "pbp_gameId";
    private const string PlayByPostForceNewKey = "pbp_forceNew";
    private const string PlayByPostPendingNewGameIdKey = "pbp_pendingNewGameId";
    private const string PendingCreateShareReadyGameIdKey = "ui_pbp_createShareReadyGameId";
    private const string ReturnToMultiplayerPaneKey = "ui_returnToMultiplayerPane";
    private const string SinglePlayerPrimarySaveFileName = "save_sp.json";
    private const string LegacySharedSaveFileName = "save.json";
    private const string PbpVersionVerificationFailedMessage = "Unable to verify this game's PBp version. For safety, this match cannot be opened on this build.";
    private bool isServerOnline = true;
    private bool joinProbeInProgress;
    private Coroutine serverCheckRoutine;
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static readonly HashSet<string> PbpSnapshotReadWarningLoggedGameIds = new HashSet<string>();
#endif

    IEnumerator Start()
    {
        LocalPlayerProfileStore.GetOrCreateProfile();
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

        RefreshMultiplayerList();

        if (returnToMultiplayerPane)
        {
            OpenMultiplayerScreen();
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
            StartVsAIGame(TurnManager.AIDifficulty.Level1);
        }
    }

    // Unity UI-friendly click handler for Canvas Buttons.
    public void OnTutorialButtonClicked()
    {
        RequestTutorialAndStartVsAIGame();
    }

    // Optional: hook dedicated buttons to these for different difficulties.
    public void PlayVsAI_Level1()
    {
        StartVsAIGame(TurnManager.AIDifficulty.Level1);
    }

    public void PlayVsAI_Level2()
    {
        StartVsAIGame(TurnManager.AIDifficulty.Level2);
    }

    public void PlayVsAI_Level3()
    {
        StartVsAIGame(TurnManager.AIDifficulty.Level3);
    }

    public void PlayVsAI_Unfair()
    {
        StartVsAIGame(TurnManager.AIDifficulty.Unfair);
    }

    void StartVsAIGame(TurnManager.AIDifficulty difficulty)
    {
        GameModeSelection.SetPendingMode(TurnManager.GameMode.VsAI);
        AIDifficultySelection.SetPending(difficulty);
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

    private void RequestTutorialAndStartVsAIGame()
    {
        if (tutorialLaunchQueued || TutorialGate.IsActive)
            return;

        tutorialLaunchQueued = true;
        TutorialLaunch.RequestShow(resetCompleted: true);
        StartVsAIGame(TurnManager.AIDifficulty.Level1);
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

    public void PlayHotseat()
    {
        GameModeSelection.SetPendingMode(TurnManager.GameMode.Hotseat);
        SceneManager.LoadScene(gameplaySceneName);

        if (modeSelectionPanel != null)
        {
            modeSelectionPanel.SetActive(false);
        }
    }

    public void PlayByPost()
    {
        string gameId = System.Guid.NewGuid().ToString();
        LocalPlayerSeatStore.SetSeat(gameId, 0);
        PlayerPrefs.SetInt(PlayByPostForceNewKey, 1);
        PlayerPrefs.SetString(PlayByPostPendingNewGameIdKey, gameId);
        PlayerPrefs.Save();

        GameModeSelection.SetPendingMode(TurnManager.GameMode.PlayByPost);
        SceneManager.LoadScene(gameplaySceneName);

        if (modeSelectionPanel != null)
        {
            modeSelectionPanel.SetActive(false);
        }
    }

    public void JoinPlayByPostFromInput()
    {
        // Legacy Canvas input has been retired; active joins now flow through UITK's text field.
        TryJoinPlayByPostInternal(rawGameId: null, out _);
    }

    public bool TryJoinPlayByPost(string rawGameId)
    {
        return TryJoinPlayByPostInternal(rawGameId, out _);
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

        if (!TryValidateJoinGameId(rawGameId, out normalizedGameId, out string validationError))
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
        IsMultiplayerScreenRequested = true;
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
        IsMultiplayerScreenRequested = false;
        // UITK handles panel visibility locally.
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
        ActivePbpGamesChanged?.Invoke();
        RecomputePbpBadge();

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
            if (TryGetIsYourTurnFromManifest(summary, out bool isYourTurn, out _, out _) && isYourTurn)
            {
                countMyTurn++;
            }
        }

        if (PbpBadgeCountMyTurn == countMyTurn)
            return;

        PbpBadgeCountMyTurn = countMyTurn;
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
        if (!isServerOnline)
        {
            SetImportStatus(BuildConnectivityWarningStatus());
            return;
        }

        PlayByPost();
    }

    public void Multiplayer_JoinGame()
    {
        JoinPlayByPostFromInput();
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
        public bool isPlayerTurn;
        public int turnNumber;
        public bool gameOver;
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

    private static string BuildPbpVersionText(string gameId)
    {
        if (TryGetLocalPbpSnapshotProtocolVersion(gameId, out int protocolVersion))
        {
            return $"PBp Version: {protocolVersion}";
        }

        return "PBp Version: Unverified";
    }

    private static bool TryGetPbpVersionMismatchWarning(int gameProtocolVersion, out string warning)
    {
        warning = null;
        if (gameProtocolVersion <= 0)
        {
            return false;
        }

        int supportedVersion = TurnManager.PbpProtocolVersion;
        if (gameProtocolVersion == supportedVersion)
        {
            return false;
        }

        warning = $"This game uses PBp {gameProtocolVersion}. Your app supports PBp {supportedVersion}. This match cannot be opened on this build.";
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

        return TryGetPbpVersionMismatchWarning(gameProtocolVersion, out warning);
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[MP] Local cleanup gameId={gameId} manifestUpdated={manifestUpdated} deletedTurnsFolder={deletedTurnsFolder} deletedSaveJson={deletedSaveJson} deletedImportedJson={deletedImportedJson} prefsChanged={prefsChanged}");
#endif
    }
}
