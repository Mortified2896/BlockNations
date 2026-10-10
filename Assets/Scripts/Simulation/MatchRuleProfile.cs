using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockNations.Simulation
{
    // Explicit per-match experimental rules. Never mutate the registry, saved
    // unit identities, or the retained policy catalog when disabling recruitment.
    public sealed class MatchRuleProfile
    {
        public const string ArcherVisionVersion = "blocknations-simulation-v5-vision2";
        public const string CoreThreeVersion = "blocknations-simulation-v5-core3";
        public static MatchRuleProfile Baseline { get; } = Create(SimulationRules.Version, false, false);
        public static MatchRuleProfile ArcherVision { get; } = Create(ArcherVisionVersion, true, false);
        public static MatchRuleProfile CoreThree { get; } = Create(CoreThreeVersion, true, true);

        private readonly Dictionary<string, UnitDefinition> definitions;
        private readonly HashSet<string> enabled;
        public string Version { get; }
        public IReadOnlyList<UnitDefinition> PolicyCatalog { get; }
        public IReadOnlyList<UnitDefinition> RecruitableUnits { get; }

        private MatchRuleProfile(string version, IEnumerable<UnitDefinition> catalog, IEnumerable<string> enabledTypes)
        {
            Version = version;
            var ordered = catalog.OrderBy(t => t.TypeId, StringComparer.Ordinal).ToList();
            definitions = ordered.ToDictionary(t => t.TypeId, StringComparer.Ordinal);
            enabled = new HashSet<string>(enabledTypes, StringComparer.Ordinal);
            if (enabled.Count == 0 || enabled.Any(id => !definitions.ContainsKey(id)))
                throw new ArgumentException("Enabled recruits must belong to the stable policy catalog.");
            PolicyCatalog = ordered.AsReadOnly();
            RecruitableUnits = ordered.Where(t => enabled.Contains(t.TypeId)).ToList().AsReadOnly();
        }

        public bool CanRecruit(string typeId) => typeId != null && enabled.Contains(typeId);
        public bool TryGetDefinition(string typeId, out UnitDefinition definition) =>
            definitions.TryGetValue(UnitRegistry.NormalizeTypeId(typeId), out definition);

        public static MatchRuleProfile Resolve(string version)
        {
            if (version == Baseline.Version) return Baseline;
            if (version == ArcherVision.Version) return ArcherVision;
            if (version == CoreThree.Version) return CoreThree;
            throw new ArgumentException("Unsupported explicit match rules version.", nameof(version));
        }

        private static MatchRuleProfile Create(string version, bool archerVisionTwo, bool disableScout)
        {
            var catalog = UnitRegistry.AllDefinitions.Select(t => archerVisionTwo && t.TypeId == UnitRegistry.ArcherTypeId
                ? new UnitDefinition(t.TypeId, t.DisplayName, t.RecruitCost, 2, t.PrefabTypeId,
                    t.MaxHealthUnits, t.AttackUnits, t.AttackRange, t.CanAttackAfterMoving, t.DefenseUnits,
                    t.MaxMovesPerTurn, t.MaxAttacksPerTurn, t.UsesCommittedMoveAction, t.AttackEndsMovement)
                : t).ToArray();
            return new MatchRuleProfile(version, catalog,
                catalog.Where(t => !disableScout || t.TypeId != UnitRegistry.ScoutTypeId).Select(t => t.TypeId));
        }
    }
}
