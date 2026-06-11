using System.Collections.Generic;

public enum LegalActionType
{
    UnitMove,
    UnitAttack
}

public enum LegalActionVisibilityMode
{
    CurrentViewerVisible
}

public readonly struct LegalTurnAction
{
    public LegalTurnAction(
        LegalActionType actionType,
        int seatIndex,
        Unit unit,
        TileVisibility originTile,
        TileVisibility targetTile,
        Unit targetUnit,
        IReadOnlyList<TileVisibility> path)
    {
        ActionType = actionType;
        SeatIndex = seatIndex;
        Unit = unit;
        OriginTile = originTile;
        TargetTile = targetTile;
        TargetUnit = targetUnit;
        Path = path;
    }

    public LegalActionType ActionType { get; }
    public int SeatIndex { get; }
    public Unit Unit { get; }
    public TileVisibility OriginTile { get; }
    public TileVisibility TargetTile { get; }
    public Unit TargetUnit { get; }
    public IReadOnlyList<TileVisibility> Path { get; }
}
