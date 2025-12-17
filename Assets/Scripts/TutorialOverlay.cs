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

    [Header("Pointer Tuning")]
    [Tooltip("Minimum arrow length in pixels. Reduced automatically if needed to keep the arrow tail visible.")]
    public float pointerMinArrowLength = 320f;
    [Tooltip("Minimum distance (in pixels) from the screen safe area for the arrow tail.")]
    public float pointerTailScreenPadding = 12f;
    [Tooltip("Minimum normalized viewport Y for arrow tail (prevents it hugging the bottom edge).")]
    [Range(0f, 0.5f)]
    public float pointerTailMinViewportY = 0.08f;
    [Tooltip("Extra clearance (in pixels) above the bottom HUD/panels for the arrow tail.")]
    public float pointerTailBottomHudClearance = 40f;

    [Header("Scenes")]
    public string mainMenuSceneName = "MainMenu";

    [Header("Tutorial Tuning")]
    [Tooltip("Force AI delay during the tutorial so scripted events have time to run.")]
    public float minimumAiTurnDelayDuringTutorial = 0.8f;
    [Tooltip("How long (in seconds) before a step shows an extra hint, if not completed.")]
    public float defaultHintAfterSeconds = 10f;

    private Canvas canvas;
    private GameObject root;
    private RectTransform panelRect;
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

    private Coroutine scriptedRoutine;
    private Coroutine autoAdvanceRoutine;
    private int autoAdvanceStepIndex = -1;

    // Pointer UI
    private RectTransform pointerLayer;
    private RectTransform pointerArrowHeadRect;
    private Image pointerArrowHeadImage;
    private RectTransform pointerArrowShaftRect;
    private Image pointerArrowShaftImage;
    private RectTransform pointerHighlightRect;
    private Image pointerHighlightImage;
    private bool pointerShowHighlight;
    private bool pointerWorldMode;
    private Vector3 pointerWorldPosition;
    private RectTransform pointerUiTarget;
    private bool pointerVisible;
    private float pointerPulseT;
    private bool pointerShowArrow;
    private bool pointerArrowFromScreenCenter;
    private bool pointerArrowPreferRightMiddle;
    private bool pointerArrowPreferTopRight;
    private bool pointerArrowStartOverride;
    private Vector2 pointerArrowStartScreen;
    private bool pointerArrowEndOverride;
    private Vector2 pointerArrowEndScreen;
    private bool pointerHighlightYellow;

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

    private static bool IsMobilePlatform()
    {
        return Application.isMobilePlatform;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Object.FindFirstObjectByType<TutorialOverlay>() != null)
            return;

        // Only spawn the tutorial when explicitly requested from the main menu.
        if (!TutorialLaunch.IsShowRequested())
            return;

        // Only spawn in scenes that contain gameplay.
        if (Object.FindFirstObjectByType<TurnManager>() == null)
            return;

        GameObject go = new GameObject("TutorialOverlay");
        go.AddComponent<TutorialOverlay>();
    }

    void Awake()
    {
        EnsureEventSystem();
        BuildUI();
        Show(false);
    }

    IEnumerator Start()
    {
        yield return WaitForTurnManagerReady();

        tm = TurnManager.Instance;
        if (tm == null)
        {
            Destroy(gameObject);
            yield break;
        }

        bool forced = TutorialLaunch.TryConsumeShowRequest();
        if (!forced)
        {
            Destroy(gameObject);
            yield break;
        }

        // Tutorial assumes a single player vs AI flow.
        if (tm.currentMode != TurnManager.GameMode.VsAI)
        {
            Destroy(gameObject);
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

        Destroy(gameObject);
    }

    private void Show(bool show)
    {
        isShowing = show;
        if (root != null)
            root.SetActive(show);
        if (canvas != null)
            canvas.enabled = show;
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

    private static Sprite cachedWhiteSprite;
    private static Sprite cachedTriangleSprite;

    private static Sprite GetWhiteSprite()
    {
        if (cachedWhiteSprite != null)
            return cachedWhiteSprite;

        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        cachedWhiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return cachedWhiteSprite;
    }

    private static Sprite GetTriangleSpriteDown()
    {
        if (cachedTriangleSprite != null)
            return cachedTriangleSprite;

        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
        tex.filterMode = FilterMode.Bilinear;

        // Transparent background.
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color fill = Color.white;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                tex.SetPixel(x, y, clear);
            }
        }

        // Down-pointing triangle: point at bottom center, wide at top.
        // Note: Texture2D coordinates have y=0 at the bottom.
        for (int y = 0; y < size; y++)
        {
            float t = y / (float)(size - 1); // 0 at bottom (tip), 1 at top (wide)
            int halfWidth = Mathf.RoundToInt((size * 0.48f) * t);
            int cx = size / 2;
            int x0 = Mathf.Clamp(cx - halfWidth, 0, size - 1);
            int x1 = Mathf.Clamp(cx + halfWidth, 0, size - 1);
            for (int x = x0; x <= x1; x++)
            {
                tex.SetPixel(x, y, fill);
            }
        }

        tex.Apply();
        cachedTriangleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return cachedTriangleSprite;
    }

    private void HidePointer()
    {
        pointerVisible = false;
        pointerWorldMode = false;
        pointerUiTarget = null;
        pointerPulseT = 0f;
        pointerShowArrow = false;
        pointerArrowFromScreenCenter = false;
        pointerArrowPreferRightMiddle = false;
        pointerArrowPreferTopRight = false;
        pointerArrowStartOverride = false;
        pointerArrowEndOverride = false;
        pointerShowHighlight = false;
        pointerHighlightYellow = false;

        if (pointerHighlightRect != null) pointerHighlightRect.gameObject.SetActive(false);
        if (pointerArrowHeadRect != null) pointerArrowHeadRect.gameObject.SetActive(false);
        if (pointerArrowShaftRect != null) pointerArrowShaftRect.gameObject.SetActive(false);
    }

    private void SetPointerArrowEndOverride(Vector2 screenPos)
    {
        pointerArrowEndOverride = true;
        pointerArrowEndScreen = screenPos;
    }

    private void SetPointerArrowStartOverride(Vector2 screenPos)
    {
        pointerArrowStartOverride = true;
        pointerArrowStartScreen = ClampArrowTailToSafeArea(screenPos);
    }

    private void PointAtWorld(Vector3 worldPos, bool showArrow = false, bool arrowFromScreenCenter = false, bool showHighlight = true, bool arrowFromRightMiddle = false, bool arrowFromTopRight = false, bool highlightYellow = false)
    {
        pointerVisible = true;
        pointerWorldMode = true;
        pointerWorldPosition = worldPos;
        pointerUiTarget = null;
        pointerPulseT = 0f;
        pointerShowArrow = showArrow;
        pointerArrowFromScreenCenter = arrowFromScreenCenter;
        pointerArrowPreferRightMiddle = arrowFromRightMiddle;
        pointerArrowPreferTopRight = arrowFromTopRight;
        pointerShowHighlight = showHighlight;
        pointerArrowStartOverride = false;
        pointerArrowEndOverride = false;
        pointerHighlightYellow = highlightYellow;

        // Use only the highlight rectangle for world targets (avoids missing-glyph squares on some fonts/platforms).
        if (pointerHighlightRect != null) pointerHighlightRect.gameObject.SetActive(pointerShowHighlight);
        ApplyPointerHighlightTheme(isUi: false);
        if (pointerArrowHeadRect != null) pointerArrowHeadRect.gameObject.SetActive(pointerShowArrow);
        if (pointerArrowShaftRect != null) pointerArrowShaftRect.gameObject.SetActive(pointerShowArrow);
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
        pointerArrowFromScreenCenter = arrowFromScreenCenter;
        pointerShowHighlight = showHighlight;
        pointerArrowPreferRightMiddle = arrowFromRightMiddle;
        pointerArrowPreferTopRight = arrowFromTopRight;
        pointerArrowStartOverride = false;
        pointerArrowEndOverride = false;
        pointerHighlightYellow = highlightYellow;

        // Use only the highlight rectangle for UI targets (keeps the pointer minimal and clear).
        if (pointerHighlightRect != null) pointerHighlightRect.gameObject.SetActive(pointerShowHighlight);
        ApplyPointerHighlightTheme(isUi: true);
        if (pointerArrowHeadRect != null) pointerArrowHeadRect.gameObject.SetActive(pointerShowArrow);
        if (pointerArrowShaftRect != null) pointerArrowShaftRect.gameObject.SetActive(pointerShowArrow);

        // Pre-size highlight. Actual positioning is updated every frame.
        if (pointerHighlightRect != null)
            pointerHighlightRect.sizeDelta = target.rect.size + new Vector2(padding * 2f, padding * 2f);
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

    private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        float o1 = Orientation(p1, p2, q1);
        float o2 = Orientation(p1, p2, q2);
        float o3 = Orientation(q1, q2, p1);
        float o4 = Orientation(q1, q2, p2);

        if (o1 == 0f && OnSegment(p1, q1, p2)) return true;
        if (o2 == 0f && OnSegment(p1, q2, p2)) return true;
        if (o3 == 0f && OnSegment(q1, p1, q2)) return true;
        if (o4 == 0f && OnSegment(q1, p2, q2)) return true;

        return (o1 > 0f) != (o2 > 0f) && (o3 > 0f) != (o4 > 0f);
    }

    private static float Orientation(Vector2 a, Vector2 b, Vector2 c)
    {
        float v = (b.y - a.y) * (c.x - b.x) - (b.x - a.x) * (c.y - b.y);
        if (Mathf.Abs(v) < 0.00001f) return 0f;
        return v;
    }

    private static bool OnSegment(Vector2 a, Vector2 b, Vector2 c)
    {
        return b.x <= Mathf.Max(a.x, c.x) + 0.00001f &&
               b.x >= Mathf.Min(a.x, c.x) - 0.00001f &&
               b.y <= Mathf.Max(a.y, c.y) + 0.00001f &&
               b.y >= Mathf.Min(a.y, c.y) - 0.00001f;
    }

    private static bool SegmentIntersectsRect(Vector2 a, Vector2 b, Rect r)
    {
        if (r.Contains(a) || r.Contains(b))
            return true;

        Vector2 r1 = new Vector2(r.xMin, r.yMin);
        Vector2 r2 = new Vector2(r.xMax, r.yMin);
        Vector2 r3 = new Vector2(r.xMax, r.yMax);
        Vector2 r4 = new Vector2(r.xMin, r.yMax);

        return SegmentsIntersect(a, b, r1, r2) ||
               SegmentsIntersect(a, b, r2, r3) ||
               SegmentsIntersect(a, b, r3, r4) ||
               SegmentsIntersect(a, b, r4, r1);
    }

    private void GetArrowTailSafeBounds(out float minX, out float maxX, out float minY, out float maxY)
    {
        Rect safeArea = Screen.safeArea;
        float pad = Mathf.Max(0f, pointerTailScreenPadding);
        minX = safeArea.xMin + pad;
        maxX = safeArea.xMax - pad;
        minY = safeArea.yMin + pad;
        maxY = safeArea.yMax - pad;

        minY = Mathf.Max(minY, Screen.height * Mathf.Clamp01(pointerTailMinViewportY));
        float bottomHudTop = GetBottomHudTopScreenY();
        if (bottomHudTop > 0f)
            minY = Mathf.Max(minY, bottomHudTop + pointerTailBottomHudClearance);

        minX = Mathf.Min(minX, maxX);
        minY = Mathf.Min(minY, maxY);
    }

    private Vector2 ClampArrowTailToSafeArea(Vector2 v)
    {
        GetArrowTailSafeBounds(out float minX, out float maxX, out float minY, out float maxY);
        return new Vector2(
            Mathf.Clamp(v.x, minX, maxX),
            Mathf.Clamp(v.y, minY, maxY));
    }

    private float GetBottomHudTopScreenY()
    {
        EnsureGameplayHudButtonsCached();

        float topY = 0f;

        void Consider(RectTransform rt)
        {
            if (rt == null)
                return;
            Rect r = GetScreenRect(rt);
            topY = Mathf.Max(topY, r.yMax);
        }

        if (CityUIManager.Instance != null && CityUIManager.Instance.panelRoot != null)
            Consider(CityUIManager.Instance.panelRoot.GetComponent<RectTransform>());
        if (UnitUIManager.Instance != null && UnitUIManager.Instance.panelRoot != null)
            Consider(UnitUIManager.Instance.panelRoot.GetComponent<RectTransform>());

        if (gameplayHudMenuButton != null)
            Consider(gameplayHudMenuButton.GetComponent<RectTransform>());
        if (gameplayHudEndTurnOrNextButton != null)
            Consider(gameplayHudEndTurnOrNextButton.GetComponent<RectTransform>());

        return topY;
    }

    private Vector2 GetArrowStartScreen(Vector2 endScreen)
    {
        GetArrowTailSafeBounds(out float minX, out float maxX, out float minY, out float maxY);

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float rightX = Mathf.Max(minX, maxX - 140f);
        Vector2 rightMiddle = new Vector2(rightX, Mathf.Clamp(Screen.height * 0.5f, minY, maxY));
        Vector2 topRight = new Vector2(rightX, Mathf.Max(minY, maxY - 140f));

        Vector2 ClampSafe(Vector2 v)
        {
            return new Vector2(
                Mathf.Clamp(v.x, minX, maxX),
                Mathf.Clamp(v.y, minY, maxY));
        }

        Vector2 EnsureMinLength(Vector2 s, float desiredMinLen)
        {
            Vector2 d = endScreen - s;
            float l = d.magnitude;
            if (l < 0.001f)
                return ClampSafe(s);

            if (l < desiredMinLen)
                s = endScreen - (d / l) * desiredMinLen;

            return ClampSafe(s);
        }

        Rect nextRect = default;
        bool hasNextRect = false;
        if (nextButton != null)
        {
            RectTransform nextRt = nextButton.GetComponent<RectTransform>();
            if (nextRt != null)
            {
                nextRect = GetScreenRect(nextRt);
                hasNextRect = true;
            }
        }

        float minLen = Mathf.Max(80f, pointerMinArrowLength);

        Vector2 start = pointerArrowPreferTopRight ? topRight : (pointerArrowPreferRightMiddle ? rightMiddle : screenCenter);

        bool IntersectsNext(Vector2 a, Vector2 b)
        {
            return hasNextRect && SegmentIntersectsRect(a, b, nextRect);
        }

        if (pointerArrowPreferTopRight)
        {
            // For top-right starts, try a few nearby top-right positions first (avoid pushing the start below the panel).
            Vector2[] candidates =
            {
                topRight,
                new Vector2(Screen.width - 140f, Screen.height - 240f),
                new Vector2(Screen.width - 240f, Screen.height - 140f),
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

        Vector3 screenCenter;
        if (pointerWorldMode)
        {
            if (cam == null) cam = Camera.main;
            if (cam == null)
                return;
            Vector3 sp = cam.WorldToScreenPoint(pointerWorldPosition);
            screenCenter = new Vector3(sp.x, sp.y, 0f);

            if (pointerHighlightRect != null)
            {
                pointerHighlightRect.position = screenCenter;
                pointerHighlightRect.sizeDelta = GetWorldTargetHighlightSizePixels(pointerWorldPosition);
                pointerHighlightRect.localScale = Vector3.one * pulse;
            }

            if (pointerShowArrow && pointerArrowHeadRect != null && pointerArrowShaftRect != null)
            {
                float headH = 28f;
                float headW = 38f;
                float shaftW = 10f;

                Vector2 hi = pointerHighlightRect != null ? pointerHighlightRect.sizeDelta : new Vector2(96f, 96f);

                if (pointerArrowFromScreenCenter)
                {
                    Vector2 end = new Vector2(screenCenter.x, screenCenter.y);
                    Vector2 start = pointerArrowStartOverride ? pointerArrowStartScreen : GetArrowStartScreen(end);

                    Vector2 dir = end - start;
                    if (dir.sqrMagnitude < 0.001f)
                        dir = Vector2.down;
                    dir.Normalize();

                    float targetRadius = Mathf.Max(hi.x, hi.y) * 0.5f;
                    float tipToTargetPadding = 8f;
                    Vector2 tipPos = end - dir * (targetRadius + tipToTargetPadding);

                    float tipToCenter = headH * 0.45f;
                    Vector2 headCenter = tipPos - dir * tipToCenter;

                    Vector2 shaftStart = start;
                    // Stop the shaft far enough before the head so scaling/pulsing never makes it overlap the head.
                    Vector2 shaftEnd = headCenter - dir * (headH * 0.55f);
                    float shaftLen = Mathf.Max(18f, Vector2.Distance(shaftStart, shaftEnd));
                    Vector2 shaftCenter = (shaftStart + shaftEnd) * 0.5f;

                    pointerArrowShaftRect.sizeDelta = new Vector2(shaftW, shaftLen);
                    pointerArrowShaftRect.position = new Vector3(shaftCenter.x, shaftCenter.y, 0f);
                    pointerArrowShaftRect.rotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.up, dir));
                    pointerArrowShaftRect.localScale = Vector3.one * pulse;

                    pointerArrowHeadRect.sizeDelta = new Vector2(headW, headH);
                    pointerArrowHeadRect.position = new Vector3(headCenter.x, headCenter.y, 0f);
                    pointerArrowHeadRect.rotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.down, dir));
                    pointerArrowHeadRect.localScale = Vector3.one * pulse;
                }
                else
                {
                    // Default arrow mode: vertical arrow hovering above the highlight.
                    pointerArrowHeadRect.rotation = Quaternion.identity;
                    pointerArrowShaftRect.rotation = Quaternion.identity;

                    float topY = screenCenter.y + (hi.y * 0.5f);

                    // Arrow head slightly above the highlight.
                    pointerArrowHeadRect.sizeDelta = new Vector2(headW, headH);
                    pointerArrowHeadRect.position = new Vector3(screenCenter.x, topY + (headH * 0.5f) + 14f, 0f);
                    pointerArrowHeadRect.localScale = Vector3.one * pulse;

                    // Arrow shaft connects to the highlight top.
                    float shaftTop = pointerArrowHeadRect.position.y - (headH * 0.5f);
                    float shaftBottom = topY + 4f;
                    float shaftH = Mathf.Max(18f, shaftTop - shaftBottom);
                    pointerArrowShaftRect.sizeDelta = new Vector2(shaftW, shaftH);
                    pointerArrowShaftRect.position = new Vector3(screenCenter.x, shaftBottom + shaftH * 0.5f, 0f);
                    pointerArrowShaftRect.localScale = Vector3.one * pulse;
                }
            }

            return;
        }

        if (pointerUiTarget == null)
            return;

        Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(pointerLayer, pointerUiTarget);
        Vector3 centerLocal = b.center;
        Vector3 centerWorld = pointerLayer.TransformPoint(centerLocal);
        screenCenter = centerWorld;

        if (pointerHighlightRect != null)
        {
            pointerHighlightRect.position = centerWorld;
            pointerHighlightRect.sizeDelta = new Vector2(b.size.x, b.size.y) + new Vector2(24f, 18f);
            pointerHighlightRect.localScale = Vector3.one * pulse;
        }

        if (pointerShowArrow && pointerArrowHeadRect != null && pointerArrowShaftRect != null)
        {
            float headH = 28f;
            float headW = 38f;
            float shaftW = 10f;

            Vector2 end = pointerArrowEndOverride ? pointerArrowEndScreen : new Vector2(centerWorld.x, centerWorld.y);
            Vector2 start =
                pointerArrowStartOverride ? pointerArrowStartScreen :
                (pointerArrowFromScreenCenter ? GetArrowStartScreen(end) : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));

            Vector2 dir = end - start;
            if (dir.sqrMagnitude < 0.001f)
                dir = Vector2.down;
            dir.Normalize();

            float targetEdgeDist = 0f;
            if (pointerShowHighlight)
            {
                Vector2 targetSize = new Vector2(b.size.x, b.size.y);
                Vector2 absDir = new Vector2(Mathf.Abs(dir.x), Mathf.Abs(dir.y));

                float tX = absDir.x > 0.0001f ? (targetSize.x * 0.5f) / absDir.x : float.PositiveInfinity;
                float tY = absDir.y > 0.0001f ? (targetSize.y * 0.5f) / absDir.y : float.PositiveInfinity;
                targetEdgeDist = Mathf.Min(tX, tY);

                if (float.IsInfinity(targetEdgeDist) || float.IsNaN(targetEdgeDist))
                    targetEdgeDist = Mathf.Max(targetSize.x, targetSize.y) * 0.5f;
            }

            float tipToTargetPadding = pointerShowHighlight ? 0f : 8f;
            Vector2 tipPos = end - dir * (targetEdgeDist + tipToTargetPadding);

            float tipToCenter = headH * 0.45f;
            Vector2 headCenter = tipPos - dir * tipToCenter;

            Vector2 shaftStart = start;
            // Stop the shaft far enough before the head so scaling/pulsing never makes it overlap the head.
            Vector2 shaftEnd = headCenter - dir * (headH * 0.55f);
            float shaftLen = Mathf.Max(18f, Vector2.Distance(shaftStart, shaftEnd));
            Vector2 shaftCenter = (shaftStart + shaftEnd) * 0.5f;

            pointerArrowShaftRect.sizeDelta = new Vector2(shaftW, shaftLen);
            pointerArrowShaftRect.position = new Vector3(shaftCenter.x, shaftCenter.y, 0f);
            pointerArrowShaftRect.rotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.up, dir));
            pointerArrowShaftRect.localScale = Vector3.one * pulse;

            pointerArrowHeadRect.sizeDelta = new Vector2(headW, headH);
            pointerArrowHeadRect.position = new Vector3(headCenter.x, headCenter.y, 0f);
            pointerArrowHeadRect.rotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.down, dir));
            pointerArrowHeadRect.localScale = Vector3.one * pulse;
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
        Destroy(gameObject);
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
    }

    private void SetPanelLayoutAvoidTopCenter()
    {
        if (panelRect == null)
            return;

        // Keep the Gold UI (top-center) visible by moving the panel slightly down and to the left.
        if (IsMobilePlatform())
        {
            panelRect.anchorMin = new Vector2(0.04f, 0.60f);
            panelRect.anchorMax = new Vector2(0.72f, 0.84f);
        }
        else
        {
            panelRect.anchorMin = new Vector2(0.04f, 0.34f);
            panelRect.anchorMax = new Vector2(0.72f, 0.60f);
        }
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
    }

    private void SetPanelLayoutUpperLeft()
    {
        if (panelRect == null)
            return;

        if (IsMobilePlatform())
        {
            panelRect.anchorMin = new Vector2(0.05f, 0.70f);
            panelRect.anchorMax = new Vector2(0.95f, 0.94f);
        }
        else
        {
            // Upper-left, narrow enough to keep the top-center HUD readable.
            panelRect.anchorMin = new Vector2(0.03f, 0.70f);
            panelRect.anchorMax = new Vector2(0.58f, 0.94f);
        }

        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
    }

    private void SetPanelLayoutIntroTopLeft()
    {
        if (panelRect == null)
            return;

        // Intro step: keep the city fully visible by placing the panel at the top-left with a bit of pixel padding.
        // Use "top anchored" layout (y anchors both at 1) so padding is reliable across aspect ratios.
        panelRect.pivot = new Vector2(panelRect.pivot.x, 1f);
        panelRect.anchorMin = new Vector2(panelRect.anchorMin.x, 1f);
        panelRect.anchorMax = new Vector2(panelRect.anchorMax.x, 1f);

        if (IsMobilePlatform())
        {
            panelRect.anchorMin = new Vector2(0.04f, 1.0f);
            panelRect.anchorMax = new Vector2(0.74f, 1.0f);
        }
        else
        {
            panelRect.anchorMin = new Vector2(0.03f, 1.0f);
            panelRect.anchorMax = new Vector2(0.58f, 1.0f);
        }

        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panelRect.anchoredPosition = new Vector2(0f, -Mathf.Max(0f, introPanelTopPadding));
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
        Vector3 tile_54 = GetTileWorld(cx + 4, cy + 3);

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
                    RectTransform hud = goldRect.parent as RectTransform;
                    Rect goldScreen = GetScreenRect(goldRect);
                    Rect hudScreen = hud != null ? GetScreenRect(hud) : goldScreen;
                    SetPointerArrowEndOverride(new Vector2(goldScreen.center.x, hudScreen.yMin));
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
                    RectTransform hud = goldRect.parent as RectTransform;
                    Rect goldScreen = GetScreenRect(goldRect);
                    Rect hudScreen = hud != null ? GetScreenRect(hud) : goldScreen;
                    SetPointerArrowEndOverride(new Vector2(goldScreen.center.x, hudScreen.yMin));
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

                        if (panelRect != null)
                        {
                            Rect panelScreen = GetScreenRect(panelRect);
                            Rect targetScreen = GetScreenRect(targetRect);
                            float startX = Mathf.Clamp(targetScreen.center.x, panelScreen.xMin + 24f, panelScreen.xMax - 24f);
                            SetPointerArrowStartOverride(new Vector2(startX, panelScreen.yMin - 10f));
                            SetPointerArrowEndOverride(new Vector2(targetScreen.center.x, targetScreen.yMax));
                        }
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
            canAdvance = () => tm != null && tm.isPlayerTurn && tm.turnNumber == 4,
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
                    RectTransform recruitRect = GetRecruitButtonRectTransform();
                    if (recruitRect != null)
                    {
                        PointAtUI(recruitRect, padding: 12f, showArrow: true, arrowFromScreenCenter: true, showHighlight: true, arrowFromRightMiddle: true);
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
                    enemy1 = SpawnTutorialUnit(tile_55, isPlayerOwned: false, "TutorialEnemy1");
                }

                if (enemy1 != null)
                {
                    PointAtWorld(enemy1.transform.position);
                }

                if (warrior1 != null)
                {
                    SpeechBubble.Show(warrior1.transform, "Hey, nice to meet you!", seconds: 2.3f);
                }
                if (enemy1 != null)
                {
                    SpeechBubble.Show(enemy1.transform, "Greetings from Clan Chief Salami!", seconds: 2.6f);
                }

                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            }
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 5",
            body = "Move your second Warrior to the highlighted tile.",
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
            body = "Warrior: Noooooo!\n\nSelect Warrior 2.",
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
                    TutorialGate.CanSelectUnit = u => u == warrior2;
                }
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            },
            canAdvance = () => UnitSelectionManager.Instance != null && warrior2 != null && UnitSelectionManager.Instance.SelectedUnit == warrior2,
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Tap your second Warrior."
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 6",
            body = "Now attack the enemy!",
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
            hintText = "Tap the enemy unit."
        });

        steps.Add(new TutorialStep
        {
            title = "Revenge",
            body = "That's revenge.\n\nNow let's take the fight to their city (red). Capturing it wins the game.",
            nextLabel = "Next",
            autoAdvance = true,
            autoAdvanceDelaySeconds = 2.25f,
            onEnter = () =>
            {
                CacheCitiesAndUnits();
                if (enemyCity != null)
                {
                    SetPanelLayoutUpperLeft();
                    PointAtWorld(enemyCity.transform.position, showArrow: true, arrowFromScreenCenter: true, showHighlight: true);
                    LockCameraToWorld(enemyCity.transform.position);
                }
                else
                {
                    HidePointer();
                }

                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            },
            canAdvance = () => Time.unscaledTime - stepEnterUnscaledTime > 1.8f
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 6",
            body = "Another enemy will show up...\n\nYou have taken all possible actions this turn.\n\nIn this case, the game moves to the next turn automatically.",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                if (CityUIManager.Instance != null) CityUIManager.Instance.ClosePanel();
                if (UnitUIManager.Instance != null) UnitUIManager.Instance.ClosePanel();
                CachePlayerWarriorsIfNeeded();
                if (warrior2 != null)
                    LockCameraToWorld(warrior2.transform.position);

                if (scriptedRoutine != null) StopCoroutine(scriptedRoutine);
                scriptedRoutine = StartCoroutine(ScriptSpawnEnemy2OnAITurn6());
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => false;
            },
            canAdvance = () => tm != null && tm.isPlayerTurn && tm.turnNumber == 7,
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Wait a moment — the next turn will start on its own."
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 7",
            body = "The enemy is already adjacent.\n\nAttack it without moving first.",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                CachePlayerWarriorsIfNeeded();
                if (enemy2 != null)
                {
                    HidePointer();
                    SetAllowedAttack(warrior2, enemy2);
                    TutorialGate.CanSelectUnit = u => u == warrior2;
                    if (UnitSelectionManager.Instance != null && warrior2 != null)
                        UnitSelectionManager.Instance.SelectUnit(warrior2);
                }
            },
            canAdvance = () => enemy2 == null,
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Select your Warrior, then tap the enemy next to it."
        });

        steps.Add(new TutorialStep
        {
            title = "Turn 7",
            body = "After you attack, the yellow ring disappears — that unit has used its action for this turn.",
            nextLabel = "Next",
            dynamicBody = () => "After you attack, the yellow ring disappears — that unit has used its action for this turn.",
            canAdvance = HasAnyPlayerUnitAttackedThisTurn
        });

        steps.Add(new TutorialStep
        {
            title = "Cutscene",
            body = "End your turn to continue the story...",
            nextLabel = "Next",
            autoAdvance = true,
            onEnter = () =>
            {
                RectTransform endTurnRect = FindButtonRectByLabelContains("End Turn");
                if (endTurnRect != null)
                {
                    PointAtUI(endTurnRect);
                }
                if (scriptedRoutine != null) StopCoroutine(scriptedRoutine);
                scriptedRoutine = StartCoroutine(ScriptBossCutsceneOnAITurn7());
                TutorialGate.CanSelectUnit = _ => false;
                TutorialGate.CanClickCity = _ => false;
                TutorialGate.CanRecruitWarrior = () => false;
                TutorialGate.CanEndTurn = () => true;
            },
            canAdvance = () => tm != null && tm.isPlayerTurn && tm.turnNumber == 8,
            hintAfterSeconds = defaultHintAfterSeconds,
            hintText = "Press End Turn."
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
                    tm.autoEndTurnWhenNoActions = prevAutoEndTurnWhenNoActions;
                }
            },
            dynamicBody = () =>
            {
                int gold = tm != null ? tm.playerGold : 0;
                return "Enemy units can also move and then attack.\n\nClan Chief Big Salami wants revenge.\n\nNow you can play freely against the AI. Recruit units, explore, and capture the enemy city (red) to win.\n\n(Current Gold: " + gold + ")";
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
        // While the City/Unit panel is open, hide the gameplay HUD buttons (Menu / End Turn / Next),
        // just like the normal game UI flow.
        bool shouldHide = HasCityPanelOpen() || HasUnitPanelOpen();
        EnsureGameplayHudButtonsCached();

        bool desiredActive = !shouldHide;
        if (gameplayHudMenuButton != null && gameplayHudMenuButton.gameObject.activeSelf != desiredActive)
            gameplayHudMenuButton.gameObject.SetActive(desiredActive);
        if (gameplayHudEndTurnOrNextButton != null && gameplayHudEndTurnOrNextButton.gameObject.activeSelf != desiredActive)
            gameplayHudEndTurnOrNextButton.gameObject.SetActive(desiredActive);

        gameplayHudHidden = shouldHide;
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

        Transform tutorialRoot = root != null ? root.transform : null;
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

        enemy1 = SpawnTutorialUnit(spawnPos, isPlayerOwned: false, "TutorialEnemy1");
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

        enemy2 = SpawnTutorialUnit(spawnPos, isPlayerOwned: false, "TutorialEnemy2");

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
        if (EventSystem.current != null)
            return;

        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    private void BuildUI()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 450;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        root = new GameObject("Root", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);
        RectTransform rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        // Background (non-blocking).
        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(root.transform, false);
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        Image bgImg = bg.GetComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0f);
        bgImg.raycastTarget = false;

        // Panel (small, so world is still clickable).
        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.transform.SetParent(root.transform, false);
        panelRect = panel.GetComponent<RectTransform>();
        if (IsMobilePlatform())
        {
            panelRect.anchorMin = new Vector2(0.05f, 0.70f);
            panelRect.anchorMax = new Vector2(0.95f, 0.94f);
        }
        else
        {
            // Default: centered panel that stays clear of the top HUD (Turn/Gold) on desktop/web.
            panelRect.anchorMin = new Vector2(0.05f, 0.62f);
            panelRect.anchorMax = new Vector2(0.95f, 0.86f);
        }
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = new Vector2(0f, -Mathf.Max(0f, panelTopMargin));

        Image panelImg = panel.GetComponent<Image>();
        panelImg.color = new Color(0.08f, 0.10f, 0.14f, 0.92f);
        panelImg.raycastTarget = true;

        VerticalLayoutGroup v = panel.GetComponent<VerticalLayoutGroup>();
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.spacing = 10f;
        v.padding = new RectOffset(24, 24, 28, 18);

        ContentSizeFitter fitter = panel.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Header: title only (leaving the tutorial is done via the normal Menu button).
        titleText = CreateTMP(panel.transform, "Title", 54, FontStyles.Bold);

        bodyText = CreateTMP(panel.transform, "Body", 36, FontStyles.Normal);

        GameObject spacer = new GameObject("ButtonSpacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(panel.transform, false);
        LayoutElement spacerLe = spacer.GetComponent<LayoutElement>();
        spacerLe.minHeight = IsMobilePlatform() ? 18f : 12f;
        spacerLe.flexibleHeight = 0f;

        GameObject row = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(panel.transform, false);
        HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        h.spacing = 12f;
        h.childAlignment = TextAnchor.MiddleCenter;

        float nextHeight = IsMobilePlatform() ? 86f : 64f;
        float nextWidth = IsMobilePlatform() ? 520f : 360f;
        nextButton = CreateButton(row.transform, "Next", out nextLabel, minWidth: nextWidth, flexibleWidth: 0f, minHeight: nextHeight, fontSize: IsMobilePlatform() ? 40 : 30);
        nextLabel.text = "Next";
        nextButton.onClick.AddListener(OnNextClicked);
        nextButton.interactable = true;

        // Pointer layer (arrows/highlights) - never blocks clicks.
        GameObject pointer = new GameObject("Pointers", typeof(RectTransform));
        pointer.transform.SetParent(root.transform, false);
        pointerLayer = pointer.GetComponent<RectTransform>();
        pointerLayer.anchorMin = Vector2.zero;
        pointerLayer.anchorMax = Vector2.one;
        pointerLayer.offsetMin = Vector2.zero;
        pointerLayer.offsetMax = Vector2.zero;
        // Keep pointers behind the tutorial panel so arrows don't draw over the tutorial text/buttons.
        pointer.transform.SetAsFirstSibling();

        GameObject highlight = new GameObject("Highlight", typeof(RectTransform), typeof(Image), typeof(Outline));
        highlight.transform.SetParent(pointerLayer, false);
        pointerHighlightRect = highlight.GetComponent<RectTransform>();
        pointerHighlightRect.anchorMin = new Vector2(0.5f, 0.5f);
        pointerHighlightRect.anchorMax = new Vector2(0.5f, 0.5f);
        pointerHighlightRect.sizeDelta = new Vector2(120f, 60f);
        pointerHighlightImage = highlight.GetComponent<Image>();
        pointerHighlightImage.sprite = GetWhiteSprite();
        pointerHighlightImage.color = new Color(0.18f, 0.52f, 0.82f, 0.18f);
        pointerHighlightImage.raycastTarget = false;
        Outline hiOutline = highlight.GetComponent<Outline>();
        hiOutline.effectColor = new Color(0.18f, 0.52f, 0.82f, 0.85f);
        hiOutline.effectDistance = new Vector2(4f, -4f);

        GameObject arrowShaft = new GameObject("ArrowShaft", typeof(RectTransform), typeof(Image));
        arrowShaft.transform.SetParent(pointerLayer, false);
        pointerArrowShaftRect = arrowShaft.GetComponent<RectTransform>();
        pointerArrowShaftRect.anchorMin = new Vector2(0.5f, 0.5f);
        pointerArrowShaftRect.anchorMax = new Vector2(0.5f, 0.5f);
        pointerArrowShaftRect.sizeDelta = new Vector2(10f, 60f);
        pointerArrowShaftImage = arrowShaft.GetComponent<Image>();
        pointerArrowShaftImage.sprite = GetWhiteSprite();
        pointerArrowShaftImage.color = new Color(0.98f, 0.92f, 0.30f, 1f);
        pointerArrowShaftImage.raycastTarget = false;

        GameObject arrowHead = new GameObject("ArrowHead", typeof(RectTransform), typeof(Image));
        arrowHead.transform.SetParent(pointerLayer, false);
        pointerArrowHeadRect = arrowHead.GetComponent<RectTransform>();
        pointerArrowHeadRect.anchorMin = new Vector2(0.5f, 0.5f);
        pointerArrowHeadRect.anchorMax = new Vector2(0.5f, 0.5f);
        pointerArrowHeadRect.sizeDelta = new Vector2(38f, 28f);
        pointerArrowHeadImage = arrowHead.GetComponent<Image>();
        pointerArrowHeadImage.sprite = GetTriangleSpriteDown();
        pointerArrowHeadImage.color = new Color(0.98f, 0.92f, 0.30f, 1f);
        pointerArrowHeadImage.raycastTarget = false;

        CaptureDefaultPanelLayout();
        HidePointer();
    }

    private TextMeshProUGUI CreateTMP(Transform parent, string name, int fontSize, FontStyles style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        LayoutElement le = go.GetComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.flexibleHeight = 0f;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.raycastTarget = false;
        tmp.enableAutoSizing = false;
        tmp.textWrappingMode = TextWrappingModes.Normal;

        return tmp;
    }

    private Button CreateCompactButton(Transform parent, string name, out TextMeshProUGUI label, float minHeight, int fontSize)
    {
        GameObject go = new GameObject(name + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        go.transform.SetParent(parent, false);

        LayoutElement le = go.GetComponent<LayoutElement>();
        le.minHeight = minHeight;
        le.flexibleWidth = 0f;
        le.minWidth = 0f;
        le.preferredWidth = -1f;

        HorizontalLayoutGroup layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 0f;
        int padX = IsMobilePlatform() ? 22 : 16;
        int padY = IsMobilePlatform() ? 10 : 8;
        layout.padding = new RectOffset(padX, padX, padY, padY);

        ContentSizeFitter fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Image img = go.GetComponent<Image>();
        img.color = new Color(0.18f, 0.52f, 0.82f, 1f);

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;

        GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        textGO.transform.SetParent(go.transform, false);

        LayoutElement textLe = textGO.GetComponent<LayoutElement>();
        textLe.minWidth = 0f;
        textLe.flexibleWidth = 0f;

        label = textGO.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = fontSize;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.raycastTarget = false;
        label.enableAutoSizing = false;

        return btn;
    }

    private Button CreateButton(Transform parent, string name, out TextMeshProUGUI label, float minWidth = 0f, float flexibleWidth = 1f, float minHeight = -1f, int fontSize = -1)
    {
        GameObject go = new GameObject(name + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        LayoutElement le = go.GetComponent<LayoutElement>();
        le.minHeight = minHeight > 0f ? minHeight : (IsMobilePlatform() ? 86f : 64f);
        le.minWidth = Mathf.Max(0f, minWidth);
        le.flexibleWidth = flexibleWidth;

        Image img = go.GetComponent<Image>();
        img.color = new Color(0.18f, 0.52f, 0.82f, 1f);

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;

        GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(go.transform, false);
        RectTransform rt = textGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        label = textGO.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = fontSize > 0 ? fontSize : (IsMobilePlatform() ? 40 : 30);
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.raycastTarget = false;

        return btn;
    }
}
