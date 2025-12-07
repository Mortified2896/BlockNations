using UnityEngine;

/// <summary>
/// Handles clicks on a unit and notifies the UnitSelectionManager.
/// Requires a Collider2D on the same GameObject.
/// </summary>
public class UnitClickHandler : MonoBehaviour
{
    private Unit unit;

    void Awake()
    {
        unit = GetComponent<Unit>();
        if (unit == null)
        {
            Debug.LogWarning("UnitClickHandler requires a Unit component on the same GameObject.", this);
        }
    }

    void OnMouseDown()
    {
        if (unit == null) return;

        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.SelectUnit(unit);
        }
    }
}

