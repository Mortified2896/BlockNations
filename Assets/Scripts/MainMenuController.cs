using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.IO;

/// <summary>
/// Hook this to your MainMenu scene Canvas/buttons to load the gameplay scene
/// with the selected mode.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Scenes")]
    [SerializeField] private string gameplaySceneName = "SampleScene";

    [Header("UI")]
    [SerializeField] private GameObject modeSelectionPanel;
    [SerializeField] private GameObject aiDifficultyPanel;
    [SerializeField] private TMP_Text importStatusText;

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
        GameModeSelection.SetPendingMode(TurnManager.GameMode.PlayByPost);
        SceneManager.LoadScene(gameplaySceneName);

        if (modeSelectionPanel != null)
        {
            modeSelectionPanel.SetActive(false);
        }
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
}
