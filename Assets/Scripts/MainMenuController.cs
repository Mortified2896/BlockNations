using UnityEngine;
using UnityEngine.SceneManagement;

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
        GameModeSelection.SetPendingMode(TurnManager.GameMode.VsAI);
        SceneManager.LoadScene(gameplaySceneName);

        if (modeSelectionPanel != null)
        {
            modeSelectionPanel.SetActive(false);
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

    public void ContinueLastSave()
    {
        string path = System.IO.Path.Combine(Application.persistentDataPath, "save.json");
        if (!System.IO.File.Exists(path))
        {
            Debug.LogWarning("No save file found at " + path + ". Continue canceled; staying in menu.");
            return;
        }

        Debug.Log("Continue requested. Loading save at " + path);
        SaveLoadRequest.RequestLoad(path);
        SceneManager.LoadScene(gameplaySceneName);
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}
