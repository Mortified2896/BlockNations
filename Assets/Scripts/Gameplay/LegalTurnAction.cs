using System.Collections.Generic;

public enum LegalActionType
{
    UnitMove,
    UnitAttack,
    CityRecruit,
    EndTurn
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
        IReadOnlyList<TileVisibility> path,
        City city = null,
        string recruitUnitTypeId = null,
        int recruitCost = 0)
    {
        ActionType = actionType;
        SeatIndex = seatIndex;
        Unit = unit;
        OriginTile = originTile;
        TargetTile = targetTile;
        TargetUnit = targetUnit;
        Path = path;
        City = city;
        RecruitUnitTypeId = recruitUnitTypeId;
        RecruitCost = recruitCost;
    }

    public LegalActionType ActionType { get; }
    public int SeatIndex { get; }
    public Unit Unit { get; }
    public TileVisibility OriginTile { get; }
    public TileVisibility TargetTile { get; }
    public Unit TargetUnit { get; }
    public IReadOnlyList<TileVisibility> Path { get; }
    public City City { get; }
    public string RecruitUnitTypeId { get; }
    public int RecruitCost { get; }
}
