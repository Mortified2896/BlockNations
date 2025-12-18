using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class SafeAreaApplier : MonoBehaviour
{
    [Tooltip("Enable to log the applied safe area once for verification.")]
    public bool debugLogs = false;

    private RectTransform rectTransform;
    private Rect lastSafeArea = Rect.zero;
    private Vector2Int lastScreenSize = Vector2Int.zero;
    private bool hasLogged;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        ApplySafeArea(force: true);
    }

    private void OnEnable()
    {
        ApplySafeArea(force: true);
    }

    private void Update()
    {
        ApplySafeArea();
    }

    private void ApplySafeArea(bool force = false)
    {
        Rect currentSafeArea = Screen.safeArea;
        Vector2Int currentScreenSize = new Vector2Int(Screen.width, Screen.height);

        if (!force && currentSafeArea == lastSafeArea && currentScreenSize == lastScreenSize)
        {
            return;
        }

        lastSafeArea = currentSafeArea;
        lastScreenSize = currentScreenSize;

        if (currentScreenSize.x <= 0 || currentScreenSize.y <= 0)
        {
            return;
        }

        Vector2 anchorMin = new Vector2(
            currentSafeArea.xMin / currentScreenSize.x,
            currentSafeArea.yMin / currentScreenSize.y);
        Vector2 anchorMax = new Vector2(
            currentSafeArea.xMax / currentScreenSize.x,
            currentSafeArea.yMax / currentScreenSize.y);

        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        if (debugLogs && !hasLogged)
        {
            Debug.Log($"SafeAreaApplier applied safe area: {currentSafeArea}", this);
            hasLogged = true;
        }
    }
}
