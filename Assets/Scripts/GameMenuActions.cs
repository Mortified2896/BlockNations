using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Simple button hooks for saving/loading and returning to the main menu.
/// Drop this on a UI GameObject and wire the public methods to Buttons.
/// </summary>
public class GameMenuActions : MonoBehaviour
{
    [Header("Scenes")]
    public string mainMenuSceneName = "MainMenu";

    public void SaveGame()
    {
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.SaveToFile();
        }
    }

    public void LoadGame()
    {
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.LoadFromFile();
        }
    }

    public void QuitToMainMenu()
    {
        if (!string.IsNullOrEmpty(mainMenuSceneName))
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(mainMenuSceneName);
        }
        else
        {
            Debug.LogWarning("Main menu scene name is not set on GameMenuActions.");
        }
    }
}
