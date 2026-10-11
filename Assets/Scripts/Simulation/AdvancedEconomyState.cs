using System;

namespace BlockNations.Simulation
{
    public sealed class AdvancedCityEconomy
    {
        public int CityId { get; }
        public int Men { get; internal set; }
        public int Women { get; internal set; }
        public int Civilians => Men + Women;
        public int Pairs => Math.Min(Men, Women);
        public int StoredFood { get; internal set; }
        public int GrowthProgress { get; internal set; }
        public PopulationSex NextBirthSex { get; internal set; }
        public int HousingLevel { get; internal set; }
        public int FoodLevel { get; internal set; }
        public int GoldLevel { get; internal set; }
        public bool UpgradedThisTurn { get; internal set; }

        public AdvancedCityEconomy(int cityId, int men = AdvancedEconomyRules.StartingMen,
            int women = AdvancedEconomyRules.StartingWomen, int food = AdvancedEconomyRules.StartingFood)
        {
            if (cityId == 0 || men < 0 || women < 0 || food < 0 || food > AdvancedEconomyRules.FoodStorage)
                throw new ArgumentOutOfRangeException(nameof(cityId));
            CityId = cityId; Men = men; Women = women; StoredFood = food;
        }
        public int Available(PopulationSex sex) => sex == PopulationSex.Male ? Men : Women;
        internal void ChangePopulation(PopulationSex sex, int amount)
        {
            if (sex == PopulationSex.Male) Men += amount; else Women += amount;
        }
        public int Level(CityUpgrade upgrade)
        {
            switch (upgrade)
            {
                case CityUpgrade.Housing: return HousingLevel;
                case CityUpgrade.Food: return FoodLevel;
                case CityUpgrade.Gold: return GoldLevel;
                default: throw new ArgumentOutOfRangeException(nameof(upgrade));
            }
        }
        internal void Upgrade(CityUpgrade upgrade)
        {
            switch (upgrade)
            {
                case CityUpgrade.Housing: HousingLevel++; break;
                case CityUpgrade.Food: FoodLevel++; break;
                case CityUpgrade.Gold: GoldLevel++; break;
                default: throw new ArgumentOutOfRangeException(nameof(upgrade));
            }
        }
    }

    public sealed class AdvancedSoldier
    {
        public int UnitId { get; }
        public int HomeCityId { get; }
        public PopulationSex Sex { get; }
        internal AdvancedSoldier(int unitId, int homeCityId, PopulationSex sex)
        { UnitId = unitId; HomeCityId = homeCityId; Sex = sex; }
    }

    // Forecasts expose consequences before purchase; reports expose what was actually settled.
    public readonly struct AdvancedCityForecast
    {
        public readonly int Civilians, Soldiers, Housing, FoodProduced, FoodConsumed, Pairs, GoldProduced;
        public int Residents => Civilians + Soldiers;
        public int FoodBalance => FoodProduced - FoodConsumed;
        public AdvancedCityForecast(int civilians, int soldiers, int housing, int foodProduced, int pairs, int goldProduced)
        {
            Civilians = civilians; Soldiers = soldiers; Housing = housing; FoodProduced = foodProduced;
            FoodConsumed = civilians + soldiers; Pairs = pairs; GoldProduced = goldProduced;
        }
    }

    public sealed class AdvancedTurnReport
    {
        public int Seat { get; internal set; }
        public int GoldProduced { get; internal set; }
        public int GoldUpkeep { get; internal set; }
        public int UnpaidUnits { get; internal set; }
        public int Births { get; internal set; }
        public int CivilianDeaths { get; internal set; }
        public int UnitDeaths { get; internal set; }
        public int FoodShortfall { get; internal set; }
        public int UnfedUnits { get; internal set; }
    }
}
