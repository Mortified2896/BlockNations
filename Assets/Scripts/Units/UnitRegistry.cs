using System.Collections.Generic;

public static class UnitRegistry
{
    public const string WarriorTypeId = "warrior";

    private static readonly Dictionary<string, UnitDefinition> Definitions =
        new Dictionary<string, UnitDefinition>(System.StringComparer.Ordinal)
        {
            [WarriorTypeId] = new UnitDefinition(
                typeId: WarriorTypeId,
                displayName: "Warrior",
                recruitCost: 2,
                maxHealth: 1,
                attack: 1,
                defense: 0,
                maxMovesPerTurn: 1)
        };

    public static UnitDefinition Warrior => Definitions[WarriorTypeId];

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
