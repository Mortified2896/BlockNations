using System;

public sealed class UnitDefinition
{
    public UnitDefinition(
        string typeId,
        string displayName,
        int recruitCost,
        int maxHealth,
        int attack,
        int defense,
        int maxMovesPerTurn)
    {
        if (string.IsNullOrWhiteSpace(typeId))
        {
            throw new ArgumentException("typeId is required.", nameof(typeId));
        }

        TypeId = typeId.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? TypeId : displayName.Trim();
        RecruitCost = Math.Max(0, recruitCost);
        MaxHealth = Math.Max(1, maxHealth);
        Attack = Math.Max(0, attack);
        Defense = Math.Max(0, defense);
        MaxMovesPerTurn = Math.Max(1, maxMovesPerTurn);
    }

    public string TypeId { get; }
    public string DisplayName { get; }
    public int RecruitCost { get; }
    public int MaxHealth { get; }
    public int Attack { get; }
    public int Defense { get; }
    public int MaxMovesPerTurn { get; }
}
