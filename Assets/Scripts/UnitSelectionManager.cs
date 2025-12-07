using UnityEngine;

/// <summary>
/// Manages which unit is selected and handles simple one-tile movement per turn.
/// </summary>
public class UnitSelectionManager : MonoBehaviour
{
    public static UnitSelectionManager Instance { get; private set; }

    [Header("References")]
    public TurnManager turnManager;

    [Header("Grid")]
    public float tileSize = 1f;

    private Unit selectedUnit;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void SelectUnit(Unit unit)
    {
        if (unit == null)
            return;

        // Only select player units during the player's turn
        if (turnManager != null)
        {
            if (!turnManager.isPlayerTurn || !unit.isPlayerOwned)
            {
                return;
            }
        }

        selectedUnit = unit;
        Debug.Log("Selected unit: " + unit.name);

        if (UnitUIManager.Instance != null)
        {
            UnitUIManager.Instance.ShowUnit(unit);
        }
    }

    public void ClearSelection()
    {
        selectedUnit = null;

        if (UnitUIManager.Instance != null)
        {
            UnitUIManager.Instance.ClosePanel();
        }
    }

    /// <summary>
    /// Called when the player clicks on a tile in the world.
    /// </summary>
    public void OnTileClicked(Transform tileTransform)
    {
        if (selectedUnit == null || tileTransform == null)
            return;

        if (turnManager != null)
        {
            if (!turnManager.isPlayerTurn)
            {
                Debug.Log("Cannot move units when it is not the player's turn.");
                return;
            }
        }

        if (!selectedUnit.isPlayerOwned)
        {
            Debug.Log("Cannot move AI units during the player turn.");
            return;
        }

        if (selectedUnit.hasMovedThisTurn)
        {
            Debug.Log("This unit has already moved this turn.");
            return;
        }

        Vector3 from = selectedUnit.transform.position;
        Vector3 to = tileTransform.position;
        Vector3 delta = to - from;
        delta.z = 0f;

        // Allow any adjacent tile (including diagonals) as "one move"
        float dist = delta.magnitude;
        if (dist < 0.5f * tileSize || dist > 1.5f * tileSize)
        {
            Debug.Log("Can only move to an adjacent tile (one tile away).");
            return;
        }

        // If the unit was stationed in a city, clear that link when it moves away
        if (selectedUnit.currentCity != null)
        {
            selectedUnit.currentCity.stationedUnit = null;
            selectedUnit.currentCity = null;
        }

        // Snap to the tile position
        Vector3 newPos = tileTransform.position;
        newPos.z = selectedUnit.transform.position.z;
        selectedUnit.transform.position = newPos;
        selectedUnit.hasMovedThisTurn = true;

        Debug.Log("Moved unit to " + newPos);
    }

    /// <summary>
    /// Called at the start of the player's turn to allow units to move again.
    /// </summary>
    public void ResetMovementForPlayerUnits()
    {
        Unit[] units = FindObjectsOfType<Unit>();
        foreach (Unit unit in units)
        {
            if (unit.isPlayerOwned)
            {
                unit.hasMovedThisTurn = false;
            }
        }
    }
}
