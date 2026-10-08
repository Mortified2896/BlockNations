using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// Manages which unit is selected and handles tile movement and attacks.
/// </summary>
public class UnitSelectionManager : MonoBehaviour
{
    public static UnitSelectionManager Instance { get; private set; }

    [Header("References")]
    public TurnManager turnManager;

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

        if (!TryGetUnitOriginTile(unit, out TileVisibility originTile))
        {
            return;
        }

        Dictionary<TileVisibility, List<TileVisibility>> reachablePaths = BuildReachablePathMap(unit, originTile);
        TileHighlighter[] tiles = Object.FindObjectsByType<TileHighlighter>(FindObjectsSortMode.None);
        bool canMove = unit.CanMoveThisTurn();
        bool canAttack = unit.CanAttackThisTurn();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        HashSet<TileVisibility> debugMoveHighlights = LegalActionDebugComparer.Enabled ? new HashSet<TileVisibility>() : null;
        HashSet<TileVisibility> debugAttackHighlights = LegalActionDebugComparer.Enabled ? new HashSet<TileVisibility>() : null;
#endif

        foreach (TileHighlighter tile in tiles)
        {
            if (tile == null) continue;

            TileVisibility targetTile = tile.GetComponent<TileVisibility>();
            if (targetTile == null)
            {
                continue;
            }

            Unit occupant = GridUtils.GetUnitAtPosition(targetTile.transform.position, unit);
            int stepDistance = GetChebyshevDistance(originTile, targetTile);
            bool hasVisibleOccupant = occupant != null && targetTile.isVisibleNow;
            if (hasVisibleOccupant &&
                occupant.ownerSeatIndex != unit.ownerSeatIndex &&
                canAttack &&
                unit.IsTargetInAttackRange(stepDistance))
            {
                tile.SetAttackable(true);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                debugAttackHighlights?.Add(targetTile);
#endif
            }
            else if (!hasVisibleOccupant && canMove && reachablePaths.ContainsKey(targetTile))
            {
                tile.SetReachable(true);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                debugMoveHighlights?.Add(targetTile);
#endif
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        LegalActionDebugComparer.CompareUnitHighlights(turnManager, unit, debugMoveHighlights, debugAttackHighlights);
#endif
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

    public bool HasLegalAttackTargetNow(Unit unit)
    {
        return HasAttackableTilesInRange(unit);
    }

    private bool HasAttackableTilesInRange(Unit unit)
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

            int tileDistance = GetChebyshevDistance(originTile, targetTile);
            if (!unit.IsTargetInAttackRange(tileDistance))
            {
                continue;
            }

            Unit occupant = GridUtils.GetUnitAtPosition(targetTile.transform.position, unit);
            if (occupant != null && targetTile.isVisibleNow && occupant.ownerSeatIndex != unit.ownerSeatIndex)
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
            reason = "no_targets_in_range";
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

        Dictionary<TileVisibility, List<TileVisibility>> reachablePaths = BuildReachablePathMap(unit, originTile);
        bool canMove = unit.CanMoveThisTurn();
        bool canAttack = unit.CanAttackThisTurn();

        foreach (TileHighlighter tile in tiles)
        {
            if (tile == null) continue;

            TileVisibility targetTile = tile.GetComponent<TileVisibility>();
            if (targetTile == null)
                continue;

            int stepDistance = GetChebyshevDistance(originTile, targetTile);
            if (stepDistance <= 0)
                continue;

            Unit occupant = GridUtils.GetUnitAtPosition(targetTile.transform.position, unit);
            bool hasVisibleOccupant = occupant != null && targetTile.isVisibleNow;
            if (hasVisibleOccupant && occupant.ownerSeatIndex != unit.ownerSeatIndex && canAttack)
            {
                if (unit.IsTargetInAttackRange(stepDistance))
                {
                    attackableCount++;
                }
            }
            else if (!hasVisibleOccupant && canMove && reachablePaths.ContainsKey(targetTile))
            {
                reachableCount++;
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
        return unit != null ? unit.GetRemainingMoveRangeThisTurn() : 0;
    }

    private static int GetChebyshevDistance(TileVisibility from, TileVisibility to)
    {
        return UnitActionRules.GetChebyshevDistance(from, to);
    }

    private Dictionary<TileVisibility, List<TileVisibility>> BuildReachablePathMap(Unit unit, TileVisibility originTile)
    {
        Dictionary<TileVisibility, List<TileVisibility>> reachablePaths = new Dictionary<TileVisibility, List<TileVisibility>>();
        if (unit == null || originTile == null)
        {
            return reachablePaths;
        }

        if (turnManager == null) return reachablePaths;
        foreach (LegalTurnAction action in LegalActionService.GetLegalActionsForUnit(turnManager, unit, unit.ownerSeatIndex,
            turnManager.ComputeVisibilityForSeat(unit.ownerSeatIndex)))
            if (action.ActionType == LegalActionType.UnitMove) reachablePaths[action.TargetTile] = new List<TileVisibility>(action.Path);
        return reachablePaths;
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

        bool isActiveTurnForUnit = turnManager == null || turnManager.IsCurrentSideOwner(selectedUnit.ownerSeatIndex);

        Vector3 from = selectedUnit.transform.position;
        GridManager grid = turnManager != null ? turnManager.gridManager : null;
        if (grid == null ||
            !grid.TryGetTileAtWorldPosition(from, out TileVisibility originTile) ||
            !grid.TryGetTileAtWorldPosition(targetWorldPosition, out TileVisibility targetTile))
        {
            ClearSelection();
            return;
        }

        bool actionPerformed = false;
        foreach (LegalTurnAction legal in LegalActionService.GetLegalActionsForUnit(turnManager, selectedUnit,
            selectedUnit.ownerSeatIndex, turnManager.ComputeVisibilityForSeat(selectedUnit.ownerSeatIndex)))
        {
            if (legal.TargetTile != targetTile) continue;
            actionPerformed = new SceneSimulationAdapter(turnManager).TryApply(legal);
            break;
        }
        if (!actionPerformed) { ClearSelection(); return; }
        turnManager.RecalculatePlayerVisibility();
        if (turnManager.gameOver) return;
        selectedUnit.UpdateMoveOutline(isActiveTurnForUnit);

        // If this unit has no moves left, deselect it. Otherwise, update reachable tiles.
        if (!selectedUnit.CanMoveThisTurn())
        {
            // Special case: if the unit cannot move anymore but still
            // has not attacked and has an enemy in range, keep it
            // selected and show red attack tiles as a reminder.
            if (selectedUnit.CanAttackThisTurn() && HasAttackableTilesInRange(selectedUnit))
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
            RefreshMoveOutlinesForCurrentTurn();
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
        ResetMovementForSeat(isPlayerOwnedSide ? 0 : 1, isActiveTurn);
    }

    public void ResetMovementForSeat(int seatIndex, bool isActiveTurn)
    {
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            bool matchesSide = unit.ownerSeatIndex == seatIndex;
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
                isActiveTurn = turnManager.IsCurrentSideOwner(unit.ownerSeatIndex) && turnManager.IsHumanTurn();
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
