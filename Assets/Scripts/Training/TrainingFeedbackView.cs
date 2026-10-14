using UnityEngine;
using UnityEngine.UIElements;

// Authored inspector references; the standard report form stays available in spectator mode.
public sealed class TrainingFeedbackView : MonoBehaviour
{
    [SerializeField] private UIDocument document;
    [SerializeField] private TrainingViewer viewer;
    [SerializeField] private ThemeStyleSheet theme;
    private FeedbackUITKController feedback;
    private VisualElement boundRoot;
    public bool IsFormVisible => feedback != null && feedback.IsFormVisible;
    private void LateUpdate()
    {
        if (document == null || viewer == null) return;
        document.enabled = viewer.enabled;
        if (!document.enabled) return;
        if (document.panelSettings.themeStyleSheet == null) document.panelSettings.themeStyleSheet = theme;
        var root = document.rootVisualElement;
        if (root == null) return;
        root.style.position = Position.Absolute;
        root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
        root.style.width = Length.Percent(100);
        root.style.height = Length.Percent(100);
        root.pickingMode = PickingMode.Ignore;
        var content = root.Q<VisualElement>("GameplayTopHudRoot");
        if (content == null) return;
        content.pickingMode = PickingMode.Ignore;
        if (root != boundRoot)
        {
            feedback?.Dispose();boundRoot = root;
            content.Q<VisualElement>("TopBar").style.display = DisplayStyle.None;
            content.Q<VisualElement>("TournamentStandingsPanel").style.display = DisplayStyle.None;
            content.Q<Button>("FeedbackButton").style.display = DisplayStyle.None;
            feedback = new FeedbackUITKController(this, root, () => "Training/model selection", () => "Training inspector", () => viewer.Round);
        }
        feedback.RefreshSafeArea();
    }
    // The spectator's IMGUI boards render over UITK. Keep its utility above those
    // boards, then let the shared UITK form own the screen while it is displayed.
    public void DrawButton()
    {
        if (viewer == null || !viewer.enabled || IsFormVisible) return;
        float scale = TrainingOverlay.Scale;
        var button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(17 * scale) };
        var area = Screen.safeArea;
        if (GUI.Button(new Rect(area.xMax - 146 * scale, Screen.height - area.yMax + 16 * scale, 130 * scale, 48 * scale), "Feedback", button))
            feedback?.Open();
    }
    public void OnBrowserFeedbackClosed() => feedback?.BrowserClosed();
    private void OnDisable() { feedback?.Dispose();feedback=null;boundRoot=null; }
}
