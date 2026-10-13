using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class MainMenuController
{
    public static bool CanStartPolyGame => Application.isEditor || (PublicWebBuild.UsesGoogleAccounts && WebAccountClient.CanPlayPoly);
    public void StartPolyGame()
    {
        if (!CanStartPolyGame) return;
        if (PublicWebBuild.UsesGoogleAccounts) StartCoroutine(StartPolyForOwner()); else SceneManager.LoadScene(PolyGameController.SceneName);
    }
    private IEnumerator StartPolyForOwner()
    {
        bool verified = false;
        yield return WebAccountClient.Initialize(ok => verified = ok);
        if (verified && CanStartPolyGame) SceneManager.LoadScene(PolyGameController.SceneName);
    }
}
