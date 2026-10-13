using System;
using System.Linq;

namespace BlockNations.Simulation.Poly
{
    public sealed partial class PolyMatch
    {
        private static PolyCity CopyCity(PolyCity c) => new PolyCity { id = c.id, position = c.position, ownerSeatIndex = c.ownerSeatIndex, level = c.level,
            population = c.population, food = c.food, foodRemainder = c.foodRemainder, workshops = c.workshops, walls = c.walls, pendingRewards = c.pendingRewards, recruited = c.recruited };
        public PolySave CreateSave()
        {
            return new PolySave {
                seed = Seed, currentTurnSeatIndex = currentTurnSeatIndex, round = Board.Round, gameOver = Board.GameOver, winnerSeatIndex = Board.WinnerSeat,
                tiles = Tiles.Select(t => t.Copy()).ToArray(), cities = Cities.Select(CopyCity).ToArray(),
                units = Board.Units.OrderBy(u => u.Id).Select(u => { var s = UnitState(u.Id); return new PolyUnitSave {
                    id = u.Id, ownerSeatIndex = u.Seat, position = u.Position, type = u.Definition.TypeId, health = u.Health,
                    homeCityId = s.homeCityId, kills = s.kills, moved = s.moved, attacked = s.attacked, rested = s.rested, retreatUsed = s.retreatUsed, attackStarted = s.attackStarted }; }).ToArray(),
                seats = Enumerable.Range(0, 2).Select(seat => new PolySeatSave { gold = Board.GoldForSeat(seat), goldRemainder = goldRemainders[seat], technologies = technologies[seat].OrderBy(t => t, StringComparer.Ordinal).ToArray(),
                    memory = memory[seat].Select(t => t?.Copy() ?? new PolyTile()).ToArray(), explored = memory[seat].Select(t => t != null).ToArray(), cityOwners = (int[])knownOwners[seat].Clone(), cityLevels = (int[])knownLevels[seat].Clone() }).ToArray()
            };
        }
        public static PolyMatch Restore(PolySave save)
        {
            void Check(bool condition) { if (!condition) throw new ArgumentException("Unsupported or invalid Poly save. The original file was preserved."); }
            Check(save != null && save.mode == "Poly" && (save.rulesVersion == PolyRules.Version || save.rulesVersion == PolyRules.PreviousVersion || save.rulesVersion == PolyRules.LegacyVersion));
            Check(save.tiles?.Length == 121 && save.cities?.Length == 7 && save.units != null && save.units.Length <= 121 && save.seats?.Length == 2);
            Check(save.currentTurnSeatIndex >= 0 && save.currentTurnSeatIndex < 2 && save.round > 0 && save.round <= 100000 &&
                (save.gameOver ? save.winnerSeatIndex >= 0 && save.winnerSeatIndex < 2 : save.winnerSeatIndex == -1));
            Check(save.cities.All(c => c != null && c.id > 0 && c.position >= 0 && c.position < 121 && c.ownerSeatIndex >= -1 && c.ownerSeatIndex < 2 &&
                c.level >= 1 && c.level <= 100 && c.population >= 0 && c.population < c.PopulationNeeded && c.workshops >= 0 && c.workshops <= c.level &&
                c.walls >= 0 && c.walls <= c.level && c.pendingRewards >= 0 && c.pendingRewards < c.level && c.food >= 0 && c.food < c.FoodNeeded && c.foodRemainder >= 0 && c.foodRemainder < PolyRules.YieldScale));
            Check(save.cities.Select(c => c.id).Distinct().Count() == 7 && save.cities.Select(c => c.position).Distinct().Count() == 7);
            bool ValidTile(PolyTile t) => t != null && Enum.IsDefined(typeof(PolyTerrain), t.terrain) && Enum.IsDefined(typeof(PolyResource), t.resource) &&
                Enum.IsDefined(typeof(PolyImprovement), t.improvement) && (t.cityId == 0 || save.cities.Any(c => c.id == t.cityId)) &&
                (save.rulesVersion != PolyRules.Version || t.improvementOwnerSeatIndex >= -1 && t.improvementOwnerSeatIndex < 2 &&
                    t.roadOwnerSeatIndex >= -1 && t.roadOwnerSeatIndex < 2 && (t.improvement != PolyImprovement.None || t.improvementOwnerSeatIndex == -1) && (t.road || t.roadOwnerSeatIndex == -1));
            Check(save.tiles.All(ValidTile));
            foreach (var city in save.cities) for (int p = 0; p < 121; p++) Check((save.tiles[p].cityId == city.id) == (Distance(p, city.position) <= 1));
            var result = new PolyMatch { Seed = save.seed, Tiles = save.tiles.Select(t => t.Copy()).ToArray(), Cities = save.cities.Select(CopyCity).ToList(),
                Board = new MatchState(11, 11, 2, PolyRules.Units, save.currentTurnSeatIndex, save.round, incomePerCity: 0) };
            foreach (var city in result.Cities) if (city.ownerSeatIndex >= 0) result.Board.AddCity(new SimulationCity(city.id, city.ownerSeatIndex, city.position));
            for (int seat = 0; seat < 2; seat++) {
                var s = save.seats[seat];
                Check(s != null && s.gold >= 0 && s.gold <= 10000000 && s.goldRemainder >= 0 && s.goldRemainder < PolyRules.YieldScale && s.technologies != null && s.technologies.Length <= PolyRules.Technologies.Length &&
                    s.memory?.Length == 121 && s.explored?.Length == 121 && s.cityOwners?.Length == 121 && s.cityLevels?.Length == 121);
                Check(s.technologies.Distinct().Count() == s.technologies.Length && s.technologies.All(id => PolyRules.Technology(id) != null &&
                    (PolyRules.Technology(id).Parent == null || s.technologies.Contains(PolyRules.Technology(id).Parent))));
                Check(s.memory.All(t => t == null || ValidTile(t)) && s.cityOwners.All(o => o >= -2 && o < 2) && s.cityLevels.All(l => l >= 0 && l <= 100));
                result.Board.SetGold(seat, s.gold); result.goldRemainders[seat] = s.goldRemainder; foreach (var id in s.technologies) result.technologies[seat].Add(id);
                for (int p = 0; p < 121; p++) result.memory[seat][p] = s.explored[p] ? s.memory[p]?.Copy() : null;
                Array.Copy(s.cityOwners, result.knownOwners[seat], 121); Array.Copy(s.cityLevels, result.knownLevels[seat], 121);
            }
            Check(save.units.All(u => u != null && u.id > 0 && u.id < int.MaxValue && u.ownerSeatIndex >= 0 && u.ownerSeatIndex < 2 && u.position >= 0 && u.position < 121 &&
                PolyRules.Definition(u.type) != null && u.health > 0 && u.health <= PolyRules.Definition(u.type).MaxHealthUnits &&
                result.City(u.homeCityId) != null && u.kills >= 0));
            Check(save.units.Select(u => u.id).Distinct().Count() == save.units.Length && save.units.Select(u => u.position).Distinct().Count() == save.units.Length);
            foreach (var u in save.units) {
                result.Board.AddUnit(new SimulationUnit(u.id, u.ownerSeatIndex, u.position, PolyRules.Definition(u.type), u.health));
                result.unitState.Add(u.id, new PolyUnitSave { id = u.id, homeCityId = u.homeCityId, kills = u.kills, moved = u.moved, attacked = u.attacked, rested = u.rested, retreatUsed = u.retreatUsed, attackStarted = u.attackStarted });
            }
            result.Board.RestoreOutcome(save.gameOver, save.winnerSeatIndex);
            if (save.rulesVersion == PolyRules.LegacyVersion) result.MigrateLegacyImprovements();
            if (save.rulesVersion != PolyRules.Version) result.MigrateImprovementOwnership();
            result.UpdateMemory(); return result;
        }
        private void MigrateImprovementOwnership()
        {
            for (int p = 0; p < 121; p++) {
                var city = City(Tiles[p].cityId);
                Tiles[p].improvementOwnerSeatIndex = Tiles[p].improvement == PolyImprovement.None ? -1 : city?.ownerSeatIndex ?? -1;
                Tiles[p].roadOwnerSeatIndex = !Tiles[p].road ? -1 : city?.ownerSeatIndex ?? -1;
                for (int seat = 0; seat < 2; seat++) {
                    var tile = memory[seat][p]; if (tile == null) continue;
                    var knownCity = City(tile.cityId);
                    int owner = knownCity == null ? -1 : Math.Max(-1, knownOwners[seat][knownCity.position]);
                    tile.improvementOwnerSeatIndex = tile.improvement == PolyImprovement.None ? -1 : owner;
                    tile.roadOwnerSeatIndex = !tile.road ? -1 : owner;
                }
            }
        }
        private void MigrateLegacyImprovements()
        {
            var original = new PolyMatch(Seed).Tiles;
            void Migrate(PolyTile tile, int p)
            {
                if (tile == null || CityAt(p) != null) return;
                if (tile.improvement == PolyImprovement.None && tile.resource == PolyResource.None) {
                    if (original[p].resource == PolyResource.Fruit) tile.improvement = PolyImprovement.Orchard;
                    else if (original[p].resource == PolyResource.Animals) tile.improvement = PolyImprovement.HuntingHut;
                }
                if (tile.resource != PolyResource.None) return;
                if (tile.improvement == PolyImprovement.Farm) tile.resource = PolyResource.Crops;
                else if (tile.improvement == PolyImprovement.Mine) tile.resource = PolyResource.Ore;
                else if (tile.improvement == PolyImprovement.Orchard) tile.resource = PolyResource.Fruit;
                else if (tile.improvement == PolyImprovement.HuntingHut || tile.improvement == PolyImprovement.LumberHut) tile.resource = original[p].resource;
            }
            for (int p = 0; p < 121; p++) {
                Migrate(Tiles[p], p);
                for (int seat = 0; seat < 2; seat++) Migrate(memory[seat][p], p);
            }
        }
    }
}
