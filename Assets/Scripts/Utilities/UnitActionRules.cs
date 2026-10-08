using System;
using System.Collections.Generic;
using UnityEngine;

internal static class UnitActionRules
{
    public static bool UsesCommittedMoveActionThisTurn(string unitTypeId)
    {
        return UnitRegistry.GetDefinitionOrDefault(unitTypeId).UsesCommittedMoveAction;
    }

    public static int GetRemainingMoveRangeThisTurn(string unitTypeId, int maxMovesPerTurn, int movesUsedThisTurn)
    {
        return BlockNations.AI.AIActionRules.RemainingMoves(
            UsesCommittedMoveActionThisTurn(unitTypeId), maxMovesPerTurn, movesUsedThisTurn);
    }

    public static bool CanAttackThisTurn(
        bool canAttackAfterMoving,
        int maxAttacksPerTurn,
        int attacksUsedThisTurn,
        int movesUsedThisTurn)
    {
        return BlockNations.AI.AIActionRules.CanAttack(canAttackAfterMoving, maxAttacksPerTurn, attacksUsedThisTurn, movesUsedThisTurn);
    }

    public static int RegisterMove(int movesUsedThisTurn, int maxMovesPerTurn)
    {
        return BlockNations.Simulation.SimulationRules.Consume(movesUsedThisTurn, maxMovesPerTurn);
    }

    public static int RegisterMove(int movesUsedThisTurn, int maxMovesPerTurn, int moveCount) =>
        BlockNations.Simulation.SimulationRules.Consume(movesUsedThisTurn, maxMovesPerTurn, moveCount);

    public static int RegisterAttack(int attacksUsedThisTurn, int maxAttacksPerTurn) =>
        BlockNations.Simulation.SimulationRules.Consume(attacksUsedThisTurn, maxAttacksPerTurn);

    public static int ComputeMitigatedDamage(int attackUnits, int defenseUnits)
    {
        return BlockNations.AI.AIActionRules.Damage(attackUnits, defenseUnits);
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
        return BlockNations.AI.AIActionRules.Distance(fromX, fromY, toX, toY);
    }

    public static Dictionary<TileVisibility, List<TileVisibility>> BuildReachablePathMap(
        GridManager grid,
        TileVisibility originTile,
        int remainingMoves,
        Func<TileVisibility, bool> isTileBlocked)
    {
        var paths = new Dictionary<TileVisibility, List<TileVisibility>>();
        if (grid == null || originTile == null || remainingMoves <= 0) return paths;
        bool[] tiles = new bool[grid.width * grid.height];
        foreach (TileVisibility tile in grid.GetAllTiles()) tiles[tile.gridY * grid.width + tile.gridX] = true;
        var corePaths = BlockNations.Simulation.MatchPaths.Build(grid.width, grid.height, tiles,
            originTile.gridY * grid.width + originTile.gridX, remainingMoves, position =>
            {
                grid.TryGetTile(position % grid.width, position / grid.width, out TileVisibility tile);
                return isTileBlocked != null && isTileBlocked(tile);
            });
        foreach (var entry in corePaths)
        {
            grid.TryGetTile(entry.Key % grid.width, entry.Key / grid.width, out TileVisibility target);
            var path = new List<TileVisibility>();
            foreach (int position in entry.Value)
            {
                grid.TryGetTile(position % grid.width, position / grid.width, out TileVisibility tile);
                path.Add(tile);
            }
            paths.Add(target, path);
        }
        return paths;
    }
}
