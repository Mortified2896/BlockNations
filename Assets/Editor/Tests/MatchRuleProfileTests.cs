using System;
using System.Linq;
using BlockNations.AI;
using BlockNations.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class MatchRuleProfileTests
{
    private static MatchState Board(MatchRuleProfile profile, int seat = 0) =>
        new MatchState(7, 7, 2, profile.PolicyCatalog, seat, firstSeat: seat, ruleProfile: profile);

    [TestCase(SimulationRules.Version, 4, 1)]
    [TestCase(MatchRuleProfile.ArcherVisionVersion, 4, 2)]
    [TestCase(MatchRuleProfile.CoreThreeVersion, 3, 2)]
    public void ExplicitProfilesKeepCatalogSlotsAndBaselineImmutable(string version, int active, int vision)
    {
        MatchRuleProfile rules = MatchRuleProfile.Resolve(version);
        Assert.That(rules.PolicyCatalog.Select(t => t.TypeId), Is.EqualTo(new[] { "archer", "rider", "scout", "warrior" }));
        Assert.That(rules.RecruitableUnits.Count, Is.EqualTo(active));
        Assert.That(rules.PolicyCatalog[0].VisionRange, Is.EqualTo(vision));
        Assert.That(UnitRegistry.Archer.VisionRange, Is.EqualTo(1));
        Assert.That(MatchRuleProfile.Baseline.CanRecruit("scout"), Is.True);
        Assert.Throws<ArgumentException>(() => MatchRuleProfile.Resolve("typo-or-unknown-rules"));
    }

    [TestCase(0)] [TestCase(1)]
    public void CoreThreeDisablesScoutLegalityWithoutReindexingWarriorOrChangingTensor(int seat)
    {
        MatchState state = Board(MatchRuleProfile.CoreThree, seat);
        int cityPosition = state.Position(1, 1);
        state.AddCity(new SimulationCity(1, seat, cityPosition)); state.SetGold(seat, 5);
        var observation = new SimulationObservationSource().Observe(state, seat).Observation;
        int source = LearnedActionSchema.CanonicalPosition(LearnedActionSchema.CanvasPosition(observation, cityPosition), seat);
        var choices = LearnedActionSchema.Choices(observation, source);
        Assert.That(choices.ContainsKey(LearnedActionSchema.RecruitOffset + 3), Is.True, "Warrior remains in slot 3.");
        Assert.That(choices.ContainsKey(LearnedActionSchema.RecruitOffset + 2), Is.False, "Scout slot is retained but masked.");
        Assert.That(LearnedActionSchema.Encode(observation, source).Length, Is.EqualTo(3120));
        var tensor = LearnedActionSchema.Encode(observation, source);
        int recruits = LearnedActionSchema.Positions * LearnedActionSchema.TileChannels;
        Assert.That(tensor[recruits + 2 * LearnedActionSchema.RecruitChannels], Is.Zero);
        Assert.That(tensor[recruits + 3 * LearnedActionSchema.RecruitChannels], Is.EqualTo(1));
        var tactical = new AITacticalState(observation);
        Assert.That(tactical.Actions().Any(a => a.Kind == AIActionKind.Recruit && a.RecruitType == 2), Is.False);
        var scout = new MatchCommand(MatchActionKind.Recruit, seat, 1, cityPosition, recruitType: "scout");
        Assert.That(MatchEngine.Apply(state, scout).Applied, Is.False);
        Assert.That(state.GoldForSeat(seat), Is.EqualTo(5)); Assert.That(state.GetCity(1).Recruited, Is.False);
        Assert.That(state.Copy().IsRecruitEnabled("scout"), Is.False);
        Assert.That(state.Copy().RulesVersion, Is.EqualTo(MatchRuleProfile.CoreThreeVersion));
        Assert.That(MatchEngine.LegalActions(state, 1 - seat), Is.Empty);
    }

    [Test]
    public void SupportedExistingScoutStillImportsAndMovesInCoreExperiment()
    {
        MatchState state = Board(MatchRuleProfile.CoreThree);
        var scout = new SimulationUnit(1, 0, state.Position(2, 2), UnitRegistry.Scout); state.AddUnit(scout);
        Assert.That(state.Copy().GetUnit(1).Definition.TypeId, Is.EqualTo("scout"));
        Assert.That(MatchEngine.Apply(state, new MatchCommand(MatchActionKind.Move, 0, 1, state.Position(3, 2))).Applied, Is.True);
        Assert.That(state.IsRecruitEnabled("scout"), Is.False);
    }

    [TestCase(0, false)] [TestCase(0, true)] [TestCase(1, false)] [TestCase(1, true)]
    public void VisionTwoArcherShootsVisibleRangeTwoThenCanRetreatEvenAfterKill(int seat, bool kill)
    {
        MatchState state = Board(MatchRuleProfile.CoreThree, seat);
        UnitDefinition archerType = state.RecruitType("archer");
        var archer = new SimulationUnit(1, seat, state.Position(2, 2), archerType); state.AddUnit(archer);
        var enemy = new SimulationUnit(2, 1 - seat, state.Position(4, 2), UnitRegistry.Warrior,
            health: kill ? CombatValues.FromDisplay(1) : CombatValues.FromDisplay(3)); state.AddUnit(enemy);
        state.AddUnit(new SimulationUnit(3, 1 - seat, state.Position(5, 2), UnitRegistry.Warrior));
        var context = new SimulationObservationSource().Observe(state, seat);
        Assert.That(context.Observation.Visible[enemy.Position], Is.True);
        Assert.That(context.Observation.Units.Length, Is.EqualTo(2), "An enemy beyond vision remains hidden.");
        var shot = new MatchCommand(MatchActionKind.Attack, seat, archer.Id, enemy.Position, enemy.Id);
        Assert.That(MatchEngine.Apply(state, shot).Applied, Is.True);
        Assert.That(archer.Position, Is.EqualTo(state.Position(2, 2)), "A ranged kill never advances onto its target.");
        Assert.That(archer.RemainingMoves, Is.EqualTo(1)); Assert.That(archer.CanAttack, Is.False);
        Assert.That(MatchEngine.Apply(state, new MatchCommand(MatchActionKind.Move, seat, 1, state.Position(1, 2))).Applied, Is.True);
        Assert.That(archer.RemainingMoves, Is.Zero); Assert.That(archer.CanAttack, Is.False);
    }

    [Test]
    public void MovingArcherFirstStillForbidsShooting()
    {
        MatchState state = Board(MatchRuleProfile.CoreThree);
        var archer = new SimulationUnit(1, 0, state.Position(2, 2), state.RecruitType("archer")); state.AddUnit(archer);
        var target = new SimulationUnit(2, 1, state.Position(4, 2), UnitRegistry.Warrior); state.AddUnit(target);
        Assert.That(MatchEngine.Apply(state, new MatchCommand(MatchActionKind.Move, 0, 1, state.Position(3, 2))).Applied, Is.True);
        Assert.That(MatchEngine.Apply(state, new MatchCommand(MatchActionKind.Attack, 0, 1, target.Position, 2)).Applied, Is.False);
    }

    [Test]
    public void UnityUsesProfileDefinitionsForRecruitmentAndSpawning()
    {
        var root = new GameObject("Inactive profile test"); root.SetActive(false);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Archer.prefab");
        Assert.That(prefab, Is.Not.Null, "Use the authored unit and its presentation references.");
        GameObject spawned = null;
        try
        {
            var manager = root.AddComponent<TurnManager>();
            TrainingSceneBuilder.Set(manager, "externallyDrivenMatch", true);
            manager.officialUnitRegistrations = UnitRegistry.AllDefinitions.Select(t => new TurnManager.OfficialUnitRegistration
                { unitTypeId = t.TypeId, recruitable = true, prefab = prefab }).ToList();
            manager.ConfigureExperimentalRules(MatchRuleProfile.CoreThree);
            Assert.That(manager.GetRecruitableOfficialUnitDefinitions().Select(t => t.TypeId), Does.Not.Contain("scout"));
            Assert.That(manager.GetOfficialUnitPolicyCatalog().Count, Is.EqualTo(4));
            spawned = manager.InstantiateConfiguredUnit("archer", prefab, Vector3.zero, 0, null, true);
            Assert.That(spawned.GetComponent<Unit>().VisionRange, Is.EqualTo(2));
            Assert.That(SceneSimulationAdapter.DefinitionForUnit(spawned.GetComponent<Unit>()).VisionRange, Is.EqualTo(2));
            Assert.That(spawned.GetComponent<Unit>().AttackEndsMovement, Is.False);
        }
        finally { if (spawned != null) Object.DestroyImmediate(spawned); Object.DestroyImmediate(root); }
    }
}
