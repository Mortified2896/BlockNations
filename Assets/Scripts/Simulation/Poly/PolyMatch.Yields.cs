using System;
using System.Linq;

namespace BlockNations.Simulation.Poly
{
    public sealed partial class PolyMatch
    {
        private readonly int[] goldRemainders = new int[2];
        private PolyConnection Connect(int owner, int position, int viewer = -1)
        {
            if (owner < 0) return new PolyConnection();
            var city = Cities.Where(c => (viewer < 0 || owner == viewer) ? c.ownerSeatIndex == owner : knownOwners[viewer][c.position] == owner)
                .OrderBy(c => Distance(position, c.position)).ThenBy(c => c.id).FirstOrDefault();
            return city == null ? new PolyConnection() : new PolyConnection(city.id, Distance(position, city.position));
        }
        public PolyConnection Connection(int position) => Connect(Tiles[position].improvementOwnerSeatIndex >= 0 ? Tiles[position].improvementOwnerSeatIndex : Tiles[position].roadOwnerSeatIndex, position);
        public PolyConnection KnownConnection(int seat, int position) => Connect(memory[seat][position]?.improvementOwnerSeatIndex >= 0 ? memory[seat][position].improvementOwnerSeatIndex : memory[seat][position]?.roadOwnerSeatIndex ?? -1, position, seat);
        public PolyConnection ConstructionConnection(int seat, int position) => Connect(seat, position);
        private static int AdjacentCount(PolyTile[] map, int p, PolyImprovement improvement) => Enumerable.Range(0, 121).Count(n =>
            n != p && Distance(n, p) == 1 && map[n]?.improvementOwnerSeatIndex == map[p].improvementOwnerSeatIndex && map[n]?.improvement == improvement);
        private static PolyYield BaseTileYield(PolyTile[] map, int p)
        {
            var tile = map[p]; if (tile == null) return new PolyYield();
            switch (tile.improvement) {
                case PolyImprovement.Orchard: return new PolyYield(1, 0);
                case PolyImprovement.HuntingHut: return new PolyYield(2, 0);
                case PolyImprovement.Farm: return new PolyYield(3, 0);
                case PolyImprovement.LumberHut: return new PolyYield(0, 1);
                case PolyImprovement.Mine: return new PolyYield(0, 2);
                case PolyImprovement.Windmill: return new PolyYield(AdjacentCount(map, p, PolyImprovement.Farm) * 2, 0);
                case PolyImprovement.Sawmill: return new PolyYield(0, AdjacentCount(map, p, PolyImprovement.LumberHut));
                case PolyImprovement.Forge: return new PolyYield(0, AdjacentCount(map, p, PolyImprovement.Mine) * 2);
                default: return new PolyYield();
            }
        }
        private bool Blocked(int owner, int position) => Board.UnitAt(position) is SimulationUnit u && Board.Relationships.IsHostile(owner, u.Seat);
        private PolyYield TileYield(PolyTile[] map, int p, int viewer = -1)
        {
            var tile = map[p]; if (tile == null || tile.improvementOwnerSeatIndex < 0) return new PolyYield();
            var connection = Connect(tile.improvementOwnerSeatIndex, p, viewer);
            if (connection.CityId == 0 || (viewer < 0 || Visibility(viewer)[p]) && Blocked(tile.improvementOwnerSeatIndex, p)) return new PolyYield();
            var baseYield = BaseTileYield(map, p); int distance = Math.Max(1, connection.Distance);
            return PolyYield.FromUnits(baseYield.FoodUnits / distance, baseYield.GoldUnits / distance);
        }
        private PolyYield CityTileYield(int cityId, PolyTile[] map)
        {
            int food = 0, gold = 0;
            for (int p = 0; p < 121; p++) if (Connect(map[p]?.improvementOwnerSeatIndex ?? -1, p).CityId == cityId) {
                var yield = TileYield(map, p); food += yield.FoodUnits; gold += yield.GoldUnits;
            }
            return PolyYield.FromUnits(food, gold);
        }
        private PolyYield SeatTileYield(int seat, PolyTile[] map)
        {
            int food = 0, gold = 0;
            foreach (var city in Cities.Where(c => c.ownerSeatIndex == seat)) {
                var yield = CityTileYield(city.id, map); food += yield.FoodUnits; gold += yield.GoldUnits;
            }
            return PolyYield.FromUnits(food, gold);
        }
        public decimal CityFood(int cityId) => CityTileYield(cityId, Tiles).Food;
        public decimal CityIncome(int cityId)
        {
            var city = City(cityId);
            return city == null || city.ownerSeatIndex < 0 ? 0 : city.Income + CityTileYield(cityId, Tiles).Gold;
        }
        public PolyYield KnownYield(int seat, int position)
        {
            // Neighbour bonuses and enemy connections use remembered observations only.
            if (memory[seat][position] == null) return new PolyYield();
            var city = CityAt(position);
            return city?.ownerSeatIndex == seat ? PolyYield.FromUnits(0, city.Income * PolyRules.YieldScale + CityTileYield(city.id, Tiles).GoldUnits) : TileYield(memory[seat], position, seat);
        }
        private void GrowCities(int seat)
        {
            foreach (var city in Cities.Where(c => c.ownerSeatIndex == seat && c.level < 100)) {
                int food = city.food * PolyRules.YieldScale + city.foodRemainder + CityTileYield(city.id, Tiles).FoodUnits;
                while (city.level < 100 && food >= city.FoodNeeded * PolyRules.YieldScale) {
                    food -= city.FoodNeeded * PolyRules.YieldScale; AddPopulation(city, 1);
                }
                if (city.level == 100) food = Math.Min(food, city.FoodNeeded * PolyRules.YieldScale - 1);
                city.food = food / PolyRules.YieldScale; city.foodRemainder = food % PolyRules.YieldScale;
            }
        }
        private void CollectGold(int seat)
        {
            int units = goldRemainders[seat] + Cities.Where(c => c.ownerSeatIndex == seat).Sum(c => c.Income * PolyRules.YieldScale) + SeatTileYield(seat, Tiles).GoldUnits;
            Board.SetGold(seat, checked(Board.GoldForSeat(seat) + units / PolyRules.YieldScale));
            goldRemainders[seat] = units % PolyRules.YieldScale;
        }
    }
}
