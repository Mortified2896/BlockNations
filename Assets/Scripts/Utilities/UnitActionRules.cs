using System;
using System.Collections.Generic;
using UnityEngine;

internal static class UnitActionRules
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

    private struct TileNode
    {
        public TileVisibility Tile;
        public int Steps;
    }

    public static bool UsesCommittedMoveActionThisTurn(string unitTypeId)
    {
        return UnitRegistry.NormalizeTypeId(unitTypeId) == UnitRegistry.RiderTypeId;
    }

    public static int GetRemainingMoveRangeThisTurn(string unitTypeId, int maxMovesPerTurn, int movesUsedThisTurn)
    {
        if (UsesCommittedMoveActionThisTurn(unitTypeId))
        {
            return movesUsedThisTurn > 0 ? 0 : Mathf.Max(0, maxMovesPerTurn);
        }

        return Mathf.Max(0, maxMovesPerTurn - movesUsedThisTurn);
    }

    public static bool CanAttackThisTurn(
        bool canAttackAfterMoving,
        int maxAttacksPerTurn,
        int attacksUsedThisTurn,
        int movesUsedThisTurn)
    {
        if (attacksUsedThisTurn >= maxAttacksPerTurn)
        {
            return false;
        }

        if (!canAttackAfterMoving && movesUsedThisTurn > 0)
        {
            return false;
        }

        return true;
    }

    public static int RegisterMove(int movesUsedThisTurn, int maxMovesPerTurn)
    {
        if (movesUsedThisTurn < maxMovesPerTurn)
        {
            movesUsedThisTurn++;
        }

        return movesUsedThisTurn;
    }

    public static int RegisterMove(int movesUsedThisTurn, int maxMovesPerTurn, int moveCount)
    {
        for (int i = 0; i < moveCount; i++)
        {
            movesUsedThisTurn = RegisterMove(movesUsedThisTurn, maxMovesPerTurn);
        }

        return movesUsedThisTurn;
    }

    public static int RegisterAttack(int attacksUsedThisTurn, int maxAttacksPerTurn)
    {
        if (attacksUsedThisTurn < maxAttacksPerTurn)
        {
            attacksUsedThisTurn++;
        }

        return attacksUsedThisTurn;
    }

    public static int ComputeMitigatedDamage(int attackUnits, int defenseUnits)
    {
        return Mathf.Max(0, attackUnits - defenseUnits);
    }

    public static bool IsTargetInAttackRange(int attackRange, int tileDistance)
    {
        return tileDistance > 0 && tileDistance <= Mathf.Max(1, attackRange);
    }

    public static bool AdvancesIntoDefenderTileOnKill(int attackRange)
    {
        return Mathf.Max(1, attackRange) <= 1;
    }

    public static int GetChebyshevDistance(TileVisibility from, TileVisibility to)
    {
        if (from == null || to == null)
        {
            return int.MaxValue;
        }

        return GetChebyshevDistance(from.gridX, from.gridY, to.gridX, to.gridY);
    }

    public static int GetChebyshevDistance(int fromX, int fromY, int toX, int toY)
    {
        return Mathf.Max(Mathf.Abs(toX - fromX), Mathf.Abs(toY - fromY));
    }

    public static Dictionary<TileVisibility, List<TileVisibility>> BuildReachablePathMap(
        GridManager grid,
        TileVisibility originTile,
        int remainingMoves,
        Func<TileVisibility, bool> isTileBlocked)
    {
        Dictionary<TileVisibility, List<TileVisibility>> reachablePaths = new Dictionary<TileVisibility, List<TileVisibility>>();
        if (grid == null || originTile == null || remainingMoves <= 0)
        {
            return reachablePaths;
        }

        Func<TileVisibility, bool> safeIsTileBlocked = isTileBlocked ?? (_ => false);
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

                if (safeIsTileBlocked(nextTile))
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
}
