using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockNations.Simulation.Poly
{
    // A policy receives this seat observation, never PolyMatch or authoritative hidden units.
    public sealed class PolyObservation
    {
        public int SeatIndex, Gold, Income, Round;
        public PolyTile[] KnownTiles;
        public int[] KnownCityOwners;
        public SimulationUnit[] Units;
        public PolyCity[] OwnCities;
        public List<PolyAction> Actions;
        public static PolyObservation ForSeat(PolyMatch match, int seatIndex)
        {
            var visible = match.Visibility(seatIndex);
            return new PolyObservation { SeatIndex = seatIndex, Gold = match.Board.GoldForSeat(seatIndex), Income = match.Income(seatIndex), Round = match.Board.Round,
                KnownTiles = Enumerable.Range(0, 121).Select(p => match.KnownTile(seatIndex, p)).ToArray(),
                KnownCityOwners = Enumerable.Range(0, 121).Select(p => match.KnownCityOwner(seatIndex, p)).ToArray(),
                Units = match.Board.Units.Where(u => u.Seat == seatIndex || visible[u.Position]).Select(u => u.Copy()).OrderBy(u => u.Id).ToArray(),
                OwnCities = match.Cities.Where(c => c.ownerSeatIndex == seatIndex).Select(c => new PolyCity { id = c.id, position = c.position, ownerSeatIndex = seatIndex,
                    level = c.level, population = c.population, food = c.food, workshops = c.workshops, walls = c.walls, pendingRewards = c.pendingRewards }).ToArray(), Actions = match.LegalActions(seatIndex) };
        }
    }
    public static class PolyOpponent
    {
        public static PolyAction Choose(PolyObservation o)
        {
            return o.Actions.Select((a, index) => new { Action = a, Score = Score(o, a), Index = index }).OrderByDescending(a => a.Score).ThenBy(a => a.Index).FirstOrDefault()?.Action;
        }
        private static double Score(PolyObservation o, PolyAction a)
        {
            var own = o.Units.Where(u => u.Seat == o.SeatIndex).ToArray();
            var actor = o.Units.FirstOrDefault(u => u.Id == a.ActorId);
            switch (a.Kind) {
                case PolyActionKind.Capture: return 1000;
                case PolyActionKind.Reward: return a.Choice == "giant" ? 300 : a.Choice == "workshop" ? 250 : a.Choice == "population" ? 200 : 150;
                case PolyActionKind.Attack:
                    var target = o.Units.First(u => u.Position == a.Destination);
                    return 30 + a.Damage * 0.7 - a.Retaliation * 0.8 + (a.Damage >= target.Health ? 80 : 0) - (a.Retaliation >= actor.Health ? 80 : 0);
                case PolyActionKind.Heal: return actor.Health < actor.Definition.MaxHealthUnits * 0.6 ? 70 : 4;
                case PolyActionKind.Develop:
                case PolyActionKind.ResearchAndDevelop: return a.Food == 0 && a.GoldYield == 0 ? -1 : 35 + a.Food * 6 + a.GoldYield * 12 - a.Cost;
                case PolyActionKind.Recruit:
                    int wanted = Math.Min(14, o.OwnCities.Length * 2 + o.Round / 5);
                    if (own.Length >= wanted) return -2;
                    return 45 + (a.Choice == "poly-rider" ? 5 : a.Choice == "poly-swordsman" ? 12 : 0) - a.Cost +
                        (a.Choice == "poly-archer" && !own.Any(u => u.Definition.TypeId == a.Choice) ? 8 : 0);
                case PolyActionKind.Research:
                    string[] economic = { "organization", "hunting", "forestry", "farming", "climbing", "mining" };
                    return economic.Contains(a.Choice) ? 25 + EconomicPotential(o, a.Choice) * 4 : own.Length > 0 ? 15 : -1;
                case PolyActionKind.Move:
                    int[] goals = Enumerable.Range(0, 121).Where(p => o.KnownCityOwners[p] >= -1 && o.KnownCityOwners[p] != o.SeatIndex).ToArray();
                    int before = goals.Length > 0 ? goals.Min(p => PolyMatch.Distance(actor.Position, p)) : 0;
                    int after = goals.Length > 0 ? goals.Min(p => PolyMatch.Distance(a.Destination, p)) : 0;
                    int discovery = Enumerable.Range(0, 121).Count(p => PolyMatch.Distance(p, a.Destination) <= actor.Definition.VisionRange && o.KnownTiles[p] == null);
                    double risk = o.Units.Where(u => u.Seat != o.SeatIndex && PolyMatch.Distance(u.Position, a.Destination) <= u.Definition.AttackRange).Sum(u => u.Definition.AttackUnits / 10.0);
                    return 8 + (before - after) * 15 + discovery * 2 - risk * 2 + (goals.Contains(a.Destination) ? 50 : 0);
                default: return 0;
            }
        }
        private static int EconomicPotential(PolyObservation o, string tech)
        {
            var owned = new HashSet<int>(o.OwnCities.Select(c => c.id));
            return o.KnownTiles.Count(t => t != null && owned.Contains(t.cityId) && t.improvement == PolyImprovement.None &&
                (tech == "organization" && t.resource == PolyResource.Fruit || tech == "hunting" && t.resource == PolyResource.Animals ||
                 tech == "farming" && t.resource == PolyResource.Crops || tech == "forestry" && t.terrain == PolyTerrain.Forest ||
                 tech == "mining" && t.resource == PolyResource.Ore || tech == "climbing" && t.terrain == PolyTerrain.Mountain));
        }
    }
}
