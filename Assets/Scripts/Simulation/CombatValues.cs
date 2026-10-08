using System;
using System.Globalization;

public static class CombatValues
{
    public const int Scale = 10;

    public static int FromDisplay(int whole, int tenths = 0)
    {
        if (whole < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(whole), "Combat display values cannot be negative.");
        }

        if (tenths < 0 || tenths >= Scale)
        {
            throw new ArgumentOutOfRangeException(nameof(tenths), $"Tenths must be between 0 and {Scale - 1}.");
        }

        return checked((whole * Scale) + tenths);
    }

    public static int FromLegacyWhole(int whole)
    {
        return Math.Max(0, whole) * Scale;
    }

    public static string FormatUnits(int units)
    {
        int clampedUnits = Math.Max(0, units);
        int whole = clampedUnits / Scale;
        int remainder = clampedUnits % Scale;

        if (remainder == 0)
        {
            return whole.ToString(CultureInfo.InvariantCulture);
        }

        return whole.ToString(CultureInfo.InvariantCulture) + "." + remainder.ToString(CultureInfo.InvariantCulture);
    }

    public static string FormatRatio(int currentUnits, int maxUnits)
    {
        return $"{FormatUnits(currentUnits)}/{FormatUnits(maxUnits)}";
    }
}
