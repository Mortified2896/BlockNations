using BlockNations.AI;
using BlockNations.Simulation;

internal static class ContractChecks
{
    private static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); }

    private static float[] Vector(SimulationObservationSource.Context context, int source = -1) =>
        LearnedObservationSchemaV3.Encode(context.Observation, source, context.PublicFirstSeat);

    private static void SameDecisions(SimulationObservationSource.Context before, SimulationObservationSource.Context after)
    {
        Require(Program.Sources(before.Observation).SequenceEqual(Program.Sources(after.Observation)), "Source choices changed.");
        foreach (int source in Program.Sources(before.Observation))
        {
            Require(Vector(before, source).SequenceEqual(Vector(after, source)), "Private information changed model inputs.");
            Require(LearnedObservationSchemaV3.Choices(before.Observation, source, before.PublicFirstSeat).Keys.OrderBy(v => v)
                .SequenceEqual(LearnedObservationSchemaV3.Choices(after.Observation, source, after.PublicFirstSeat).Keys.OrderBy(v => v)),
                "Private information changed conditional masks.");
        }
    }

    private static void Reject(Action action)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "Invalid model input was accepted.");
    }

    internal static int Run()
    {
        PrefixAndRotation();
        RoleMetadata();
        PublicCapabilities();
        HiddenInformation();
        ActualAttackOrder();
        InvalidInputs();
        Console.WriteLine("6 observation-contract groups passed: prefix/masks, symmetry, role, capabilities, fog/privacy, actual attack order and rejection guards");
        return 0;
    }

    private static void PrefixAndRotation()
    {
        foreach (int size in new[] { 5, 6, 7, 9, 11 })
        foreach (int first in new[] { 0, 1 })
        {
            var left = Program.Observe(Program.Position(size, 0, first), 0);
            var rotated = Program.Observe(Program.Position(size, 1, 1 - first), 1);
            // Seat rotation changes identities/coordinates but retains public
            // role, canonical tensors and all source/conditional mask indices.
            SameDecisions(left, rotated);
            foreach (var context in new[] { left, rotated })
            foreach (int source in Program.Sources(context.Observation))
            {
                float[] old = LearnedActionSchema.Encode(context.Observation, source), expanded = Vector(context, source);
                Require(old.SequenceEqual(expanded.Take(old.Length)), "Legacy prefix changed.");
                Require(expanded.Length == 3259 && expanded[LearnedObservationSchemaV3.VersionOffset] == 3,
                    "Expanded contract has the wrong dimension/version.");
                var prior = LearnedActionSchema.Choices(context.Observation, source);
                var current = LearnedObservationSchemaV3.Choices(context.Observation, source, context.PublicFirstSeat);
                Require(prior.Count == current.Count && prior.All(p => current.TryGetValue(p.Key, out var action) && action.Equals(p.Value)),
                    "An action was remapped during the observation-only migration.");
            }
        }
    }

    private static void RoleMetadata()
    {
        foreach (int seat in new[] { 0, 1 })
        {
            var first = Program.Observe(Program.Position(7, seat, seat), seat);
            var second = Program.Observe(Program.Position(7, seat, 1 - seat), seat);
            Require(first.PublicFirstSeat == seat && second.PublicFirstSeat == 1 - seat, "Starting seat was inferred from current turn.");
            foreach (int source in Program.Sources(first.Observation))
            {
                float[] a = Vector(first, source), b = Vector(second, source);
                Require(a[LearnedObservationSchemaV3.FirstPlayerOffset] == 1 && b[LearnedObservationSchemaV3.FirstPlayerOffset] == 0,
                    "Public first/second role is absent.");
                Require(a.Where((value, index) => value != b[index]).Count() == 1, "Role metadata changed other inputs.");
            }
        }
    }

    private static void PublicCapabilities()
    {
        var context = Program.Observe(Program.Position(7, 0, 1), 0);
        float[] before = Vector(context);
        int warrior = Array.FindIndex(context.Observation.Units, unit => unit.Seat == 0 && unit.Type == "warrior");
        AIUnitState piece = context.Observation.Units[warrior];
        int canonical = LearnedActionSchema.CanvasPosition(context.Observation, piece.Position(7));
        Require(before[LearnedObservationSchemaV3.UnitAttackEndsMovementOffset + canonical] == 1, "Visible attack commitment missing.");
        // The same capability can belong to a custom unit. Type names do not
        // influence the tensor; this is not a Warrior/Rider heuristic.
        piece.Type = "custom-unit";
        context.Observation.Units[warrior] = piece;
        Require(before.SequenceEqual(Vector(context)), "A unit name entered the model input.");
        piece.AttackEndsMovement = false;
        context.Observation.Units[warrior] = piece;
        float[] after = Vector(context);
        Require(after[LearnedObservationSchemaV3.UnitAttackEndsMovementOffset + canonical] == 0 &&
            before.Where((value, index) => value != after[index]).Count() == 1, "Declared capability did not change exactly its public channel.");

        var roster = context.Observation.RecruitTypes;
        foreach (string type in new[] { "archer", "rider", "scout", "warrior" })
        {
            int slot = Array.FindIndex(roster, unit => unit.Type == type);
            bool expected = type == "rider" || type == "warrior";
            Require(before[LearnedObservationSchemaV3.RecruitAttackEndsMovementOffset + slot] == (expected ? 1 : 0),
                "Recruit action-order capability missing.");
        }
        int scout = Array.FindIndex(roster, unit => unit.Type == "scout");
        Require(!context.Observation.CanRecruitType(scout) &&
            !Program.Sources(context.Observation).Skip(1).Any(source => LearnedObservationSchemaV3.Choices(context.Observation, source, 1)
                .ContainsKey(LearnedActionSchema.RecruitOffset + scout)), "Disabled Scout was removed or re-enabled.");
        for (int slot = roster.Length; slot < LearnedActionSchema.RecruitCapacity; slot++)
            Require(before[LearnedObservationSchemaV3.RecruitAttackEndsMovementOffset + slot] == 0, "Padding contains a recruit capability.");
    }

    private static MatchState FogPosition(bool hidden = false, bool changed = false, bool visible = false)
    {
        var rules = MatchRuleProfile.CoreThree;
        var state = new MatchState(7, 7, 2, rules.PolicyCatalog, firstSeat: 1, round: 4, ruleProfile: rules);
        state.SetGold(0, 5); state.SetGold(1, changed ? 999 : 5);
        state.AddCity(new SimulationCity(1, 0, 8));
        state.AddCity(new SimulationCity(2, 1, 40, changed));
        rules.TryGetDefinition("rider", out var rider);
        rules.TryGetDefinition("warrior", out var warrior);
        state.AddUnit(new SimulationUnit(1, 0, 8, rider));
        if (hidden) state.AddUnit(new SimulationUnit(changed ? 902 : 2, 1, changed ? 24 : 39, warrior,
            health: changed ? 10 : 30, movesUsed: changed ? 1 : 0, attacksUsed: changed ? 1 : 0));
        if (visible) state.AddUnit(new SimulationUnit(changed ? 904 : 4, 1, 9, warrior,
            movesUsed: changed ? 1 : 0, attacksUsed: changed ? 1 : 0));
        return state;
    }

    private static void HiddenInformation()
    {
        var empty = Program.Observe(FogPosition(), 0);
        var hidden = Program.Observe(FogPosition(hidden: true), 0);
        var changed = Program.Observe(FogPosition(hidden: true, changed: true), 0);
        Require(!hidden.Observation.Visible[39] && !changed.Observation.Visible[24], "Hidden fixture is visible.");
        SameDecisions(empty, hidden);
        SameDecisions(hidden, changed);
        var visible = Program.Observe(FogPosition(visible: true), 0);
        var privateCounters = Program.Observe(FogPosition(changed: true, visible: true), 0);
        Require(visible.Observation.Visible[9], "Visible fixture is hidden.");
        SameDecisions(visible, privateCounters);
        Require(privateCounters.Observation.Units.Where(unit => unit.Seat == 1)
            .All(unit => unit.MovesUsed == 0 && unit.AttacksUsed == 0), "Fair adapter did not neutralize opponent counters.");
    }

    private static void ActualAttackOrder()
    {
        foreach (string type in new[] { "archer", "rider", "warrior" })
        {
            var rules = MatchRuleProfile.CoreThree;
            var state = new MatchState(7, 7, 2, rules.PolicyCatalog, firstSeat: 1, round: 4, ruleProfile: rules);
            rules.TryGetDefinition(type, out var definition);
            rules.TryGetDefinition("rider", out var enemy);
            state.AddCity(new SimulationCity(1, 0, 8)); state.AddCity(new SimulationCity(2, 1, 40));
            state.AddUnit(new SimulationUnit(1, 0, 24, definition));
            state.AddUnit(new SimulationUnit(2, 1, 25, enemy));
            var context = Program.Observe(state, 0);
            var command = context.Commands[context.Observation.LegalActions.Single(a => a.Kind == AIActionKind.Attack).Key];
            Require(MatchEngine.Apply(state, command).Applied, "Actual shared-engine attack was rejected.");
            var after = Program.Observe(state, 0);
            bool archer = type == "archer";
            Require(after.Observation.LegalActions.Any(a => a.Kind == AIActionKind.Move) == archer &&
                !after.Observation.LegalActions.Any(a => a.Kind == AIActionKind.Attack), "Capability does not match actual post-attack legality.");
            float[] input = Vector(after);
            int canonical = LearnedActionSchema.CanvasPosition(after.Observation, 24);
            Require(input[LearnedObservationSchemaV3.UnitAttackEndsMovementOffset + canonical] == (archer ? 0 : 1),
                "Post-attack capability was reinterpreted as remaining budget.");
        }
    }

    private static void InvalidInputs()
    {
        var context = Program.Observe(Program.Position(7, 0, 1), 0);
        Reject(() => LearnedObservationSchemaV3.Encode(context.Observation, -1, -1));
        Reject(() => LearnedObservationSchemaV3.Encode(context.Observation, -1, 2));
        Reject(() => LearnedObservationSchemaV3.Encode(context.Observation, -2, 1));
        Reject(() => LearnedObservationSchemaV3.Encode(context.Observation, LearnedActionSchema.Positions, 1));
        Reject(() => LearnedObservationSchemaV3.Encode(null, -1, 1));
        var hidden = Program.Observe(FogPosition(), 0);
        MatchRuleProfile.CoreThree.TryGetDefinition("warrior", out var definition);
        AIUnitState enemy = SimulationObservationSource.UnitCapabilities(definition, 1);
        enemy.X = 3; enemy.Y = 3;
        hidden.Observation.Units = hidden.Observation.Units.Append(enemy).ToArray();
        Reject(() => Vector(hidden));
        var counters = Program.Observe(FogPosition(visible: true), 0);
        int index = Array.FindIndex(counters.Observation.Units, unit => unit.Seat == 1);
        enemy = counters.Observation.Units[index]; enemy.AttacksUsed = 1;
        counters.Observation.Units[index] = enemy;
        Reject(() => Vector(counters));
    }
}
