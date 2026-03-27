using UnityEngine;

/// <summary>
/// Shows basic information about the currently selected unit.
/// </summary>
public class UnitUIManager : MonoBehaviour
{
    public static UnitUIManager Instance { get; private set; }

    private Unit currentUnit;
    private static bool hasLoggedMissingBottomStripController;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void ShowUnit(Unit unit)
    {
        currentUnit = unit;

        BottomStripController bottomStrip = GetBottomStripController();
        if (bottomStrip != null)
        {
            // Claim the bottom strip mode first so handoffs do not flash DefaultHud.
            bottomStrip.SetMode(BottomStripController.BottomStripMode.UnitUi);
        }

        if (CityUIManager.Instance != null)
        {
            CityUIManager.Instance.ClosePanel();
        }
    }

    public void ClosePanel()
    {
        currentUnit = null;

        BottomStripController bottomStrip = GetBottomStripController();
        if (bottomStrip != null)
        {
            bottomStrip.ReleaseMode(BottomStripController.BottomStripMode.UnitUi);
        }
    }

    public bool IsPanelOpen => currentUnit != null;
    public Unit CurrentUnit => currentUnit;

    private static BottomStripController GetBottomStripController()
    {
        BottomStripController bottomStrip = BottomStripController.Instance;
        if (bottomStrip == null)
        {
            bottomStrip = UnityEngine.Object.FindFirstObjectByType<BottomStripController>();
        }

        if (bottomStrip == null)
        {
            BottomStripController[] controllers = UnityEngine.Object.FindObjectsByType<BottomStripController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (controllers != null && controllers.Length > 0)
            {
                bottomStrip = controllers[0];
            }
        }

        if (bottomStrip == null && !hasLoggedMissingBottomStripController)
        {
            hasLoggedMissingBottomStripController = true;
            Debug.LogError("UnitUIManager requires BottomStripController in the gameplay scene.");
        }

        return bottomStrip;
    }
}
