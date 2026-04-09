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

    private const string PlayByPostGameIdKeyRaw = "pbp_gameId";
    private const string ReturnToMultiplayerPaneKeyRaw = "ui_returnToMultiplayerPane";
    private static string PlayByPostGameIdKey => DevClientInstanceScope.ScopePlayerPrefsKey(PlayByPostGameIdKeyRaw);
    private static string ReturnToMultiplayerPaneKey => DevClientInstanceScope.ScopePlayerPrefsKey(ReturnToMultiplayerPaneKeyRaw);

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
        if (TurnManager.Instance != null && TurnManager.Instance.IsPbpEndgameMenuExitBlocked)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("QuitToMainMenu blocked: PBp endgame submit flow is active; use the endgame button.");
#endif
            return;
        }

        DoQuitToMainMenu();
    }

    private void DoQuitToMainMenu()
    {
        TurnManager tm = TurnManager.Instance;
        bool shouldReturnToMultiplayerPane = tm != null
            ? tm.currentMode == TurnManager.GameMode.PlayByPost
            : !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty));

        if (shouldReturnToMultiplayerPane)
        {
            PlayerPrefs.SetInt(ReturnToMultiplayerPaneKey, 1);
            PlayerPrefs.Save();
        }

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
