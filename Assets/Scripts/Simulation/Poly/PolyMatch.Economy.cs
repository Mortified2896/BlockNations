using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockNations.Simulation.Poly
{
    public sealed partial class PolyMatch
    {
        private static int BasePopulation(string choice) => choice == "farm" || choice == "mine" ? 2 : choice == "road" ? 0 : 1;
        public IEnumerable<PolyAction> DevelopActions(int seat, int p)
        {
            var tile = Tiles[p];
            if (City(tile.cityId)?.ownerSeatIndex != seat || CityAt(p) != null) yield break;
            var choices = new List<Tuple<string, string, int>>();
            if (tile.improvement == PolyImprovement.None) {
                if (tile.resource == PolyResource.Fruit && HasTechnology(seat, "organization")) choices.Add(Tuple.Create("fruit", "Harvest fruit", 2));
                if (tile.resource == PolyResource.Animals && HasTechnology(seat, "hunting")) choices.Add(Tuple.Create("hunt", "Hunt animals", 2));
                if (tile.resource == PolyResource.Crops && HasTechnology(seat, "farming")) choices.Add(Tuple.Create("farm", "Build farm", 5));
                if (tile.resource == PolyResource.Ore && HasTechnology(seat, "mining")) choices.Add(Tuple.Create("mine", "Build mine", 5));
                if (tile.terrain == PolyTerrain.Forest && HasTechnology(seat, "forestry")) choices.Add(Tuple.Create("lumber", "Build lumber hut", 3));
                if (tile.terrain == PolyTerrain.Plains && tile.resource == PolyResource.None) {
                    if (HasTechnology(seat, "construction") && !HasBuilding(tile.cityId, PolyImprovement.Windmill)) choices.Add(Tuple.Create("windmill", "Build windmill", 5));
                    if (HasTechnology(seat, "mathematics") && !HasBuilding(tile.cityId, PolyImprovement.Sawmill)) choices.Add(Tuple.Create("sawmill", "Build sawmill", 5));
                    if (HasTechnology(seat, "smithing") && !HasBuilding(tile.cityId, PolyImprovement.Forge)) choices.Add(Tuple.Create("forge", "Build forge", 5));
                }
            }
            if (!tile.road && HasTechnology(seat, "roads")) choices.Add(Tuple.Create("road", "Build road", 2));
            foreach (var choice in choices) {
                int gain = choice.Item1 == "windmill" ? AdjacentCount(p, PolyImprovement.Farm) * 2 :
                    choice.Item1 == "sawmill" ? AdjacentCount(p, PolyImprovement.LumberHut) * 2 :
                    choice.Item1 == "forge" ? AdjacentCount(p, PolyImprovement.Mine) * 2 : BasePopulation(choice.Item1);
                if (choice.Item1 == "farm" && AdjacentCount(p, PolyImprovement.Windmill) > 0 || choice.Item1 == "mine" && AdjacentCount(p, PolyImprovement.Forge) > 0 ||
                    choice.Item1 == "lumber" && AdjacentCount(p, PolyImprovement.Sawmill) > 0) gain += 2;
                if (gain > 0 || choice.Item1 == "road") yield return new PolyAction(PolyActionKind.Develop, seat, tile.cityId, p, choice.Item1) { Cost = choice.Item3, Population = gain, Label = choice.Item2 };
            }
        }
        private bool HasBuilding(int cityId, PolyImprovement type) => Tiles.Any(t => t.cityId == cityId && t.improvement == type);
        private int AdjacentCount(int p, PolyImprovement type) => Enumerable.Range(0, 121).Count(n => n != p && Distance(n, p) == 1 && Tiles[n].cityId == Tiles[p].cityId && Tiles[n].improvement == type);
        private void Develop(PolyAction action)
        {
            var tile = Tiles[action.Destination];
            switch (action.Choice) {
                case "fruit": case "hunt": tile.resource = PolyResource.None; break;
                case "farm": tile.resource = PolyResource.None; tile.improvement = PolyImprovement.Farm; break;
                case "mine": tile.resource = PolyResource.None; tile.improvement = PolyImprovement.Mine; break;
                case "lumber": tile.resource = PolyResource.None; tile.improvement = PolyImprovement.LumberHut; break;
                case "windmill": tile.improvement = PolyImprovement.Windmill; break;
                case "sawmill": tile.improvement = PolyImprovement.Sawmill; break;
                case "forge": tile.improvement = PolyImprovement.Forge; break;
                case "road": tile.road = true; break;
            }
            AddPopulation(City(action.ActorId), action.Population);
        }
        private void AddPopulation(PolyCity city, int amount)
        {
            city.population += amount;
            while (city.population >= city.PopulationNeeded) { city.population -= city.PopulationNeeded; city.level++; city.pendingRewards++; }
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
