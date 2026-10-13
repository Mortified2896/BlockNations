using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockNations.Simulation.Poly
{
    public sealed partial class PolyMatch
    {
        private sealed class Development
        {
            public readonly string Choice, Label, Technology;
            public readonly int Cost;
            public readonly PolyImprovement Improvement;
            public Development(string choice, string label, string technology, int cost, PolyImprovement improvement)
            { Choice = choice; Label = label; Technology = technology; Cost = cost; Improvement = improvement; }
        }
        private static readonly Development[] Developments = {
            new Development("fruit", "Build orchard", "organization", 2, PolyImprovement.Orchard),
            new Development("hunt", "Build hunting hut", "hunting", 2, PolyImprovement.HuntingHut),
            new Development("farm", "Build farm", "farming", 5, PolyImprovement.Farm),
            new Development("mine", "Build mine", "mining", 5, PolyImprovement.Mine),
            new Development("lumber", "Build lumber hut", "forestry", 3, PolyImprovement.LumberHut),
            new Development("windmill", "Build windmill", "construction", 5, PolyImprovement.Windmill),
            new Development("sawmill", "Build sawmill", "mathematics", 5, PolyImprovement.Sawmill),
            new Development("forge", "Build forge", "smithing", 5, PolyImprovement.Forge),
            new Development("road", "Build road", "roads", 2, PolyImprovement.None)
        };
        // Includes locked development and its complete prerequisite bill. The legal
        // action is atomic, so an unaffordable or stale request cannot partially spend Gold.
        public IEnumerable<PolyAction> DevelopmentOptions(int seat, int p)
        {
            if (seat < 0 || seat > 1 || p < 0 || p >= 121) yield break;
            var tile = Tiles[p];
            if (City(tile.cityId)?.ownerSeatIndex != seat || CityAt(p) != null || !Visibility(seat)[p] ||
                Board.UnitAt(p) != null && Board.UnitAt(p).Seat != seat) yield break;
            foreach (var d in Developments) {
                if (d.Choice == "road") { if (tile.road) continue; }
                else {
                    if (tile.improvement != PolyImprovement.None) continue;
                    bool suitable = d.Choice == "fruit" ? tile.resource == PolyResource.Fruit :
                        d.Choice == "hunt" ? tile.resource == PolyResource.Animals :
                        d.Choice == "farm" ? tile.resource == PolyResource.Crops :
                        d.Choice == "mine" ? tile.resource == PolyResource.Ore :
                        d.Choice == "lumber" ? tile.terrain == PolyTerrain.Forest :
                        tile.terrain == PolyTerrain.Plains && !HasBuilding(tile.cityId, d.Improvement);
                    if (!suitable) continue;
                }
                var nextTile = tile.Copy();
                if (d.Choice == "road") nextTile.road = true; else nextTile.improvement = d.Improvement;
                var nextMap = (PolyTile[])Tiles.Clone(); nextMap[p] = nextTile;
                var before = CityTileYield(tile.cityId, Tiles); var after = CityTileYield(tile.cityId, nextMap);
                int food = after.Food - before.Food, gold = after.Gold - before.Gold;
                if (food == 0 && gold == 0 && d.Choice != "road") continue;
                var missing = MissingTechnologies(seat, d.Technology);
                int technologyCost = missing.Sum(id => ResearchCost(seat, id));
                yield return new PolyAction(missing.Length == 0 ? PolyActionKind.Develop : PolyActionKind.ResearchAndDevelop, seat, tile.cityId, p, d.Choice) {
                    Cost = d.Cost + technologyCost, BuildCost = d.Cost, TechnologyCost = technologyCost,
                    Technologies = missing, Food = food, GoldYield = gold, Label = d.Label
                };
            }
        }
        public IEnumerable<PolyAction> DevelopActions(int seat, int p) => DevelopmentOptions(seat, p).Where(a => a.Kind == PolyActionKind.Develop);
        public string[] MissingTechnologies(int seat, string id)
        {
            var result = new List<string>();
            while (id != null && !HasTechnology(seat, id)) {
                var tech = PolyRules.Technology(id); if (tech == null) break;
                result.Add(id); id = tech.Parent;
            }
            result.Reverse(); return result.ToArray();
        }
        private bool HasBuilding(int cityId, PolyImprovement type) => Tiles.Any(t => t.cityId == cityId && t.improvement == type);
        private void Develop(PolyAction action)
        {
            var d = Developments.First(x => x.Choice == action.Choice);
            if (action.Choice == "road") Tiles[action.Destination].road = true;
            else Tiles[action.Destination].improvement = d.Improvement;
        }
        private void AddPopulation(PolyCity city, int amount)
        {
            city.population += amount;
            while (city.level < 100 && city.population >= city.PopulationNeeded) { city.population -= city.PopulationNeeded; city.level++; city.pendingRewards++; }
            if (city.level == 100) city.population = Math.Min(city.population, city.PopulationNeeded - 1);
        }
        private int GiantPosition(PolyCity city)
        {
            if (Board.UnitAt(city.position) == null) return city.position;
            return Enumerable.Range(0, 121).Where(p => Tiles[p].cityId == city.id && Board.UnitAt(p) == null &&
                (Tiles[p].terrain != PolyTerrain.Mountain || HasTechnology(city.ownerSeatIndex, "climbing"))).OrderBy(p => Distance(p, city.position)).ThenBy(p => p).DefaultIfEmpty(-1).First();
        }
        public static string RewardLabel(string choice)
        {
            switch (choice) {
                case "workshop": return "Workshop: +1 Gold every turn";
                case "walls": return "Fortifications: stronger city defense";
                case "gold": return "Treasury: +5 Gold now";
                case "population": return "Settlement: +2 population";
                case "giant": return "Giant: a free powerful unit";
                default: return "Prosperity: +10 Gold now";
            }
        }
        private void Reward(PolyAction action)
        {
            var city = City(action.ActorId); city.pendingRewards--;
            switch (action.Choice) {
                case "workshop": city.workshops++; break; case "walls": city.walls++; break;
                case "population": AddPopulation(city, 2); break;
                case "giant": Spawn(action.SeatIndex, GiantPosition(city), "poly-giant", city.id); break;
                case "gold": Board.SetGold(action.SeatIndex, Board.GoldForSeat(action.SeatIndex) + 5); break;
                case "treasury": Board.SetGold(action.SeatIndex, Board.GoldForSeat(action.SeatIndex) + 10); break;
            }
        }
    }
}
