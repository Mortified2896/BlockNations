using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class SafeAreaApplier : MonoBehaviour
{
    private RectTransform rectTransform;
    private Rect lastSafeArea = Rect.zero;
    private Vector2Int lastScreenSize = Vector2Int.zero;
    private ScreenOrientation lastOrientation = ScreenOrientation.AutoRotation;

    private void OnEnable()
    {
        rectTransform = GetComponent<RectTransform>();

        // Apply immediately and also right before UI renders
        Canvas.willRenderCanvases += ApplyIfNeeded;
        ApplySafeArea(force: true);
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= ApplyIfNeeded;
    }

    private void ApplyIfNeeded()
    {
        ApplySafeArea(force: false);
    }

    private void ApplySafeArea(bool force)
    {
        if (rectTransform == null)
            return;

        Rect currentSafeArea = Screen.safeArea;
        Vector2Int currentScreenSize = new Vector2Int(Screen.width, Screen.height);
        ScreenOrientation currentOrientation = Screen.orientation;

        if (currentScreenSize.x <= 0 || currentScreenSize.y <= 0)
            return;

        if (!force
            && currentSafeArea == lastSafeArea
            && currentScreenSize == lastScreenSize
            && currentOrientation == lastOrientation)
        {
            return;
        }

        lastSafeArea = currentSafeArea;
        lastScreenSize = currentScreenSize;
        lastOrientation = currentOrientation;

        // Convert safe area from pixels to normalized anchors
        Vector2 anchorMin = currentSafeArea.position;
        Vector2 anchorMax = currentSafeArea.position + currentSafeArea.size;

        anchorMin.x /= currentScreenSize.x;
        anchorMin.y /= currentScreenSize.y;
        anchorMax.x /= currentScreenSize.x;
        anchorMax.y /= currentScreenSize.y;

        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        // Force layout rebuild so layout groups update correctly
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);

    }

}
