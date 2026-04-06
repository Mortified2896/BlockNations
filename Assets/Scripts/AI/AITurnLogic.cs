using System.Collections.Generic;
using UnityEngine;

public static class AITurnLogic
{
    private const int FastStealRadiusTiles = 2;

    public sealed class CityDefensePlan
    {
        public CityDefensePlan(City city)
        {
            City = city;
        }

        public City City { get; }
        public bool IsAtRisk { get; set; }
        public bool CanVacateCityTile { get; set; }
        public Vector3? PreferredCombatPosition { get; set; }
        public Vector3? PreferredSupportCombatPosition { get; set; }
        public Vector3? PreferredScoutPosition { get; set; }
        public Unit AssignedCombatUnit { get; set; }
        public Unit AssignedSupportCombatUnit { get; set; }
        public Unit AssignedScoutUnit { get; set; }
    }

    public static Dictionary<City, CityDefensePlan> BuildCityDefensePlans(
        GridManager gridManager,
        City[] allCities,
        Unit[] allUnits,
        City primaryFriendlyCity,
        bool actingSideIsPlayerOwned,
        HashSet<TileVisibility> visibleTiles,
        bool aiHasPerfectInfo)
    {
        Dictionary<City, CityDefensePlan> plans = new Dictionary<City, CityDefensePlan>();
        if (gridManager == null || allCities == null || allUnits == null)
        {
            return plans;
        }

        List<Unit> friendlyCombatUnits = new List<Unit>();
        List<Unit> friendlyScouts = new List<Unit>();
        List<Unit> visibleEnemyFastUnits = new List<Unit>();
        List<Vector2Int> enemyCityCoords = new List<Vector2Int>();

        for (int i = 0; i < allCities.Length; i++)
        {
            City city = allCities[i];
            if (city == null)
            {
                continue;
            }

            if (city.isPlayerOwned != actingSideIsPlayerOwned)
            {
                enemyCityCoords.Add(new Vector2Int(city.x, city.y));
            }
        }

        for (int i = 0; i < allUnits.Length; i++)
        {
            Unit unit = allUnits[i];
            if (unit == null)
            {
                continue;
            }

            if (unit.isPlayerOwned == actingSideIsPlayerOwned)
            {
                if (unit.UnitTypeId == UnitRegistry.ScoutTypeId)
                {
                    friendlyScouts.Add(unit);
                }
                else
                {
                    friendlyCombatUnits.Add(unit);
                }

                continue;
            }

            if (unit.maxMovesPerTurn < FastStealRadiusTiles)
            {
                continue;
            }

            if (IsVisibleToSide(gridManager, unit, visibleTiles, aiHasPerfectInfo))
            {
                visibleEnemyFastUnits.Add(unit);
            }
        }

        for (int i = 0; i < allCities.Length; i++)
        {
            City city = allCities[i];
            if (city == null || city.isPlayerOwned != actingSideIsPlayerOwned)
            {
                continue;
            }

            CityDefensePlan plan = TryBuildCityDefensePlan(
                gridManager,
                city,
                friendlyCombatUnits,
                friendlyScouts,
                visibleEnemyFastUnits,
                enemyCityCoords,
                primaryFriendlyCity,
                visibleTiles,
                aiHasPerfectInfo);
            if (plan != null)
            {
                plans[city] = plan;
            }
        }

        return plans;
    }

    private static CityDefensePlan TryBuildCityDefensePlan(
        GridManager gridManager,
        City city,
        List<Unit> friendlyCombatUnits,
        List<Unit> friendlyScouts,
        List<Unit> visibleEnemyFastUnits,
        List<Vector2Int> enemyCityCoords,
        City primaryFriendlyCity,
        HashSet<TileVisibility> visibleTiles,
        bool aiHasPerfectInfo)
    {
        List<TileVisibility> threatTiles = GetThreatTiles(gridManager, city, FastStealRadiusTiles);
        if (threatTiles.Count == 0)
        {
            return null;
        }

        bool allThreatTilesVisible = aiHasPerfectInfo || AreAllTilesVisible(threatTiles, visibleTiles);
        List<TileVisibility> visibleFastThreatTilesNearCity = new List<TileVisibility>();
        bool isPrimaryFriendlyCity = city == primaryFriendlyCity;

        for (int i = 0; i < visibleEnemyFastUnits.Count; i++)
        {
            Unit enemyUnit = visibleEnemyFastUnits[i];
            if (!gridManager.TryGetTileAtWorldPosition(enemyUnit.transform.position, out TileVisibility enemyTile))
            {
                continue;
            }

            if (GetChebyshevDistance(enemyTile.gridX, enemyTile.gridY, city.x, city.y) <= FastStealRadiusTiles + 1)
            {
                visibleFastThreatTilesNearCity.Add(enemyTile);
            }
        }

        bool visibleFastThreatNearCity = visibleFastThreatTilesNearCity.Count > 0;

        if (allThreatTilesVisible && !visibleFastThreatNearCity && city.stationedUnit == null && HasAdjacentCombatUnit(gridManager, city, friendlyCombatUnits))
        {
            return null;
        }

        if (!TryFindBestDefenseTile(gridManager, city, threatTiles, visibleTiles, aiHasPerfectInfo, enemyCityCoords, out TileVisibility defenseTile))
        {
            return null;
        }

        CityDefensePlan plan = new CityDefensePlan(city)
        {
            PreferredCombatPosition = defenseTile.transform.position
        };

        Unit cityOccupant = GridUtils.GetUnitAtPosition(city.transform.position);
        bool cityOccupantCanBeCombatReserve = cityOccupant != null &&
                                              cityOccupant.isPlayerOwned == city.isPlayerOwned &&
                                              cityOccupant.UnitTypeId != UnitRegistry.ScoutTypeId;
        bool canVacateWithCombatCover = AllThreatTilesCoveredByProjectedVision(threatTiles, visibleTiles, defenseTile, combatVisionRange: 1);

        plan.CanVacateCityTile = canVacateWithCombatCover;
        plan.IsAtRisk = city.stationedUnit != null || !canVacateWithCombatCover || visibleFastThreatNearCity;

        bool cityHasLocalCombatCover =
            cityOccupantCanBeCombatReserve ||
            HasAdjacentCombatUnit(gridManager, city, friendlyCombatUnits);

        if (cityOccupantCanBeCombatReserve && plan.CanVacateCityTile)
        {
            plan.AssignedCombatUnit = cityOccupant;
        }
        else
        {
            plan.AssignedCombatUnit = FindNearestUnitForPosition(
                friendlyCombatUnits,
                defenseTile.transform.position,
                city,
                unitsToSkip: cityOccupantCanBeCombatReserve ? new Unit[] { cityOccupant } : null);
        }

        bool shouldReserveProactiveScout =
            isPrimaryFriendlyCity &&
            !visibleFastThreatNearCity &&
            !allThreatTilesVisible &&
            cityHasLocalCombatCover;

        if ((!plan.CanVacateCityTile || shouldReserveProactiveScout) &&
            TryFindScoutScreeningTile(gridManager, city, defenseTile, threatTiles, visibleTiles, out TileVisibility scoutTile))
        {
            plan.PreferredScoutPosition = scoutTile.transform.position;
            plan.AssignedScoutUnit = FindNearestUnitForPosition(friendlyScouts, scoutTile.transform.position, city, null);
        }

        if (visibleFastThreatTilesNearCity.Count >= 2 &&
            TryFindSecondaryDefenseTile(
                gridManager,
                city,
                threatTiles,
                visibleFastThreatTilesNearCity,
                defenseTile,
                visibleTiles,
                aiHasPerfectInfo,
                enemyCityCoords,
                out TileVisibility supportTile))
        {
            List<Unit> supportUnitsToSkip = new List<Unit>();
            if (plan.AssignedCombatUnit != null)
            {
                supportUnitsToSkip.Add(plan.AssignedCombatUnit);
            }

            if (!plan.CanVacateCityTile && cityOccupantCanBeCombatReserve && cityOccupant != null)
            {
                supportUnitsToSkip.Add(cityOccupant);
            }

            plan.PreferredSupportCombatPosition = supportTile.transform.position;
            plan.AssignedSupportCombatUnit = FindNearestUnitForPosition(
                friendlyCombatUnits,
                supportTile.transform.position,
                city,
                supportUnitsToSkip);
        }

        return plan;
    }

    private static bool IsVisibleToSide(
        GridManager gridManager,
        Unit unit,
        HashSet<TileVisibility> visibleTiles,
        bool aiHasPerfectInfo)
    {
        if (aiHasPerfectInfo)
        {
            return true;
        }

        if (gridManager == null || visibleTiles == null || visibleTiles.Count == 0)
        {
            return false;
        }

        return gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile) &&
               visibleTiles.Contains(tile);
    }

    private static bool HasAdjacentCombatUnit(GridManager gridManager, City city, List<Unit> friendlyCombatUnits)
    {
        for (int i = 0; i < friendlyCombatUnits.Count; i++)
        {
            Unit unit = friendlyCombatUnits[i];
            if (unit == null || !gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility unitTile))
            {
                continue;
            }

            int dist = GetChebyshevDistance(unitTile.gridX, unitTile.gridY, city.x, city.y);
            if (dist == 1)
            {
                return true;
            }
        }

        return false;
    }

    private static bool AreAllTilesVisible(List<TileVisibility> tiles, HashSet<TileVisibility> visibleTiles)
    {
        if (visibleTiles == null)
        {
            return false;
        }

        for (int i = 0; i < tiles.Count; i++)
        {
            if (!visibleTiles.Contains(tiles[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AllThreatTilesCoveredByProjectedVision(
        List<TileVisibility> threatTiles,
        HashSet<TileVisibility> visibleTiles,
        TileVisibility combatTile,
        int combatVisionRange)
    {
        for (int i = 0; i < threatTiles.Count; i++)
        {
            TileVisibility threatTile = threatTiles[i];
            bool visibleNow = visibleTiles != null && visibleTiles.Contains(threatTile);
            bool visibleFromCombat = combatTile != null &&
                                     GetChebyshevDistance(combatTile.gridX, combatTile.gridY, threatTile.gridX, threatTile.gridY) <= combatVisionRange;
            if (!visibleNow && !visibleFromCombat)
            {
                return false;
            }
        }

        return true;
    }

    private static List<TileVisibility> GetThreatTiles(GridManager gridManager, City city, int distance)
    {
        List<TileVisibility> threatTiles = new List<TileVisibility>();
        for (int dx = -distance; dx <= distance; dx++)
        {
            for (int dy = -distance; dy <= distance; dy++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != distance)
                {
                    continue;
                }

                if (gridManager.TryGetTile(city.x + dx, city.y + dy, out TileVisibility tile) && tile != null)
                {
                    threatTiles.Add(tile);
                }
            }
        }

        return threatTiles;
    }

    private static bool TryFindBestDefenseTile(
        GridManager gridManager,
        City city,
        List<TileVisibility> threatTiles,
        HashSet<TileVisibility> visibleTiles,
        bool aiHasPerfectInfo,
        List<Vector2Int> enemyCityCoords,
        out TileVisibility bestTile)
    {
        bestTile = null;
        int bestScore = int.MinValue;
        float bestTieBreak = float.MaxValue;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                if (!gridManager.TryGetTile(city.x + dx, city.y + dy, out TileVisibility candidate) || candidate == null)
                {
                    continue;
                }

                Unit occupant = GridUtils.GetUnitAtPosition(candidate.transform.position);
                if (occupant != null && occupant.isPlayerOwned != city.isPlayerOwned)
                {
                    continue;
                }

                int score = 0;
                for (int i = 0; i < threatTiles.Count; i++)
                {
                    TileVisibility threatTile = threatTiles[i];
                    if (GetChebyshevDistance(candidate.gridX, candidate.gridY, threatTile.gridX, threatTile.gridY) > 1)
                    {
                        continue;
                    }

                    bool currentlyVisible = aiHasPerfectInfo || (visibleTiles != null && visibleTiles.Contains(threatTile));
                    score += currentlyVisible ? 1 : 3;
                }

                float tieBreak = GetApproachTieBreak(candidate, city, enemyCityCoords, gridManager);
                if (score > bestScore || (score == bestScore && tieBreak < bestTieBreak))
                {
                    bestScore = score;
                    bestTieBreak = tieBreak;
                    bestTile = candidate;
                }
            }
        }

        return bestTile != null;
    }

    private static bool TryFindScoutScreeningTile(
        GridManager gridManager,
        City city,
        TileVisibility defenseTile,
        List<TileVisibility> threatTiles,
        HashSet<TileVisibility> visibleTiles,
        out TileVisibility bestTile)
    {
        bestTile = null;
        if (defenseTile == null)
        {
            return false;
        }

        int bestScore = int.MinValue;
        for (int i = 0; i < threatTiles.Count; i++)
        {
            TileVisibility threatTile = threatTiles[i];
            if (visibleTiles != null && visibleTiles.Contains(threatTile))
            {
                continue;
            }

            if (GetChebyshevDistance(defenseTile.gridX, defenseTile.gridY, threatTile.gridX, threatTile.gridY) > 1)
            {
                continue;
            }

            Unit occupant = GridUtils.GetUnitAtPosition(threatTile.transform.position);
            if (occupant != null && occupant.isPlayerOwned == city.isPlayerOwned && occupant.UnitTypeId != UnitRegistry.ScoutTypeId)
            {
                continue;
            }

            int score = 0;
            for (int otherIndex = 0; otherIndex < threatTiles.Count; otherIndex++)
            {
                TileVisibility otherThreat = threatTiles[otherIndex];
                if (visibleTiles != null && visibleTiles.Contains(otherThreat))
                {
                    continue;
                }

                if (GetChebyshevDistance(threatTile.gridX, threatTile.gridY, otherThreat.gridX, otherThreat.gridY) <= 2)
                {
                    score++;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestTile = threatTile;
            }
        }

        return bestTile != null;
    }

    private static bool TryFindSecondaryDefenseTile(
        GridManager gridManager,
        City city,
        List<TileVisibility> threatTiles,
        List<TileVisibility> visibleFastThreatTilesNearCity,
        TileVisibility primaryDefenseTile,
        HashSet<TileVisibility> visibleTiles,
        bool aiHasPerfectInfo,
        List<Vector2Int> enemyCityCoords,
        out TileVisibility bestTile)
    {
        bestTile = null;
        if (gridManager == null || city == null || primaryDefenseTile == null || visibleFastThreatTilesNearCity == null || visibleFastThreatTilesNearCity.Count < 2)
        {
            return false;
        }

        int bestScore = int.MinValue;
        float bestTieBreak = float.MaxValue;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                if (!gridManager.TryGetTile(city.x + dx, city.y + dy, out TileVisibility candidate) ||
                    candidate == null ||
                    candidate == primaryDefenseTile)
                {
                    continue;
                }

                Unit occupant = GridUtils.GetUnitAtPosition(candidate.transform.position);
                if (occupant != null)
                {
                    if (occupant.isPlayerOwned != city.isPlayerOwned)
                    {
                        continue;
                    }

                    if (occupant.UnitTypeId == UnitRegistry.ScoutTypeId)
                    {
                        continue;
                    }
                }

                int score = 0;
                for (int i = 0; i < visibleFastThreatTilesNearCity.Count; i++)
                {
                    TileVisibility fastThreatTile = visibleFastThreatTilesNearCity[i];
                    if (GetChebyshevDistance(candidate.gridX, candidate.gridY, fastThreatTile.gridX, fastThreatTile.gridY) > 1)
                    {
                        continue;
                    }

                    bool primaryAlreadyCoversThreat =
                        GetChebyshevDistance(primaryDefenseTile.gridX, primaryDefenseTile.gridY, fastThreatTile.gridX, fastThreatTile.gridY) <= 1;
                    score += primaryAlreadyCoversThreat ? 2 : 8;
                }

                for (int i = 0; i < threatTiles.Count; i++)
                {
                    TileVisibility threatTile = threatTiles[i];
                    bool primaryCoversThreat =
                        GetChebyshevDistance(primaryDefenseTile.gridX, primaryDefenseTile.gridY, threatTile.gridX, threatTile.gridY) <= 1;
                    bool candidateCoversThreat =
                        GetChebyshevDistance(candidate.gridX, candidate.gridY, threatTile.gridX, threatTile.gridY) <= 1;

                    if (!candidateCoversThreat || primaryCoversThreat)
                    {
                        continue;
                    }

                    bool currentlyVisible = aiHasPerfectInfo || (visibleTiles != null && visibleTiles.Contains(threatTile));
                    score += currentlyVisible ? 1 : 2;
                }

                if (score <= 0)
                {
                    continue;
                }

                float tieBreak = GetApproachTieBreak(candidate, city, enemyCityCoords, gridManager);
                if (score > bestScore || (score == bestScore && tieBreak < bestTieBreak))
                {
                    bestScore = score;
                    bestTieBreak = tieBreak;
                    bestTile = candidate;
                }
            }
        }

        return bestTile != null;
    }

    private static Unit FindNearestUnitForPosition(
        List<Unit> candidateUnits,
        Vector3 targetPosition,
        City homeCity,
        ICollection<Unit> unitsToSkip)
    {
        Unit bestUnit = null;
        float bestScore = float.MaxValue;

        for (int i = 0; i < candidateUnits.Count; i++)
        {
            Unit unit = candidateUnits[i];
            if (unit == null)
            {
                continue;
            }

            if (unitsToSkip != null && unitsToSkip.Contains(unit))
            {
                continue;
            }

            float score = (unit.transform.position - targetPosition).sqrMagnitude;
            if (homeCity != null && GridUtils.GetCityAtPosition(unit.transform.position) == homeCity)
            {
                score -= 0.25f;
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestUnit = unit;
            }
        }

        return bestUnit;
    }

    private static float GetApproachTieBreak(
        TileVisibility candidate,
        City city,
        List<Vector2Int> enemyCityCoords,
        GridManager gridManager)
    {
        float bestDistance = float.MaxValue;
        for (int i = 0; i < enemyCityCoords.Count; i++)
        {
            Vector2Int enemyCity = enemyCityCoords[i];
            float distance = GetChebyshevDistance(candidate.gridX, candidate.gridY, enemyCity.x, enemyCity.y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
            }
        }

        if (bestDistance < float.MaxValue)
        {
            return bestDistance;
        }

        float centerX = (gridManager.width - 1) * 0.5f;
        float centerY = (gridManager.height - 1) * 0.5f;
        return Mathf.Abs(candidate.gridX - centerX) + Mathf.Abs(candidate.gridY - centerY);
    }

    private static int GetChebyshevDistance(int ax, int ay, int bx, int by)
    {
        return Mathf.Max(Mathf.Abs(ax - bx), Mathf.Abs(ay - by));
    }
}
