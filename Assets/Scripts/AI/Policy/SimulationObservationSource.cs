using System;
using System.Collections.Generic;
using BlockNations.Simulation;

namespace BlockNations.AI
{
    // Information history belongs to an observer, not to the complete world state.
    // Stable simulator IDs are only adapter keys; the network gets local indices.
    public sealed class SimulationObservationSource
    {
        public sealed class Context
        {
            public AIObservation Observation;
            public readonly Dictionary<string, MatchCommand> Commands = new Dictionary<string, MatchCommand>(StringComparer.Ordinal);
            public int[] UnitIds, CityIds;
        }
        private sealed class Knowledge
        {
            public bool[] Seen;
            public readonly Dictionary<int, AICityState> Cities = new Dictionary<int, AICityState>();
        }
        private readonly Dictionary<int, Knowledge> knowledge = new Dictionary<int, Knowledge>();
        private AICityState[] publicCities = Array.Empty<AICityState>();
        public void ResetKnowledge() { knowledge.Clear(); publicCities = Array.Empty<AICityState>(); }
        public void SetPublicStartingCities(AICityState[] cities)
        {
            knowledge.Clear(); publicCities = (AICityState[])cities.Clone();
            for (int i = 0; i < publicCities.Length; i++) { publicCities[i].Recruited = false; publicCities[i].CurrentlyVisible = false; }
        }

        public Context Observe(MatchState state, int seat, bool includeZeroDamageAttacks = true, ISet<string> excludedActions = null, bool[] previouslySeen = null)
        {
            if (seat < 0 || seat >= state.SeatCount) throw new ArgumentOutOfRangeException(nameof(seat));
            if (!knowledge.TryGetValue(seat, out Knowledge memory))
            {
                knowledge[seat] = memory = new Knowledge { Seen = new bool[state.Tiles.Length] };
                foreach (AICityState city in publicCities)
                    if (state.Contains(city.X, city.Y)) memory.Cities[city.Position(state.Width)] = city;
            }
            if (memory.Seen.Length != state.Tiles.Length) throw new InvalidOperationException("Reset knowledge before changing boards.");
            bool[] visible = MatchEngine.Visibility(state, seat);
            for (int i = 0; i < visible.Length; i++) memory.Seen[i] |= visible[i] || (previouslySeen != null && previouslySeen[i]);
            var observation = new AIObservation { Width = state.Width, Height = state.Height, Seat = seat,
                Gold = state.GoldForSeat(seat), CityVision = state.CityVision, Round = state.Round, IncomePerCity = state.IncomePerCity,
                Tiles = (bool[])state.Tiles.Clone(), Seen = (bool[])memory.Seen.Clone(), Visible = visible };
            var units = new List<SimulationUnit>();
            foreach (SimulationUnit unit in state.Units) if (unit.Seat == seat || visible[unit.Position]) units.Add(unit);
            units.Sort((a, b) =>
            {
                int order = a.Seat.CompareTo(b.Seat);
                if (order == 0) order = (a.Position / state.Width).CompareTo(b.Position / state.Width);
                if (order == 0) order = (a.Position % state.Width).CompareTo(b.Position % state.Width);
                return order != 0 ? order : string.CompareOrdinal(a.Definition.TypeId, b.Definition.TypeId);
            });
            var unitIndices = new Dictionary<int, int>();
            var context = new Context { Observation = observation, UnitIds = new int[units.Count] };
            observation.Units = new AIUnitState[units.Count];
            for (int i = 0; i < units.Count; i++)
            {
                SimulationUnit unit = units[i];
                context.UnitIds[i] = unit.Id; unitIndices.Add(unit.Id, i);
                AIUnitState projected = UnitCapabilities(unit.Definition, unit.Seat);
                projected.X = unit.Position % state.Width; projected.Y = unit.Position / state.Width;
                projected.Health = unit.Health;
                // Opponent action budgets are private. Neutral constants cannot reveal
                // a hidden turn, recruitment, or stable unit identity through counters.
                if (unit.Seat == seat) { projected.MovesUsed = unit.MovesUsed; projected.AttacksUsed = unit.AttacksUsed; }
                observation.Units[i] = projected;
            }
            var positions = new List<int>(memory.Cities.Keys);
            foreach (int p in positions) { AICityState city = memory.Cities[p]; city.CurrentlyVisible = false; memory.Cities[p] = city; }
            var liveCities = new Dictionary<int, SimulationCity>();
            foreach (SimulationCity city in state.Cities)
                if (city.Seat == seat || visible[city.Position])
                {
                    memory.Cities[city.Position] = new AICityState { Seat = city.Seat, X = city.Position % state.Width,
                        Y = city.Position / state.Width, Recruited = city.Seat == seat && city.Recruited, CurrentlyVisible = true };
                    liveCities[city.Position] = city;
                }
            positions = new List<int>(memory.Cities.Keys); positions.Sort();
            observation.Cities = new AICityState[positions.Count]; context.CityIds = new int[positions.Count];
            var cityIndices = new Dictionary<int, int>();
            for (int i = 0; i < positions.Count; i++)
            {
                observation.Cities[i] = memory.Cities[positions[i]];
                if (liveCities.TryGetValue(positions[i], out SimulationCity city)) { context.CityIds[i] = city.Id; cityIndices.Add(city.Id, i); }
            }
            var hostileSeats = new List<int>();
            for (int other = 0; other < state.SeatCount; other++) if (state.Relationships.IsHostile(seat, other)) hostileSeats.Add(other);
            observation.HostileSeats = hostileSeats.ToArray();
            observation.RecruitTypes = new AIUnitState[state.Roster.Count];
            for (int i = 0; i < state.Roster.Count; i++) observation.RecruitTypes[i] = UnitCapabilities(state.Roster[i], seat);
            var roots = new List<AIAction>();
            foreach (MatchLegalAction legal in MatchEngine.LegalActions(state, seat, visible))
            {
                MatchCommand command = legal.Command;
                AIAction action = new AIAction { Kind = (AIActionKind)command.Kind, Actor = -1, Target = -1,
                    Destination = command.Kind == MatchActionKind.EndTurn ? 0 : command.Destination };
                if (command.Kind == MatchActionKind.Move || command.Kind == MatchActionKind.Attack)
                {
                    action.Actor = unitIndices[command.ActorId]; action.MoveCost = legal.Path.Length;
                    if (command.Kind == MatchActionKind.Attack)
                    {
                        action.Target = unitIndices[command.TargetId];
                        if (!includeZeroDamageAttacks && SimulationRules.Damage(units[action.Actor].Definition.AttackUnits,
                            units[action.Target].Definition.DefenseUnits) <= 0) continue;
                    }
                }
                else if (command.Kind == MatchActionKind.Recruit)
                {
                    action.Actor = cityIndices[command.ActorId];
                    for (int i = 0; i < state.Roster.Count; i++) if (state.Roster[i].TypeId == command.RecruitType) { action.RecruitType = i; break; }
                }
                if (excludedActions != null && excludedActions.Contains(action.Key)) continue;
                roots.Add(action); context.Commands.Add(action.Key, command);
            }
            roots.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key)); observation.LegalActions = roots.ToArray();
            return context;
        }

        public static AIUnitState UnitCapabilities(UnitDefinition definition, int seat) => new AIUnitState {
            Seat = seat, Type = definition.TypeId, Health = definition.MaxHealthUnits, MaxHealth = definition.MaxHealthUnits,
            Attack = definition.AttackUnits, Defense = definition.DefenseUnits, Range = definition.AttackRange,
            Vision = definition.VisionRange, MaxMoves = definition.MaxMovesPerTurn, MaxAttacks = definition.MaxAttacksPerTurn,
            AttackAfterMoving = definition.CanAttackAfterMoving, CommittedMove = definition.UsesCommittedMoveAction,
            AttackEndsMovement = definition.AttackEndsMovement, Cost = definition.RecruitCost };
    }
}
