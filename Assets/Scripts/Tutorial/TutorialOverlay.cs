using System;
using System.Collections;
using System.Collections.Generic;
using Object = UnityEngine.Object;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Lightweight, runtime-built tutorial overlay for the gameplay scene.
/// It does not block world interaction (panel only) and is skippable.
/// </summary>
public class TutorialOverlay : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Top margin (in reference pixels) so the panel isn't flush with the screen top.")]
    public float panelTopMargin = 25f;
    [Tooltip("Intro step only: extra top padding so the panel isn't glued to the screen top.")]
    public float introPanelTopPadding = 50f;
    [Tooltip("Reserved space (in pixels) between the top HUD/safe area and the tutorial panel.")]
    public float topHudPadding = 120f;

    [Header("Pointer Tuning")]
    [Tooltip("Arrow thickness in canvas pixels (length is driven by the distance to the target).")]
    public float pointerArrowThickness = 26f;
    [Tooltip("Extra spacing (in pixels) between the arrow tip and the highlight center.")]
    public float pointerArrowTargetPadding = 20f;

    [Header("Scenes")]
    public string mainMenuSceneName = "MainMenu";

    [Header("Tutorial Tuning")]
    [Tooltip("Force AI delay during the tutorial so scripted events have time to run.")]
    public float minimumAiTurnDelayDuringTutorial = 0.8f;
    [Tooltip("How long (in seconds) before a step shows an extra hint, if not completed.")]
    public float defaultHintAfterSeconds = 10f;

    private Canvas canvas;
    private GraphicRaycaster graphicRaycaster;
    private RectTransform safeAreaRoot;
    private RectTransform panelRect;
    private RectTransform arrowStartRect;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI bodyText;
    private Button nextButton;
    private TextMeshProUGUI nextLabel;

    private class TutorialStep
    {
        public string title;
        public string body;
        public string nextLabel;
        public Func<bool> canAdvance;
        public Action onEnter;
        public Action onExit;
        public bool autoAdvance;
        public float autoAdvanceDelaySeconds = 0.25f;
        public float hintAfterSeconds;
        public string hintText;
        public Func<string> dynamicBody;
    }

    private readonly List<TutorialStep> steps = new List<TutorialStep>();
    private int stepIndex = 0;
    private bool isShowing;

    private TurnManager tm;
    private GridManager grid;
    private Camera cam;
    private CameraController cameraController;

    private bool prevDisableAI;
    private float prevAiTurnDelay;
    private bool prevAutoEndTurnWhenNoActions;

    // Temporary UI highlights (restored on step changes).
    private TMP_Text goldTmp;
    private Color goldTmpOriginalColor;
    private bool goldTmpOriginalColorSet;
    private bool goldTmpHighlighted;

    private float stepEnterUnscaledTime;
    private int stepEnterFrame;
    private bool hintDirty;

    private int baselineSeenTiles;
    private int baselinePlayerActionSum;
    private Vector3 baselineUnitPos;
    private float baselineZoom;
    private int baselineEnemyUnitCount;

    private City playerCity;
    private City enemyCity;
    private Unit warrior1;
    private Unit warrior2;
    private Unit enemy1;
    private Unit enemy2;
    private Unit boss;
    private Vector3 turn7MoveTarget;
    private Vector2Int turn7MoveDir = new Vector2Int(1, 1);

    private Coroutine scriptedRoutine;
    private Coroutine autoAdvanceRoutine;
    private int autoAdvanceStepIndex = -1;

    // Pointer UI
    private RectTransform pointerLayer;
    private RectTransform pointerArrowRect;
    private Image pointerArrowImage;
    private RectTransform pointerHighlightRect;
    private Image pointerHighlightImage;
    private bool pointerShowHighlight;
    private bool pointerWorldMode;
    private Vector3 pointerWorldPosition;
    private RectTransform pointerUiTarget;
    private bool pointerVisible;
    private float pointerPulseT;
    private bool pointerShowArrow;
    private bool pointerHighlightYellow;

    private enum ArrowAnchorPlacement
    {
        RightMiddle,
        TopRight,
        PanelCenter
    }

    private ArrowAnchorPlacement arrowAnchorPlacement = ArrowAnchorPlacement.RightMiddle;

    private Button menuButton;
    private bool menuButtonPrevInteractable;

    // Gameplay HUD buttons (bottom Menu / End Turn / Next) that should hide when a panel (city/unit) is open.
    private Button gameplayHudMenuButton;
    private Button gameplayHudEndTurnOrNextButton;
    private bool gameplayHudHidden;

    private bool cameraLockActive;
    private Vector3 cameraLockWorldPosition;

    // Panel layout (we adjust per-step so it doesn't cover what we're explaining).
    private Vector2 defaultPanelAnchorMin;
    private Vector2 defaultPanelAnchorMax;
    private Vector2 defaultPanelOffsetMin;
    private Vector2 defaultPanelOffsetMax;
    private Vector2 defaultPanelPivot;
    private Vector2 defaultPanelAnchoredPosition;

    private static bool IsMobilePlatform()
    {
        return Application.isMobilePlatform;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        // Only spawn the tutorial when explicitly requested from the main menu.
        if (!TutorialLaunch.IsShowRequested())
            return;
    }

    void Awake()
    {
        EnsureEventSystem();
        BindFromScene();
        if (nextButton != null)
        {
            nextButton.onClick.RemoveListener(OnNextClicked);
            nextButton.onClick.AddListener(OnNextClicked);
        }
        CaptureDefaultPanelLayout();
        HidePointer();
        Show(false);
    }

    IEnumerator Start()
    {
        yield return WaitForTurnManagerReady();

        tm = TurnManager.Instance;
        if (tm == null)
        {
            Show(false);
            enabled = false;
            yield break;
        }

        bool forced = TutorialLaunch.TryConsumeShowRequest();
        if (!forced)
        {
            Show(false);
            enabled = false;
            yield break;
        }

        // Tutorial assumes a single player vs AI flow.
        if (tm.currentMode != TurnManager.GameMode.VsAI)
        {
            Show(false);
            enabled = false;
            yield break;
        }

        grid = tm.gridManager;
        cam = Camera.main;

        prevDisableAI = tm.disableAI;
        prevAiTurnDelay = tm.aiTurnDelay;
        prevAutoEndTurnWhenNoActions = tm.autoEndTurnWhenNoActions;
        tm.disableAI = true;
        tm.aiTurnDelay = Mathf.Max(tm.aiTurnDelay, minimumAiTurnDelayDuringTutorial);
        tm.autoEndTurnWhenNoActions = true;

        TutorialGate.SetActive(true);
        TutorialGate.ClearAll();

        CacheCitiesAndUnits();
        BuildSteps();
        stepIndex = 0;
        EnterStep(stepIndex);
        Show(true);
    }

    IEnumerator WaitForTurnManagerReady()
    {
        while (TurnManager.Instance == null)
            yield return null;

        // Wait until the mode is chosen/loaded so we can decide whether to show.
        while (TurnManager.Instance.currentMode == TurnManager.GameMode.None)
            yield return null;
    }

    void Update()
    {
        if (!isShowing)
            return;

        if (tm == null || tm.gameOver)
        {
            LeaveTutorial(markCompleted: true);
            return;
        }

        if (stepIndex < 0 || stepIndex >= steps.Count)
        {
            nextButton.interactable = true;
            return;
        }

        UpdatePointerVisuals();

        TutorialStep step = steps[stepIndex];
        bool canAdvance = step.canAdvance == null || step.canAdvance();
        nextButton.interactable = canAdvance;

        UpdateGameplayHudButtonsVisibility();
        EnsureGameplayPanelsDontOverlap();

        if (!canAdvance && ShouldShowHintForCurrentStep() && !hintDirty)
        {
            hintDirty = true;
            ApplyCurrentStep(copy: true);
        }

        if (canAdvance && step.autoAdvance)
        {
            ScheduleAutoAdvance(step.autoAdvanceDelaySeconds);
        }
    }

    void LateUpdate()
    {
        if (!isShowing)
            return;

        if (cameraLockActive)
        {
            ApplyCameraLock();
        }
    }

    private bool ShouldShowHintForCurrentStep()
    {
        if (stepIndex < 0 || stepIndex >= steps.Count)
            return false;

        TutorialStep step = steps[stepIndex];
        float hintAfter = step.hintAfterSeconds > 0f ? step.hintAfterSeconds : defaultHintAfterSeconds;
        if (hintAfter <= 0f)
            return false;

        if (string.IsNullOrWhiteSpace(step.hintText))
            return false;

        return Time.unscaledTime - stepEnterUnscaledTime >= hintAfter;
    }

    private bool HasAnyPlayerUnit()
    {
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit u in units)
        {
            if (u != null && u.isPlayerOwned)
                return true;
        }
        return false;
    }

    private bool HasSelectedPlayerUnit()
    {
        if (UnitSelectionManager.Instance == null)
            return false;

        Unit selected = UnitSelectionManager.Instance.SelectedUnit;
        if (selected == null)
            return false;

        if (!selected.isPlayerOwned)
            return false;

        if (TurnManager.Instance != null && !TurnManager.Instance.CanControlUnit(selected))
            return false;

        return true;
    }

    private bool HasPlayerActedSinceBaseline()
    {
        return GetPlayerActionSum() > baselinePlayerActionSum;
    }

    private int GetPlayerActionSum()
    {
        int sum = 0;
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit u in units)
        {
            if (u == null || !u.isPlayerOwned)
                continue;

            sum += Mathf.Max(0, u.movesUsedThisTurn);
            sum += u.hasAttackedThisTurn ? 100 : 0;
        }
        return sum;
    }

    private bool HasAnyPlayerUnitAttackedThisTurn()
    {
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit u in units)
        {
            if (u != null && u.isPlayerOwned && u.hasAttackedThisTurn)
                return true;
        }
        return false;
    }

    private void OnNextClicked()
    {
        AdvanceToNextStep(playClickSound: true);
    }

    private void AdvanceToNextStep(bool playClickSound)
    {
        if (stepIndex < 0 || stepIndex >= steps.Count)
        {
            if (playClickSound)
                TryPlayNextClickSound();
            CompleteTutorialAndHideOverlay();
            return;
        }

        TutorialStep step = steps[stepIndex];
        if (step.canAdvance != null && !step.canAdvance())
            return;

        if (playClickSound)
            TryPlayNextClickSound();

        step.onExit?.Invoke();

        stepIndex++;
        if (stepIndex >= steps.Count)
        {
            CompleteTutorialAndHideOverlay();
            return;
        }

        EnterStep(stepIndex);
    }

    private static void TryPlayNextClickSound()
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayUIClick();
        }
    }

    private void OnSkip()
    {
        LeaveTutorial(markCompleted: false);
    }

    private void LeaveTutorial(bool markCompleted)
    {
        if (markCompleted)
        {
            TutorialLaunch.MarkCompleted();
        }

        if (tm != null)
        {
            tm.disableAI = prevDisableAI;
            tm.aiTurnDelay = prevAiTurnDelay;
            tm.autoEndTurnWhenNoActions = prevAutoEndTurnWhenNoActions;
        }

        TutorialGate.SetActive(false);

        if (scriptedRoutine != null)
        {
            StopCoroutine(scriptedRoutine);
            scriptedRoutine = null;
        }

        if (autoAdvanceRoutine != null)
        {
            StopCoroutine(autoAdvanceRoutine);
            autoAdvanceRoutine = null;
        }

        Time.timeScale = 1f;
        if (!string.IsNullOrEmpty(mainMenuSceneName))
        {
            SceneManager.LoadScene(mainMenuSceneName);
        }

        Show(false);
        enabled = false;
    }

    private void Show(bool show)
    {
        isShowing = show;
        if (canvas != null)
            canvas.enabled = show;
        if (graphicRaycaster != null)
            graphicRaycaster.enabled = show;
    }

    private void ApplyCurrentStep(bool copy)
    {
        if (titleText == null || bodyText == null || nextLabel == null)
            return;

        if (stepIndex < 0 || stepIndex >= steps.Count)
            return;

        TutorialStep step = steps[stepIndex];

        string title = step.title ?? "Tutorial";
        string next = string.IsNullOrWhiteSpace(step.nextLabel) ? "Next" : step.nextLabel;
        string body = step.dynamicBody != null ? step.dynamicBody() : (step.body ?? string.Empty);

        bool canAdvance = step.canAdvance == null || step.canAdvance();
        if (!canAdvance && ShouldShowHintForCurrentStep())
        {
            body = body + "\n\nHint: " + step.hintText;
        }

        if (copy)
        {
            titleText.text = SanitizeForUI(title);
            bodyText.text = SanitizeForUI(body);
            nextLabel.text = SanitizeForUI(next);
        }
    }

    private static string SanitizeForUI(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;

        // Replace common smart punctuation and mojibake sequences with simple ASCII.
        s = s.Replace("â€”", "-").Replace("â€“", "-");
        s = s.Replace('—', '-').Replace('–', '-');
        s = s.Replace('“', '"').Replace('”', '"').Replace('‘', '\'').Replace('’', '\'');
        s = s.Replace('\u00A0', ' '); // NBSP
        s = s.Replace('ƒ', '-');

        // Strip control chars (keep newlines/tabs).
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\n' || c == '\r' || c == '\t' || !char.IsControl(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private void BindFromScene()
    {
        canvas = GetComponent<Canvas>();
        graphicRaycaster = GetComponent<GraphicRaycaster>();

        safeAreaRoot = FindRectTransform("SafeAreaRoot");
        panelRect = FindRectTransform("SafeAreaRoot/Panel");
        arrowStartRect = FindRectTransform("SafeAreaRoot/Panel/ArrowStart");
        titleText = FindComponent<TextMeshProUGUI>("SafeAreaRoot/Panel/Title");
        bodyText = FindComponent<TextMeshProUGUI>("SafeAreaRoot/Panel/Body");
        nextButton = FindComponent<Button>("SafeAreaRoot/Panel/Buttons/NextButton");
        nextLabel = nextButton != null ? nextButton.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        pointerLayer = FindRectTransform("PointerLayer");
        pointerHighlightRect = FindRectTransform("PointerLayer/Highlight");
        pointerHighlightImage = pointerHighlightRect != null ? pointerHighlightRect.GetComponent<Image>() : null;
        pointerArrowRect = FindRectTransform("PointerLayer/Arrow");
        pointerArrowImage = pointerArrowRect != null ? pointerArrowRect.GetComponent<Image>() : null;

        if (canvas == null)
            Debug.LogError("TutorialOverlay: Missing Canvas component on TutorialCanvas.");
        if (graphicRaycaster == null)
            Debug.LogWarning("TutorialOverlay: No GraphicRaycaster found on TutorialCanvas.");
        if (safeAreaRoot == null)
            Debug.LogError("TutorialOverlay: Missing SafeAreaRoot under TutorialCanvas.");
        if (panelRect == null)
            Debug.LogError("TutorialOverlay: Missing Panel under SafeAreaRoot.");
        if (arrowStartRect == null)
            Debug.LogError("TutorialOverlay: Missing ArrowStart under Panel.");
        if (titleText == null)
            Debug.LogError("TutorialOverlay: Missing Title text under Panel.");
        if (bodyText == null)
            Debug.LogError("TutorialOverlay: Missing Body text under Panel.");
        if (nextButton == null)
            Debug.LogError("TutorialOverlay: Missing NextButton under Panel/Buttons.");
        if (nextLabel == null)
            Debug.LogError("TutorialOverlay: Missing NextButton label TextMeshProUGUI.");
        if (pointerLayer == null)
            Debug.LogError("TutorialOverlay: Missing PointerLayer under TutorialCanvas.");
        if (pointerHighlightRect == null)
            Debug.LogError("TutorialOverlay: Missing Highlight under PointerLayer.");
        if (pointerHighlightImage == null)
            Debug.LogError("TutorialOverlay: Missing Highlight Image component.");
        if (pointerArrowRect == null)
            Debug.LogError("TutorialOverlay: Missing Arrow under PointerLayer.");
        if (pointerArrowImage == null)
            Debug.LogError("TutorialOverlay: Missing Arrow Image component.");

        if (pointerArrowImage != null)
        {
            pointerArrowImage.type = Image.Type.Simple;
            pointerArrowImage.preserveAspect = false;
        }
        if (pointerArrowRect != null)
        {
            pointerArrowRect.pivot = new Vector2(0.5f, 0.5f);
        }
    }

    private RectTransform FindRectTransform(string path)
    {
        Transform t = transform.Find(path);
        if (t == null)
            return null;
        RectTransform rt = t.GetComponent<RectTransform>();
        if (rt == null)
            Debug.LogError("TutorialOverlay: Missing RectTransform at " + path + ".");
        return rt;
    }

    private T FindComponent<T>(string path) where T : Component
    {
        Transform t = transform.Find(path);
        if (t == null)
            return null;
        T component = t.GetComponent<T>();
        if (component == null)
            Debug.LogError("TutorialOverlay: Missing " + typeof(T).Name + " at " + path + ".");
        return component;
    }

    private void HidePointer()
    {
        pointerVisible = false;
        pointerWorldMode = false;
        pointerUiTarget = null;
        pointerPulseT = 0f;
        pointerShowArrow = false;
        pointerShowHighlight = false;
        pointerHighlightYellow = false;

        if (pointerHighlightRect != null) pointerHighlightRect.gameObject.SetActive(false);
        if (pointerArrowRect != null) pointerArrowRect.gameObject.SetActive(false);
    }

    private void PointAtWorld(Vector3 worldPos, bool showArrow = false, bool arrowFromScreenCenter = false, bool showHighlight = true, bool arrowFromRightMiddle = false, bool arrowFromTopRight = false, bool highlightYellow = false)
    {
        pointerVisible = true;
        pointerWorldMode = true;
        pointerWorldPosition = worldPos;
        pointerUiTarget = null;
        pointerPulseT = 0f;
        pointerShowArrow = showArrow;
        pointerShowHighlight = showHighlight;
        pointerHighlightYellow = highlightYellow;
        ApplyArrowAnchorPlacement(arrowFromScreenCenter, arrowFromTopRight);

        if (pointerHighlightRect != null)
        {
            pointerHighlightRect.gameObject.SetActive(pointerShowHighlight);
            ApplyPointerHighlightTheme(isUi: false);
        }

        if (pointerArrowRect != null)
            pointerArrowRect.gameObject.SetActive(pointerShowArrow);
    }

    private void PointAtUI(RectTransform target, float padding = 14f, bool showArrow = false, bool arrowFromScreenCenter = false, bool showHighlight = true, bool arrowFromRightMiddle = false, bool arrowFromTopRight = false, bool highlightYellow = false)
    {
        if (target == null)
        {
            HidePointer();
            return;
        }

        pointerVisible = true;
        pointerWorldMode = false;
        pointerUiTarget = target;
        pointerPulseT = 0f;
        pointerShowArrow = showArrow;
        pointerShowHighlight = showHighlight;
        pointerHighlightYellow = highlightYellow;
        ApplyArrowAnchorPlacement(arrowFromScreenCenter, arrowFromTopRight);

        if (pointerHighlightRect != null)
        {
            pointerHighlightRect.gameObject.SetActive(pointerShowHighlight);
            ApplyPointerHighlightTheme(isUi: true);
            pointerHighlightRect.sizeDelta = target.rect.size + new Vector2(padding * 2f, padding * 2f);
        }

        if (pointerArrowRect != null)
            pointerArrowRect.gameObject.SetActive(pointerShowArrow);
    }

    private void ApplyArrowAnchorPlacement(bool usePanelCenter, bool useTopRight)
    {
        if (useTopRight)
        {
            arrowAnchorPlacement = ArrowAnchorPlacement.TopRight;
        }
        else if (usePanelCenter)
        {
            arrowAnchorPlacement = ArrowAnchorPlacement.PanelCenter;
        }
        else
        {
            arrowAnchorPlacement = ArrowAnchorPlacement.RightMiddle;
        }

        if (arrowStartRect == null)
            return;

        switch (arrowAnchorPlacement)
        {
            case ArrowAnchorPlacement.TopRight:
                arrowStartRect.anchorMin = new Vector2(1f, 1f);
                arrowStartRect.anchorMax = new Vector2(1f, 1f);
                arrowStartRect.pivot = new Vector2(1f, 1f);
                arrowStartRect.anchoredPosition = new Vector2(28f, -28f);
                break;
            case ArrowAnchorPlacement.PanelCenter:
                arrowStartRect.anchorMin = new Vector2(0.5f, 0.5f);
                arrowStartRect.anchorMax = new Vector2(0.5f, 0.5f);
                arrowStartRect.pivot = new Vector2(0.5f, 0.5f);
                arrowStartRect.anchoredPosition = new Vector2(0f, -24f);
                break;
            default:
                arrowStartRect.anchorMin = new Vector2(1f, 0.5f);
                arrowStartRect.anchorMax = new Vector2(1f, 0.5f);
                arrowStartRect.pivot = new Vector2(1f, 0.5f);
                arrowStartRect.anchoredPosition = new Vector2(28f, 0f);
                break;
        }
    }

    private void ApplyPointerHighlightTheme(bool isUi)
    {
        if (pointerHighlightImage == null)
            return;

        Color fill;
        Color outline;

        if (pointerHighlightYellow)
        {
            fill = isUi ? new Color(0.98f, 0.92f, 0.30f, 0.04f) : new Color(0.98f, 0.92f, 0.30f, 0.14f);
            outline = new Color(0.98f, 0.92f, 0.30f, 0.92f);
        }
        else
        {
            fill = isUi ? new Color(0.18f, 0.52f, 0.82f, 0.02f) : new Color(0.18f, 0.52f, 0.82f, 0.18f);
            outline = new Color(0.18f, 0.52f, 0.82f, 0.85f);
        }

        pointerHighlightImage.color = fill;

        Outline hiOutline = pointerHighlightImage.GetComponent<Outline>();
        if (hiOutline != null)
        {
            hiOutline.effectColor = outline;
        }
    }

    private bool TryGetArrowStartLocal(out Vector2 localPoint)
    {
        localPoint = Vector2.zero;
        if (pointerLayer == null || arrowStartRect == null)
            return false;

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, arrowStartRect.position);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(pointerLayer, screenPoint, null, out localPoint);
    }

    private bool TryGetCanvasLocalPoint(Vector2 screenPoint, out Vector2 localPoint)
    {
        localPoint = Vector2.zero;
        if (pointerLayer == null)
            return false;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(pointerLayer, screenPoint, null, out localPoint);
    }

    private Vector2 AdjustArrowEndForPadding(Vector2 startLocal, Vector2 endLocal, Vector2? highlightSize)
    {
        Vector2 dir = endLocal - startLocal;
        float len = dir.magnitude;
        if (len < 0.001f)
            return endLocal;

        float padding = Mathf.Max(0f, pointerArrowTargetPadding);
        if (highlightSize.HasValue)
        {
            padding += Mathf.Max(highlightSize.Value.x, highlightSize.Value.y) * 0.5f;
        }

        float adjustedLength = Mathf.Max(0f, len - padding);
        if (adjustedLength <= 0.001f)
            return startLocal + dir.normalized * 0.001f;

        return startLocal + dir.normalized * adjustedLength;
    }

    private void ApplyArrowVisual(Vector2 startLocal, Vector2 endLocal, float pulse)
    {
        if (pointerArrowRect == null || pointerArrowImage == null)
            return;

        Vector2 delta = endLocal - startLocal;
        float len = delta.magnitude;
        if (len < 1f)
        {
            pointerArrowRect.gameObject.SetActive(false);
            return;
        }

        pointerArrowRect.gameObject.SetActive(pointerShowArrow);
        // Length is encoded along the RectTransform Y axis so rotation stays simple.
        pointerArrowRect.sizeDelta = new Vector2(Mathf.Max(2f, pointerArrowThickness), len);
        pointerArrowRect.anchoredPosition = (startLocal + endLocal) * 0.5f;
        pointerArrowRect.localRotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.up, delta));
        pointerArrowRect.localScale = Vector3.one * pulse;
    }

    private static Rect GetScreenRect(RectTransform rt)
    {
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < 4; i++)
        {
            Vector2 sp = RectTransformUtility.WorldToScreenPoint(null, corners[i]);
            min = Vector2.Min(min, sp);
            max = Vector2.Max(max, sp);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private Vector2 GetArrowStartScreen(Vector2 endScreen)
    {
        float marginX = Mathf.Clamp(Screen.width * 0.12f, 120f, 320f);
        float marginY = Mathf.Clamp(Screen.height * 0.12f, 120f, 280f);
        float minX = marginX;
        float maxX = Screen.width - marginX;
        float minY = marginY;
        float maxY = Screen.height - marginY;
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float minLen = Mathf.Clamp(Screen.height * 0.26f, 160f, 320f);

        Rect nextRect = default;
        Rect nextRectExpanded = default;
        bool hasNextRect = false;
        if (nextButton != null)
        {
            RectTransform nextRt = nextButton.GetComponent<RectTransform>();
            if (nextRt != null && nextButton.gameObject.activeInHierarchy)
            {
                nextRect = GetScreenRect(nextRt);
                nextRectExpanded = Rect.MinMaxRect(nextRect.xMin - 24f, nextRect.yMin - 18f, nextRect.xMax + 24f, nextRect.yMax + 18f);
                hasNextRect = true;
            }
        }

        float rightX = Mathf.Clamp(Screen.width - marginX * 0.5f, minX, maxX);
        Vector2 topRight = new Vector2(rightX, maxY);

        Vector2 start;
        switch (arrowAnchorPlacement)
        {
            case ArrowAnchorPlacement.TopRight:
                start = topRight;
                break;
            case ArrowAnchorPlacement.PanelCenter:
                start = new Vector2(screenCenter.x, Mathf.Clamp(screenCenter.y, minY, maxY));
                break;
            default:
                start = new Vector2(rightX, Mathf.Clamp(screenCenter.y, minY, maxY));
                break;
        }

        Vector2 ClampSafe(Vector2 point)
        {
            return new Vector2(Mathf.Clamp(point.x, minX, maxX), Mathf.Clamp(point.y, minY, maxY));
        }

        Vector2 EnsureMinLength(Vector2 candidate, float desiredLength)
        {
            Vector2 dir = endScreen - candidate;
            float len = dir.magnitude;
            if (len < 0.001f)
                return candidate;

            if (len >= desiredLength)
                return candidate;

            dir /= len;
            return endScreen - dir * desiredLength;
        }

        bool IntersectsNext(Vector2 from, Vector2 to)
        {
            if (!hasNextRect)
                return false;

            float minSegX = Mathf.Min(from.x, to.x);
            float maxSegX = Mathf.Max(from.x, to.x);
            float minSegY = Mathf.Min(from.y, to.y);
            float maxSegY = Mathf.Max(from.y, to.y);
            Rect arrowBounds = Rect.MinMaxRect(minSegX, minSegY, maxSegX, maxSegY);
            return arrowBounds.Overlaps(nextRectExpanded);
        }

        if (arrowAnchorPlacement == ArrowAnchorPlacement.TopRight)
        {
            Vector2[] candidates =
            {
                topRight,
                new Vector2(Screen.width - 220f, Screen.height - 320f),
                new Vector2(Screen.width - 320f, Screen.height - 220f),
            };

            foreach (Vector2 candidate in candidates)
            {
                Vector2 s = EnsureMinLength(ClampSafe(candidate), minLen);

                if (!IntersectsNext(s, endScreen))
                    return s;
            }
        }
        else
        {
            // If the arrow would pass through the tutorial Next button, start slightly below it (but stay near center).
            if (IntersectsNext(start, endScreen))
            {
                start = new Vector2(start.x, Mathf.Clamp(nextRect.yMin - 52f, minY, maxY));
            }
        }

        // Ensure the arrow is long enough to be readable.
        Vector2 dir = endScreen - start;
        if (dir.sqrMagnitude < 0.001f)
            dir = Vector2.up;
        float len = dir.magnitude;
        if (len < minLen)
        {
            dir /= len;
            start = endScreen - dir * minLen;
        }

        start = ClampSafe(start);

        // If we still intersect the Next button, try offsetting left/right from center.
        if (IntersectsNext(start, endScreen))
        {
            Vector2[] offsets =
            {
                new Vector2(screenCenter.x + 280f, screenCenter.y),
                new Vector2(screenCenter.x - 280f, screenCenter.y),
                new Vector2(screenCenter.x + 200f, screenCenter.y - 120f),
                new Vector2(screenCenter.x - 200f, screenCenter.y - 120f),
            };

            foreach (Vector2 candidate in offsets)
            {
                Vector2 s = EnsureMinLength(ClampSafe(candidate), minLen);

                if (!IntersectsNext(s, endScreen))
                    return s;
            }
        }

        // If we ended up at the bottom edge of our safe area, prefer alternative starts on the right side
        // (and accept shorter arrows) rather than falling back to a barely visible tail.
        if (start.y <= minY + 1f)
        {
            float belowNextY = hasNextRect ? Mathf.Clamp(nextRect.yMin - 80f, minY, maxY) : Mathf.Clamp(Screen.height * 0.4f, minY, maxY);
            Vector2[] edgeAvoidance =
            {
                new Vector2(rightX, belowNextY),
                new Vector2(rightX, Mathf.Clamp(endScreen.y + minLen * 0.35f, minY, maxY)),
                new Vector2(rightX, Mathf.Clamp(endScreen.y, minY, maxY)),
                topRight,
            };

            foreach (Vector2 candidate in edgeAvoidance)
            {
                Vector2 s = EnsureMinLength(ClampSafe(candidate), minLen * 0.65f);
                if (!IntersectsNext(s, endScreen))
                    return s;
            }
        }

        // Final fallback: a point below the target.
        Vector2 fallback = new Vector2(Mathf.Clamp(screenCenter.x, minX, maxX), Mathf.Clamp(endScreen.y - minLen, minY, maxY));
        if (!IntersectsNext(fallback, endScreen))
            return fallback;

        return start;
    }

    private void UpdatePointerVisuals()
    {
        if (!pointerVisible || pointerLayer == null)
            return;

        pointerPulseT += Time.unscaledDeltaTime;
        float pulse = 1f + Mathf.Sin(pointerPulseT * 4.5f) * 0.06f;

        bool hasArrowStart = TryGetArrowStartLocal(out Vector2 arrowStartLocal);

        if (pointerWorldMode)
        {
            if (cam == null)
                cam = Camera.main;
            if (cam == null)
                return;

            Vector3 screenPos = cam.WorldToScreenPoint(pointerWorldPosition);
            if (screenPos.z < 0f)
            {
                if (pointerArrowRect != null)
                    pointerArrowRect.gameObject.SetActive(false);
                return;
            }

            Vector2 screenPoint = new Vector2(screenPos.x, screenPos.y);

            if (pointerHighlightRect != null && pointerShowHighlight)
            {
                pointerHighlightRect.gameObject.SetActive(true);
                pointerHighlightRect.position = screenPoint;
                pointerHighlightRect.sizeDelta = GetWorldTargetHighlightSizePixels(pointerWorldPosition);
                pointerHighlightRect.localScale = Vector3.one * pulse;
            }

            if (pointerShowArrow && hasArrowStart && TryGetCanvasLocalPoint(screenPoint, out Vector2 endLocal))
            {
                Vector2? hiSize = pointerShowHighlight && pointerHighlightRect != null ? pointerHighlightRect.sizeDelta : (Vector2?)null;
                Vector2 adjustedEnd = AdjustArrowEndForPadding(arrowStartLocal, endLocal, hiSize);
                ApplyArrowVisual(arrowStartLocal, adjustedEnd, pulse);
            }
            else if (pointerArrowRect != null)
            {
                pointerArrowRect.gameObject.SetActive(false);
            }

            return;
        }

        if (pointerUiTarget == null)
            return;

        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(pointerLayer, pointerUiTarget);
        Vector3 centerWorld = pointerLayer.TransformPoint(bounds.center);

        if (pointerHighlightRect != null && pointerShowHighlight)
        {
            pointerHighlightRect.gameObject.SetActive(true);
            pointerHighlightRect.position = centerWorld;
            pointerHighlightRect.sizeDelta = new Vector2(bounds.size.x, bounds.size.y) + new Vector2(24f, 18f);
            pointerHighlightRect.localScale = Vector3.one * pulse;
        }

        if (pointerShowArrow && hasArrowStart)
        {
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, centerWorld);
            if (TryGetCanvasLocalPoint(screenPoint, out Vector2 endLocal))
            {
                Vector2? hiSize = pointerShowHighlight && pointerHighlightRect != null ? pointerHighlightRect.sizeDelta : (Vector2?)null;
                Vector2 adjustedEnd = AdjustArrowEndForPadding(arrowStartLocal, endLocal, hiSize);
                ApplyArrowVisual(arrowStartLocal, adjustedEnd, pulse);
            }
        }
        else if (pointerArrowRect != null)
        {
            pointerArrowRect.gameObject.SetActive(false);
        }
    }

    private void ScheduleAutoAdvance(float delaySeconds)
    {
        if (autoAdvanceRoutine != null && autoAdvanceStepIndex == stepIndex)
            return;

        if (autoAdvanceRoutine != null)
        {
            StopCoroutine(autoAdvanceRoutine);
            autoAdvanceRoutine = null;
        }

        autoAdvanceStepIndex = stepIndex;
        autoAdvanceRoutine = StartCoroutine(AutoAdvanceAfterDelay(stepIndex, Mathf.Max(0f, delaySeconds)));
    }

    private IEnumerator AutoAdvanceAfterDelay(int scheduledStep, float delaySeconds)
    {
        yield return new WaitForSecondsRealtime(delaySeconds);

        if (!isShowing || tm == null || tm.gameOver)
            yield break;

        if (stepIndex != scheduledStep)
            yield break;

        TutorialStep step = steps[stepIndex];
        bool canAdvance = step.canAdvance == null || step.canAdvance();
        if (!canAdvance)
            yield break;

        autoAdvanceRoutine = null;
        autoAdvanceStepIndex = -1;
        AdvanceToNextStep(playClickSound: false);
    }

    private Vector2 GetWorldTargetHighlightSizePixels(Vector3 worldPos)
    {
        if (cam == null)
            cam = Camera.main;
        if (cam == null)
            return new Vector2(96f, 96f);

        // Try to match the tile size on screen.
        if (grid != null && grid.TryGetTileAtWorldPosition(worldPos, out TileVisibility tile) && tile != null)
        {
            SpriteRenderer sr = tile.GetComponent<SpriteRenderer>();
            if (sr == null)
                sr = tile.GetComponentInChildren<SpriteRenderer>();

            if (sr != null)
            {
                Vector3 center = sr.bounds.center;
                Vector3 sizeWorld = sr.bounds.size;

                Vector3 right = cam.WorldToScreenPoint(center + new Vector3(sizeWorld.x * 0.5f, 0f, 0f));
                Vector3 left = cam.WorldToScreenPoint(center - new Vector3(sizeWorld.x * 0.5f, 0f, 0f));
                Vector3 up = cam.WorldToScreenPoint(center + new Vector3(0f, sizeWorld.y * 0.5f, 0f));
                Vector3 down = cam.WorldToScreenPoint(center - new Vector3(0f, sizeWorld.y * 0.5f, 0f));

                float w = Mathf.Abs(right.x - left.x);
                float h = Mathf.Abs(up.y - down.y);

                // Add a little padding so the highlight reads well.
                return new Vector2(Mathf.Max(48f, w + 14f), Mathf.Max(48f, h + 14f));
            }
        }

        return new Vector2(96f, 96f);
    }

    private void CompleteTutorialAndHideOverlay()
    {
        TutorialLaunch.MarkCompleted();

        if (tm != null)
        {
            tm.disableAI = false;
            tm.aiTurnDelay = prevAiTurnDelay;
            tm.autoEndTurnWhenNoActions = prevAutoEndTurnWhenNoActions;
        }

        TutorialGate.SetActive(false);

        if (autoAdvanceRoutine != null)
        {
            StopCoroutine(autoAdvanceRoutine);
            autoAdvanceRoutine = null;
        }

        Show(false);
        enabled = false;
    }

    private void EnterStep(int index)
    {
        hintDirty = false;
        stepEnterUnscaledTime = Time.unscaledTime;
        stepEnterFrame = Time.frameCount;

        RestoreTemporaryHighlights();
        RestorePanelLayout();

        TutorialGate.ClearAll();
        // Default: block gameplay interaction unless the step explicitly enables it.
        TutorialGate.CanSelectUnit = _ => false;
        TutorialGate.CanMoveOrAttackToPosition = (_, __) => false;
        TutorialGate.CanClickCity = _ => false;
        TutorialGate.CanRecruitWarrior = () => false;
        TutorialGate.CanEndTurn = () => false;
        cameraLockActive = false;
        HidePointer();
        autoAdvanceStepIndex = -1;
        if (autoAdvanceRoutine != null)
        {
            StopCoroutine(autoAdvanceRoutine);
            autoAdvanceRoutine = null;
        }

        CacheCitiesAndUnits();

        TutorialStep step = steps[index];
        step.onEnter?.Invoke();
        ApplyCurrentStep(copy: true);
    }

    private void LockCameraToWorld(Vector3 worldPos)
    {
        cameraLockActive = true;
        cameraLockWorldPosition = worldPos;

        if (cameraController == null)
            cameraController = Object.FindFirstObjectByType<CameraController>();
    }

    private void ApplyCameraLock()
    {
        if (cam == null)
            cam = Camera.main;
        if (cam == null)
            return;

        Vector3 p = cam.transform.position;
        p.x = cameraLockWorldPosition.x;
        p.y = cameraLockWorldPosition.y;
        cam.transform.position = p;
    }

    private void RestoreTemporaryHighlights()
    {
        if (goldTmpHighlighted && goldTmp != null && goldTmpOriginalColorSet)
        {
            goldTmp.color = goldTmpOriginalColor;
        }
        goldTmpHighlighted = false;
    }

    private void CaptureDefaultPanelLayout()
    {
        if (panelRect == null)
            return;

        defaultPanelAnchorMin = panelRect.anchorMin;
        defaultPanelAnchorMax = panelRect.anchorMax;
        defaultPanelOffsetMin = panelRect.offsetMin;
        defaultPanelOffsetMax = panelRect.offsetMax;
        defaultPanelPivot = panelRect.pivot;
        defaultPanelAnchoredPosition = panelRect.anchoredPosition;
    }

    private void RestorePanelLayout()
    {
        if (panelRect == null)
            return;

        panelRect.anchorMin = defaultPanelAnchorMin;
        panelRect.anchorMax = defaultPanelAnchorMax;
        panelRect.offsetMin = defaultPanelOffsetMin;
        panelRect.offsetMax = defaultPanelOffsetMax;
        panelRect.pivot = defaultPanelPivot;
        panelRect.anchoredPosition = defaultPanelAnchoredPosition;
    }

    private void SetPanelAnchors(float anchorMinX, float anchorMaxX)
    {
        if (panelRect == null)
            return;

        float minX = Mathf.Clamp01(Mathf.Min(anchorMinX, anchorMaxX));
        float maxX = Mathf.Clamp01(Mathf.Max(anchorMinX, anchorMaxX));

        panelRect.anchorMin = new Vector2(minX, 1f);
        panelRect.anchorMax = new Vector2(maxX, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        Vector2 anchored = panelRect.anchoredPosition;
        panelRect.anchoredPosition = new Vector2(0f, anchored.y);
    }

    private void ApplyPanelTopOffset(float extraPadding = 0f)
    {
        if (panelRect == null)
            return;

        panelRect.anchoredPosition = new Vector2(0f, -GetPanelTopOffset(extraPadding));
    }

    private float GetPanelTopOffset(float extraPadding = 0f)
    {
        return Mathf.Max(0f, topHudPadding + Mathf.Max(0f, panelTopMargin) + Mathf.Max(0f, extraPadding));
    }

    private void SetPanelLayoutAvoidTopCenter()
    {
        if (panelRect == null)
            return;

        if (IsMobilePlatform())
        {
            SetPanelAnchors(0.04f, 0.72f);
        }
        else
        {
            SetPanelAnchors(0.04f, 0.72f);
        }
        ApplyPanelTopOffset();
    }

    private void SetPanelLayoutUpperLeft()
    {
        if (panelRect == null)
            return;

        if (IsMobilePlatform())
        {
            SetPanelAnchors(0.05f, 0.95f);
        }
        else
        {
            SetPanelAnchors(0.03f, 0.58f);
        }
        ApplyPanelTopOffset();
    }

    private void SetPanelLayoutIntroTopLeft()
    {
        if (panelRect == null)
            return;

        if (IsMobilePlatform())
        {
            SetPanelAnchors(0.04f, 0.74f);
        }
        else
        {
            SetPanelAnchors(0.03f, 0.58f);
        }
        ApplyPanelTopOffset(introPanelTopPadding);
    }

    private void BuildSteps()
    {
        steps.Clear();

        int cx = playerCity != null ? playerCity.x : 1;
        int cy = playerCity != null ? playerCity.y : 1;

        Vector3 tile_22 = GetTileWorld(cx + 1, cy + 1);
        Vector3 tile_33 = GetTileWorld(cx + 2, cy + 2);
        Vector3 tile_44 = GetTileWorld(cx + 3, cy + 3);
        Vector3 tile_55 = GetTileWorld(cx + 4, cy + 4);

        steps.Add(new TutorialStep
        {
            title = "Tutorial",
            body = "This is your city (blue).\n\nCities produce Gold each turn.\n\nYou can leave the tutorial anytime via the Menu button.",
            nextLabel = "Next",
            onEnter = () =>
            {
                SetPanelLayoutIntroTopLeft();
                CacheCitiesAndUnits();
                if (playerCity != null)
                {
                    PointAtWorld(playerCity.transform.position, showArrow: true, arrowFromScreenCenter: true, arrowFromTopRight: true);
                    LockCameraToWorld(playerCity.transform.position);
                }
                TutorialGate.CanEndTurn = () => false;
            }
        });

        steps.Add(new TutorialStep
        {
            title = "Your City",
            body = "Tap/click your city to open the city menu.",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                SetPanelLayoutIntroTopLeft();
                CacheCitiesAndUnits();
                if (playerCity != null)
                {
                    PointAtWorld(playerCity.transform.position, showArrow: true, arrowFromScreenCenter: true, arrowFromTopRight: true);
                    LockCameraToWorld(playerCity.transform.position);
                }

                TutorialGate.CanClickCity = c => c == playerCity;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanEndTurn = () => false;
            },
            canAdvance = HasCityPanelOpen,
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Click the blue city icon on the map."
        });

        steps.Add(new TutorialStep
        {
            title = "Gold",
            body = "At the top you can see your current Gold.\n\nYou need Gold to recruit units.",
            nextLabel = "Next",
            onEnter = () =>
            {
                SetPanelLayoutAvoidTopCenter();
                RectTransform goldRect = GetGoldRectTransform();
                if (goldRect != null)
                {
                    PointAtUI(goldRect, padding: 10f, showArrow: true, arrowFromScreenCenter: true, showHighlight: false);
                }
                else
                    HidePointer();
                TutorialGate.CanEndTurn = () => false;
            }
        });

        steps.Add(new TutorialStep
        {
            title = "Zoom In",
            body = "Zoom in on your city (mouse wheel / pinch).",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                SetPanelLayoutUpperLeft();
                baselineZoom = GetCurrentZoomValue();
                CacheCitiesAndUnits();
                if (playerCity != null)
                {
                    PointAtWorld(playerCity.transform.position, showArrow: true, arrowFromScreenCenter: true);
                    LockCameraToWorld(playerCity.transform.position);
                }
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            },
            canAdvance = () => HasZoomChangedBy(atLeastDelta: -0.75f),
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Zoom in until the city is clearly visible."
        });

        steps.Add(new TutorialStep
        {
            title = "Recruit",
            body = "In the city menu, press Warrior to recruit a unit.\n\nCost: " + GetWarriorCostLabel(),
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                SetPanelLayoutUpperLeft();
                CacheCitiesAndUnits();
                if (playerCity != null)
                    LockCameraToWorld(playerCity.transform.position);

                // Don't force-close the city panel here; this step is about using it.
                // Show the city as the target until the player opens the city UI.
                if (playerCity != null)
                    PointAtWorld(playerCity.transform.position, showArrow: true, arrowFromScreenCenter: true, showHighlight: false);

                if (playerCity != null)
                {
                    TutorialGate.CanClickCity = c => c == playerCity;
                }
                TutorialGate.CanRecruitWarrior = () => true;
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanEndTurn = () => false;
            },
            canAdvance = () =>
            {
                // If the city UI is open, point at the recruit button; otherwise point at the city.
                if (HasCityPanelOpen())
                {
                    RectTransform recruitRect = GetRecruitButtonRectTransform();
                    if (recruitRect != null)
                    {
                        PointAtUI(recruitRect, padding: 12f, showArrow: true, arrowFromScreenCenter: false, showHighlight: true, arrowFromRightMiddle: false);
                    }
                }
                else if (playerCity != null)
                {
                    PointAtWorld(playerCity.transform.position, showArrow: true, arrowFromScreenCenter: true, showHighlight: false);
                }

                return HasAnyPlayerUnit();
            },
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Open the city menu, then press the Warrior button."
        });

        steps.Add(new TutorialStep
        {
            title = "Gold Spent",
            body = "Recruiting costs Gold, so now you are broke again.\n\nBut dont worry, you gain Gold again at the start of each turn!",
            nextLabel = "Next",
            onEnter = () =>
            {
                SetPanelLayoutAvoidTopCenter();
                RectTransform goldRect = GetGoldRectTransform();
                if (goldRect != null)
                {
                    PointAtUI(goldRect, padding: 10f, showArrow: true, arrowFromScreenCenter: true, showHighlight: false);
                }
                else
                    HidePointer();
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            }
        });

        steps.Add(new TutorialStep
        {
            title = "Ready To Move",
            body = "The yellow ring around a unit means it can still act this turn.",
            nextLabel = "Next",
            onEnter = () =>
            {
                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                CachePlayerWarriorsIfNeeded();
                // Don't obscure the yellow ring with a blue highlight rectangle.
                if (warrior1 != null) PointAtWorld(warrior1.transform.position, showArrow: true, arrowFromScreenCenter: true, showHighlight: false);
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            }
        });

        steps.Add(new TutorialStep
        {
            title = "Zoom Out",
            body = "Now zoom out (mouse wheel / pinch) so you can see more of the map.",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                baselineZoom = GetCurrentZoomValue();
                CacheCitiesAndUnits();
                if (playerCity != null)
                {
                    LockCameraToWorld(playerCity.transform.position);
                }
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            },
            canAdvance = () => HasZoomChangedBy(atLeastDelta: +0.75f),
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Zoom out until you can see more tiles around you."
        });

        steps.Add(new TutorialStep
        {
            title = "Fog Of War",
            body = "Black tiles are unexplored. Move units to discover the map.\n\nGrey tiles are explored, but not currently visible.",
            nextLabel = "Next",
            onEnter = () =>
            {
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            }
        });

        steps.Add(new TutorialStep
        {
            title = "Select Unit",
            body = "Tap your Warrior to select it.\n\nCyan tiles = you can move there.",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                CachePlayerWarriorsIfNeeded();
                if (warrior1 != null)
                {
                    PointAtWorld(warrior1.transform.position);
                }
                SetPanelLayoutUpperLeft();
                TutorialGate.CanSelectUnit = u => u == warrior1;
                TutorialGate.CanEndTurn = () => false;
            },
            canAdvance = HasSelectedPlayerUnit,
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Tap the Warrior you recruited."
        });

        steps.Add(new TutorialStep
        {
            title = "Move",
            body = "Move your Warrior to the highlighted tile.",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                // Prevent the game from auto-ending the turn until we explain why.
                if (tm != null)
                    tm.autoEndTurnWhenNoActions = false;

                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                if (UnitUIManager.Instance != null) UnitUIManager.Instance.ClosePanel();
                CachePlayerWarriorsIfNeeded();
                PointAtWorld(tile_22, showArrow: false, showHighlight: true, highlightYellow: true);
                SetAllowedMove(warrior1, tile_22, forceSingleHighlight: false);
            },
            canAdvance = () => IsUnitAt(warrior1, tile_22),
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Select the Warrior, then tap the highlighted tile."
        });

        steps.Add(new TutorialStep
        {
            title = "Explore",
            body = "Units reveal nearby tiles.\n\nMoving discovers new tiles and updates the fog-of-war.",
            nextLabel = "Next",
            onEnter = () =>
            {
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            }
        });

        steps.Add(new TutorialStep
        {
            title = "Next Turn",
            body = "You have taken all possible actions this turn.\n\nIn this case, the game moves to the next turn automatically.",
            nextLabel = "Continue",
            onEnter = () =>
            {
                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                if (UnitUIManager.Instance != null) UnitUIManager.Instance.ClosePanel();
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            },
            onExit = () =>
            {
                // Resume normal behavior: let the game auto-end the turn when no actions remain.
                if (tm != null)
                {
                    tm.autoEndTurnWhenNoActions = true;
                    tm.ScheduleAutoEndTurnCheck();
                }
            }
        });

        steps.Add(new TutorialStep
        {
            title = "Next Turn",
            body = "Continuing...",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                HidePointer();
                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                if (UnitUIManager.Instance != null) UnitUIManager.Instance.ClosePanel();
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            },
            canAdvance = () => tm != null && tm.isPlayerTurn && tm.turnNumber == 2,
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Wait a moment - the next turn will start on its own."
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 2",
            body = "Move your Warrior to the highlighted tile.",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                if (UnitUIManager.Instance != null) UnitUIManager.Instance.ClosePanel();
                CachePlayerWarriorsIfNeeded();
                PointAtWorld(tile_33, showArrow: false, showHighlight: true, highlightYellow: true);
                SetAllowedMove(warrior1, tile_33, forceSingleHighlight: false);
            },
            canAdvance = () => IsUnitAt(warrior1, tile_33),
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Tap the highlighted tile."
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 2",
            body = "You have taken all possible actions this turn.\n\nIn this case, the game moves to the next turn automatically.",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                if (UnitUIManager.Instance != null) UnitUIManager.Instance.ClosePanel();
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            },
            canAdvance = () => tm != null && tm.isPlayerTurn && tm.turnNumber == 3,
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Wait a moment — the next turn will start on its own."
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 3",
            body = "Can you remember how to recruit a Warrior?\n\nTry it now.",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                // Remind the player where to recruit from: point at the city until the city UI is open,
                // then point at the Warrior recruit button.
                if (playerCity != null)
                {
                    PointAtWorld(playerCity.transform.position, showArrow: true, arrowFromScreenCenter: true, showHighlight: false);
                }
                else
                {
                    HidePointer();
                }
                if (playerCity != null)
                {
                    TutorialGate.CanClickCity = c => c == playerCity;
                }
                TutorialGate.CanRecruitWarrior = () => true;
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanEndTurn = () => false;
            },
            canAdvance = () =>
            {
                // If the city UI is open, point at the recruit button; otherwise point at the city.
                if (HasCityPanelOpen())
                {
                    RectTransform recruitRect = null;
                    if (CityUIManager.Instance != null && CityUIManager.Instance.recruitWarriorButtonText != null)
                        recruitRect = CityUIManager.Instance.recruitWarriorButtonText.GetComponent<RectTransform>();
                    recruitRect ??= GetRecruitButtonRectTransform();

                    if (recruitRect != null)
                    {
                        RectTransform recruitLabelRect = CityUIManager.Instance != null && CityUIManager.Instance.recruitWarriorButtonText != null
                            ? CityUIManager.Instance.recruitWarriorButtonText.GetComponent<RectTransform>()
                            : null;
                        RectTransform targetRect = recruitLabelRect != null ? recruitLabelRect : recruitRect;

                        // Connect the tutorial text panel to the actual recruit label so the arrow reads like a "flow"
                        // from the instruction down to the action.
                        PointAtUI(targetRect, padding: 10f, showArrow: true, arrowFromScreenCenter: true, showHighlight: true, arrowFromRightMiddle: false);

                    }
                }
                else if (playerCity != null)
                {
                    PointAtWorld(playerCity.transform.position, showArrow: true, arrowFromScreenCenter: true, showHighlight: false);
                }

                return HasRecentFailedRecruitAttempt();
            },
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Open the city menu and press the Warrior button."
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 3",
            body = "Damn, seems like we are one Gold short.\n\nLet's keep exploring then.",
            nextLabel = "Next",
            onEnter = () =>
            {
                HidePointer();
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            }
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 3",
            body = "Do you remember how to move your Warrior to the highlighted tile?",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                CachePlayerWarriorsIfNeeded();
                PointAtWorld(tile_44, showArrow: false, showHighlight: true, highlightYellow: true);
                SetAllowedMove(warrior1, tile_44, forceSingleHighlight: false);
            },
            canAdvance = () => IsUnitAt(warrior1, tile_44),
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Tap the highlighted tile."
        });

        steps.Add(new TutorialStep
        {
            title = "Explored Tiles",
            body = "Grey tiles are explored but not currently visible.\n\nKeep an eye out — enemies could be hiding in the shadows!",
            nextLabel = "Next",
            dynamicBody = () => "Grey tiles are explored but not currently visible.\n\nKeep an eye out — enemies could be hiding in the shadows!",
            onEnter = () =>
            {
                if (TryGetExploredNotVisibleTile(out Vector3 greyTile))
                {
                    PointAtWorld(greyTile, showArrow: true, arrowFromScreenCenter: true);
                }
                else
                {
                    HidePointer();
                }

                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            }
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 3",
            body = "You have taken all possible actions this turn.\n\nIn this case, the game moves to the next turn automatically.",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                if (UnitUIManager.Instance != null) UnitUIManager.Instance.ClosePanel();
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            },
            canAdvance = () =>
            {
                HidePointer();
                return tm != null && tm.isPlayerTurn && tm.turnNumber == 4;
            },
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Wait a moment — the next turn will start on its own."
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 4",
            body = "Now you should have enough Gold.\n\nRecruit a second Warrior from your city.",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                if (playerCity != null)
                {
                    PointAtWorld(playerCity.transform.position, showArrow: true, arrowFromScreenCenter: true, showHighlight: false);
                    TutorialGate.CanClickCity = c => c == playerCity;
                }
                else
                {
                    HidePointer();
                }
                TutorialGate.CanRecruitWarrior = () => true;
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanEndTurn = () => false;
            },
            canAdvance = () =>
            {
                // If the city UI is open, point at the recruit button; otherwise point at the city.
                if (HasCityPanelOpen())
                {
                    RectTransform recruitRect = null;
                    if (CityUIManager.Instance != null && CityUIManager.Instance.recruitWarriorButtonText != null)
                        recruitRect = CityUIManager.Instance.recruitWarriorButtonText.GetComponent<RectTransform>();
                    recruitRect ??= GetRecruitButtonRectTransform();

                    if (recruitRect != null)
                    {
                        RectTransform recruitLabelRect = CityUIManager.Instance != null && CityUIManager.Instance.recruitWarriorButtonText != null
                            ? CityUIManager.Instance.recruitWarriorButtonText.GetComponent<RectTransform>()
                            : null;
                        RectTransform targetRect = recruitLabelRect != null ? recruitLabelRect : recruitRect;

                        PointAtUI(targetRect, padding: 10f, showArrow: true, arrowFromScreenCenter: true, showHighlight: true, arrowFromRightMiddle: false);

                    }
                }
                else if (playerCity != null)
                {
                    PointAtWorld(playerCity.transform.position, showArrow: true, arrowFromScreenCenter: true, showHighlight: false);
                }

                return CountUnits(isPlayerOwned: true) >= 2;
            },
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Tap the city, then press Warrior."
        });

        steps.Add(new TutorialStep
        {
            title = "Two Units",
            body = "Each unit can act once per turn.\n\nAt the start of your turn, units show a yellow ring to indicate they can still act.",
            nextLabel = "Next",
            onEnter = () =>
            {
                CachePlayerWarriorsIfNeeded();
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            }
        });

        steps.Add(new TutorialStep
        {
            title = "Move Warrior 2",
            body = "Select your new Warrior (on your city) and move it to the highlighted tile.",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                if (UnitUIManager.Instance != null) UnitUIManager.Instance.ClosePanel();
                CachePlayerWarriorsIfNeeded();
                warrior2 = GetUnitOnCityTile();
                PointAtWorld(tile_22, showArrow: false, showHighlight: true, highlightYellow: true);
                SetAllowedMove(warrior2, tile_22, forceSingleHighlight: false);
            },
            canAdvance = () => IsUnitAt(warrior2, tile_22),
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Tap the highlighted tile."
        });

        steps.Add(new TutorialStep
        {
            title = "End Turn",
            body = "I could still move this turn, but let's take it easy.\n\nYou can end your turn early by pressing End Turn.",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                if (UnitUIManager.Instance != null) UnitUIManager.Instance.ClosePanel();
                RectTransform endTurnRect = FindButtonRectByLabelContains("End Turn");
                if (endTurnRect != null)
                {
                    PointAtUI(endTurnRect);
                }
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => true;
            },
            canAdvance = () => tm != null && tm.isPlayerTurn && tm.turnNumber == 5,
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Press End Turn."
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 5",
            body = "What is that?\n\nA Warrior from another clan approaches...",
            nextLabel = "Next",
            onEnter = () =>
            {
                CacheCitiesAndUnits();

                if (enemy1 == null)
                {
                    enemy1 = SpawnTutorialUnit(tile_55, isPlayerOwned: false, "Enemy Warrior 1");
                }

                if (enemy1 != null)
                {
                    PointAtWorld(enemy1.transform.position);
                }

                if (warrior1 != null)
                {
                    SpeechBubble.Show(warrior1.transform, "Hey, nice to meet you!", seconds: 9999f);
                }
                if (enemy1 != null)
                {
                    // Greeting happens after the kill later in the scripted AI turn.
                }

                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            },
            onExit = () =>
            {
                if (warrior1 != null) SpeechBubble.HideAll(warrior1.transform);
                if (enemy1 != null) SpeechBubble.HideAll(enemy1.transform);
            }
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 5",
            body = "Move your second Warrior to the highlighted tile. Lets greet Clan Salami properly",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                if (UnitUIManager.Instance != null) UnitUIManager.Instance.ClosePanel();
                CachePlayerWarriorsIfNeeded();
                PointAtWorld(tile_33, showArrow: false, showHighlight: true, highlightYellow: true);
                SetAllowedMove(warrior2, tile_33, forceSingleHighlight: false);
            },
            canAdvance = () => IsUnitAt(warrior2, tile_33),
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Select Warrior 2 and tap the highlighted tile."
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 5",
            body = "End your turn.\n\nLet's greet the Warrior from the Salami Clan!",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                if (UnitUIManager.Instance != null) UnitUIManager.Instance.ClosePanel();
                RectTransform endTurnRect = FindButtonRectByLabelContains("End Turn");
                if (endTurnRect != null)
                {
                    PointAtUI(endTurnRect);
                }
                if (scriptedRoutine != null) StopCoroutine(scriptedRoutine);
                scriptedRoutine = StartCoroutine(ScriptEnemyKillWarrior1OnAITurn5());
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => true;
            },
            canAdvance = () => tm != null && tm.isPlayerTurn && tm.turnNumber == 6,
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Press End Turn."
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 6",
            body = "Select Warrior 2.",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                if (UnitUIManager.Instance != null) UnitUIManager.Instance.ClosePanel();
                CachePlayerWarriorsIfNeeded();
                if (warrior2 != null)
                {
                    PointAtWorld(warrior2.transform.position);
                    SpeechBubble.Show(warrior2.transform, "Noooooo!", seconds: 9999f);
                    TutorialGate.CanSelectUnit = u => u == warrior2;
                }
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            },
            onExit = () =>
            {
                if (warrior2 != null) SpeechBubble.HideAll(warrior2.transform);
                if (enemy1 != null) SpeechBubble.HideAll(enemy1.transform);
            },
            canAdvance = () => UnitSelectionManager.Instance != null && warrior2 != null && UnitSelectionManager.Instance.SelectedUnit == warrior2,
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Tap your second Warrior."
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 6",
            body = "Even after moving, you can still attack.\n\nNow attack Enemy Warrior 1!",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                if (UnitUIManager.Instance != null) UnitUIManager.Instance.ClosePanel();
                CachePlayerWarriorsIfNeeded();

                if (enemy1 != null)
                {
                    // Yellow pulse highlight to differentiate from the blue move tiles.
                    PointAtWorld(enemy1.transform.position, showArrow: false, showHighlight: true, highlightYellow: true);
                    SetAllowedAttack(warrior2, enemy1, forceSingleHighlight: true);
                }
                else
                {
                    HidePointer();
                    TutorialGate.CanSelectUnit = _ => false;
                    TutorialGate.CanMoveOrAttackToPosition = null;
                }

                if (UnitSelectionManager.Instance != null && warrior2 != null && UnitSelectionManager.Instance.SelectedUnit != warrior2)
                {
                    UnitSelectionManager.Instance.SelectUnit(warrior2);
                }
            },
            canAdvance = () => enemy1 == null,
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Tap Enemy Warrior 1."
        });
        steps.Add(new TutorialStep
        {
            title = "Turn 7",
            body = "Keep pressing forward, Clan Salami cant get away with this!\n\nMove Warrior 2 toward the enemy city (top-right).",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                if (UnitUIManager.Instance != null) UnitUIManager.Instance.ClosePanel();
                CachePlayerWarriorsIfNeeded();
                turn7MoveTarget = Vector3.zero;
                turn7MoveDir = new Vector2Int(1, 1);

                if (warrior2 != null)
                {
                    Vector3 origin = warrior2.transform.position;
                    if (TryGetFreeNeighborTile(origin, 1, 1, out turn7MoveTarget))
                    {
                        turn7MoveDir = new Vector2Int(1, 1);
                    }
                    else if (TryGetFreeNeighborTile(origin, 1, 0, out turn7MoveTarget))
                    {
                        turn7MoveDir = new Vector2Int(1, 0);
                    }
                    else if (TryGetFreeNeighborTile(origin, 0, 1, out turn7MoveTarget))
                    {
                        turn7MoveDir = new Vector2Int(0, 1);
                    }
                    else if (TryFindFreeNeighborTile(origin, out turn7MoveTarget))
                    {
                        turn7MoveDir = GetGridDirection(origin, turn7MoveTarget);
                    }
                }

                if (turn7MoveTarget != Vector3.zero)
                {
                    PointAtWorld(turn7MoveTarget, showArrow: false, showHighlight: true, highlightYellow: true);
                    SetAllowedMove(warrior2, turn7MoveTarget, forceSingleHighlight: false);
                }
                else
                {
                    HidePointer();
                    TutorialGate.CanSelectUnit = _ => false;
                    TutorialGate.CanMoveOrAttackToPosition = null;
                }
            },
            canAdvance = () => turn7MoveTarget != Vector3.zero && IsUnitAt(warrior2, turn7MoveTarget),
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Select Warrior 2 and tap the highlighted tile."
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 7",
            body = "An enemy warrior just arrived from the enemy city.\n\nAfter moving, you can still attack.\n\nDefeat Enemy Warrior 2.",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                if (UnitUIManager.Instance != null) UnitUIManager.Instance.ClosePanel();
                CachePlayerWarriorsIfNeeded();
                if (enemy2 == null)
                {
                    Vector2Int dir = turn7MoveDir;
                    if (dir == Vector2Int.zero)
                    {
                        dir = new Vector2Int(1, 1);
                    }

                    Vector3 spawnPos = Vector3.zero;
                    if (warrior2 != null && TryGetFreeNeighborTile(warrior2.transform.position, dir.x, dir.y, out spawnPos))
                    {
                        // Keep the enemy one step further in the same direction.
                    }
                    else if (warrior2 != null && TryGetFreeNeighborTile(warrior2.transform.position, 1, 1, out spawnPos))
                    {
                        turn7MoveDir = new Vector2Int(1, 1);
                    }
                    else if (warrior2 != null && TryFindFreeNeighborTile(warrior2.transform.position, out spawnPos))
                    {
                        // Fallback: any adjacent free tile.
                    }

                    if (spawnPos != Vector3.zero)
                    {
                        enemy2 = SpawnTutorialUnit(spawnPos, isPlayerOwned: false, "Enemy Warrior 2");
                        if (tm != null)
                        {
                            tm.RecalculatePlayerVisibility();
                        }
                    }
                }

                if (enemy2 != null)
                {
                    PointAtWorld(enemy2.transform.position, showArrow: false, showHighlight: true, highlightYellow: true);
                    SetAllowedAttack(warrior2, enemy2, forceSingleHighlight: true);
                }
                else
                {
                    HidePointer();
                    TutorialGate.CanSelectUnit = _ => false;
                    TutorialGate.CanMoveOrAttackToPosition = null;
                }

                if (UnitSelectionManager.Instance != null && warrior2 != null && UnitSelectionManager.Instance.SelectedUnit != warrior2)
                {
                    UnitSelectionManager.Instance.SelectUnit(warrior2);
                }
            },
            canAdvance = () => enemy2 == null,
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Select Warrior 2 and tap Enemy Warrior 2."
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 7",
            body = "Lets storm their village!\n\nMove Warrior 2 one step closer to the enemy city.",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                if (UnitUIManager.Instance != null) UnitUIManager.Instance.ClosePanel();
                CacheCitiesAndUnits();

                if (warrior2 != null)
                {
                    warrior2.ResetMovementForTurn();
                    bool isActiveTurn = tm == null || (tm.IsCurrentSideOwner(warrior2.isPlayerOwned) && tm.IsHumanTurn());
                    warrior2.UpdateMoveOutline(isActiveTurn);
                }

                turn7MoveTarget = Vector3.zero;
                turn7MoveDir = new Vector2Int(1, 1);

                if (warrior2 != null)
                {
                    Vector3 origin = warrior2.transform.position;
                    Vector2Int dir = enemyCity != null ? GetGridDirection(origin, enemyCity.transform.position) : new Vector2Int(1, 1);
                    if (dir == Vector2Int.zero)
                        dir = new Vector2Int(1, 1);

                    turn7MoveDir = dir;

                    if (TryGetFreeNeighborTile(origin, dir.x, dir.y, out turn7MoveTarget))
                    {
                        // Preferred: step toward the enemy city.
                    }
                    else if (TryGetFreeNeighborTile(origin, dir.x, 0, out turn7MoveTarget))
                    {
                        turn7MoveDir = new Vector2Int(dir.x, 0);
                    }
                    else if (TryGetFreeNeighborTile(origin, 0, dir.y, out turn7MoveTarget))
                    {
                        turn7MoveDir = new Vector2Int(0, dir.y);
                    }
                    else if (TryFindFreeNeighborTile(origin, out turn7MoveTarget))
                    {
                        turn7MoveDir = GetGridDirection(origin, turn7MoveTarget);
                    }
                }

                if (turn7MoveTarget != Vector3.zero)
                {
                    PointAtWorld(turn7MoveTarget, showArrow: false, showHighlight: true, highlightYellow: true);
                    SetAllowedMove(warrior2, turn7MoveTarget, forceSingleHighlight: false);
                }
                else
                {
                    HidePointer();
                    TutorialGate.CanSelectUnit = _ => false;
                    TutorialGate.CanMoveOrAttackToPosition = null;
                }
            },
            canAdvance = () => turn7MoveTarget != Vector3.zero && IsUnitAt(warrior2, turn7MoveTarget),
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Select Warrior 2 and tap the highlighted tile."
        });

        steps.Add(new TutorialStep
        {
            title = "Enemy City",
            body = "If you move a unit onto the enemy city (red), you capture it and win.",
            nextLabel = "Next",
            onEnter = () =>
            {
                CacheCitiesAndUnits();
                if (enemyCity != null)
                {
                    PointAtWorld(enemyCity.transform.position, showArrow: true, arrowFromScreenCenter: true, showHighlight: true, highlightYellow: true);
                    if (cam == null) cam = Camera.main;
                    if (cam != null && warrior2 != null)
                    {
                        Vector3 warriorScreen = cam.WorldToScreenPoint(warrior2.transform.position);
                    }
                }
                else
                {
                    HidePointer();
                }

                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            }
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 7",
            body = "Warrior 2 seems exhausted.",
            nextLabel = "Next",
            onEnter = () =>
            {
                HidePointer();
                if (warrior2 != null)
                {
                    SpeechBubble.Show(warrior2.transform, "I'm not feeling so well.", seconds: 9999f);
                }
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            },
            onExit = () =>
            {
                if (warrior2 != null)
                {
                    if (UnitSelectionManager.Instance != null)
                        UnitSelectionManager.Instance.ClearSelection();
                    SpeechBubble.HideAll(warrior2.transform);
                    warrior2.Die();
                    warrior2 = null;
                }
            }
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 7",
            body = "Warrior 2 died of over extortion.\n\nIt is up to you to revenge your fallen comrade.",
            nextLabel = "Next",
            onEnter = () =>
            {
                HidePointer();
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            }
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 8",
            nextLabel = "Let's Go",
            onEnter = () =>
            {
                TutorialGate.SetActive(false);
                if (tm != null)
                {
                    tm.disableAI = false;
                    tm.aiDifficulty = TurnManager.AIDifficulty.Level1;
                    tm.aiGold = 1;
                    tm.aiTurnDelay = prevAiTurnDelay;
                    tm.autoEndTurnWhenNoActions = prevAutoEndTurnWhenNoActions;
                }
            },
            dynamicBody = () =>
            {
                int gold = tm != null ? tm.playerGold : 0;
                return "From next turn, you can play freely against the AI. Clan Chief Salami wants revenge.\n\nRecruit units, explore, and capture the enemy city (red) to win.\n\n(Current Gold: " + gold + ")";
            }
        });

        steps.Add(new TutorialStep
        {
            title = "Good luck",
            body = "The tutorial overlay will now close.\n\nYou can leave to the main menu anytime via the Menu button.",
            nextLabel = "Close"
        });
    }

    private void CacheCitiesAndUnits()
    {
        if (playerCity == null || enemyCity == null)
        {
            City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
            foreach (City c in cities)
            {
                if (c == null) continue;
                if (c.isPlayerOwned && playerCity == null) playerCity = c;
                if (!c.isPlayerOwned && enemyCity == null) enemyCity = c;
            }
        }

        CachePlayerWarriorsIfNeeded();
    }

    private void CachePlayerWarriorsIfNeeded()
    {
        if (CountUnits(isPlayerOwned: true) == 0)
            return;

        if (warrior1 == null)
        {
            Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
            foreach (Unit u in units)
            {
                if (u != null && u.isPlayerOwned)
                {
                    warrior1 = u;
                    break;
                }
            }
        }

        if (playerCity != null)
        {
            Unit onCity = GridUtils.GetUnitAtPosition(playerCity.transform.position, null);
            if (onCity != null && onCity.isPlayerOwned)
            {
                if (warrior1 == null) warrior1 = onCity;
                if (warrior2 == null && onCity != warrior1) warrior2 = onCity;
            }
        }

        if (CountUnits(isPlayerOwned: true) >= 2 && warrior2 == null)
        {
            Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
            foreach (Unit u in units)
            {
                if (u == null || !u.isPlayerOwned) continue;
                if (u != warrior1)
                {
                    warrior2 = u;
                    break;
                }
            }
        }

        if (warrior1 != null && warrior1.gameObject != null && warrior1.gameObject.name != "Tutorial Warrior 1")
        {
            warrior1.gameObject.name = "Tutorial Warrior 1";
        }
        if (warrior2 != null && warrior2.gameObject != null && warrior2.gameObject.name != "Tutorial Warrior 2")
        {
            warrior2.gameObject.name = "Tutorial Warrior 2";
        }
    }

    private int CountUnits(bool isPlayerOwned)
    {
        int count = 0;
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit u in units)
        {
            if (u != null && u.isPlayerOwned == isPlayerOwned)
                count++;
        }
        return count;
    }

    private bool HasCityPanelOpen()
    {
        if (CityUIManager.Instance == null || CityUIManager.Instance.panelRoot == null)
            return false;
        return CityUIManager.Instance.panelRoot.activeInHierarchy;
    }

    private bool HasUnitPanelOpen()
    {
        if (UnitUIManager.Instance == null || UnitUIManager.Instance.panelRoot == null)
            return false;
        return UnitUIManager.Instance.panelRoot.activeInHierarchy;
    }

    private void UpdateGameplayHudButtonsVisibility()
    {
        // Bottom strip visibility is now owned by BottomStripController.
        // Keep this state as read-only debug context for tutorial logic.
        gameplayHudHidden = HasCityPanelOpen() || HasUnitPanelOpen();
    }

    private void EnsureGameplayPanelsDontOverlap()
    {
        if (!HasCityPanelOpen() || !HasUnitPanelOpen())
            return;

        Unit selected = UnitSelectionManager.Instance != null ? UnitSelectionManager.Instance.SelectedUnit : null;
        if (selected != null)
        {
            if (CityUIManager.Instance != null)
                CityUIManager.Instance.ClosePanel();
        }
        else
        {
            if (UnitUIManager.Instance != null)
                UnitUIManager.Instance.ClosePanel();
        }
    }

    private void EnsureGameplayHudButtonsCached()
    {
        if (gameplayHudMenuButton != null && gameplayHudEndTurnOrNextButton != null)
            return;

        Button[] buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Button bestMenu = null;
        Button bestNext = null;
        float bestMenuY = float.PositiveInfinity;
        float bestNextY = float.PositiveInfinity;

        Transform tutorialRoot = transform;
        Transform cityPanel = CityUIManager.Instance != null && CityUIManager.Instance.panelRoot != null ? CityUIManager.Instance.panelRoot.transform : null;
        Transform unitPanel = UnitUIManager.Instance != null && UnitUIManager.Instance.panelRoot != null ? UnitUIManager.Instance.panelRoot.transform : null;

        foreach (Button b in buttons)
        {
            if (b == null)
                continue;
            if (!b.gameObject.activeInHierarchy)
                continue;

            // Ignore tutorial UI buttons and panel buttons (recruit/close/etc) so we don't hide the wrong things.
            if (tutorialRoot != null && b.transform.IsChildOf(tutorialRoot))
                continue;
            if (cityPanel != null && b.transform.IsChildOf(cityPanel))
                continue;
            if (unitPanel != null && b.transform.IsChildOf(unitPanel))
                continue;

            string label = GetButtonLabel(b);
            if (string.IsNullOrWhiteSpace(label))
                continue;

            float centerY = GetButtonScreenCenterY(b);

            if (string.Equals(label, "Menu", StringComparison.OrdinalIgnoreCase))
            {
                if (centerY < bestMenuY)
                {
                    bestMenuY = centerY;
                    bestMenu = b;
                }
            }
            else if (string.Equals(label, "End Turn", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(label, "Next", StringComparison.OrdinalIgnoreCase))
            {
                if (centerY < bestNextY)
                {
                    bestNextY = centerY;
                    bestNext = b;
                }
            }
        }

        if (bestMenu != null)
            gameplayHudMenuButton = bestMenu;
        if (bestNext != null)
            gameplayHudEndTurnOrNextButton = bestNext;
    }

    private static string GetButtonLabel(Button b)
    {
        if (b == null)
            return null;

        TMP_Text tmp = b.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text))
            return tmp.text.Trim();

        Text txt = b.GetComponentInChildren<Text>(true);
        if (txt != null && !string.IsNullOrWhiteSpace(txt.text))
            return txt.text.Trim();

        return null;
    }

    private static float GetButtonScreenCenterY(Button b)
    {
        if (b == null)
            return float.PositiveInfinity;

        RectTransform rt = b.GetComponent<RectTransform>();
        if (rt == null)
            return float.PositiveInfinity;

        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < 4; i++)
        {
            Vector2 sp = RectTransformUtility.WorldToScreenPoint(null, corners[i]);
            min = Vector2.Min(min, sp);
            max = Vector2.Max(max, sp);
        }
        return (min.y + max.y) * 0.5f;
    }

    private float GetCurrentZoomValue()
    {
        if (cam == null)
            cam = Camera.main;
        if (cam == null)
            return 0f;

        return cam.orthographic ? cam.orthographicSize : cam.fieldOfView;
    }

    private bool HasZoomChangedBy(float atLeastDelta)
    {
        float now = GetCurrentZoomValue();
        if (Mathf.Approximately(now, 0f) || Mathf.Approximately(baselineZoom, 0f))
            return false;

        float delta = now - baselineZoom;
        if (atLeastDelta < 0f)
            return delta <= atLeastDelta;

        return delta >= atLeastDelta;
    }

    private bool IsCameraNearPlayerCity(float maxDistanceWorld)
    {
        if (playerCity == null)
            return true;

        if (cam == null)
            cam = Camera.main;
        if (cam == null)
            return true;

        Vector3 camPos = cam.transform.position;
        camPos.z = 0f;

        Vector3 cityPos = playerCity.transform.position;
        cityPos.z = 0f;

        return (camPos - cityPos).sqrMagnitude <= maxDistanceWorld * maxDistanceWorld;
    }

    private int CountSeenTilesForPlayer()
    {
        if (grid == null)
            return 0;

        int count = 0;
        foreach (TileVisibility t in grid.GetAllTiles())
        {
            if (t == null) continue;
            t.GetSeenState(out bool playerSeen, out _);
            if (playerSeen) count++;
        }
        return count;
    }

    private bool TryGetExploredNotVisibleTile(out Vector3 worldPos)
    {
        worldPos = Vector3.zero;
        if (grid == null)
            return false;

        foreach (TileVisibility t in grid.GetAllTiles())
        {
            if (t == null)
                continue;

            if (t.hasBeenSeen && !t.isVisibleNow)
            {
                worldPos = t.transform.position;
                return true;
            }
        }

        return false;
    }

    private bool HasRecentFailedRecruitAttempt()
    {
        if (CityUIManager.Instance == null)
            return false;

        if (CityUIManager.Instance.lastRecruitAttemptFrame < stepEnterFrame)
            return false;

        return CityUIManager.Instance.lastRecruitAttemptSucceeded == false;
    }

    private string GetWarriorCostLabel()
    {
        if (tm == null)
            return "Gold";

        return tm.warriorCost + " Gold";
    }

    private Vector3 GetTileWorld(int gridX, int gridY)
    {
        if (grid != null && grid.TryGetTile(gridX, gridY, out TileVisibility tile) && tile != null)
        {
            return tile.transform.position;
        }

        return playerCity != null ? playerCity.transform.position : Vector3.zero;
    }

    private static bool ApproximatelySameTile(Vector3 a, Vector3 b, float epsilon = 0.01f)
    {
        a.z = 0f;
        b.z = 0f;
        return (a - b).sqrMagnitude <= epsilon * epsilon;
    }

    private bool IsUnitAt(Unit unit, Vector3 worldPos)
    {
        if (unit == null)
            return false;

        return ApproximatelySameTile(unit.transform.position, worldPos);
    }

    private void SetAllowedMove(Unit unit, Vector3 allowedTargetWorld, bool forceSingleHighlight = true)
    {
        TutorialGate.CanSelectUnit = u => u == unit;
        TutorialGate.CanMoveOrAttackToPosition = (u, pos) => u == unit && ApproximatelySameTile(pos, allowedTargetWorld);
        TutorialGate.CanClickCity = _ => false;
        TutorialGate.CanRecruitWarrior = () => false;
        TutorialGate.CanEndTurn = () => false;

        TutorialGate.ForceSingleTargetHighlight = forceSingleHighlight;
        TutorialGate.ForcedTargetWorldPosition = forceSingleHighlight ? allowedTargetWorld : Vector3.zero;
        TutorialGate.ForcedTargetIsAttack = false;
    }

    private void SetAllowedAttack(Unit attacker, Unit target, bool forceSingleHighlight = true)
    {
        if (attacker == null || target == null)
        {
            TutorialGate.CanSelectUnit = _ => false;
            TutorialGate.CanMoveOrAttackToPosition = null;
            TutorialGate.ForceSingleTargetHighlight = false;
            TutorialGate.CanEndTurn = () => false;
            return;
        }

        Vector3 targetPos = target.transform.position;
        TutorialGate.CanSelectUnit = u => u == attacker;
        TutorialGate.CanMoveOrAttackToPosition = (u, pos) => u == attacker && ApproximatelySameTile(pos, targetPos);
        TutorialGate.CanClickCity = _ => false;
        TutorialGate.CanRecruitWarrior = () => false;
        TutorialGate.CanEndTurn = () => false;

        TutorialGate.ForceSingleTargetHighlight = forceSingleHighlight;
        TutorialGate.ForcedTargetWorldPosition = forceSingleHighlight ? targetPos : Vector3.zero;
        TutorialGate.ForcedTargetIsAttack = true;
    }

    private RectTransform GetGoldRectTransform()
    {
        if (tm != null && tm.goldText != null)
        {
            return tm.goldText.GetComponent<RectTransform>();
        }

        TMP_Text[] texts = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (TMP_Text t in texts)
        {
            if (t == null) continue;
            if (!string.IsNullOrEmpty(t.text) && t.text.TrimStart().StartsWith("Gold", StringComparison.OrdinalIgnoreCase))
            {
                return t.GetComponent<RectTransform>();
            }
        }

        return null;
    }

    private TMP_Text GetGoldTMP()
    {
        if (tm != null && tm.goldText != null)
            return tm.goldText;

        TMP_Text[] texts = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (TMP_Text t in texts)
        {
            if (t == null) continue;
            if (!string.IsNullOrEmpty(t.text) && t.text.TrimStart().StartsWith("Gold", StringComparison.OrdinalIgnoreCase))
                return t;
        }

        return null;
    }

    private void HighlightGoldText(bool highlight)
    {
        goldTmp ??= GetGoldTMP();
        if (goldTmp == null)
            return;

        if (!goldTmpOriginalColorSet)
        {
            goldTmpOriginalColor = goldTmp.color;
            goldTmpOriginalColorSet = true;
        }

        if (highlight)
        {
            goldTmp.color = new Color(0.18f, 0.52f, 0.82f, 1f);
            goldTmpHighlighted = true;
        }
        else if (goldTmpOriginalColorSet)
        {
            goldTmp.color = goldTmpOriginalColor;
            goldTmpHighlighted = false;
        }
    }

    private RectTransform GetRecruitButtonRectTransform()
    {
        if (CityUIManager.Instance != null && CityUIManager.Instance.recruitWarriorButtonText != null)
        {
            Button btn = CityUIManager.Instance.recruitWarriorButtonText.GetComponentInParent<Button>();
            if (btn != null)
                return btn.GetComponent<RectTransform>();

            return CityUIManager.Instance.recruitWarriorButtonText.GetComponent<RectTransform>();
        }

        return FindButtonRectByLabelContains("Warrior");
    }

    private RectTransform FindButtonRectByLabelContains(string contains)
    {
        if (string.IsNullOrWhiteSpace(contains))
            return null;

        TMP_Text[] texts = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (TMP_Text t in texts)
        {
            if (t == null || string.IsNullOrEmpty(t.text))
                continue;

            if (t.text.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            Button b = t.GetComponentInParent<Button>();
            if (b == null)
                continue;

            return b.GetComponent<RectTransform>();
        }

        return null;
    }

    private Unit GetUnitOnCityTile()
    {
        if (playerCity == null)
            return null;
        Unit u = GridUtils.GetUnitAtPosition(playerCity.transform.position, null);
        return u != null && u.isPlayerOwned ? u : null;
    }

    private void EnsureEnemy1VisibleForStory()
    {
        CacheCitiesAndUnits();

        if (enemy1 != null)
            return;

        int cx = playerCity != null ? playerCity.x : 1;
        int cy = playerCity != null ? playerCity.y : 1;
        Vector3 preferred = GetTileWorld(cx + 4, cy + 4);

        Vector3 spawnPos = preferred;
        if (GridUtils.IsTileOccupied(spawnPos, null) || GridUtils.GetCityAtPosition(spawnPos) != null)
        {
            Unit anchor = warrior1 != null ? warrior1 : warrior2;
            Vector3 anchorPos = anchor != null ? anchor.transform.position : (playerCity != null ? playerCity.transform.position : Vector3.zero);
            if (!TryFindFreeNeighborTile(anchorPos, out spawnPos))
                return;
        }

        enemy1 = SpawnTutorialUnit(spawnPos, isPlayerOwned: false, "Enemy Warrior 1");
        if (tm != null)
        {
            tm.RecalculatePlayerVisibility();
        }
    }

    private Unit SpawnTutorialUnit(Vector3 worldPos, bool isPlayerOwned, string unitName)
    {
        GameObject prefab = null;
        if (isPlayerOwned)
        {
            if (playerCity != null) prefab = playerCity.warriorPrefab;
        }
        else
        {
            if (enemyCity != null) prefab = enemyCity.warriorPrefab;
            if (prefab == null && playerCity != null) prefab = playerCity.warriorPrefab;
        }

        if (prefab == null)
        {
            Debug.LogWarning("TutorialOverlay: Cannot spawn tutorial unit, no warriorPrefab found on cities.");
            return null;
        }

        if (GridUtils.IsTileOccupied(worldPos, null))
            return null;

        if (GridUtils.GetCityAtPosition(worldPos) != null)
            return null;

        GameObject go = Instantiate(prefab, worldPos, Quaternion.identity);
        go.name = unitName;

        Unit unit = go.GetComponent<Unit>();
        if (unit != null)
        {
            unit.isPlayerOwned = isPlayerOwned;
            unit.currentCity = null;
            unit.ResetMovementForTurn();
            bool isActiveTurn = tm != null && tm.IsCurrentSideOwner(isPlayerOwned) && tm.IsHumanTurn();
            unit.UpdateMoveOutline(isActiveTurn);
        }

        OwnedSprite owned = go.GetComponent<OwnedSprite>();
        if (owned != null)
        {
            owned.SetOwner(isPlayerOwned);
        }

        return unit;
    }

    private bool AreAdjacent(Vector3 a, Vector3 b)
    {
        if (grid == null)
            return (a - b).sqrMagnitude <= 2.5f;

        if (!grid.TryGetTileAtWorldPosition(a, out TileVisibility ta))
            return false;
        if (!grid.TryGetTileAtWorldPosition(b, out TileVisibility tb))
            return false;

        int dx = Mathf.Abs(ta.gridX - tb.gridX);
        int dy = Mathf.Abs(ta.gridY - tb.gridY);
        return dx <= 1 && dy <= 1 && (dx + dy) > 0;
    }

    private bool TryFindFreeNeighborTile(Vector3 originWorld, out Vector3 tileWorld)
    {
        tileWorld = originWorld;

        if (grid == null || !grid.TryGetTileAtWorldPosition(originWorld, out TileVisibility originTile))
            return false;

        // Prefer cardinal directions first, then diagonals.
        int[] dxs = { 1, -1, 0, 0, 1, 1, -1, -1 };
        int[] dys = { 0, 0, 1, -1, 1, -1, 1, -1 };

        for (int i = 0; i < dxs.Length; i++)
        {
            int nx = originTile.gridX + dxs[i];
            int ny = originTile.gridY + dys[i];
            if (!grid.TryGetTile(nx, ny, out TileVisibility neighbor))
                continue;

            Vector3 pos = neighbor.transform.position;
            if (GridUtils.IsTileOccupied(pos, null))
                continue;
            if (GridUtils.GetCityAtPosition(pos) != null)
                continue;

            tileWorld = pos;
            return true;
        }

        return false;
    }

    private bool TryGetNeighborTile(Vector3 originWorld, int dx, int dy, out Vector3 tileWorld)
    {
        tileWorld = originWorld;

        if (grid == null || !grid.TryGetTileAtWorldPosition(originWorld, out TileVisibility originTile))
            return false;

        int nx = originTile.gridX + dx;
        int ny = originTile.gridY + dy;
        if (!grid.TryGetTile(nx, ny, out TileVisibility neighbor) || neighbor == null)
            return false;

        tileWorld = neighbor.transform.position;
        return true;
    }

    private bool IsTileFree(Vector3 pos)
    {
        if (GridUtils.IsTileOccupied(pos, null))
            return false;
        if (GridUtils.GetCityAtPosition(pos) != null)
            return false;

        return true;
    }

    private bool TryGetFreeNeighborTile(Vector3 originWorld, int dx, int dy, out Vector3 tileWorld)
    {
        tileWorld = originWorld;
        if (!TryGetNeighborTile(originWorld, dx, dy, out Vector3 candidate))
            return false;
        if (!IsTileFree(candidate))
            return false;

        tileWorld = candidate;
        return true;
    }

    private Vector2Int GetGridDirection(Vector3 fromWorld, Vector3 toWorld)
    {
        if (grid == null)
            return Vector2Int.zero;
        if (!grid.TryGetTileAtWorldPosition(fromWorld, out TileVisibility fromTile))
            return Vector2Int.zero;
        if (!grid.TryGetTileAtWorldPosition(toWorld, out TileVisibility toTile))
            return Vector2Int.zero;

        int dx = Mathf.Clamp(toTile.gridX - fromTile.gridX, -1, 1);
        int dy = Mathf.Clamp(toTile.gridY - fromTile.gridY, -1, 1);
        return new Vector2Int(dx, dy);
    }

    private IEnumerator ScriptEnemyKillWarrior1OnAITurn5()
    {
        // Wait for AI turn 5 to start.
        while (tm != null && (tm.turnNumber != 5 || tm.isPlayerTurn))
            yield return null;

        CacheCitiesAndUnits();

        if (enemy1 == null)
        {
            EnsureEnemy1VisibleForStory();
        }

        if (enemy1 == null)
            yield break;

        Unit victim = warrior1 != null ? warrior1 : warrior2;
        if (victim == null)
            yield break;

        if (!AreAdjacent(enemy1.transform.position, victim.transform.position))
        {
            if (TryFindFreeNeighborTile(victim.transform.position, out Vector3 adjPos))
            {
                enemy1.transform.position = adjPos;
            }
        }

        yield return new WaitForSeconds(0.15f);

        if (victim != null)
        {
            Vector3 victimPos = victim.transform.position;
            bool killed = enemy1.Attack(victim);
            if (killed)
            {
                enemy1.transform.position = victimPos;
                if (victim == warrior1)
                {
                    SpeechBubble.Show(enemy1.transform, "Greetings from Clan Chief Salami!", seconds: 9999f);
                }
            }
        }

        if (tm != null)
        {
            tm.RecalculatePlayerVisibility();
        }
    }

    private IEnumerator ScriptSpawnEnemy2OnAITurn6()
    {
        while (tm != null && (tm.turnNumber != 6 || tm.isPlayerTurn))
            yield return null;

        CacheCitiesAndUnits();

        if (enemy2 != null)
            yield break;

        int cx = playerCity != null ? playerCity.x : 1;
        int cy = playerCity != null ? playerCity.y : 1;
        Vector3 preferred = GetTileWorld(cx + 4, cy + 3);

        Vector3 spawnPos = preferred;
        if (GridUtils.IsTileOccupied(spawnPos, null) || GridUtils.GetCityAtPosition(spawnPos) != null)
        {
            Unit anchor = warrior2 != null ? warrior2 : warrior1;
            if (anchor == null)
                yield break;

            if (!TryFindFreeNeighborTile(anchor.transform.position, out spawnPos))
                yield break;
        }

        enemy2 = SpawnTutorialUnit(spawnPos, isPlayerOwned: false, "Enemy Warrior 2");

        if (tm != null)
        {
            tm.RecalculatePlayerVisibility();
        }
    }

    private IEnumerator ScriptBossCutsceneOnAITurn7()
    {
        while (tm != null && (tm.turnNumber != 7 || tm.isPlayerTurn))
            yield return null;

        CacheCitiesAndUnits();

        Unit hero = warrior2 != null ? warrior2 : warrior1;
        if (hero == null)
            yield break;

        if (enemyCity == null)
            yield break;

        int ex = enemyCity.x;
        int ey = enemyCity.y;
        Vector3 enemyCityPos = enemyCity.transform.position;

        // Move hero in front of the enemy city (deterministic).
        Vector3 heroPos = GetTileWorld(ex - 1, ey - 1);
        if (!GridUtils.IsTileOccupied(heroPos, hero) && GridUtils.GetCityAtPosition(heroPos) == null)
        {
            hero.transform.position = heroPos;
        }

        yield return new WaitForSeconds(0.2f);

        if (boss == null)
        {
            Vector3 bossPos = GetTileWorld(ex - 1, ey);
            if (GridUtils.IsTileOccupied(bossPos, null) || GridUtils.GetCityAtPosition(bossPos) != null)
            {
                // Fallback to any free neighbor.
                Vector3 bossAnchor = hero != null ? hero.transform.position : enemyCityPos;
                if (!TryFindFreeNeighborTile(bossAnchor, out bossPos))
                    yield break;
            }

            boss = SpawnTutorialUnit(bossPos, isPlayerOwned: false, "Clan Chief Big Salami");
        }

        yield return new WaitForSeconds(0.25f);

        if (boss != null && hero != null)
        {
            SpeechBubble.Show(hero.transform, "This is the village of Clan Chief Salami!", seconds: 2.3f, worldOffset: new Vector3(0f, 1.0f, 0f));
            SpeechBubble.Show(boss.transform, "Let's see if you can take this salami!", seconds: 2.3f, worldOffset: new Vector3(0f, 1.0f, 0f));

            boss.Attack(hero);
        }

        if (tm != null)
        {
            tm.RecalculatePlayerVisibility();
        }
    }

    private void EnsureEventSystem()
    {
        EventSystem[] systems = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (systems != null && systems.Length > 0)
            return;

        Debug.LogWarning("TutorialOverlay: No EventSystem found. Please add one to the gameplay scene.");
    }

}
