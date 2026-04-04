using System.Collections.Generic;

public static class UnitRegistry
{
    public const string WarriorTypeId = "warrior";
    public const string ScoutTypeId = "scout";
    public const string RiderTypeId = "rider";

    private static readonly Dictionary<string, UnitDefinition> Definitions =
        new Dictionary<string, UnitDefinition>(System.StringComparer.Ordinal)
        {
            [WarriorTypeId] = new UnitDefinition(
                typeId: WarriorTypeId,
                displayName: "Warrior",
                recruitCost: 2,
                visionRange: 1,
                prefabTypeId: WarriorTypeId,
                maxHealthUnits: CombatValues.FromDisplay(1),
                attackUnits: CombatValues.FromDisplay(1),
                defenseUnits: CombatValues.FromDisplay(0),
                maxMovesPerTurn: 1,
                maxAttacksPerTurn: 1),
            [ScoutTypeId] = new UnitDefinition(
                typeId: ScoutTypeId,
                displayName: "Scout",
                recruitCost: 2,
                visionRange: 2,
                prefabTypeId: ScoutTypeId,
                maxHealthUnits: CombatValues.FromDisplay(1),
                attackUnits: CombatValues.FromDisplay(0),
                defenseUnits: CombatValues.FromDisplay(0),
                maxMovesPerTurn: 1,
                maxAttacksPerTurn: 0),
            [RiderTypeId] = new UnitDefinition(
                typeId: RiderTypeId,
                displayName: "Rider",
                recruitCost: 2,
                visionRange: 1,
                prefabTypeId: RiderTypeId,
                maxHealthUnits: CombatValues.FromDisplay(1),
                attackUnits: CombatValues.FromDisplay(0, 5),
                defenseUnits: CombatValues.FromDisplay(0),
                maxMovesPerTurn: 2,
                maxAttacksPerTurn: 1)
        };

    public static UnitDefinition Warrior => Definitions[WarriorTypeId];
    public static UnitDefinition Scout => Definitions[ScoutTypeId];
    public static UnitDefinition Rider => Definitions[RiderTypeId];

    public static System.Collections.Generic.IEnumerable<UnitDefinition> AllDefinitions => Definitions.Values;

    public static bool TryGetDefinition(string unitTypeId, out UnitDefinition definition)
    {
        string normalizedTypeId = NormalizeTypeId(unitTypeId);
        return Definitions.TryGetValue(normalizedTypeId, out definition);
    }

    public static UnitDefinition GetDefinitionOrDefault(string unitTypeId)
    {
        if (TryGetDefinition(unitTypeId, out UnitDefinition definition))
        {
            return definition;
        }

        return Warrior;
    }

    public static string NormalizeTypeId(string unitTypeId)
    {
        return string.IsNullOrWhiteSpace(unitTypeId)
            ? WarriorTypeId
            : unitTypeId.Trim();
    }
}
