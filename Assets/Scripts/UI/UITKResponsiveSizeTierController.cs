using UnityEngine;
using UnityEngine.UIElements;

internal sealed class UITKResponsiveSizeTierController
{
    private const string SharedStyleSheetResourceName = "UITKResponsiveShared";
    private const string ResponsiveRootClass = "ui-responsive-root";
    private const string CompactClass = "ui-compact";
    private const string RegularClass = "ui-regular";
    private const string LargeClass = "ui-large";
    private const float CompactShortestSideMax = 700f;
    private const float CompactHeightMax = 1180f;
    private const float LargeShortestSideMin = 900f;
    private const float LargeHeightMin = 1700f;
    private const float ReferenceResponsiveDpi = 160f;
    private const float MinimumReliableDpi = 72f;

    private StyleSheet sharedStyleSheet;
    private Rect lastSafeArea = Rect.zero;
    private Vector2Int lastScreenSize = Vector2Int.zero;
    private Vector2 lastResponsiveSize = Vector2.zero;
    private string currentTierClass;
    private VisualElement lastRoot;
    private VisualElement styleSheetAttachedRoot;

    public string CurrentTierClass => currentTierClass ?? string.Empty;
    public Vector2 LastResponsiveSize => lastResponsiveSize;

    public bool IsSharedStyleSheetAttached(VisualElement root)
    {
        return root != null && styleSheetAttachedRoot == root;
    }

    public void Apply(VisualElement root)
    {
        if (root == null)
        {
            return;
        }

        EnsureSharedStyleSheet(root);
        root.EnableInClassList(ResponsiveRootClass, true);
        bool rootChanged = root != lastRoot;
        lastRoot = root;

        Rect safeArea = Screen.safeArea;
        Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
        if (screenSize.x <= 0 || screenSize.y <= 0)
        {
            return;
        }

        if (safeArea.width <= 0f || safeArea.height <= 0f)
        {
            safeArea = new Rect(0f, 0f, screenSize.x, screenSize.y);
        }

        if (!rootChanged && safeArea == lastSafeArea && screenSize == lastScreenSize)
        {
            return;
        }

        lastSafeArea = safeArea;
        lastScreenSize = screenSize;

        Vector2 responsiveSize = NormalizeForResponsiveSizing(safeArea.size);
        lastResponsiveSize = responsiveSize;

        string nextTierClass = ResolveTierClass(responsiveSize);
        if (!rootChanged && currentTierClass == nextTierClass)
        {
            return;
        }

        root.EnableInClassList(CompactClass, nextTierClass == CompactClass);
        root.EnableInClassList(RegularClass, nextTierClass == RegularClass);
        root.EnableInClassList(LargeClass, nextTierClass == LargeClass);
        currentTierClass = nextTierClass;
    }

    public void Reset(VisualElement root)
    {
        if (root != null)
        {
            root.EnableInClassList(ResponsiveRootClass, false);
            root.EnableInClassList(CompactClass, false);
            root.EnableInClassList(RegularClass, false);
            root.EnableInClassList(LargeClass, false);
        }

        lastSafeArea = Rect.zero;
        lastScreenSize = Vector2Int.zero;
        lastResponsiveSize = Vector2.zero;
        currentTierClass = null;
        lastRoot = null;
        styleSheetAttachedRoot = null;
    }

    private void EnsureSharedStyleSheet(VisualElement root)
    {
        if (sharedStyleSheet == null)
        {
            sharedStyleSheet = Resources.Load<StyleSheet>(SharedStyleSheetResourceName);
        }

        if (sharedStyleSheet == null)
        {
            return;
        }

        if (styleSheetAttachedRoot == root)
        {
            return;
        }

        root.styleSheets.Add(sharedStyleSheet);
        styleSheetAttachedRoot = root;
    }

    private static Vector2 NormalizeForResponsiveSizing(Vector2 safeAreaSize)
    {
        float dpi = Screen.dpi;
        if (dpi < MinimumReliableDpi)
        {
            return safeAreaSize;
        }

        float densityScale = dpi / ReferenceResponsiveDpi;
        if (densityScale <= 0f)
        {
            return safeAreaSize;
        }

        return safeAreaSize / densityScale;
    }

    private static string ResolveTierClass(Vector2 responsiveSize)
    {
        float shortestSide = Mathf.Min(responsiveSize.x, responsiveSize.y);
        float usableHeight = responsiveSize.y;

        if (shortestSide <= CompactShortestSideMax || usableHeight <= CompactHeightMax)
        {
            return CompactClass;
        }

        if (shortestSide >= LargeShortestSideMin && usableHeight >= LargeHeightMin)
        {
            return LargeClass;
        }

        return RegularClass;
    }
}
