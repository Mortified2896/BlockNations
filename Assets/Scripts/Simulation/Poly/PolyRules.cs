using System;
using System.Linq;

namespace BlockNations.Simulation.Poly
{
    public enum PolyTerrain { Plains, Forest, Mountain }
    public enum PolyResource { None, Fruit, Animals, Crops, Ore }
    public enum PolyImprovement { None, Farm, LumberHut, Mine, Windmill, Sawmill, Forge }
    public enum PolyActionKind { Move, Attack, Heal, Capture, Recruit, Develop, Research, Reward, EndTurn }

    public sealed class PolyTechnology
    {
        public readonly string Id, Name, Parent, Description;
        public readonly int Tier;
        public PolyTechnology(string id, string name, string parent, int tier, string description)
        { Id = id; Name = name; Parent = parent; Tier = tier; Description = description; }
    }

    public static class PolyRules
    {
        public const string Version = "poly-land-v1";
        public const int Size = 11;
        public static readonly PolyTechnology[] Technologies = {
            new PolyTechnology("organization", "Organization", null, 1, "Harvest fruit for population."),
            new PolyTechnology("farming", "Farming", "organization", 2, "Build farms on crops: +2 population."),
            new PolyTechnology("construction", "Construction", "farming", 3, "Windmills gain population for adjacent farms."),
            new PolyTechnology("shields", "Shields", "organization", 2, "Recruit Defenders to hold cities."),
            new PolyTechnology("hunting", "Hunting", null, 1, "Hunt animals for population."),
            new PolyTechnology("archery", "Archery", "hunting", 2, "Recruit Archers; forest defense bonus."),
            new PolyTechnology("forestry", "Forestry", "hunting", 2, "Build lumber huts in forests: +1 population."),
            new PolyTechnology("mathematics", "Mathematics", "forestry", 3, "Recruit Catapults; build sawmills beside lumber huts."),
            new PolyTechnology("climbing", "Climbing", null, 1, "Cross mountains; mountain defense bonus."),
            new PolyTechnology("mining", "Mining", "climbing", 2, "Build mines on ore: +2 population."),
            new PolyTechnology("smithing", "Smithing", "mining", 3, "Recruit Swordsmen; build forges beside mines."),
            new PolyTechnology("riding", "Riding", null, 1, "Recruit Riders with retreat after attacking."),
            new PolyTechnology("roads", "Roads", "riding", 2, "Connect friendly land with roads; movement costs one."),
            new PolyTechnology("chivalry", "Chivalry", "roads", 3, "Recruit Knights that attack again after a kill.")
        };
        public static readonly UnitDefinition[] Units = {
            Unit("warrior", "Warrior", 2, 10, 2, 2, 1, 1),
            Unit("archer", "Archer", 3, 10, 2, 1, 1, 2),
            Unit("rider", "Rider", 3, 10, 2, 1, 2, 1),
            Unit("defender", "Defender", 3, 15, 1, 3, 1, 1),
            Unit("swordsman", "Swordsman", 5, 15, 3, 3, 1, 1),
            Unit("knight", "Knight", 8, 10, 4, 1, 3, 1),
            Unit("catapult", "Catapult", 8, 10, 4, 0, 1, 3),
            Unit("giant", "Giant", 0, 30, 5, 4, 1, 1)
        };
        private static UnitDefinition Unit(string id, string name, int cost, int hp, int attack, int defense, int movement, int range) =>
            new UnitDefinition("poly-" + id, name, cost, id == "archer" ? 2 : 1, id, hp * 10, attack * 10, range, id != "catapult", defense * 10, movement, 1);
        public static UnitDefinition Definition(string id) => Units.FirstOrDefault(u => u.TypeId == id);
        public static PolyTechnology Technology(string id) => Technologies.FirstOrDefault(t => t.Id == id);
        public static string UnitTechnology(string id)
        {
            switch (id) {
                case "poly-archer": return "archery"; case "poly-rider": return "riding";
                case "poly-defender": return "shields"; case "poly-swordsman": return "smithing";
                case "poly-knight": return "chivalry"; case "poly-catapult": return "mathematics";
                default: return null;
            }
        }
        public static string UnitAbility(string id)
        {
            switch (id) {
                case "poly-rider": return "Move, attack, then retreat up to 2 tiles.";
                case "poly-knight": return "Each kill permits another attack.";
                case "poly-catapult": return "Range 3. Cannot attack after moving or retaliate.";
                case "poly-defender": return "High defense; strong retaliation.";
                case "poly-giant": return "City level reward. Powerful melee unit.";
                case "poly-archer": return "Range 2. Attacks only currently visible targets.";
                default: return "Move then attack. Rest to recover health.";
            }
        }
    }
    [Serializable] public sealed class PolyTile
    {
        public PolyTerrain terrain;
        public PolyResource resource;
        public PolyImprovement improvement;
        public int cityId;
        public bool road;
        public PolyTile Copy() => (PolyTile)MemberwiseClone();
    }
    [Serializable] public sealed class PolyCity
    {
        public int id, position, ownerSeatIndex = -1, level = 1, population, workshops, walls, pendingRewards;
        public bool recruited;
        public int Income => ownerSeatIndex < 0 ? 0 : level + 1 + workshops;
        public int PopulationNeeded => level + 1;
    }
    [Serializable] public sealed class PolyUnitSave
    {
        public int id, ownerSeatIndex, position, health, homeCityId, kills;
        public string type;
        public bool moved, attacked, rested, retreatUsed, attackStarted;
    }
    [Serializable] public sealed class PolySeatSave
    {
        public int gold;
        public string[] technologies;
        public PolyTile[] memory;
        public bool[] explored;
        public int[] cityOwners, cityLevels;
    }
    [Serializable] public sealed class PolySave
    {
        public string mode = "Poly", rulesVersion = PolyRules.Version;
        public int seed, currentTurnSeatIndex, round = 1, winnerSeatIndex = -1;
        public bool gameOver;
        public PolyTile[] tiles;
        public PolyCity[] cities;
        public PolyUnitSave[] units;
        public PolySeatSave[] seats;
    }
    public sealed class PolyAction
    {
        public PolyActionKind Kind;
        public int SeatIndex, ActorId, Destination = -1;
        public string Choice;
        public int Cost, Population, Damage, Retaliation;
        public string Label;
        public PolyAction(PolyActionKind kind, int seat, int actor = 0, int destination = -1, string choice = null)
        { Kind = kind; SeatIndex = seat; ActorId = actor; Destination = destination; Choice = choice; }
        public bool Same(PolyAction b) => b != null && Kind == b.Kind && SeatIndex == b.SeatIndex && ActorId == b.ActorId && Destination == b.Destination && Choice == b.Choice;
    }
}
