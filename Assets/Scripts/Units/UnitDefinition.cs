using System;

public sealed class UnitDefinition
{
    public UnitDefinition(
        string typeId,
        string displayName,
        int recruitCost,
        int visionRange,
        string prefabTypeId,
        int maxHealth,
        int attack,
        int defense,
        int maxMovesPerTurn,
        int maxAttacksPerTurn)
    {
        if (string.IsNullOrWhiteSpace(typeId))
        {
            throw new ArgumentException("typeId is required.", nameof(typeId));
        }

        TypeId = typeId.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? TypeId : displayName.Trim();
        RecruitCost = Math.Max(0, recruitCost);
        VisionRange = Math.Max(1, visionRange);
        PrefabTypeId = string.IsNullOrWhiteSpace(prefabTypeId) ? TypeId : prefabTypeId.Trim();
        MaxHealth = Math.Max(1, maxHealth);
        Attack = Math.Max(0, attack);
        Defense = Math.Max(0, defense);
        MaxMovesPerTurn = Math.Max(1, maxMovesPerTurn);
        MaxAttacksPerTurn = Math.Max(0, maxAttacksPerTurn);
    }

    public string TypeId { get; }
    public string DisplayName { get; }
    public int RecruitCost { get; }
    public int VisionRange { get; }
    public string PrefabTypeId { get; }
    public int MaxHealth { get; }
    public int Attack { get; }
    public int Defense { get; }
    public int MaxMovesPerTurn { get; }
    public int MaxAttacksPerTurn { get; }
}
