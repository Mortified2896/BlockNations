#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class LegalActionDebugComparer
{
    public const bool Enabled = false;

    private const int MaxLoggedPositions = 8;

    public static void CompareUnitHighlights(
        TurnManager turnManager,
        Unit unit,
        IReadOnlyCollection<TileVisibility> uiMoveHighlights,
        IReadOnlyCollection<TileVisibility> uiAttackHighlights)
    {
        if (!Enabled)
        {
            return;
        }

        if (turnManager == null || unit == null)
        {
            return;
        }

        HashSet<TileVisibility> uiMoves = ToTileSet(uiMoveHighlights);
        HashSet<TileVisibility> uiAttacks = ToTileSet(uiAttackHighlights);
        HashSet<TileVisibility> serviceMoves = new HashSet<TileVisibility>();
        HashSet<TileVisibility> serviceAttacks = new HashSet<TileVisibility>();

        List<LegalTurnAction> actions = LegalActionService.GetLegalActionsForUnit(
            turnManager,
            unit,
            unit.ownerSeatIndex);

        foreach (LegalTurnAction action in actions)
        {
            if (action.TargetTile == null)
            {
                continue;
            }

            switch (action.ActionType)
            {
                case LegalActionType.UnitMove:
                    serviceMoves.Add(action.TargetTile);
                    break;
                case LegalActionType.UnitAttack:
                    serviceAttacks.Add(action.TargetTile);
                    break;
            }
        }

        if (SetsMatch(uiMoves, serviceMoves) && SetsMatch(uiAttacks, serviceAttacks))
        {
            return;
        }

        Debug.LogWarning(BuildMismatchMessage(unit, uiMoves, uiAttacks, serviceMoves, serviceAttacks));
    }

    private static HashSet<TileVisibility> ToTileSet(IReadOnlyCollection<TileVisibility> tiles)
    {
        HashSet<TileVisibility> result = new HashSet<TileVisibility>();
        if (tiles == null)
        {
            return result;
        }

        foreach (TileVisibility tile in tiles)
        {
            if (tile != null)
            {
                result.Add(tile);
            }
        }

        return result;
    }

    private static bool SetsMatch(HashSet<TileVisibility> left, HashSet<TileVisibility> right)
    {
        return left.Count == right.Count && left.SetEquals(right);
    }

    private static string BuildMismatchMessage(
        Unit unit,
        HashSet<TileVisibility> uiMoves,
        HashSet<TileVisibility> uiAttacks,
        HashSet<TileVisibility> serviceMoves,
        HashSet<TileVisibility> serviceAttacks)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("[LegalActionDebugComparer] UI highlights differ from LegalActionService for ");
        builder.Append(unit != null ? unit.name : "unknown unit");
        builder.Append(" at ");
        builder.Append(FormatWorldPosition(unit != null ? unit.transform.position : Vector3.zero));
        builder.Append(" seat=");
        builder.Append(unit != null ? unit.ownerSeatIndex : -1);
        builder.Append(". UI moves=");
        builder.Append(uiMoves.Count);
        builder.Append(", service moves=");
        builder.Append(serviceMoves.Count);
        builder.Append(", UI attacks=");
        builder.Append(uiAttacks.Count);
        builder.Append(", service attacks=");
        builder.Append(serviceAttacks.Count);

        AppendDiff(builder, "missing moves", serviceMoves, uiMoves);
        AppendDiff(builder, "extra moves", uiMoves, serviceMoves);
        AppendDiff(builder, "missing attacks", serviceAttacks, uiAttacks);
        AppendDiff(builder, "extra attacks", uiAttacks, serviceAttacks);

        return builder.ToString();
    }

    private static void AppendDiff(
        StringBuilder builder,
        string label,
        HashSet<TileVisibility> expected,
        HashSet<TileVisibility> actual)
    {
        int appended = 0;
        int total = 0;

        foreach (TileVisibility tile in expected)
        {
            if (tile == null || actual.Contains(tile))
            {
                continue;
            }

            total++;
            if (appended >= MaxLoggedPositions)
            {
                continue;
            }

            if (appended == 0)
            {
                builder.Append("; ");
                builder.Append(label);
                builder.Append("=[");
            }
            else
            {
                builder.Append(", ");
            }

            builder.Append(FormatWorldPosition(tile.transform.position));
            appended++;
        }

        if (appended > 0)
        {
            if (total > appended)
            {
                builder.Append(", +");
                builder.Append(total - appended);
                builder.Append(" more");
            }

            builder.Append("]");
        }
    }

    private static string FormatWorldPosition(Vector3 position)
    {
        return $"({position.x:0.##}, {position.y:0.##}, {position.z:0.##})";
    }
}
#endif
