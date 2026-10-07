using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[InitializeOnLoad]
public static class HardAIReviewMenu
{
    private const string BackgroundScopeKey = "BlockNations.HardAI.ReviewBackgroundScope";
    private const string OriginalBackgroundKey = "BlockNations.HardAI.ReviewOriginalBackground";

    static HardAIReviewMenu() => EditorApplication.playModeStateChanged += RestoreBackgroundExecution;

    [MenuItem("Tools/Block Nations/Prepare Hard AI Tournament Review")]
    public static void Prepare()
    {
        if (!EditorApplication.isPlaying || SceneManager.GetActiveScene().path != "Assets/Scenes/MainMenu.unity")
            throw new InvalidOperationException("Enter Play Mode in MainMenu before preparing the review. This command does not load scenes or start games.");
        MainMenuUITKView view = UnityEngine.Object.FindFirstObjectByType<MainMenuUITKView>();
        if (view == null) throw new InvalidOperationException("Main menu view is unavailable.");
        view.PrepareHardAIComparisonReview();
        if (!SessionState.GetBool(BackgroundScopeKey, false))
        {
            SessionState.SetBool(OriginalBackgroundKey, Application.runInBackground);
            SessionState.SetBool(BackgroundScopeKey, true);
        }
        Application.runInBackground = true;
        HardAIInspectorWindow.Open();
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        Type gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        EditorWindow gameView = EditorWindow.GetWindow(gameViewType);
        gameView.maximized = true;
        gameView.Focus();
        VisualElement root = view.GetComponent<UIDocument>().rootVisualElement;
        Debug.Log($"[Hard AI review] Prepared 11x11, three opponents, six seat-swapped games, Ultra Fast, no loop; Game view maximized for laptop inspection. Background execution is scoped to this Play Mode session. Human Hard selector display={root.Q<Button>("GeneralSettingsAiStyleHardButton").resolvedStyle.display}. Start has NOT been pressed.");
    }

    [MenuItem("Tools/Block Nations/AI Review Speed/Normal")]
    public static void UseNormalSpeed() => SetSpeed(TurnManager.AIVsAIBatchSpeedPreset.Normal);
    [MenuItem("Tools/Block Nations/AI Review Speed/Fast")]
    public static void UseFastSpeed() => SetSpeed(TurnManager.AIVsAIBatchSpeedPreset.Fast);
    [MenuItem("Tools/Block Nations/AI Review Speed/Very Fast")]
    public static void UseVeryFastSpeed() => SetSpeed(TurnManager.AIVsAIBatchSpeedPreset.VeryFast);
    [MenuItem("Tools/Block Nations/AI Review Speed/Ultra Fast")]
    public static void UseUltraFastSpeed() => SetSpeed(TurnManager.AIVsAIBatchSpeedPreset.UltraFast);

    private static void SetSpeed(TurnManager.AIVsAIBatchSpeedPreset speed)
    {
        TurnManager manager = UnityEngine.Object.FindFirstObjectByType<TurnManager>();
        if (!EditorApplication.isPlaying || manager == null || !manager.TrySetAIVsAIDebugSpeed(speed))
            throw new InvalidOperationException("Live review speed requires a running AI-vs-AI match.");
        Debug.Log($"[Hard AI review] Live speed set to {speed}; policy search work is unchanged.");
    }

    private static void RestoreBackgroundExecution(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode || !SessionState.GetBool(BackgroundScopeKey, false)) return;
        Application.runInBackground = SessionState.GetBool(OriginalBackgroundKey, false);
        SessionState.EraseBool(BackgroundScopeKey);
        SessionState.EraseBool(OriginalBackgroundKey);
    }
}
