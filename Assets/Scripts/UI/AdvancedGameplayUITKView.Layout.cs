using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class AdvancedGameplayUITKView
{
    private Vector2Int lastScreen;
    private bool refitBoard = true;
    private bool lastCityOpen, lastUnitOpen;
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern float AdvancedCanvasPixelRatio();
#endif

    private void Layout()
    {
        var screen = new Vector2Int(Screen.width, Screen.height);
        float pixelRatio = 1f;
#if UNITY_WEBGL && !UNITY_EDITOR
        pixelRatio = Mathf.Max(1f, AdvancedCanvasPixelRatio());
#else
        if (Application.isMobilePlatform && Screen.dpi > 160) pixelRatio = Screen.dpi / 160f;
#endif
        float width = screen.x / pixelRatio, height = screen.y / pixelRatio;
        bool portrait = height > width;
        runtimePanel.scale = pixelRatio * Mathf.Clamp(portrait ? width / 420f : height / 900f, 0.8f, 1.25f);
        hud.EnableInClassList("advanced-compact", width < 600);
        hud.EnableInClassList("advanced-short", height < 650);
        Rect safe = Screen.safeArea;
        float scale = runtimePanel.scale;
        economyBar.style.top = (screen.y - safe.yMax) / scale;
        bottom.style.bottom = safe.yMin / scale;
        hud.style.left = safe.xMin / scale; hud.style.right = (screen.x - safe.xMax) / scale;
        bool cityOpen = citySheet.resolvedStyle.display != DisplayStyle.None;
        bool unitOpen = unitSheet.resolvedStyle.display != DisplayStyle.None;
        if (screen != lastScreen || cityOpen != lastCityOpen || unitOpen != lastUnitOpen) refitBoard = true;
        lastScreen = screen; lastCityOpen = cityOpen; lastUnitOpen = unitOpen;
        if (refitBoard && root.panel != null && economyBar.worldBound.height > 0 && bottom.worldBound.height > 0)
        {
            refitBoard = !TryFitBoard();
        }
        // Layout settles one frame after a sheet/tab change. Keep a board
        // allowance large enough for any city tab, so it does not jump while
        // the player compares recruits, buildings and population.
    }

    private bool TryFitBoard()
    {
        Rect bounds = root.worldBound;
        // A UIDocument can attach before its first geometry pass, including
        // after a scale/orientation change. Never project an unsettled rect
        // into the world camera; retry once layout has finite dimensions.
        if (!ValidRect(bounds) || !ValidRect(hud.worldBound) || !ValidRect(economyBar.worldBound) ||
            !ValidRect(bottom.worldBound) || Screen.height <= 0 || Screen.width <= 0 || boardCamera.aspect <= 0) return false;
        float gap = 12f;
        float left = hud.worldBound.xMin + gap, right = hud.worldBound.xMax - gap;
        float top = economyBar.worldBound.yMax + gap;
        float bottomY = bottom.worldBound.yMax - Required<VisualElement>("AdvancedToolbar").worldBound.height - gap;
        if (lastCityOpen) bottomY -= hud.ClassListContains("advanced-short") ? 308 : 336;
        else if (lastUnitOpen) bottomY -= 154;
        else bottomY -= 42;
        float availableWidth = Mathf.Max(80, right - left), availableHeight = Mathf.Max(80, bottomY - top);
        float unitsPerPixel = bounds.height / Screen.height;
        float span = (controller.Manager.gridManager.width + 0.35f) * controller.Manager.gridManager.tileSize;
        float ortho = Mathf.Max(span * Screen.height / (2 * availableHeight / unitsPerPixel),
            span * Screen.width / (2 * availableWidth / unitsPerPixel * boardCamera.aspect));
        float cx = (left + right) / 2 - bounds.xMin, cy = (top + bottomY) / 2 - bounds.yMin;
        Vector3 position = new Vector3(
            -(cx / bounds.width - 0.5f) * 2 * ortho * boardCamera.aspect,
            (cy / bounds.height - 0.5f) * 2 * ortho, -10);
        if (!Finite(ortho) || !Finite(position.x) || !Finite(position.y)) return false;
        boardCamera.orthographicSize = ortho;
        boardCamera.transform.position = position;
        return true;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool ValidRect(Rect rect) => rect.width > 0 && rect.height > 0 &&
        Finite(rect.x) && Finite(rect.y) && Finite(rect.width) && Finite(rect.height);
}
