using UnityEngine;

public static class PbpGameDisplayNameGenerator
{
    private static readonly string[] PlaceNames =
    {
        "Oakridge",
        "Northwatch",
        "Red Hollow",
        "Stoneford",
        "Ironvale",
        "Dawnkeep",
        "Wolfpass",
        "Silvermoor",
        "Highcrest",
        "Blackharbor",
        "Raven Hill",
        "Goldmere",
        "Frostgate",
        "Emberfield",
        "West March",
        "East Haven",
        "Pinewatch",
        "Riverbend",
        "Ash Hollow",
        "Stormridge",
        "Kingsford",
        "Queensrest",
        "Shadowvale",
        "Brightwater",
        "Grimwatch",
        "Whiterock",
        "Deepwood",
        "Mirthvale",
        "Suncrest",
        "Moon Harbor",
        "Thornfield",
        "Cedar Point"
    };

    public static string BuildForGameId(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return null;
        }

        string normalized = gameId.Trim();
        string hash = Hash128.Compute(normalized).ToString();
        uint seed = 0u;
        int charsToRead = Mathf.Min(8, hash.Length);
        for (int i = 0; i < charsToRead; i++)
        {
            uint hexValue = ParseHex(hash[i]);
            seed = (seed << 4) | hexValue;
        }

        int index = (int)(seed % (uint)PlaceNames.Length);
        return "Battle of " + PlaceNames[index];
    }

    private static uint ParseHex(char c)
    {
        if (c >= '0' && c <= '9')
        {
            return (uint)(c - '0');
        }

        if (c >= 'a' && c <= 'f')
        {
            return (uint)(c - 'a' + 10);
        }

        if (c >= 'A' && c <= 'F')
        {
            return (uint)(c - 'A' + 10);
        }

        return 0u;
    }
}
