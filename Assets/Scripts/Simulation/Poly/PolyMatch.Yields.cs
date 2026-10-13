using System.Linq;

namespace BlockNations.Simulation.Poly
{
    public sealed partial class PolyMatch
    {
        private static int AdjacentCount(PolyTile[] map, int p, PolyImprovement improvement) => Enumerable.Range(0, 121).Count(n =>
            n != p && Distance(n, p) == 1 && map[n]?.cityId == map[p].cityId && map[n]?.improvement == improvement);
        private static PolyYield TileYield(PolyTile[] map, int p)
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
        private static PolyYield CityTileYield(int cityId, PolyTile[] map)
        {
            int food = 0, gold = 0;
            for (int p = 0; p < 121; p++) if (map[p]?.cityId == cityId) { var yield = TileYield(map, p); food += yield.Food; gold += yield.Gold; }
            return new PolyYield(food, gold);
        }
        public int CityFood(int cityId) => CityTileYield(cityId, Tiles).Food;
        public int CityIncome(int cityId)
        {
            var city = City(cityId);
            return city == null || city.ownerSeatIndex < 0 ? 0 : city.Income + CityTileYield(cityId, Tiles).Gold;
        }
        public PolyYield KnownYield(int seat, int position)
        {
            // Neighbour bonuses use remembered improvements, never hidden live tiles.
            if (memory[seat][position] == null) return new PolyYield();
            var city = CityAt(position);
            return city?.ownerSeatIndex == seat ? new PolyYield(0, CityIncome(city.id)) : TileYield(memory[seat], position);
        }
        private void GrowCities(int seat)
        {
            foreach (var city in Cities.Where(c => c.ownerSeatIndex == seat && c.level < 100)) {
                city.food += CityFood(city.id);
                while (city.level < 100 && city.food >= city.FoodNeeded) { city.food -= city.FoodNeeded; AddPopulation(city, 1); }
                if (city.level == 100) city.food = System.Math.Min(city.food, city.FoodNeeded - 1);
            }
        }
    }
}
