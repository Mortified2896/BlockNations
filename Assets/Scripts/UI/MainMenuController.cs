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
    /*
     * Wiring Instructions (Multiplayer Screen)
     * - Assign `mainMenuPanel` to the root GameObject of the main menu panel.
     * - Assign `multiplayerPanel` to the root GameObject of the multiplayer panel.
     * - Main Menu "Multiplayer" button -> OpenMultiplayerScreen()
     * - Multiplayer "Back" button -> CloseMultiplayerScreen()
     * - Multiplayer "Create" button -> Multiplayer_CreateGame()
     * - Multiplayer "Join" button -> Multiplayer_JoinGame()
     * - Each "Resume" button -> ResumePlayByPostGame(gameId)
     *   - Or set `selectedGameId` from your UI and call ResumePlayByPostGame_FromSelected().
     */

    [Header("Scenes")]
    [SerializeField] private string gameplaySceneName = "SampleScene";

    [Header("UI")]
    [SerializeField] private GameObject modeSelectionPanel;
    [SerializeField] private GameObject aiDifficultyPanel;
    [SerializeField] private TMP_InputField joinGameIdInput;
    [SerializeField] private TMP_Text importStatusText;
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject multiplayerPanel;
    [SerializeField] private GameObject joinPopupPanel;
    [SerializeField] private string selectedGameId;

    [Header("Layout")]
    [SerializeField] private bool autoFitMenuToScreenOnDesktop = true;
    private bool tutorialLaunchQueued;
    private const string PlayByPostGameIdKey = "pbp_gameId";
    private const string PlayByPostIsPlayer1Key = "pbp_isPlayer1";
    private bool isServerOnline = true;
    private Coroutine serverCheckRoutine;

    public event Action ActivePbpGamesChanged;
    public IReadOnlyList<SaveManifestService.ManifestGameSummary> ActivePbpGames => activePbpGames;
    private List<SaveManifestService.ManifestGameSummary> activePbpGames = new List<SaveManifestService.ManifestGameSummary>();

    IEnumerator Start()
    {
        if (joinGameIdInput != null && IsPlaceholderGameId(joinGameIdInput.text))
        {
            joinGameIdInput.text = string.Empty;
        }

        // Wait one frame so UI objects/panels are fully initialized and active state is stable.
        yield return null;
        if (autoFitMenuToScreenOnDesktop)
        {
            // One more frame so layouts are rebuilt after menus are activated.
            yield return null;
            TryAutoFitActiveMenuButtonContainer();
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
        PlayerPrefs.SetString(PlayByPostGameIdKey, gameId);
        PlayerPrefs.SetInt(PlayByPostIsPlayer1Key, 1);
        PlayerPrefs.Save();
        if (ClipboardUtility.TryCopy(gameId))
        {
            Debug.Log($"Play-by-Post game id copied to clipboard ({gameId}).");
            SetImportStatus($"Game code: {gameId} (copied to clipboard)");
        }
        else
        {
            Debug.LogWarning($"Failed to copy Play-by-Post game id to clipboard ({gameId}).");
            SetImportStatus($"Game code: {gameId}");
        }

        GameModeSelection.SetPendingMode(TurnManager.GameMode.PlayByPost);
        SceneManager.LoadScene(gameplaySceneName);

        if (modeSelectionPanel != null)
        {
            modeSelectionPanel.SetActive(false);
        }
    }

    public void JoinPlayByPostFromInput()
    {
        if (!isServerOnline)
        {
            SetImportStatus("Server offline");
            return;
        }

        string gameId = joinGameIdInput != null ? joinGameIdInput.text : null;
        if (!TryValidateJoinGameId(gameId, out string normalizedGameId, out string validationError))
        {
            SetImportStatus(validationError);
            return;
        }

        PlayerPrefs.SetString(PlayByPostGameIdKey, normalizedGameId);
        PlayerPrefs.SetInt(PlayByPostIsPlayer1Key, 0);
        PlayerPrefs.Save();
        SetImportStatus($"Joining game: {normalizedGameId}");

        GameModeSelection.SetPendingMode(TurnManager.GameMode.PlayByPost);
        SceneManager.LoadScene(gameplaySceneName);

        if (modeSelectionPanel != null)
        {
            modeSelectionPanel.SetActive(false);
        }
    }

    public void OpenMultiplayerScreen()
    {
        if (mainMenuPanel != null)
        {
            mainMenuPanel.SetActive(false);
        }

        if (multiplayerPanel != null)
        {
            multiplayerPanel.SetActive(true);
        }

        if (serverCheckRoutine != null)
        {
            StopCoroutine(serverCheckRoutine);
        }

        isServerOnline = false;
        SetImportStatus("Checking server...");
        serverCheckRoutine = StartCoroutine(CheckServerOnlineCoroutine());
    }

    public void CloseMultiplayerScreen()
    {
        if (multiplayerPanel != null)
        {
            multiplayerPanel.SetActive(false);
        }

        if (mainMenuPanel != null)
        {
            mainMenuPanel.SetActive(true);
        }
    }

    public void OpenJoinPopup()
    {
        if (joinPopupPanel != null)
        {
            joinPopupPanel.SetActive(true);
        }
    }

    public void CloseJoinPopup()
    {
        if (joinPopupPanel != null)
        {
            joinPopupPanel.SetActive(false);
        }

        if (joinGameIdInput != null)
        {
            joinGameIdInput.text = "";
        }
    }

    public void RefreshMultiplayerList()
    {
        activePbpGames = SaveManifestService.GetActivePlayByPostGames();
        ActivePbpGamesChanged?.Invoke();

        if (isServerOnline)
        {
            SetImportStatus(activePbpGames.Count > 0
                ? $"{activePbpGames.Count} active games"
                : "No active games");
        }
        else
        {
            SetImportStatus("Server offline");
        }
    }

    public void ResumePlayByPostGame(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            return;

        PlayerPrefs.SetString(PlayByPostGameIdKey, gameId);
        PlayerPrefs.Save();
        SceneManager.LoadScene(gameplaySceneName);
    }

    public void ResumePlayByPostGame_FromSelected()
    {
        ResumePlayByPostGame(selectedGameId);
    }

    public void Multiplayer_CreateGame()
    {
        if (!isServerOnline)
        {
            SetImportStatus("Server offline");
            return;
        }

        PlayByPost();
    }

    public void Multiplayer_JoinGame()
    {
        if (!isServerOnline)
        {
            SetImportStatus("Server offline");
            return;
        }

        OpenJoinPopup();
    }

    public void ContinueLastSave()
    {
        string path = System.IO.Path.Combine(Application.persistentDataPath, "save.json");
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
        if (importStatusText != null)
        {
            importStatusText.text = message;
        }
        else
        {
            Debug.LogWarning(message);
        }
    }

    // Allows runtime-built UI to register a status text field.
    public void ConfigureImportUI(GameObject panel, TMP_InputField input, TMP_Text status)
    {
        importStatusText = status;
        if (importStatusText != null)
        {
            importStatusText.text = string.Empty;
        }
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

    private IEnumerator CheckServerOnlineCoroutine()
    {
        bool online = true;
        bool hasCheck = false;

        HttpTurnTransport httpTransport = FindObjectOfType<HttpTurnTransport>();
        if (httpTransport != null)
        {
            hasCheck = true;
            bool probeResult = false;
            yield return StartCoroutine(httpTransport.CheckServerReachable(result => probeResult = result));
            online = probeResult;
        }
        else
        {
            TurnTransportProvider provider = FindObjectOfType<TurnTransportProvider>();
            if (provider != null)
            {
                hasCheck = true;
                ITurnTransport transport = provider.GetTransport();
                online = transport != null && transport.IsAvailable;
            }
        }

        if (!hasCheck)
        {
            online = true;
        }

        isServerOnline = online;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"Multiplayer server check complete. online={isServerOnline}, checkedTransport={hasCheck}");
#endif

        SetImportStatus(isServerOnline ? "Server online" : "Server offline");
        RefreshMultiplayerList();
        serverCheckRoutine = null;
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
}
