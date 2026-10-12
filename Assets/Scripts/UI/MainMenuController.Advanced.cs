using UnityEngine.SceneManagement;
using UnityEngine;
using System.Collections;

public partial class MainMenuController
{
    public static bool CanStartAdvancedGame => Application.isEditor ||
        (PublicWebBuild.UsesGoogleAccounts && WebAccountClient.IsAdmin);

    public void StartAdvancedGame()
    {
        if (!CanStartAdvancedGame) return;
        if (PublicWebBuild.UsesGoogleAccounts) StartCoroutine(StartAdvancedForAdmin());
        else LoadAdvancedGame();
    }

    private IEnumerator StartAdvancedForAdmin()
    {
        bool verified = false;
        yield return WebAccountClient.Initialize(ok => verified = ok);
        if (verified && CanStartAdvancedGame) LoadAdvancedGame();
    }

    private void LoadAdvancedGame()
    {
        SaveLoadRequest.ClearPending();
        CameraController.ClearPendingRestoreState();
        AIVsAIBatchRunController.ClearAll();
        // Clear cross-scene selections so an advanced launch cannot affect the
        // next ordinary match. The explicitly wired scene owns its rules/AI.
        GameModeSelection.TryConsume(out _);
        MapSizeSelection.TryConsume(out _);
        AIRecruitVariantSelection.TryConsume(out _);
        SnapshotHistorySelection.TryConsume(out _);
        AIVsAIDebugSelection.TryConsume(out _);
        SceneManager.LoadScene(AdvancedMatchController.SceneName);
    }
}
