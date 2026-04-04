using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// Manages which unit is selected and handles tile movement and adjacent attacks.
/// </summary>
public class UnitSelectionManager : MonoBehaviour
{
    private static readonly Vector2Int[] NeighborOffsets =
    {
        new Vector2Int(1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(-1, 0),
        new Vector2Int(0, -1),
        new Vector2Int(1, 1),
        new Vector2Int(1, -1),
        new Vector2Int(-1, 1),
        new Vector2Int(-1, -1)
    };

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
        public int actualStepsMoved;
        public int consumedMoveCount;
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

    private struct TileNode
    {
        public TileVisibility Tile;
        public int Steps;
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
            if (hasVisibleOccupant && occupant.isPlayerOwned != unit.isPlayerOwned && canAttack && stepDistance == 1)
            {
                tile.SetAttackable(true);
            }
            else if (!hasVisibleOccupant && canMove && reachablePaths.ContainsKey(targetTile))
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
            if (hasVisibleOccupant && occupant.isPlayerOwned != unit.isPlayerOwned && canAttack)
            {
                if (stepDistance == 1)
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

    private Dictionary<TileVisibility, List<TileVisibility>> BuildReachablePathMap(Unit unit, TileVisibility originTile)
    {
        Dictionary<TileVisibility, List<TileVisibility>> reachablePaths = new Dictionary<TileVisibility, List<TileVisibility>>();
        if (unit == null || originTile == null)
        {
            return reachablePaths;
        }

        int remainingMoves = GetRemainingMoveCount(unit);
        if (remainingMoves <= 0)
        {
            return reachablePaths;
        }

        GridManager grid = turnManager != null ? turnManager.gridManager : null;
        if (grid == null)
        {
            return reachablePaths;
        }

        Queue<TileNode> frontier = new Queue<TileNode>();
        Dictionary<TileVisibility, TileVisibility> previous = new Dictionary<TileVisibility, TileVisibility>();
        Dictionary<TileVisibility, int> bestSteps = new Dictionary<TileVisibility, int>();

        frontier.Enqueue(new TileNode { Tile = originTile, Steps = 0 });
        bestSteps[originTile] = 0;

        while (frontier.Count > 0)
        {
            TileNode node = frontier.Dequeue();
            if (node.Steps >= remainingMoves)
            {
                continue;
            }

            for (int i = 0; i < NeighborOffsets.Length; i++)
            {
                Vector2Int offset = NeighborOffsets[i];
                int nextX = node.Tile.gridX + offset.x;
                int nextY = node.Tile.gridY + offset.y;
                if (!grid.TryGetTile(nextX, nextY, out TileVisibility nextTile) || nextTile == null)
                {
                    continue;
                }

                Unit occupant = GridUtils.GetUnitAtPosition(nextTile.transform.position, unit);
                if (occupant != null && nextTile.isVisibleNow)
                {
                    continue;
                }

                int nextSteps = node.Steps + 1;
                if (bestSteps.TryGetValue(nextTile, out int existingSteps) && existingSteps <= nextSteps)
                {
                    continue;
                }

                bestSteps[nextTile] = nextSteps;
                previous[nextTile] = node.Tile;
                frontier.Enqueue(new TileNode { Tile = nextTile, Steps = nextSteps });
            }
        }

        foreach (KeyValuePair<TileVisibility, int> entry in bestSteps)
        {
            TileVisibility targetTile = entry.Key;
            if (targetTile == originTile)
            {
                continue;
            }

            reachablePaths[targetTile] = BuildPath(originTile, targetTile, previous);
        }

        return reachablePaths;
    }

    private static List<TileVisibility> BuildPath(
        TileVisibility originTile,
        TileVisibility targetTile,
        Dictionary<TileVisibility, TileVisibility> previous)
    {
        List<TileVisibility> reversedPath = new List<TileVisibility>();
        TileVisibility current = targetTile;
        while (current != null && current != originTile)
        {
            reversedPath.Add(current);
            if (!previous.TryGetValue(current, out current))
            {
                break;
            }
        }

        reversedPath.Reverse();
        return reversedPath;
    }

    private PlannedMoveResult ExecutePlannedPath(Unit unit, List<TileVisibility> path)
    {
        PlannedMoveResult invalidResult = new PlannedMoveResult
        {
            status = PlannedMoveStatus.Invalid,
            finalWorldPosition = unit != null ? unit.transform.position : Vector3.zero,
            actualStepsMoved = 0,
            consumedMoveCount = 0
        };

        if (unit == null || path == null || path.Count == 0)
        {
            return invalidResult;
        }

        bool isMultiStepPath = path.Count > 1;
        Vector3 currentWorldPosition = unit.transform.position;
        currentWorldPosition.z = unit.transform.position.z;
        int actualStepsMoved = 0;

        for (int stepIndex = 0; stepIndex < path.Count; stepIndex++)
        {
            TileVisibility pathTile = path[stepIndex];
            if (pathTile == null)
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

                if (!isMultiStepPath)
                {
                    return invalidResult;
                }

                return new PlannedMoveResult
                {
                    status = PlannedMoveStatus.HiddenBlockerStop,
                    finalWorldPosition = currentWorldPosition,
                    actualStepsMoved = actualStepsMoved,
                    consumedMoveCount = actualStepsMoved + 1
                };
            }

            currentWorldPosition = stepWorldPosition;
            actualStepsMoved++;
        }

        return new PlannedMoveResult
        {
            status = PlannedMoveStatus.ReachedTarget,
            finalWorldPosition = currentWorldPosition,
            actualStepsMoved = actualStepsMoved,
            consumedMoveCount = path.Count
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

        Dictionary<TileVisibility, List<TileVisibility>> reachablePaths = BuildReachablePathMap(selectedUnit, originTile);
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
        bool targetTileHasVisibleOccupant = targetUnit != null && targetTile.isVisibleNow;

        bool actionPerformed = false;

        bool canAttackTarget = targetTileHasVisibleOccupant &&
                               targetUnit.isPlayerOwned != selectedUnit.isPlayerOwned &&
                               stepDistance == 1 &&
                               selectedUnit.CanAttackThisTurn();
        bool canMoveToEmpty = !targetTileHasVisibleOccupant &&
                              selectedUnit.CanMoveThisTurn() &&
                              reachablePaths.TryGetValue(targetTile, out List<TileVisibility> plannedPath);

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
            PlannedMoveResult plannedMove = ExecutePlannedPath(selectedUnit, plannedPath);
            if (plannedMove.status == PlannedMoveStatus.Invalid || plannedMove.consumedMoveCount <= 0)
            {
                ClearSelection();
                return;
            }

            if (selectedUnit.currentCity != null && plannedMove.actualStepsMoved > 0)
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

            selectedUnit.RegisterMove(plannedMove.consumedMoveCount);
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
