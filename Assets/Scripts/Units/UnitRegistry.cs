using System.Collections.Generic;

public static class UnitRegistry
{
    public const string WarriorTypeId = "warrior";
    public const string ScoutTypeId = "scout";
    public const string RiderTypeId = "rider";
    public const string ArcherTypeId = "archer";

    private static readonly Dictionary<string, UnitDefinition> Definitions =
        new Dictionary<string, UnitDefinition>(System.StringComparer.Ordinal)
        {
            [WarriorTypeId] = new UnitDefinition(
                WarriorTypeId,
                "Warrior",
                2,
                1,
                WarriorTypeId,
                CombatValues.FromDisplay(1),
                CombatValues.FromDisplay(1),
                1,
                true,
                CombatValues.FromDisplay(0),
                1,
                1),
            [ScoutTypeId] = new UnitDefinition(
                ScoutTypeId,
                "Scout",
                2,
                2,
                ScoutTypeId,
                CombatValues.FromDisplay(1),
                CombatValues.FromDisplay(0),
                1,
                false,
                CombatValues.FromDisplay(0),
                1,
                0),
            [RiderTypeId] = new UnitDefinition(
                RiderTypeId,
                "Rider",
                2,
                1,
                RiderTypeId,
                CombatValues.FromDisplay(1),
                CombatValues.FromDisplay(0, 5),
                1,
                true,
                CombatValues.FromDisplay(0),
                2,
                1),
            [ArcherTypeId] = new UnitDefinition(
                ArcherTypeId,
                "Archer",
                2,
                1,
                ArcherTypeId,
                CombatValues.FromDisplay(1),
                CombatValues.FromDisplay(0, 5),
                2,
                false,
                CombatValues.FromDisplay(0),
                1,
                1)
        };

    public static UnitDefinition Warrior => Definitions[WarriorTypeId];
    public static UnitDefinition Scout => Definitions[ScoutTypeId];
    public static UnitDefinition Rider => Definitions[RiderTypeId];
    public static UnitDefinition Archer => Definitions[ArcherTypeId];

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
