using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class MainMenuController
{
    public static bool CanStartPolyGame => Application.isEditor || (PublicWebBuild.UsesGoogleAccounts && WebAccountClient.CanPlayPoly);
    public void StartPolyGame()
    {
        if (!CanStartPolyGame) return;
        if (PublicWebBuild.UsesGoogleAccounts) StartCoroutine(StartPolyForOwner()); else LoadPolyGame();
    }
    private IEnumerator StartPolyForOwner()
    {
        bool verified = false;
        yield return WebAccountClient.Initialize(ok => verified = ok);
        if (verified && CanStartPolyGame) LoadPolyGame();
    }
    private void LoadPolyGame()
    {
        SaveLoadRequest.ClearPending();
        CameraController.ClearPendingRestoreState();
        GameModeSelection.TryConsume(out _);
        MapSizeSelection.TryConsume(out _);
        AIVsAIBatchRunController.ClearAll();
        SceneManager.LoadScene(PolyGameController.SceneName);
    }
}
