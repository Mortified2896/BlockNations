using System;

namespace BlockNations.Simulation
{
    public enum PopulationSex { Male, Female }
    public enum CityUpgrade { Housing, Food, Gold }
    public enum RecruitmentBalance { Equal, ClassStrengthExperiment }

    // Experimental recipe, deliberately separate from standard rules and save protocols.
    public sealed class AdvancedEconomyRules
    {
        public const string Version = "advanced-economy-prototype-v1";
        public const int StartingMen = 6, StartingWomen = 6, StartingHousing = 16;
        public const int StartingFood = 12, FoodStorage = 24, BaseFoodCapacity = 16;
        public const int FoodPerWorker = 2, BaseGoldCapacity = 3, CiviliansPerGold = 4;
        public const int GrowthThreshold = 12, FoodPerBirth = 2;
        public const int UnitGoldUpkeep = 1, MaxUpgradeLevel = 3;
        public RecruitmentBalance Balance { get; }

        public AdvancedEconomyRules(RecruitmentBalance balance = RecruitmentBalance.Equal)
        {
            if (!Enum.IsDefined(typeof(RecruitmentBalance), balance)) throw new ArgumentOutOfRangeException(nameof(balance));
            Balance = balance;
        }

        public int Housing(AdvancedCityEconomy city) => StartingHousing + city.HousingLevel * 4;
        public int FoodProduction(AdvancedCityEconomy city) => FoodProduction(city.Civilians, city.FoodLevel);
        public int GoldProduction(AdvancedCityEconomy city) => GoldProduction(city.Civilians, city.GoldLevel);
        public int FoodProduction(int civilians, int level) => Math.Min(BaseFoodCapacity + level * 4, civilians * FoodPerWorker);
        public int GoldProduction(int civilians, int level) => Math.Min(BaseGoldCapacity + level, (civilians + CiviliansPerGold - 1) / CiviliansPerGold);
        public int UpgradeCost(AdvancedCityEconomy city, CityUpgrade upgrade) => 4 + city.Level(upgrade) * 4;

        // Percentages are a game experiment, not a claim about trained individuals.
        public UnitDefinition RecruitDefinition(UnitDefinition source, PopulationSex sex)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (Balance == RecruitmentBalance.Equal || sex == PopulationSex.Male) return source;
            int percent = source.TypeId == UnitRegistry.WarriorTypeId ? 80 :
                source.TypeId == UnitRegistry.ArcherTypeId || source.TypeId == UnitRegistry.RiderTypeId ? 90 : 100;
            return new UnitDefinition(source.TypeId, source.DisplayName, source.RecruitCost, source.VisionRange,
                source.PrefabTypeId, source.MaxHealthUnits, source.AttackUnits * percent / 100, source.AttackRange,
                source.CanAttackAfterMoving, source.DefenseUnits, source.MaxMovesPerTurn, source.MaxAttacksPerTurn,
                source.UsesCommittedMoveAction, source.AttackEndsMovement);
        }
    }
}
