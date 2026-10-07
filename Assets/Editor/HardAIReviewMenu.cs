using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public static class HardAIReviewMenu
{
    [MenuItem("Tools/Block Nations/Prepare Hard AI Tournament Review")]
    public static void Prepare()
    {
        if (!EditorApplication.isPlaying || SceneManager.GetActiveScene().path != "Assets/Scenes/MainMenu.unity")
            throw new InvalidOperationException("Enter Play Mode in MainMenu before preparing the review. This command does not load scenes or start games.");
        MainMenuUITKView view = UnityEngine.Object.FindFirstObjectByType<MainMenuUITKView>();
        if (view == null) throw new InvalidOperationException("Main menu view is unavailable.");
        view.PrepareHardAIComparisonReview();
        HardAIInspectorWindow.Open();
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        Type gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        EditorWindow gameView = EditorWindow.GetWindow(gameViewType);
        gameView.maximized = true;
        gameView.Focus();
        VisualElement root = view.GetComponent<UIDocument>().rootVisualElement;
        Debug.Log($"[Hard AI review] Prepared 11x11, three opponents, six seat-swapped games, normal speed, no loop; Game view maximized for laptop inspection. Human Hard selector display={root.Q<Button>("GeneralSettingsAiStyleHardButton").resolvedStyle.display}. Start has NOT been pressed.");
    }
}
