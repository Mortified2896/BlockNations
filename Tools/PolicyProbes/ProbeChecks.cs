using BlockNations.AI;
using BlockNations.Simulation;
using BlockNations.Training;

internal static class ProbeChecks
{
    internal static ProbeCase Position(string name, string type, int position, int health = -1, int moves = 0, int attacks = 0)
    {
        return new ProbeCase { name = name, rulesVersion = MatchRuleProfile.CoreThreeVersion,
            publicCities = new[] { new AICityState { Seat = 0, X = 1, Y = 1 }, new AICityState { Seat = 1, X = 5, Y = 5 } },
            state = new TrainingTraceState { width = 7, height = 7, seat = 0, firstSeat = 0, round = 4, cityVision = 1, incomePerCity = 1,
                tiles = Enumerable.Repeat(true, 49).ToArray(), gold = new[] { 0, 0 },
                cities = new[] { new TraceCity { id = 1, seat = 0, position = 8 }, new TraceCity { id = 2, seat = 1, position = 40 } },
                units = new[] { new TraceUnit { id = 1, seat = 0, position = position, type = type, health = health, movesUsed = moves, attacksUsed = attacks } } } };
    }
    private static void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static ProbeLabel[] Labels(ProbeResult result) => result.decisions.Skip(1).SelectMany(d => d.analysis).ToArray();
    internal static int Run()
    {
        var capture = Program.Build(Position("visible_capture", "rider", 32));
        Require(Labels(capture).Any(a => a.visibleWin && a.kind == "Move"), "Visible city capture not measured.");
        var shot = Position("ranged_kill", "archer", 24);
        shot.state.units = shot.state.units.Append(new TraceUnit { id = 2, seat = 1, position = 26, type = "rider", health = CombatValues.FromDisplay(1) }).ToArray();
        Require(Labels(Program.Build(shot)).Any(a => a.kind == "Attack" && a.killedVisibleUnit), "Vision-2 ranged kill missing.");
        shot.state.units[0].movesUsed = 1;
        Require(!Labels(Program.Build(shot)).Any(a => a.kind == "Attack"), "Moved Archer could shoot.");
        var retreat = Program.Build(Position("after_shot", "archer", 24, attacks: 1));
        Require(Labels(retreat).Any(a => a.kind == "Move") && !Labels(retreat).Any(a => a.kind == "Attack"), "Archer cannot move after shot.");
        var rider = Program.Build(Position("spent_attack", "rider", 24, attacks: 1));
        Require(rider.decisions.Length == 1 && rider.decisions[0].available.SequenceEqual(new[] { LearnedActionSchema.EndTurn }), "Rider retained movement after attack.");
        var warrior = Position("move_then_attack", "warrior", 24, moves: 1);
        warrior.state.units = warrior.state.units.Append(new TraceUnit { id = 2, seat = 1, position = 25, type = "rider", health = CombatValues.FromDisplay(1) }).ToArray();
        Require(Labels(Program.Build(warrior)).Any(a => a.kind == "Attack"), "Moved Warrior lost its shot.");
        var hidden = Position("hidden_world", "warrior", 8);
        hidden.state.units = hidden.state.units.Append(new TraceUnit { id = 2, seat = 1, position = 39, type = "rider", health = -1 }).ToArray();
        var before = Program.Build(hidden);
        hidden.state.gold[1] = 99; hidden.state.units[1].id = 900; hidden.state.units[1].movesUsed = 2; hidden.state.units[1].attacksUsed = 1;
        var after = Program.Build(hidden);
        Require(before.decisions.Length == after.decisions.Length && before.decisions.Zip(after.decisions)
            .All(p => p.First.model == p.Second.model && p.First.available.SequenceEqual(p.Second.available)), "Hidden resources, IDs or budgets leaked into model inputs.");
        var visible = Position("visible_private_budgets", "archer", 24);
        visible.state.units = visible.state.units.Append(new TraceUnit { id = 2, seat = 1, position = 26, type = "rider", health = -1 }).ToArray();
        before = Program.Build(visible); visible.state.units[1].id = 123; visible.state.units[1].attacksUsed = 1; visible.state.units[1].movesUsed = 2;
        after = Program.Build(visible);
        Require(before.decisions.Zip(after.decisions).All(p => p.First.model == p.Second.model && p.First.available.SequenceEqual(p.Second.available)), "Visible enemy private budgets leaked.");
        var blind = Position("hidden_blocker", "rider", 8);
        before = Program.Build(blind);
        blind.state.units = blind.state.units.Append(new TraceUnit { id = 2, seat = 1, position = 24, type = "warrior", health = -1 }).ToArray();
        after = Program.Build(blind);
        Require(before.decisions.Length == after.decisions.Length && before.decisions.Zip(after.decisions)
            .All(p => p.First.model == p.Second.model && p.First.available.SequenceEqual(p.Second.available)), "Hidden blocker changed policy inputs.");
        Require(Labels(after).Any(a => a.hiddenBlocker && !a.visibleWin), "Hidden-blocker labels claimed a guaranteed win.");
        var recruit = Position("disabled_scout", "warrior", 9); recruit.state.gold[0] = 10;
        Require(Program.Build(recruit).decisions.Skip(1).All(d => !d.available.Contains(LearnedActionSchema.RecruitOffset + 2)), "Scout recruitment became available in core-three probes.");
        var invalid = Position("invalid_dimensions", "warrior", 8); invalid.state.width = 11;
        try { Program.Build(invalid); throw new Exception("Accepted incompatible probe shape."); }
        catch (ArgumentException) { }
        Console.WriteLine("12 policy-probe checks passed");
        return 0;
    }
}
