using System;
using System.Collections.Generic;

namespace BlockNations.Simulation
{
    // Rule authority used by the scene adapter and the direct ML environment.
    public static class MatchEngine
    {
        public static bool[] Visibility(MatchState state, int seat)
        {
            var visible = new bool[state.Tiles.Length];
            foreach (SimulationCity city in state.Cities)
                if (city.Seat == seat) Reveal(state, visible, city.Position, state.CityVision);
            foreach (SimulationUnit unit in state.Units)
                if (unit.Seat == seat) Reveal(state, visible, unit.Position, unit.Definition.VisionRange);
            return visible;
        }
        private static void Reveal(MatchState state, bool[] visible, int origin, int radius)
        {
            int ox = origin % state.Width, oy = origin / state.Width;
            for (int y = Math.Max(0, oy - radius); y <= Math.Min(state.Height - 1, oy + radius); y++)
                for (int x = Math.Max(0, ox - radius); x <= Math.Min(state.Width - 1, ox + radius); x++)
                    if (state.Contains(x, y)) visible[state.Position(x, y)] = true;
        }

        public static Dictionary<int, int[]> ReachablePaths(MatchState state, SimulationUnit unit, bool[] visible)
        {
            if (unit == null || unit.RemainingMoves <= 0) return new Dictionary<int, int[]>();
            ValidateVisibility(state, visible);
            return MatchPaths.Build(state.Width, state.Height, state.Tiles, unit.Position, unit.RemainingMoves, next =>
            {
                SimulationUnit occupant = state.UnitAt(next);
                return occupant != null && (occupant.Seat == unit.Seat || visible[next]);
            });
        }

        public static List<MatchLegalAction> LegalUnitActions(MatchState state, int seat, int unitId, bool[] visible = null)
        {
            var actions = new List<MatchLegalAction>();
            SimulationUnit unit = state.GetUnit(unitId);
            if (!state.IsTurnOwnedBySeat(seat) || unit == null || unit.Seat != seat) return actions;
            visible = visible ?? Visibility(state, seat);
            foreach (KeyValuePair<int, int[]> path in ReachablePaths(state, unit, visible))
                actions.Add(new MatchLegalAction(new MatchCommand(MatchActionKind.Move, seat, unitId, path.Key), path.Value));
            if (unit.CanAttack)
                foreach (SimulationUnit target in state.Units)
                    if (state.Relationships.IsHostile(seat, target.Seat) && visible[target.Position] &&
                        SimulationRules.Distance(unit.Position % state.Width, unit.Position / state.Width,
                            target.Position % state.Width, target.Position / state.Width) <= unit.Definition.AttackRange)
                        actions.Add(new MatchLegalAction(new MatchCommand(MatchActionKind.Attack, seat, unitId, target.Position, target.Id)));
            return actions;
        }

        public static List<MatchLegalAction> LegalActions(MatchState state, int seat, bool[] visible = null)
        {
            var actions = new List<MatchLegalAction>();
            if (!state.IsTurnOwnedBySeat(seat)) return actions;
            visible = visible ?? Visibility(state, seat);
            ValidateVisibility(state, visible);
            foreach (SimulationUnit unit in state.Units)
                if (unit.Seat == seat) actions.AddRange(LegalUnitActions(state, seat, unit.Id, visible));
            foreach (SimulationCity city in state.Cities)
                if (city.Seat == seat && !city.Recruited && state.UnitAt(city.Position) == null)
                    foreach (UnitDefinition definition in state.Roster)
                        if (definition.RecruitCost <= state.GoldForSeat(seat))
                            actions.Add(new MatchLegalAction(new MatchCommand(MatchActionKind.Recruit, seat, city.Id, city.Position,
                                recruitType: definition.TypeId)));
            actions.Add(new MatchLegalAction(new MatchCommand(MatchActionKind.EndTurn, seat)));
            return actions;
        }

        public static MatchTransition Apply(MatchState state, MatchCommand command)
        {
            if (!state.IsTurnOwnedBySeat(command.Seat)) return default;
            if (command.Kind == MatchActionKind.EndTurn)
            {
                if (!command.Equals(new MatchCommand(MatchActionKind.EndTurn, command.Seat))) return default;
                int next = SimulationRules.NextSeat(command.Seat, state.SeatCount);
                // A round is a complete cycle regardless of which colour started.
                state.Round = SimulationRules.NextRound(state.Round, next, state.FirstSeat);
                BeginTurn(state, next);
                return new MatchTransition(true);
            }
            if (command.Kind == MatchActionKind.Recruit) return Recruit(state, command);
            bool[] visible = Visibility(state, command.Seat);
            MatchLegalAction legal = null;
            foreach (MatchLegalAction candidate in LegalUnitActions(state, command.Seat, command.ActorId, visible))
                if (candidate.Command.Equals(command)) { legal = candidate; break; }
            if (legal == null) return default;
            SimulationUnit unit = state.GetUnit(command.ActorId);
            if (command.Kind == MatchActionKind.Move)
            {
                int moved = 0, consumed = 0;
                bool hiddenBlocker = false;
                // Inspect first, so an invalid one-step/stale path never partly mutates state.
                foreach (int position in legal.Path)
                {
                    if (state.UnitAt(position) != null)
                    {
                        if (legal.Path.Length == 1 || visible[position]) return default;
                        consumed++; hiddenBlocker = true; break;
                    }
                    moved++; consumed++;
                }
                if (moved > 0) state.Move(unit, legal.Path[moved - 1]);
                unit.MovesUsed = SimulationRules.Consume(unit.MovesUsed, unit.Definition.MaxMovesPerTurn, consumed);
                if (hiddenBlocker) unit.AttacksUsed = unit.Definition.MaxAttacksPerTurn;
                int captured = CaptureAtUnit(state, unit);
                return new MatchTransition(consumed > 0, moved, hiddenBlocker, capturedCityId: captured);
            }
            SimulationUnit target = state.GetUnit(command.TargetId);
            unit.AttacksUsed = SimulationRules.Consume(unit.AttacksUsed, unit.Definition.MaxAttacksPerTurn);
            target.Health = Math.Max(0, target.Health - SimulationRules.Damage(unit.Definition.AttackUnits, target.Definition.DefenseUnits));
            int killed = 0, advance = 0;
            if (target.Health == 0)
            {
                killed = target.Id;
                int destination = target.Position;
                state.Remove(target);
                if (unit.Definition.AttackRange <= 1) { state.Move(unit, destination); advance = 1; }
            }
            return new MatchTransition(true, advance, killedUnitId: killed, capturedCityId: CaptureAtUnit(state, unit));
        }

        private static MatchTransition Recruit(MatchState state, MatchCommand command)
        {
            SimulationCity city = state.GetCity(command.ActorId);
            UnitDefinition definition = state.RecruitType(command.RecruitType);
            if (city == null || city.Seat != command.Seat || city.Recruited || city.Position != command.Destination ||
                command.TargetId != 0 || definition == null || state.UnitAt(city.Position) != null ||
                definition.RecruitCost > state.GoldForSeat(command.Seat)) return default;
            state.SetGold(command.Seat, state.GoldForSeat(command.Seat) - definition.RecruitCost);
            SimulationUnit unit = state.Recruit(command.Seat, city.Position, definition);
            city.Recruited = true;
            return new MatchTransition(true, recruitedUnitId: unit.Id);
        }

        public static void BeginTurn(MatchState state, int seat)
        {
            if (state.GameOver || seat < 0 || seat >= state.SeatCount) throw new InvalidOperationException("Cannot begin that seat's turn.");
            state.CurrentTurnSeat = seat;
            ResetResources(state, seat);
            CollectIncome(state, seat);
        }

        public static void ResetResources(MatchState state, int seat)
        {
            foreach (SimulationCity city in state.Cities)
                if (city.Seat == seat) city.Recruited = false;
            foreach (SimulationUnit unit in state.Units)
                if (unit.Seat == seat) { unit.MovesUsed = 0; unit.AttacksUsed = 0; }
        }

        public static void CollectIncome(MatchState state, int seat)
        {
            if (state.GameOver) return;
            int income = 0;
            foreach (SimulationCity city in state.Cities) if (city.Seat == seat) income += state.IncomePerCity;
            state.SetGold(seat, checked(state.GoldForSeat(seat) + income));
        }

        private static int CaptureAtUnit(MatchState state, SimulationUnit unit)
        {
            SimulationCity city = state.CityAt(unit.Position);
            if (city == null || !state.Relationships.IsHostile(unit.Seat, city.Seat)) return 0;
            city.Seat = unit.Seat;
            state.GameOver = true; state.WinnerSeat = unit.Seat; state.CapturedCityId = city.Id;
            return city.Id;
        }
        private static void ValidateVisibility(MatchState state, bool[] visible)
        { if (visible == null || visible.Length != state.Tiles.Length) throw new ArgumentException("Visibility mask dimensions differ."); }
    }
}
