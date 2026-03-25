using System;
using System.Collections.Generic;
using UnityEngine;

public static class ProfileUsernameGenerator
{
    public const int MaxUsernameLength = 12;
    private const string FallbackUsername = "IronWolf";

    private static readonly string[] Adjectives =
    {
        "Iron",
        "Swift",
        "Silent",
        "Crimson",
        "Golden",
        "Shadow",
        "Storm",
        "Frost",
        "Ember",
        "Noble",
        "Wild",
        "Ancient",
        "Dark",
        "Bright",
        "Steel",
        "Scarlet",
        "Azure",
        "Grim",
        "Radiant",
        "Blazing",
        "Frozen",
        "Thunder",
        "Shrouded",
        "Rising",
        "Fallen",
        "Vengeful",
        "Valiant",
        "Mighty",
        "Cunning",
        "Eternal",
        "Savage",
        "Lone"
    };

    private static readonly string[] Nouns =
    {
        "Wolf",
        "Lion",
        "Falcon",
        "Bear",
        "Raven",
        "Tiger",
        "Hawk",
        "Fox",
        "Blade",
        "Shield",
        "Crown",
        "Legion",
        "Guard",
        "Knight",
        "Hunter",
        "Warden",
        "King",
        "Queen",
        "Prince",
        "Scout",
        "Spear",
        "Arrow",
        "Banner",
        "Empire",
        "Clan",
        "Order",
        "Sentinel",
        "Champion",
        "Rider",
        "Drake",
        "Spirit",
        "Forge"
    };

    private static readonly string[] ValidUsernames = BuildValidUsernames();

    public static string Generate()
    {
        return GenerateDistinct(string.Empty);
    }

    public static string GenerateDistinct(string currentUsername)
    {
        if (ValidUsernames.Length == 0)
        {
            return FallbackUsername;
        }

        if (ValidUsernames.Length == 1)
        {
            return ValidUsernames[0];
        }

        int startIndex = UnityEngine.Random.Range(0, ValidUsernames.Length);
        for (int offset = 0; offset < ValidUsernames.Length; offset++)
        {
            string candidate = ValidUsernames[(startIndex + offset) % ValidUsernames.Length];
            if (!string.Equals(candidate, currentUsername, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return ValidUsernames[startIndex];
    }

    private static string[] BuildValidUsernames()
    {
        List<string> names = new List<string>();

        for (int i = 0; i < Adjectives.Length; i++)
        {
            string adjective = Adjectives[i];
            for (int j = 0; j < Nouns.Length; j++)
            {
                string candidate = adjective + Nouns[j];
                if (candidate.Length <= MaxUsernameLength)
                {
                    names.Add(candidate);
                }
            }
        }

        return names.ToArray();
    }
}
