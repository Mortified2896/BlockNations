using System;
using System.Collections.Generic;
using System.Linq;
using BlockNations.Simulation;

namespace BlockNations.Training
{
    // Spectator transport only. These complete-state records never enter policy tensors.
    [Serializable] public sealed class TraceCity { public int id, seat, position; public bool recruited; }
    [Serializable] public sealed class TraceUnit
    {
        public int id, seat, position, health, movesUsed, attacksUsed;
        public int surprisedRound = -1;
        public string type;
    }
    [Serializable] public sealed class TrainingTraceState
    {
        public int width, height, seat, round, firstSeat, cityVision, incomePerCity, winner;
        public bool gameOver, terminal;
        public int[] gold;
        public bool[] tiles;
        public TraceCity[] cities;
        public TraceUnit[] units;
        public string description;

        public static TrainingTraceState Capture(MatchState state, string description, bool terminal = false) =>
            new TrainingTraceState {
                width = state.Width, height = state.Height, seat = state.CurrentTurnSeat, round = state.Round,
                firstSeat = state.FirstSeat, cityVision = state.CityVision, incomePerCity = state.IncomePerCity,
                winner = state.WinnerSeat, gameOver = state.GameOver, terminal = terminal,
                gold = Enumerable.Range(0, state.SeatCount).Select(state.GoldForSeat).ToArray(),
                tiles = (bool[])state.Tiles.Clone(), description = description,
                cities = state.Cities.Select(c => new TraceCity { id = c.Id, seat = c.Seat, position = c.Position, recruited = c.Recruited }).ToArray(),
                units = state.Units.Select(u => new TraceUnit { id = u.Id, seat = u.Seat, position = u.Position,
                    type = u.Definition.TypeId, health = u.Health, movesUsed = u.MovesUsed, attacksUsed = u.AttacksUsed,
                    surprisedRound = u.SurprisedRound }).ToArray()
            };

        public MatchState Restore(IEnumerable<UnitDefinition> roster)
        {
            if (width < 1 || height != width || gold == null || gold.Length != 2 || tiles == null || tiles.Length != width * height ||
                cities == null || cities.Length > width * height || units == null || units.Length > width * height || description?.Length > 1024)
                throw new ArgumentException("Invalid spectator state.");
            var state = new MatchState(width, height, 2, roster, seat, round, cityVision, incomePerCity, tiles, firstSeat: firstSeat);
            for (int owner = 0; owner < 2; owner++) state.SetGold(owner, gold[owner]);
            foreach (TraceCity city in cities) state.AddCity(new SimulationCity(city.id, city.seat, city.position, city.recruited));
            foreach (TraceUnit unit in units)
                state.AddUnit(new SimulationUnit(unit.id, unit.seat, unit.position,
                    state.RecruitType(unit.type) ?? throw new ArgumentException("Unknown replay unit: " + unit.type),
                    unit.health, unit.movesUsed, unit.attacksUsed, unit.surprisedRound));
            state.RestoreOutcome(gameOver || terminal, winner);
            return state;
        }
    }

    [Serializable] public sealed class PolicyAssignment
    {
        public int learningSeat;
        public string learner, opponent;
        public bool SameAs(PolicyAssignment other) => other != null && learningSeat == other.learningSeat && learner == other.learner && opponent == other.opponent;
    }
    [Serializable] public sealed class TrainingTrace
    {
        public const int Version = 1, MaximumFrames = 256, MaximumPieces = 8192;
        public int version = Version, worker, match, boardSize, firstSeat;
        public string session, rulesVersion;
        public bool truncated, policyChanged;
        public PolicyAssignment policy;
        public List<TrainingTraceState> frames = new List<TrainingTraceState>();
        private int pieces;
        public void Record(TrainingTraceState frame)
        {
            int count = frame.units.Length + frame.cities.Length;
            if (frame.terminal)
            {
                while (frames.Count > 0 && (frames.Count >= MaximumFrames || pieces + count > MaximumPieces))
                { pieces -= frames[frames.Count - 1].units.Length + frames[frames.Count - 1].cities.Length; frames.RemoveAt(frames.Count - 1); truncated = true; }
            }
            else if (frames.Count >= MaximumFrames - 1 || pieces + count > MaximumPieces) { truncated = true; return; }
            frames.Add(frame); pieces += count;
        }
    }
}
