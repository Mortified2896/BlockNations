using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class TutorialUITKView : IDisposable
{
    private readonly VisualElement root, card, demoBlocker;
    private readonly TutorialVisualGuide guide;
    private readonly UIDocument topHud, bottomHud, unitPanel, cityPanel;
    private readonly Camera camera;
    private readonly Label progress, title, instruction, status;
    private readonly Button next, secondary, exit;
    private readonly Action onNext, onSecondary, onExit;
    private bool demonstrating;

    public TutorialUITKView(UIDocument document, UIDocument topHud, UIDocument bottomHud, UIDocument unitPanel, UIDocument cityPanel, Camera camera,
        Action onNext, Action onSecondary, Action onExit)
    {
        root = document.rootVisualElement;
        this.topHud = topHud; this.bottomHud = bottomHud; this.unitPanel = unitPanel; this.cityPanel = cityPanel; this.camera = camera;
        this.onNext = onNext; this.onSecondary = onSecondary; this.onExit = onExit;
        card = Required<VisualElement>("TutorialCard");
        demoBlocker = Required<VisualElement>("TutorialDemoBlocker");
        guide = new TutorialVisualGuide(root);
        progress = Required<Label>("TutorialProgress");
        title = Required<Label>("TutorialTitle");
        instruction = Required<Label>("TutorialInstruction");
        status = Required<Label>("TutorialStatus");
        next = Required<Button>("TutorialNext"); secondary = Required<Button>("TutorialDemonstrate");
        exit = Required<Button>("TutorialExit");
        root.pickingMode = PickingMode.Ignore;
        next.clicked += onNext; secondary.clicked += onSecondary; exit.clicked += onExit;
    }

    private T Required<T>(string name) where T : VisualElement => root.Q<T>(name) ??
        throw new InvalidOperationException("Missing tutorial UXML reference: " + name);

    public void Show(TutorialStep step, bool completed)
    {
        progress.text = step == TutorialStep.Welcome ? "7×7 · Warrior / Rider / Archer" : step == TutorialStep.Complete
            ? "Tutorial complete" : $"Lesson {(int)step} of {TutorialLesson.LessonCount}";
        title.text = TutorialLesson.Title(step);
        instruction.text = TutorialLesson.Instruction(step);
        status.text = completed ? TutorialLesson.Result(step) : "";
        status.style.display = completed ? DisplayStyle.Flex : DisplayStyle.None;
        next.text = step == TutorialStep.Welcome ? "Start" : step == TutorialStep.Complete ? "Back to menu" : "Continue";
        secondary.text = step == TutorialStep.Complete ? "Feedback" : completed ? "Try again" : "Show me";
        secondary.style.display = step == TutorialStep.Welcome ? DisplayStyle.None : DisplayStyle.Flex;
        next.SetEnabled(!demonstrating);
        secondary.SetEnabled(!demonstrating);
    }

    public void SetStatus(string message) { status.text = message; status.style.display = DisplayStyle.Flex; }

    public void SetDemonstrating(bool active)
    {
        demonstrating = active;
        demoBlocker.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
        next.SetEnabled(!active); secondary.SetEnabled(!active);
    }

    public void Layout(GridManager grid, Unit unit, TutorialStep step, bool completed, bool unitSelected, bool citySelected)
    {
        if (root.panel == null || root.worldBound.width <= 0 || Screen.height <= 0) return;
        Rect bounds = root.worldBound;
        var safe = Screen.safeArea;
        Vector2 safeStart = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(safe.xMin, Screen.height - safe.yMax));
        Vector2 safeEnd = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(safe.xMax, Screen.height - safe.yMin));
        bool portrait = bounds.height > bounds.width;
        float scale = portrait ? Mathf.Clamp(bounds.width / 780f, 0.5f, 1.5f) : Mathf.Clamp(bounds.height / 1080f, 0.7f, 1.5f);
        float gap = 20 * scale;
        float hudBottom = topHud.rootVisualElement.Q<VisualElement>("TopBar")?.worldBound.yMax ?? safeStart.y;
        var controls = bottomHud.rootVisualElement.Q<VisualElement>("DefaultBottomPanel");
        if (controls != null)
        {
            // Teach the game's Next control only in its turn lesson. Exit is
            // always at the top; duplicate Menu/Next controls distract here.
            controls.style.display = step == TutorialStep.EndTurn ? DisplayStyle.Flex : DisplayStyle.None;
            var menu = controls.Q<Button>("MenuButton");
            if (menu != null) menu.style.display = DisplayStyle.None;
            controls.style.minHeight = (portrait ? 112 : 100) * scale;
            controls.style.paddingTop = controls.style.paddingBottom = 12 * scale;
            foreach (var button in controls.Query<Button>().ToList())
            {
                button.style.height = (portrait ? 88 : 76) * scale;
                button.style.fontSize = 32 * scale;
                button.style.minWidth = 0;
            }
        }
        float controlsTop = step == TutorialStep.EndTurn && controls != null ? controls.worldBound.yMin : safeEnd.y;
        exit.style.left = safeStart.x - bounds.xMin + gap;
        exit.style.top = safeStart.y - bounds.yMin + 8 * scale;
        exit.style.width = 184 * scale;
        exit.style.height = 92 * scale;
        exit.style.fontSize = 24 * scale;
        card.style.left = safeStart.x - bounds.xMin + gap;
        card.style.top = Mathf.Max(safeStart.y, hudBottom) - bounds.yMin + gap;
        card.style.width = portrait ? safeEnd.x - safeStart.x - gap * 2 : Mathf.Min(640 * scale, bounds.width * 0.38f);
        card.style.paddingLeft = card.style.paddingRight = 22 * scale;
        card.style.paddingTop = card.style.paddingBottom = 18 * scale;
        title.style.fontSize = 38 * scale;
        instruction.style.fontSize = 32 * scale;
        progress.style.fontSize = status.style.fontSize = 26 * scale;
        progress.style.marginBottom = 8 * scale;
        title.style.marginBottom = 12 * scale;
        instruction.style.marginBottom = status.style.marginBottom = 10 * scale;
        foreach (var row in card.Query<VisualElement>(className: "tutorial-actions").ToList()) row.style.marginTop = 8 * scale;
        foreach (Button button in new[] { next, secondary })
        {
            button.style.fontSize = 28 * scale;
            button.style.height = (portrait ? 92 : 68) * scale;
            button.style.paddingLeft = button.style.paddingRight = 16 * scale;
            button.style.marginLeft = button.style.marginRight = button.style.marginTop = button.style.marginBottom = 4 * scale;
        }
        var feedbackHudButton = topHud.rootVisualElement.Q<Button>("FeedbackButton");
        if (portrait && feedbackHudButton != null) feedbackHudButton.style.minHeight = 92 * scale;
        float panelLeft = portrait ? 0 : card.worldBound.xMax - bounds.xMin + gap * 2;
        PlaceGameplayPanel(bottomHud.rootVisualElement.Q<VisualElement>("GameplayBottomHudRoot"), panelLeft);
        PlaceGameplayPanel(unitPanel.rootVisualElement.Q<VisualElement>("UnitPanelHudRoot"), panelLeft);
        PlaceGameplayPanel(cityPanel.rootVisualElement.Q<VisualElement>("CityPanelHudRoot"), panelLeft);
        foreach (var button in cityPanel.rootVisualElement.Query<Button>().ToList())
        {
            button.style.minWidth = 0;
            if (!portrait) button.style.fontSize = 32 * scale;
        }

        // Fit the entire practice board into the space left by the actual UI.
        // No camera restore or panning can hide the required tutorial target.
        float left = portrait ? safeStart.x + gap : card.worldBound.xMax + gap * 2;
        float right = safeEnd.x - gap;
        float top = portrait ? card.worldBound.yMax + gap : Mathf.Max(safeStart.y, hudBottom) + gap;
        // During a demonstration, prevent extra board/recruit/turn actions.
        // The lesson controls, Exit and Feedback remain above this region.
        demoBlocker.style.left = left - bounds.xMin;
        demoBlocker.style.top = top - bounds.yMin;
        demoBlocker.style.width = right - left;
        demoBlocker.style.height = safeEnd.y - top;
        float bottom = Mathf.Min(safeEnd.y - gap, controlsTop - gap);
        // Unit/city panels open above the controls. Leave room for those too.
        bottom -= 140 * scale;
        // Short phone screens must also respect an open unit/recruit panel.
        // Its actual height can exceed the allowance used for the closed panel.
        foreach (var panel in new[] { unitPanel.rootVisualElement.Q<VisualElement>("UnitPanelContainer"),
            cityPanel.rootVisualElement.Q<VisualElement>("CityPanelContainer") })
            if (panel != null && panel.resolvedStyle.display != DisplayStyle.None && panel.worldBound.height > 0)
                bottom = Mathf.Min(bottom, panel.worldBound.yMin - gap);
        if (!portrait) bottom = Mathf.Min(bottom, safeEnd.y - 300 * scale - gap);
        float availableWidth = Mathf.Max(80, right - left), availableHeight = Mathf.Max(80, bottom - top);
        float panelPerScreenPixel = bounds.height / Screen.height;
        float visibleWidth = availableWidth / panelPerScreenPixel;
        float visibleHeight = availableHeight / panelPerScreenPixel;
        float span = (grid.width + 0.4f) * grid.tileSize;
        // Orthographic size is half the vertical span. A width constraint
        // must include the camera aspect, especially on a portrait phone.
        float ortho = Mathf.Max(span * Screen.height / (2f * visibleHeight),
            span * Screen.width / (2f * visibleWidth * camera.aspect));
        camera.orthographicSize = ortho;
        float cx = (left + right) * 0.5f - bounds.xMin;
        float cy = (top + bottom) * 0.5f - bounds.yMin;
        camera.transform.position = new Vector3(
            -(cx / bounds.width - 0.5f) * 2 * ortho * camera.aspect,
            (cy / bounds.height - 0.5f) * 2 * ortho, -10);

        guide.Begin(bounds, scale);
        if (step == TutorialStep.Welcome)
        {
            var feedbackButton = topHud.rootVisualElement.Q<Button>("FeedbackButton");
            if (feedbackButton != null) guide.Target(feedbackButton.worldBound, "Help / feedback", false);
        }
        else if (step == TutorialStep.EndTurn)
        {
            var nextButton = bottomHud.rootVisualElement.Q<Button>("NextButton");
            if (nextButton != null && !completed) guide.Target(nextButton.worldBound, "End turn", true);
        }
        else if (step != TutorialStep.Complete)
        {
            int target = TutorialLesson.Target(step);
            if (target < 0 || !grid.TryGetTile(target % 7, target / 7, out TileVisibility tile)) return;
            if (completed)
                guide.Target(TileRect(tile.transform.position, grid.tileSize), TutorialLesson.Result(step), false);
            else if (step == TutorialStep.Recruit)
            {
                var recruit = cityPanel.rootVisualElement.Q<Button>("RecruitwarriorButton");
                if (citySelected && recruit != null)
                    guide.Target(recruit.worldBound, TutorialLesson.TargetCue(step), true);
                else guide.Target(TileRect(tile.transform.position, grid.tileSize), "1 · Open city", true);
            }
            else
            {
                if (unit != null) guide.Origin(TileRect(unit.transform.position, grid.tileSize), TutorialLesson.OriginCue(step), unitSelected);
                guide.Target(TileRect(tile.transform.position, grid.tileSize), TutorialLesson.TargetCue(step), unitSelected);
                guide.Connect();
            }
        }
    }

    private static void PlaceGameplayPanel(VisualElement panelRoot, float left)
    {
        if (panelRoot == null) return;
        panelRoot.style.left = left;
        panelRoot.style.right = 0;
    }

    private Rect TileRect(Vector3 world, float size)
    {
        Vector3 a = camera.WorldToScreenPoint(world + new Vector3(-size / 2, size / 2));
        Vector3 b = camera.WorldToScreenPoint(world + new Vector3(size / 2, -size / 2));
        Vector2 start = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(a.x, Screen.height - a.y));
        Vector2 end = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(b.x, Screen.height - b.y));
        return Rect.MinMaxRect(start.x, start.y, end.x, end.y);
    }

    public void Dispose()
    {
        next.clicked -= onNext; secondary.clicked -= onSecondary; exit.clicked -= onExit;
    }
}
