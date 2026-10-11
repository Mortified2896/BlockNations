using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockNations.Simulation
{
    // Opt-in in-memory playtest. Product saves, policy inputs and standard matches do not instantiate this.
    public sealed class AdvancedMatch
    {
        private readonly Dictionary<int, AdvancedCityEconomy> cities = new Dictionary<int, AdvancedCityEconomy>();
        private readonly Dictionary<int, AdvancedSoldier> soldiers = new Dictionary<int, AdvancedSoldier>();
        public MatchState Board { get; }
        public AdvancedEconomyRules Rules { get; }
        public AdvancedTurnReport LastTurnReport { get; private set; }
        public IEnumerable<AdvancedCityEconomy> Cities => cities.Values;
        public IEnumerable<AdvancedSoldier> Soldiers => soldiers.Values;

        public AdvancedMatch(RecruitmentBalance balance = RecruitmentBalance.Equal, int firstSeat = 0)
        {
            Rules = new AdvancedEconomyRules(balance);
            Board = new MatchState(7, 7, 2, MatchRuleProfile.CoreThree.RecruitableUnits,
                firstSeat, incomePerCity: 0, firstSeat: firstSeat, ruleProfile: MatchRuleProfile.CoreThree);
            AddCity(1, 0, 1, 1); AddCity(2, 1, 5, 5);
            for (int seat = 0; seat < Board.SeatCount; seat++) Board.SetGold(seat, 6 + (seat == firstSeat ? 0 : 1));
            SettleTurn(firstSeat);
        }

        private void AddCity(int id, int seat, int x, int y)
        {
            Board.AddCity(new SimulationCity(id, seat, Board.Position(x, y)));
            cities.Add(id, new AdvancedCityEconomy(id));
        }
        public AdvancedCityEconomy CityEconomy(int id) => cities.TryGetValue(id, out var city) ? city : null;
        public AdvancedSoldier Soldier(int id) => soldiers.TryGetValue(id, out var soldier) ? soldier : null;

        public AdvancedCityForecast Forecast(int cityId, PopulationSex? recruitSex = null, CityUpgrade? upgrade = null)
        {
            AdvancedCityEconomy city = CityEconomy(cityId) ?? throw new ArgumentOutOfRangeException(nameof(cityId));
            int men = city.Men, women = city.Women;
            int military = soldiers.Values.Count(s => s.HomeCityId == cityId);
            if (recruitSex.HasValue)
            {
                if (!ValidSex(recruitSex.Value) || city.Available(recruitSex.Value) <= 0) throw new ArgumentOutOfRangeException(nameof(recruitSex));
                if (recruitSex.Value == PopulationSex.Male) men--; else women--;
                military++;
            }
            int population = men + women;
            if (upgrade.HasValue && (!Enum.IsDefined(typeof(CityUpgrade), upgrade.Value) ||
                city.Level(upgrade.Value) >= AdvancedEconomyRules.MaxUpgradeLevel)) throw new ArgumentOutOfRangeException(nameof(upgrade));
            return new AdvancedCityForecast(population, military, Rules.Housing(city) + (upgrade == CityUpgrade.Housing ? 4 : 0),
                Rules.FoodProduction(population, city.FoodLevel + (upgrade == CityUpgrade.Food ? 1 : 0)),
                Math.Min(men, women), Rules.GoldProduction(population, city.GoldLevel + (upgrade == CityUpgrade.Gold ? 1 : 0)));
        }

        public int GoldUpkeep(int seat) => soldiers.Values.Count(s => Board.GetUnit(s.UnitId)?.Seat == seat) * AdvancedEconomyRules.UnitGoldUpkeep;
        public int GoldIncome(int seat) => Board.Cities.Where(c => c.Seat == seat).Sum(c => Rules.GoldProduction(cities[c.Id]));

        public string RecruitBlockReason(int seat, int cityId, string type, PopulationSex sex)
        {
            if (!Board.IsTurnOwnedBySeat(seat)) return "This seat does not own the turn.";
            var city = Board.GetCity(cityId); var economy = CityEconomy(cityId);
            if (city == null || economy == null || city.Seat != seat) return "Choose your own city.";
            if (!ValidSex(sex)) return "Choose male or female population.";
            if (economy.Available(sex) == 0) return "No population of that sex is available.";
            if (city.Recruited) return "This city already recruited this turn.";
            if (Board.UnitAt(city.Position) != null) return "Move the stationed unit first.";
            var definition = Board.RecruitType(type);
            if (definition == null || !Board.IsRecruitEnabled(type)) return "That unit is outside the advanced roster.";
            if (Board.GoldForSeat(seat) < definition.RecruitCost) return "Not enough gold.";
            return null;
        }

        public bool TryRecruit(int seat, int cityId, string type, PopulationSex sex, out string reason)
        {
            reason = RecruitBlockReason(seat, cityId, type, sex);
            if (reason != null) return false;
            SimulationCity city = Board.GetCity(cityId);
            UnitDefinition definition = Rules.RecruitDefinition(Board.RecruitType(type), sex);
            // All validation precedes any mutation; standard recruitment remains unchanged.
            SimulationUnit unit = Board.Recruit(seat, city.Position, definition);
            Board.SetGold(seat, Board.GoldForSeat(seat) - definition.RecruitCost);
            city.Recruited = true;
            cities[cityId].ChangePopulation(sex, -1);
            soldiers.Add(unit.Id, new AdvancedSoldier(unit.Id, cityId, sex));
            return true;
        }

        public string UpgradeBlockReason(int seat, int cityId, CityUpgrade upgrade)
        {
            if (!Board.IsTurnOwnedBySeat(seat)) return "This seat does not own the turn.";
            var city = Board.GetCity(cityId); var economy = CityEconomy(cityId);
            if (city == null || economy == null || city.Seat != seat) return "Choose your own city.";
            if (!Enum.IsDefined(typeof(CityUpgrade), upgrade)) return "Unknown upgrade.";
            if (economy.UpgradedThisTurn) return "One city upgrade per turn.";
            if (economy.Level(upgrade) >= AdvancedEconomyRules.MaxUpgradeLevel) return "Maximum level reached.";
            if (Board.GoldForSeat(seat) < Rules.UpgradeCost(economy, upgrade)) return "Not enough gold.";
            return null;
        }

        public bool TryUpgrade(int seat, int cityId, CityUpgrade upgrade, out string reason)
        {
            reason = UpgradeBlockReason(seat, cityId, upgrade);
            if (reason != null) return false;
            var city = cities[cityId];
            Board.SetGold(seat, Board.GoldForSeat(seat) - Rules.UpgradeCost(city, upgrade));
            city.Upgrade(upgrade); city.UpgradedThisTurn = true;
            return true;
        }

        public bool TryDisband(int seat, int unitId, out string reason)
        {
            SimulationUnit unit = Board.GetUnit(unitId);
            AdvancedSoldier soldier = Soldier(unitId);
            reason = !Board.IsTurnOwnedBySeat(seat) ? "This seat does not own the turn." :
                unit == null || soldier == null || unit.Seat != seat ? "Choose your own soldier." :
                Board.GetCity(soldier.HomeCityId)?.Seat != seat || unit.Position != Board.GetCity(soldier.HomeCityId).Position ?
                    "Return to the home city before disbanding." :
                unit.MovesUsed > 0 || unit.AttacksUsed > 0 ? "Disband before moving or attacking this turn." : null;
            if (reason != null) return false;
            cities[soldier.HomeCityId].ChangePopulation(soldier.Sex, 1);
            Board.Remove(unit); soldiers.Remove(unitId);
            // Prevent recruit/disband/recruit loops at this city within the same turn.
            Board.GetCity(soldier.HomeCityId).Recruited = true;
            return true;
        }

        public MatchTransition Apply(MatchCommand command)
        {
            // Recruitment must include explicit population choice through TryRecruit.
            if (command.Kind == MatchActionKind.Recruit) return default;
            MatchTransition transition = MatchEngine.Apply(Board, command);
            if (!transition.Applied) return transition;
            if (transition.KilledUnitId != 0) soldiers.Remove(transition.KilledUnitId);
            if (command.Kind == MatchActionKind.EndTurn) SettleTurn(Board.CurrentTurnSeat);
            return transition;
        }

        private void SettleTurn(int seat)
        {
            var report = new AdvancedTurnReport { Seat = seat };
            foreach (SimulationCity owned in Board.Cities.Where(c => c.Seat == seat).OrderBy(c => c.Id))
            {
                AdvancedCityEconomy city = cities[owned.Id]; city.UpgradedThisTurn = false;
                AdvancedCityForecast forecast = Forecast(owned.Id);
                int availableFood = city.StoredFood + forecast.FoodProduced;
                int shortfall = Math.Max(0, forecast.FoodConsumed - availableFood);
                report.FoodShortfall += shortfall;
                city.StoredFood = Math.Min(AdvancedEconomyRules.FoodStorage, Math.Max(0, availableFood - forecast.FoodConsumed));
                if (shortfall > 0)
                {
                    city.GrowthProgress = 0;
                    if (city.Civilians > 0)
                    {
                        // One civilian loss per shortage turn; losses alternate on ties, without RNG.
                        PopulationSex loss = city.Men > city.Women ? PopulationSex.Male :
                            city.Women > city.Men ? PopulationSex.Female : city.NextBirthSex;
                        city.ChangePopulation(loss, -1); report.CivilianDeaths++;
                        if (city.Men == city.Women) city.NextBirthSex = Opposite(loss);
                    }
                    // Civilians get ration priority; uncovered soldiers also suffer, even in an empty city.
                    foreach (AdvancedSoldier hungry in soldiers.Values.Where(s => s.HomeCityId == owned.Id)
                        .OrderByDescending(s => s.UnitId).Take(shortfall).ToArray())
                    {
                        report.UnfedUnits++;
                        DamageForSupply(hungry, report);
                    }
                }
                else if (forecast.Residents < forecast.Housing && city.Pairs > 0 && forecast.FoodBalance > 0)
                {
                    city.GrowthProgress = Math.Min(AdvancedEconomyRules.GrowthThreshold, city.GrowthProgress + city.Pairs);
                    if (city.GrowthProgress >= AdvancedEconomyRules.GrowthThreshold && city.StoredFood >= AdvancedEconomyRules.FoodPerBirth)
                    {
                        city.ChangePopulation(city.NextBirthSex, 1);
                        city.NextBirthSex = Opposite(city.NextBirthSex);
                        city.GrowthProgress -= AdvancedEconomyRules.GrowthThreshold;
                        city.StoredFood -= AdvancedEconomyRules.FoodPerBirth; report.Births++;
                    }
                }
                // At capacity, without pairs or without a surplus, progress pauses rather than banking births.
                report.GoldProduced += forecast.GoldProduced;
            }
            int gold = Board.GoldForSeat(seat) + report.GoldProduced;
            foreach (AdvancedSoldier soldier in soldiers.Values.Where(s => Board.GetUnit(s.UnitId)?.Seat == seat).OrderBy(s => s.UnitId).ToArray())
            {
                report.GoldUpkeep += AdvancedEconomyRules.UnitGoldUpkeep;
                if (gold >= AdvancedEconomyRules.UnitGoldUpkeep) gold -= AdvancedEconomyRules.UnitGoldUpkeep;
                else
                {
                    report.UnpaidUnits++;
                    DamageForSupply(soldier, report);
                }
            }
            Board.SetGold(seat, gold);
            LastTurnReport = report;
        }

        private void DamageForSupply(AdvancedSoldier soldier, AdvancedTurnReport report)
        {
            SimulationUnit unit = Board.GetUnit(soldier.UnitId);
            unit.Health = Math.Max(0, unit.Health - CombatValues.FromDisplay(1));
            if (unit.Health == 0) { Board.Remove(unit); soldiers.Remove(unit.Id); report.UnitDeaths++; }
        }

        private static bool ValidSex(PopulationSex sex) => sex == PopulationSex.Male || sex == PopulationSex.Female;
        private static PopulationSex Opposite(PopulationSex sex) => sex == PopulationSex.Male ? PopulationSex.Female : PopulationSex.Male;
    }
}
