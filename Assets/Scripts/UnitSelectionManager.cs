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

        if (turnManager == null)
        {
            turnManager = TurnManager.Instance;
        }
    }

    private void HighlightReachableTiles(Unit unit)
    {
        ClearReachableTiles();

        if (unit == null)
            return;

        TileHighlighter[] tiles = Object.FindObjectsByType<TileHighlighter>(FindObjectsSortMode.None);
        Vector3 from = unit.transform.position;
        bool canMove = unit.CanMoveThisTurn();
        bool canAttack = !unit.hasAttackedThisTurn;

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
                // Enemy unit on this tile that we can attack?
                Unit occupant = GridUtils.GetUnitAtPosition(to, unit);
                if (occupant != null && occupant.isPlayerOwned != unit.isPlayerOwned && canAttack)
                {
                    tile.SetAttackable(true);
                }
                else if (occupant == null && canMove)
                {
                    tile.SetReachable(true);
                }
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
                tile.SetAttackable(false);
            }
        }
    }

    private bool HasAttackableAdjacentTiles(Unit unit)
    {
        if (unit == null || unit.hasAttackedThisTurn)
        {
            return false;
        }

        TileHighlighter[] tiles = Object.FindObjectsByType<TileHighlighter>(FindObjectsSortMode.None);
        Vector3 from = unit.transform.position;

        foreach (TileHighlighter tile in tiles)
        {
            if (tile == null) continue;

            Vector3 to = tile.transform.position;
            Vector3 delta = to - from;
            delta.z = 0f;

            float dist = delta.magnitude;
            if (dist >= 0.5f * tileSize && dist <= 1.5f * tileSize)
            {
                Unit occupant = GridUtils.GetUnitAtPosition(to, unit);
                if (occupant != null && occupant.isPlayerOwned != unit.isPlayerOwned)
                {
                    return true;
                }
            }
        }

        return false;
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

        // Only select units that belong to the side whose turn it is
        if (turnManager != null && !turnManager.CanControlUnit(unit))
            return;

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
            if (!turnManager.CanControlUnit(selectedUnit))
            {
                Debug.Log("Cannot move units when it is not this side's turn or the game is over.");
                return;
            }
        }

        bool isActiveTurnForUnit = turnManager == null || turnManager.IsCurrentSideOwner(selectedUnit.isPlayerOwned);
        string sideLabel = turnManager != null ? turnManager.GetCurrentSideName() : "Player";

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

        Vector3 newPos = targetWorldPosition;
        newPos.z = selectedUnit.transform.position.z;

        Unit targetUnit = GridUtils.GetUnitAtPosition(newPos, selectedUnit);

        bool actionPerformed = false;

        bool canAttackTarget = targetUnit != null &&
                               targetUnit.isPlayerOwned != selectedUnit.isPlayerOwned &&
                               !selectedUnit.hasAttackedThisTurn;
        bool canMoveToEmpty = targetUnit == null && selectedUnit.CanMoveThisTurn();

        // If we can neither attack nor move, end selection.
        if (!canAttackTarget && !canMoveToEmpty)
        {
            ClearSelection();
            return;
        }

        // Determine what is on the target tile (ally/enemy/empty)
        if (targetUnit != null)
        {
            // Friendly unit: cannot move onto the same tile
            if (targetUnit.isPlayerOwned == selectedUnit.isPlayerOwned)
            {
                ClearSelection();
                return;
            }

            if (!canAttackTarget)
            {
                ClearSelection();
                return;
            }

            // Enemy unit: attack instead of moving onto the tile.
            // Allow this even after moving once this turn, but only once.
            selectedUnit.hasAttackedThisTurn = true;
            selectedUnit.RegisterMove();
            selectedUnit.UpdateMoveOutline(isActiveTurnForUnit);

            bool killed = selectedUnit.Attack(targetUnit);
            Debug.Log(sideLabel + " unit " + selectedUnit.name + " attacked " + targetUnit.name);
            actionPerformed = true;

            // If the defender died, move into their tile
            if (killed)
            {
                selectedUnit.transform.position = newPos;
                Debug.Log(sideLabel + " unit moved into defeated enemy tile at " + newPos);
            }
        }
        else if (canMoveToEmpty)
        {
            // Tile is empty: move normally
            selectedUnit.transform.position = newPos;
            selectedUnit.RegisterMove();
            selectedUnit.UpdateMoveOutline(isActiveTurnForUnit);
            Debug.Log(sideLabel + " unit moved to " + newPos);
            actionPerformed = true;
        }

        // Update fog visibility after movement/attack
        if (turnManager != null)
        {
            turnManager.RecalculatePlayerVisibility();

            if (turnManager.gameOver)
                return;

            City city = GridUtils.GetCityAtPosition(selectedUnit.transform.position);
            if (city != null && city.isPlayerOwned != selectedUnit.isPlayerOwned)
            {
                turnManager.OnCityCaptured(selectedUnit.isPlayerOwned);
                return;
            }
        }

        // If this unit has no moves left, deselect it. Otherwise, update reachable tiles.
        if (!selectedUnit.CanMoveThisTurn())
        {
            // Special case: if the unit cannot move anymore but still
            // has not attacked and has an enemy adjacent, keep it
            // selected and show red attack tiles as a reminder.
            if (!selectedUnit.hasAttackedThisTurn && HasAttackableAdjacentTiles(selectedUnit))
            {
                HighlightReachableTiles(selectedUnit);
            }
            else
            {
                ClearSelection();
            }
        }
        else
        {
            HighlightReachableTiles(selectedUnit);
        }

        if (actionPerformed && turnManager != null)
        {
            turnManager.AutoSaveIfEnabled();
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
    /// Called at the start of a side's turn to allow its units to move again.
    /// </summary>
    public void ResetMovementForSide(bool isPlayerOwnedSide, bool isActiveTurn)
    {
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            bool matchesSide = unit.isPlayerOwned == isPlayerOwnedSide;
            if (matchesSide)
            {
                unit.ResetMovementForTurn();
                unit.UpdateMoveOutline(isActiveTurn);
            }
            else
            {
                unit.UpdateMoveOutline(false);
            }
        }
    }

    /// <summary>
    /// Updates move outlines without resetting movement (used when toggling modes).
    /// </summary>
    public void RefreshMoveOutlinesForCurrentTurn()
    {
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            bool isActiveTurn = false;
            if (turnManager != null)
            {
                isActiveTurn = turnManager.IsCurrentSideOwner(unit.isPlayerOwned) && turnManager.IsHumanTurn();
            }
            else
            {
                isActiveTurn = unit.isPlayerOwned;
            }
            unit.UpdateMoveOutline(isActiveTurn);
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
