using UnityEngine;

public static class GridUtils
{
    /// <summary>
    /// Returns the first Unit found at the given world position (within a small radius),
    /// ignoring a specified unit if provided. Returns null if no unit occupies the tile.
    /// </summary>
    public static Unit GetUnitAtPosition(Vector3 tileWorldPosition, Unit unitToIgnore = null)
    {
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        const float epsilon = 0.25f; // tolerate slight float drift from saves/moves
        foreach (Unit unit in units)
        {
            if (unit == null || unit == unitToIgnore) continue;

            Vector3 pos = unit.transform.position;
            pos.z = 0f;

            Vector3 target = tileWorldPosition;
            target.z = 0f;

            if ((pos - target).sqrMagnitude < (epsilon * epsilon))
            {
                return unit;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the first City found at the given world position (within a small radius),
    /// or null if there is no city on that tile.
    /// </summary>
    public static City GetCityAtPosition(Vector3 tileWorldPosition)
    {
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        const float epsilon = 0.25f;
        foreach (City city in cities)
        {
            if (city == null) continue;

            Vector3 pos = city.transform.position;
            pos.z = 0f;

            Vector3 target = tileWorldPosition;
            target.z = 0f;

            if ((pos - target).sqrMagnitude < (epsilon * epsilon))
            {
                return city;
            }
        }

        return null;
    }

    /// <summary>
    /// True if any other Unit already occupies the given tileWorldPosition.
    /// </summary>
    public static bool IsTileOccupied(Vector3 tileWorldPosition, Unit unitToIgnore = null)
    {
        return GetUnitAtPosition(tileWorldPosition, unitToIgnore) != null;
    }
}
