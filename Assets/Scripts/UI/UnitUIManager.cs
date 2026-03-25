using UnityEngine;
using TMPro;

/// <summary>
/// Shows basic information about the currently selected unit.
/// </summary>
public class UnitUIManager : MonoBehaviour
{
    public static UnitUIManager Instance { get; private set; }

    [Header("UI")]
    public GameObject panelRoot;
    public TMP_Text unitNameText;
    public TMP_Text healthText;
    public TMP_Text attackText;
    public TMP_Text defenseText;

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

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
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

        if (panelRoot == null)
        {
            Debug.LogWarning("UnitUIManager panelRoot is not assigned.");
            return;
        }

        panelRoot.SetActive(true);

        if (unitNameText != null && unit != null)
        {
            // Strip Unity's "(Clone)" suffix so the UI shows a clean unit name.
            string rawName = unit.name;
            const string cloneSuffix = "(Clone)";
            if (rawName.EndsWith(cloneSuffix))
            {
                rawName = rawName.Substring(0, rawName.Length - cloneSuffix.Length).TrimEnd();
            }
            unitNameText.text = rawName;
        }

        if (healthText != null && unit != null)
        {
            healthText.text = $"HP: {unit.currentHealth}/{unit.maxHealth}";
        }

        if (attackText != null && unit != null)
        {
            attackText.text = $"ATK: {unit.attack}";
        }

        if (defenseText != null && unit != null)
        {
            defenseText.text = $"DEF: {unit.defense}";
        }
    }

    public void ClosePanel()
    {
        currentUnit = null;

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }

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
