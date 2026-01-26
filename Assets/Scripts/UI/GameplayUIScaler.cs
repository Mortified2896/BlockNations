using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Applies lightweight, runtime UI scaling/offset tweaks for gameplay UI on desktop,
/// while keeping mobile portrait touch targets large.
/// </summary>
public class GameplayUIScaler : MonoBehaviour
{
    [Header("Bottom Panels (Unit/City UI)")]
    public bool autoScaleBottomPanels = true;
    [Tooltip("Disable all runtime adjustments to the bottom panels (ButtomPopUp).")]
    public bool skipBottomPanelsRuntimeAdjustments = true;
    [Tooltip("Name of the RectTransform root for the bottom panels (Unit/City UI).")]
    public string bottomPanelsRootName = "ButtomPopUp";
    [Range(0.25f, 1f)]
    [Tooltip("Width scale applied on desktop (and in-editor desktop testing).")]
    public float desktopBottomPanelsScaleX = 1.0f;
    [Range(0.25f, 1f)]
    [Tooltip("Height scale applied on desktop (and in-editor desktop testing).")]
    public float desktopBottomPanelsScaleY = 0.5f;
    [Range(0.25f, 1f)]
    [Tooltip("Optional scale applied on mobile landscape. Mobile portrait remains unscaled.")]
    public float mobileLandscapeBottomPanelsScale = 0.8f;
    [Tooltip("Bottom margin in pixels after scaling.")]
    public float bottomPanelsBottomMargin = 12f;

    [Header("Top HUD")]
    [Tooltip("Move the top HUD closer to the top on desktop (no notch/safe-area padding needed).")]
    public bool autoAdjustUpperHudOnDesktop = true;
    public string upperHudRootName = "Upper HUD";
    public float desktopUpperHudAnchoredY = -70f;

    private RectTransform bottomPanelsRect;
    private bool bottomPanelsOriginalCached;
    private Vector3 bottomPanelsOriginalScale;
    private Vector2 bottomPanelsOriginalAnchoredPosition;
    private Vector2 bottomPanelsOriginalSizeDelta;

    private RectTransform upperHudRect;
    private bool upperHudOriginalCached;
    private Vector2 upperHudOriginalAnchoredPosition;

    private int lastScreenW = -1;
    private int lastScreenH = -1;
    private Coroutine applyRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Object.FindFirstObjectByType<GameplayUIScaler>() != null)
            return;

        GameObject go = new GameObject("GameplayUIScaler");
        go.AddComponent<GameplayUIScaler>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Start()
    {
        lastScreenW = Screen.width;
        lastScreenH = Screen.height;
        QueueApply();
    }

    void Update()
    {
        if (Screen.width == lastScreenW && Screen.height == lastScreenH)
            return;

        lastScreenW = Screen.width;
        lastScreenH = Screen.height;
        ApplyForCurrentScreen();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ClearSceneCaches();
        QueueApply();
    }

    private void QueueApply()
    {
        if (applyRoutine != null)
            StopCoroutine(applyRoutine);

        applyRoutine = StartCoroutine(ApplyNextFrame());
    }

    private IEnumerator ApplyNextFrame()
    {
        yield return null;
        ApplyForCurrentScreen();
        applyRoutine = null;
    }

    private void ClearSceneCaches()
    {
        bottomPanelsRect = null;
        bottomPanelsOriginalCached = false;
        upperHudRect = null;
        upperHudOriginalCached = false;
    }

    private void TryResolveTargets()
    {
        if (bottomPanelsRect == null)
        {
            GameObject bottomRoot = GameObject.Find(bottomPanelsRootName);
            if (bottomRoot != null)
                bottomPanelsRect = bottomRoot.GetComponent<RectTransform>();
        }

        if (upperHudRect == null)
        {
            GameObject upperHudRoot = GameObject.Find(upperHudRootName);
            if (upperHudRoot != null)
                upperHudRect = upperHudRoot.GetComponent<RectTransform>();
        }
    }

    private void ApplyForCurrentScreen()
    {
        TryResolveTargets();

        bool isMobile = Application.isMobilePlatform;
        bool isLandscape = Screen.width > Screen.height;

        if (!skipBottomPanelsRuntimeAdjustments && autoScaleBottomPanels && bottomPanelsRect != null)
        {
            if (!bottomPanelsOriginalCached)
            {
                bottomPanelsOriginalScale = bottomPanelsRect.localScale;
                bottomPanelsOriginalAnchoredPosition = bottomPanelsRect.anchoredPosition;
                bottomPanelsOriginalSizeDelta = bottomPanelsRect.sizeDelta;
                bottomPanelsOriginalCached = true;
            }

            float targetScaleX;
            float targetScaleY;
            bool shouldScale;

            if (!isMobile)
            {
                shouldScale = true;
                targetScaleX = desktopBottomPanelsScaleX;
                targetScaleY = desktopBottomPanelsScaleY;
            }
            else
            {
                // Keep mobile portrait large; optionally scale on landscape.
                shouldScale = isLandscape;
                targetScaleX = mobileLandscapeBottomPanelsScale;
                targetScaleY = mobileLandscapeBottomPanelsScale;
            }

            if (shouldScale)
            {
                // Avoid stretching text by keeping uniform scale and adjusting panel width via sizeDelta.
                float uniformScale = Mathf.Max(0.0001f, targetScaleY);
                float widthMultiplier = targetScaleX / uniformScale;

                bottomPanelsRect.localScale = new Vector3(uniformScale, uniformScale, 1f);
                bottomPanelsRect.sizeDelta = new Vector2(bottomPanelsOriginalSizeDelta.x * widthMultiplier, bottomPanelsOriginalSizeDelta.y);

                float scaledHeight = bottomPanelsOriginalSizeDelta.y * uniformScale;
                float y = bottomPanelsBottomMargin + (scaledHeight * 0.5f);
                bottomPanelsRect.anchoredPosition = new Vector2(bottomPanelsRect.anchoredPosition.x, y);
            }
            else
            {
                bottomPanelsRect.localScale = bottomPanelsOriginalScale;
                bottomPanelsRect.sizeDelta = bottomPanelsOriginalSizeDelta;
                bottomPanelsRect.anchoredPosition = bottomPanelsOriginalAnchoredPosition;
            }
        }

        if (autoAdjustUpperHudOnDesktop && upperHudRect != null)
        {
            if (!upperHudOriginalCached)
            {
                upperHudOriginalAnchoredPosition = upperHudRect.anchoredPosition;
                upperHudOriginalCached = true;
            }

            if (!isMobile)
            {
                upperHudRect.anchoredPosition = new Vector2(upperHudRect.anchoredPosition.x, desktopUpperHudAnchoredY);
            }
            else
            {
                upperHudRect.anchoredPosition = upperHudOriginalAnchoredPosition;
            }
        }
    }
}
