using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// Manages which unit is selected and handles tile movement and adjacent attacks.
/// </summary>
public class UnitSelectionManager : MonoBehaviour
{
    public static UnitSelectionManager Instance { get; private set; }

    [Header("References")]
    public TurnManager turnManager;

    private Unit selectedUnit;

    private enum PlannedMoveStatus
    {
        Invalid,
        ReachedTarget,
        HiddenBlockerStop
    }

    private struct PlannedMoveResult
    {
        public PlannedMoveStatus status;
        public Vector3 finalWorldPosition;
        public int attemptedSteps;
        public int actualStepsMoved;
    }

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

    private float GetGridTileSize()
    {
        GridManager grid = null;
        if (turnManager != null)
        {
            grid = turnManager.gridManager;
        }
        else if (TurnManager.Instance != null)
        {
            grid = TurnManager.Instance.gridManager;
        }

        if (grid != null)
        {
            return Mathf.Max(0.01f, grid.tileSize);
        }

        return 1f;
    }

    private void HighlightReachableTiles(Unit unit)
    {
        ClearReachableTiles();

        if (unit == null)
            return;

        TileHighlighter[] tiles = Object.FindObjectsByType<TileHighlighter>(FindObjectsSortMode.None);
        if (!TryGetUnitOriginTile(unit, out TileVisibility originTile))
        {
            return;
        }

        bool canMove = unit.CanMoveThisTurn();
        bool canAttack = unit.CanAttackThisTurn();
        int remainingMoves = GetRemainingMoveCount(unit);

        foreach (TileHighlighter tile in tiles)
        {
            if (tile == null) continue;

            TileVisibility targetTile = tile.GetComponent<TileVisibility>();
            if (targetTile == null)
            {
                continue;
            }

            int stepDistance = GetChebyshevDistance(originTile, targetTile);
            if (stepDistance != 1)
            {
                if (canMove && stepDistance >= 1 && stepDistance <= remainingMoves)
                {
                    PlannedMoveResult plannedMove = PlanMove(unit, targetTile.transform.position);
                    if (plannedMove.status != PlannedMoveStatus.Invalid)
                    {
                        tile.SetReachable(true);
                    }
                }

                continue;
            }

            Unit occupant = GridUtils.GetUnitAtPosition(targetTile.transform.position, unit);
            if (occupant != null && occupant.isPlayerOwned != unit.isPlayerOwned && canAttack)
            {
                tile.SetAttackable(true);
            }
            else if (canMove)
            {
                PlannedMoveResult plannedMove = PlanMove(unit, targetTile.transform.position);
                if (plannedMove.status != PlannedMoveStatus.Invalid)
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
        if (unit == null || !unit.CanAttackThisTurn())
        {
            return false;
        }

        TileHighlighter[] tiles = Object.FindObjectsByType<TileHighlighter>(FindObjectsSortMode.None);
        if (!TryGetUnitOriginTile(unit, out TileVisibility originTile))
        {
            return false;
        }

        foreach (TileHighlighter tile in tiles)
        {
            if (tile == null) continue;

            TileVisibility targetTile = tile.GetComponent<TileVisibility>();
            if (targetTile == null)
            {
                continue;
            }

            if (GetChebyshevDistance(originTile, targetTile) != 1)
            {
                continue;
            }

            Unit occupant = GridUtils.GetUnitAtPosition(targetTile.transform.position, unit);
            if (occupant != null && occupant.isPlayerOwned != unit.isPlayerOwned)
            {
                return true;
            }
        }

        return false;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static bool IsPointerOverUiForDebug()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private void LogSelectionBlockedForDebug(Unit unit, string reason)
    {
        if (!PbpDebugSettingsLoader.EnableInputLogs)
            return;

        if (turnManager != null)
        {
            turnManager.LogPbpSelectionGateIfNeeded("hit_unit_but_blocked", IsPointerOverUiForDebug(), unit, reason);
        }
    }

    private void LogSelectedNoRadiusIfNeeded(Unit unit)
    {
        if (!PbpDebugSettingsLoader.EnableInputLogs)
            return;

        if (unit == null || turnManager == null)
            return;

        CountPotentialHighlightsForDebug(unit, out int reachableCount, out int attackableCount);
        if (reachableCount > 0 || attackableCount > 0)
            return;

        string reason;
        if (!unit.CanMoveThisTurn() && !unit.CanAttackThisTurn())
        {
            reason = "no_moves_already_attacked";
        }
        else if (!unit.CanMoveThisTurn())
        {
            reason = "no_moves_remaining";
        }
        else if (!unit.CanAttackThisTurn())
        {
            reason = "already_attacked_no_targets";
        }
        else
        {
            reason = "no_adjacent_targets";
        }

        turnManager.LogPbpSelectionGateIfNeeded("selected_no_radius", IsPointerOverUiForDebug(), unit, reason);
    }

    private void CountPotentialHighlightsForDebug(Unit unit, out int reachableCount, out int attackableCount)
    {
        reachableCount = 0;
        attackableCount = 0;

        if (unit == null)
            return;

        TileHighlighter[] tiles = Object.FindObjectsByType<TileHighlighter>(FindObjectsSortMode.None);
        if (!TryGetUnitOriginTile(unit, out TileVisibility originTile))
            return;

        bool canMove = unit.CanMoveThisTurn();
        bool canAttack = unit.CanAttackThisTurn();
        int remainingMoves = GetRemainingMoveCount(unit);

        foreach (TileHighlighter tile in tiles)
        {
            if (tile == null) continue;

            TileVisibility targetTile = tile.GetComponent<TileVisibility>();
            if (targetTile == null)
                continue;

            int stepDistance = GetChebyshevDistance(originTile, targetTile);
            if (stepDistance > remainingMoves || stepDistance <= 0)
                continue;

            Unit occupant = GridUtils.GetUnitAtPosition(targetTile.transform.position, unit);
            if (occupant != null && occupant.isPlayerOwned != unit.isPlayerOwned && canAttack)
            {
                if (stepDistance == 1)
                {
                    attackableCount++;
                }
            }
            else if (canMove)
            {
                PlannedMoveResult plannedMove = PlanMove(unit, targetTile.transform.position);
                if (plannedMove.status != PlannedMoveStatus.Invalid)
                {
                    reachableCount++;
                }
            }
        }
    }
#endif

    private bool TryGetUnitOriginTile(Unit unit, out TileVisibility originTile)
    {
        originTile = null;
        GridManager grid = turnManager != null ? turnManager.gridManager : null;
        return grid != null && unit != null && grid.TryGetTileAtWorldPosition(unit.transform.position, out originTile);
    }

    private int GetRemainingMoveCount(Unit unit)
    {
        return unit != null ? Mathf.Max(0, unit.maxMovesPerTurn - unit.movesUsedThisTurn) : 0;
    }

    private static int GetChebyshevDistance(TileVisibility from, TileVisibility to)
    {
        if (from == null || to == null)
        {
            return int.MaxValue;
        }

        return Mathf.Max(Mathf.Abs(to.gridX - from.gridX), Mathf.Abs(to.gridY - from.gridY));
    }

    private PlannedMoveResult PlanMove(Unit unit, Vector3 targetWorldPosition)
    {
        PlannedMoveResult invalidResult = new PlannedMoveResult
        {
            status = PlannedMoveStatus.Invalid,
            finalWorldPosition = unit != null ? unit.transform.position : targetWorldPosition,
            attemptedSteps = 0,
            actualStepsMoved = 0
        };

        if (unit == null || !unit.CanMoveThisTurn())
        {
            return invalidResult;
        }

        GridManager grid = turnManager != null ? turnManager.gridManager : null;
        if (grid == null ||
            !grid.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility originTile) ||
            !grid.TryGetTileAtWorldPosition(targetWorldPosition, out TileVisibility targetTile))
        {
            return invalidResult;
        }

        int dx = targetTile.gridX - originTile.gridX;
        int dy = targetTile.gridY - originTile.gridY;
        int attemptedSteps = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
        int remainingMoves = GetRemainingMoveCount(unit);
        if (attemptedSteps <= 0 || attemptedSteps > remainingMoves)
        {
            return invalidResult;
        }

        int currentX = originTile.gridX;
        int currentY = originTile.gridY;
        Vector3 currentWorldPosition = unit.transform.position;
        currentWorldPosition.z = unit.transform.position.z;
        int actualStepsMoved = 0;

        for (int stepIndex = 0; stepIndex < attemptedSteps; stepIndex++)
        {
            currentX += dx == 0 ? 0 : (dx > 0 ? 1 : -1);
            currentY += dy == 0 ? 0 : (dy > 0 ? 1 : -1);

            if (!grid.TryGetTile(currentX, currentY, out TileVisibility pathTile) || pathTile == null)
            {
                return invalidResult;
            }

            Vector3 stepWorldPosition = pathTile.transform.position;
            stepWorldPosition.z = unit.transform.position.z;
            Unit occupant = GridUtils.GetUnitAtPosition(stepWorldPosition, unit);
            if (occupant != null)
            {
                if (pathTile.isVisibleNow)
                {
                    return invalidResult;
                }

                return new PlannedMoveResult
                {
                    status = PlannedMoveStatus.HiddenBlockerStop,
                    finalWorldPosition = currentWorldPosition,
                    attemptedSteps = attemptedSteps,
                    actualStepsMoved = actualStepsMoved
                };
            }

            currentWorldPosition = stepWorldPosition;
            actualStepsMoved++;
        }

        return new PlannedMoveResult
        {
            status = PlannedMoveStatus.ReachedTarget,
            finalWorldPosition = currentWorldPosition,
            attemptedSteps = attemptedSteps,
            actualStepsMoved = actualStepsMoved
        };
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
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogSelectionBlockedForDebug(unit, "cannot_control_unit");
#endif
            return;
        }

        selectedUnit = unit;
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayUnitSelect();
        }

        if (UnitUIManager.Instance != null)
        {
            UnitUIManager.Instance.ShowUnit(unit);
        }

        HighlightReachableTiles(unit);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        LogSelectedNoRadiusIfNeeded(unit);
#endif
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
                return;
            }
        }

        bool isActiveTurnForUnit = turnManager == null || turnManager.IsCurrentSideOwner(selectedUnit.isPlayerOwned);

        Vector3 from = selectedUnit.transform.position;
        GridManager grid = turnManager != null ? turnManager.gridManager : null;
        if (grid == null ||
            !grid.TryGetTileAtWorldPosition(from, out TileVisibility originTile) ||
            !grid.TryGetTileAtWorldPosition(targetWorldPosition, out TileVisibility targetTile))
        {
            ClearSelection();
            return;
        }

        int stepDistance = GetChebyshevDistance(originTile, targetTile);
        if (stepDistance <= 0)
        {
            ClearSelection();
            return;
        }

        // If the unit was stationed in a city, clear that link when it moves away
        Vector3 newPos = targetWorldPosition;
        newPos.z = selectedUnit.transform.position.z;

        Unit targetUnit = GridUtils.GetUnitAtPosition(newPos, selectedUnit);

        bool actionPerformed = false;

        bool canAttackTarget = targetUnit != null &&
                               targetUnit.isPlayerOwned != selectedUnit.isPlayerOwned &&
                               stepDistance == 1 &&
                               selectedUnit.CanAttackThisTurn();
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
            selectedUnit.RegisterAttack();
            selectedUnit.RegisterMove();
            selectedUnit.UpdateMoveOutline(isActiveTurnForUnit);

            bool killed = selectedUnit.Attack(targetUnit);
            actionPerformed = true;

            // If the defender died, move into their tile
            if (killed)
            {
                selectedUnit.transform.position = newPos;
                if (SoundManager.Instance != null)
                {
                    SoundManager.Instance.PlayMove();
                }

            }
        }
        else if (canMoveToEmpty)
        {
            PlannedMoveResult plannedMove = PlanMove(selectedUnit, newPos);
            if (plannedMove.status == PlannedMoveStatus.Invalid)
            {
                ClearSelection();
                return;
            }

            if (selectedUnit.currentCity != null)
            {
                selectedUnit.currentCity.stationedUnit = null;
                selectedUnit.currentCity = null;
            }

            if (plannedMove.actualStepsMoved > 0)
            {
                selectedUnit.transform.position = plannedMove.finalWorldPosition;
                if (SoundManager.Instance != null)
                {
                    SoundManager.Instance.PlayMove();
                }
            }

            selectedUnit.RegisterMove(plannedMove.attemptedSteps);
            if (plannedMove.status == PlannedMoveStatus.HiddenBlockerStop)
            {
                selectedUnit.ConsumeRemainingAttacksForTurn();
            }

            selectedUnit.UpdateMoveOutline(isActiveTurnForUnit);
            actionPerformed = plannedMove.actualStepsMoved > 0 || plannedMove.status == PlannedMoveStatus.HiddenBlockerStop;
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
                turnManager.OnCityCaptured(selectedUnit.isPlayerOwned, city);
                return;
            }
        }

        // If this unit has no moves left, deselect it. Otherwise, update reachable tiles.
        if (!selectedUnit.CanMoveThisTurn())
        {
            // Special case: if the unit cannot move anymore but still
            // has not attacked and has an enemy adjacent, keep it
            // selected and show red attack tiles as a reminder.
            if (selectedUnit.CanAttackThisTurn() && HasAttackableAdjacentTiles(selectedUnit))
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
            turnManager.ScheduleAutoEndTurnCheck();
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
