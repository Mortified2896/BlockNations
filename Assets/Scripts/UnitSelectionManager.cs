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

    private void HighlightReachableTiles(Unit unit)
    {
        ClearReachableTiles();

        if (unit == null || !unit.CanMoveThisTurn())
            return;

        TileHighlighter[] tiles = Object.FindObjectsByType<TileHighlighter>(FindObjectsSortMode.None);
        Vector3 from = unit.transform.position;

        foreach (TileHighlighter tile in tiles)
        {
            if (tile == null) continue;

            Vector3 to = tile.transform.position;
            Vector3 delta = to - from;
            delta.z = 0f;

            float dist = delta.magnitude;
            // Adjacent tiles (including diagonals)
            if (dist >= 0.5f * tileSize && dist <= 1.5f * tileSize)
            {
                tile.SetReachable(true);
            }
        }
    }

    public Unit SelectedUnit => selectedUnit;

    private void ClearReachableTiles()
    {
        TileHighlighter[] tiles = Object.FindObjectsByType<TileHighlighter>(FindObjectsSortMode.None);
        foreach (TileHighlighter tile in tiles)
        {
            if (tile != null)
            {
                tile.SetReachable(false);
            }
        }
    }

    public void SelectUnit(Unit unit)
    {
        if (unit == null)
            return;

        // If this unit is already selected, clicking it again will deselect it
        if (unit == selectedUnit)
        {
            ClearSelection();
            return;
        }

        // Only select player units during the player's turn (and not after game over)
        if (turnManager != null)
        {
            if (turnManager.gameOver || !turnManager.isPlayerTurn || !unit.isPlayerOwned)
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

        HighlightReachableTiles(unit);
    }

    public void ClearSelection()
    {
        selectedUnit = null;
        ClearReachableTiles();

        if (UnitUIManager.Instance != null)
        {
            UnitUIManager.Instance.ClosePanel();
        }
    }

    /// <summary>
    /// Core logic for moving or attacking toward a target world position.
    /// Used by both tile-clicks and direct enemy-clicks.
    /// </summary>
    public void TryMoveOrAttackAtPosition(Vector3 targetWorldPosition)
    {
        if (selectedUnit == null)
            return;

        if (turnManager != null)
        {
            if (turnManager.gameOver || !turnManager.isPlayerTurn)
            {
                Debug.Log("Cannot move units when it is not the player's turn or the game is over.");
                return;
            }
        }

        if (!selectedUnit.isPlayerOwned)
        {
            Debug.Log("Cannot move AI units during the player turn.");
            return;
        }

        if (!selectedUnit.CanMoveThisTurn())
        {
            ClearSelection();
            return;
        }

        Vector3 from = selectedUnit.transform.position;
        Vector3 to = targetWorldPosition;
        Vector3 delta = to - from;
        delta.z = 0f;

        // Allow any adjacent tile (including diagonals) as "one move"
        float dist = delta.magnitude;
        if (dist < 0.5f * tileSize || dist > 1.5f * tileSize)
        {
            ClearSelection();
            return;
        }

        // If the unit was stationed in a city, clear that link when it moves away
        if (selectedUnit.currentCity != null)
        {
            selectedUnit.currentCity.stationedUnit = null;
            selectedUnit.currentCity = null;
        }

        // Determine what is on the target tile (ally/enemy/empty)
        Vector3 newPos = targetWorldPosition;
        newPos.z = selectedUnit.transform.position.z;

        Unit targetUnit = GridUtils.GetUnitAtPosition(newPos, selectedUnit);

        if (targetUnit != null)
        {
            // Friendly unit: cannot move onto the same tile
            if (targetUnit.isPlayerOwned == selectedUnit.isPlayerOwned)
            {
                ClearSelection();
                return;
            }

            // Enemy unit: attack instead of moving onto the tile
            selectedUnit.RegisterMove();
            selectedUnit.UpdateMoveOutline(true);

            bool killed = selectedUnit.Attack(targetUnit);
            Debug.Log("Player unit " + selectedUnit.name + " attacked " + targetUnit.name);

            // If the defender died, move into their tile
            if (killed)
            {
                selectedUnit.transform.position = newPos;
                Debug.Log("Player unit moved into defeated enemy tile at " + newPos);
            }
        }
        else
        {
            // Tile is empty: move normally
            selectedUnit.transform.position = newPos;
            selectedUnit.RegisterMove();
            selectedUnit.UpdateMoveOutline(true);
            Debug.Log("Moved unit to " + newPos);
        }

        // Update fog visibility after movement/attack
        if (turnManager != null)
        {
            turnManager.RecalculatePlayerVisibility();

            if (turnManager.gameOver)
                return;

            City city = GridUtils.GetCityAtPosition(selectedUnit.transform.position);
            if (city != null && !city.isPlayerOwned && selectedUnit.isPlayerOwned)
            {
                turnManager.OnCityCaptured(true);
                return;
            }
        }

        // If this unit has no moves left, deselect it. Otherwise, update reachable tiles.
        if (!selectedUnit.CanMoveThisTurn())
        {
            ClearSelection();
        }
        else
        {
            HighlightReachableTiles(selectedUnit);
        }
    }

    /// <summary>
    /// Called when the player clicks on a tile in the world.
    /// </summary>
    public void OnTileClicked(Transform tileTransform)
    {
        if (tileTransform == null)
            return;

        TryMoveOrAttackAtPosition(tileTransform.position);
    }

    /// <summary>
    /// Called at the start of the player's turn to allow units to move again.
    /// </summary>
    public void ResetMovementForPlayerUnits()
    {
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            if (unit.isPlayerOwned)
            {
                unit.ResetMovementForTurn();
                unit.UpdateMoveOutline(true);
            }
            else
            {
                unit.UpdateMoveOutline(false);
            }
        }
    }

    public void HideAllMoveOutlines()
    {
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            unit.UpdateMoveOutline(false);
        }
    }
}
