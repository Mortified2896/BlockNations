using UnityEngine.SceneManagement;

public partial class MainMenuController
{
    public void StartAdvancedGame()
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
