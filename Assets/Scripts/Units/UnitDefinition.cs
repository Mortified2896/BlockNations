using System;

public sealed class UnitDefinition
{
    public UnitDefinition(
        string typeId,
        string displayName,
        int recruitCost,
        int visionRange,
        string prefabTypeId,
        int maxHealthUnits,
        int attackUnits,
        int attackRange,
        bool canAttackAfterMoving,
        int defenseUnits,
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
        MaxHealthUnits = Math.Max(1, maxHealthUnits);
        AttackUnits = Math.Max(0, attackUnits);
        AttackRange = Math.Max(1, attackRange);
        CanAttackAfterMoving = canAttackAfterMoving;
        DefenseUnits = Math.Max(0, defenseUnits);
        MaxMovesPerTurn = Math.Max(1, maxMovesPerTurn);
        MaxAttacksPerTurn = Math.Max(0, maxAttacksPerTurn);
    }

    public string TypeId { get; }
    public string DisplayName { get; }
    public int RecruitCost { get; }
    public int VisionRange { get; }
    public string PrefabTypeId { get; }
    public int MaxHealthUnits { get; }
    public int AttackUnits { get; }
    public int AttackRange { get; }
    public bool CanAttackAfterMoving { get; }
    public int DefenseUnits { get; }
    public int MaxMovesPerTurn { get; }
    public int MaxAttacksPerTurn { get; }
}
