using System;
using UnityEngine;

public static class ProfileTitleGenerator
{
    private static readonly string[] Titles =
    {
        "the Bold",
        "the Swift",
        "the Wise",
        "the Brave",
        "the Stalwart",
        "the Keen",
        "the Fierce",
        "the Steady",
        "the Valiant",
        "the Watchful",
        "the Resolute",
        "the Cunning",
        "the Radiant",
        "the Unbroken",
        "the Loyal",
        "the Daring"
    };

    public static string Generate()
    {
        return GenerateDistinct(string.Empty);
    }

    public static string GenerateDistinct(string currentTitle)
    {
        if (Titles.Length == 0)
        {
            return string.Empty;
        }

        if (Titles.Length == 1)
        {
            return Titles[0];
        }

        int startIndex = UnityEngine.Random.Range(0, Titles.Length);
        for (int offset = 0; offset < Titles.Length; offset++)
        {
            string candidate = Titles[(startIndex + offset) % Titles.Length];
            if (!string.Equals(candidate, currentTitle, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return Titles[startIndex];
    }
}
