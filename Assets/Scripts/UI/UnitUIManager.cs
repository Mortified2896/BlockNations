using UnityEngine;

/// <summary>
/// Shows basic information about the currently selected unit.
/// </summary>
public class UnitUIManager : MonoBehaviour
{
    public static UnitUIManager Instance { get; private set; }

    private Unit currentUnit;

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

        if (CityUIManager.Instance != null)
        {
            CityUIManager.Instance.ClosePanel();
        }
    }

    public void ClosePanel()
    {
        currentUnit = null;
    }

    public bool IsPanelOpen => currentUnit != null;
    public Unit CurrentUnit => currentUnit;
}
