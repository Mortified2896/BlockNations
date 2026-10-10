using System;
using System.Collections.Generic;

namespace BlockNations.Simulation
{
    public sealed class SimulationUnit
    {
        public int Id { get; }
        public int Seat { get; }
        public UnitDefinition Definition { get; }
        public int Position { get; internal set; }
        public int Health { get; internal set; }
        public int MovesUsed { get; internal set; }
        public int AttacksUsed { get; internal set; }
        public int SurprisedRound { get; internal set; }
        public bool IsSurprised(int round) => SurprisedRound == round;
        public int RemainingMoves => SimulationRules.RemainingMoves(Definition.UsesCommittedMoveAction, Definition.MaxMovesPerTurn, MovesUsed,
            Definition.AttackEndsMovement, AttacksUsed);
        public bool CanAttack => SimulationRules.CanAttack(Definition.CanAttackAfterMoving, Definition.MaxAttacksPerTurn, AttacksUsed, MovesUsed);

        public SimulationUnit(int id, int seat, int position, UnitDefinition definition, int health = -1, int movesUsed = 0, int attacksUsed = 0,
            int surprisedRound = -1)
        {
            if (id == 0) throw new ArgumentOutOfRangeException(nameof(id));
            Id = id; Seat = seat; Position = position;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Health = health < 0 ? definition.MaxHealthUnits : Math.Min(definition.MaxHealthUnits, health);
            MovesUsed = SimulationRules.Consume(movesUsed, definition.MaxMovesPerTurn, 0);
            AttacksUsed = SimulationRules.Consume(attacksUsed, definition.MaxAttacksPerTurn, 0);
            SurprisedRound = surprisedRound;
        }
        internal SimulationUnit Copy() => new SimulationUnit(Id, Seat, Position, Definition, Health, MovesUsed, AttacksUsed, SurprisedRound);
    }

    public sealed class SimulationCity
    {
        public int Id { get; }
        public int Position { get; }
        public int Seat { get; internal set; }
        public bool Recruited { get; internal set; }
        public SimulationCity(int id, int seat, int position, bool recruited = false)
        { if (id == 0) throw new ArgumentOutOfRangeException(nameof(id)); Id = id; Seat = seat; Position = position; Recruited = recruited; }
        internal SimulationCity Copy() => new SimulationCity(Id, Seat, Position, Recruited);
    }

    // Complete world state. This object never goes directly to a policy or spectator.
    public sealed class MatchState
    {
        private readonly Dictionary<int, SimulationUnit> units = new Dictionary<int, SimulationUnit>();
        private readonly Dictionary<int, SimulationCity> cities = new Dictionary<int, SimulationCity>();
        private readonly SimulationUnit[] occupancy;
        private readonly SimulationCity[] cityAt;
        private readonly Dictionary<string, UnitDefinition> roster = new Dictionary<string, UnitDefinition>(StringComparer.Ordinal);
        private readonly List<UnitDefinition> orderedRoster = new List<UnitDefinition>();
        private readonly int[] gold;
        private int nextUnitId = 1;
        public int Width { get; }
        public int Height { get; }
        public int SeatCount => gold.Length;
        public int CityVision { get; }
        public int IncomePerCity { get; }
        public int CurrentTurnSeat { get; internal set; }
        public int Round { get; internal set; }
        public int FirstSeat { get; }
        public bool GameOver { get; internal set; }
        public int WinnerSeat { get; internal set; } = -1;
        public int CapturedCityId { get; internal set; }
        public bool[] Tiles { get; }
        public ISeatRelationships Relationships { get; }
        public IEnumerable<SimulationUnit> Units => units.Values;
        public IEnumerable<SimulationCity> Cities => cities.Values;
        public IReadOnlyList<UnitDefinition> Roster => orderedRoster;
        public MatchRuleProfile RuleProfile { get; }
        public string RulesVersion => RuleProfile?.Version ?? SimulationRules.Version;
        public bool IsRecruitEnabled(string typeId) => typeId != null && roster.ContainsKey(typeId) && (RuleProfile == null || RuleProfile.CanRecruit(typeId));

        public MatchState(int width, int height, int seatCount, IEnumerable<UnitDefinition> recruitableUnits,
            int currentTurnSeat = 0, int round = 1, int cityVision = 1, int incomePerCity = 1,
            bool[] tiles = null, ISeatRelationships relationships = null, int firstSeat = 0, MatchRuleProfile ruleProfile = null)
        {
            if (width < 1 || height < 1 || seatCount < 1) throw new ArgumentOutOfRangeException(nameof(width));
            if (currentTurnSeat < 0 || currentTurnSeat >= seatCount || firstSeat < 0 || firstSeat >= seatCount)
                throw new ArgumentOutOfRangeException(nameof(currentTurnSeat));
            Width = width; Height = height; gold = new int[seatCount];
            occupancy = new SimulationUnit[checked(width * height)]; cityAt = new SimulationCity[occupancy.Length];
            if (tiles != null && tiles.Length != occupancy.Length) throw new ArgumentException("Tile mask dimensions differ.", nameof(tiles));
            Tiles = tiles == null ? new bool[occupancy.Length] : (bool[])tiles.Clone();
            if (tiles == null) for (int i = 0; i < Tiles.Length; i++) Tiles[i] = true;
            CurrentTurnSeat = currentTurnSeat; Round = Math.Max(1, round); FirstSeat = firstSeat;
            CityVision = Math.Max(0, cityVision); IncomePerCity = Math.Max(0, incomePerCity);
            Relationships = relationships ?? FreeForAllRelationships.Instance;
            RuleProfile = ruleProfile;
            foreach (UnitDefinition supplied in recruitableUnits ?? throw new ArgumentNullException(nameof(recruitableUnits)))
            {
                UnitDefinition definition = supplied;
                if (RuleProfile != null && !RuleProfile.TryGetDefinition(supplied.TypeId, out definition))
                    throw new ArgumentException("Unit catalog is outside the explicit rules profile.", nameof(recruitableUnits));
                roster.Add(definition.TypeId, definition); orderedRoster.Add(definition);
            }
            orderedRoster.Sort((a, b) => string.CompareOrdinal(a.TypeId, b.TypeId));
        }

        public bool IsTurnOwnedBySeat(int seat) => !GameOver && seat >= 0 && seat < SeatCount && CurrentTurnSeat == seat;
        public int Position(int x, int y) => y * Width + x;
        public bool Contains(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height && Tiles[Position(x, y)];
        public bool Contains(int position) => position >= 0 && position < Tiles.Length && Tiles[position];
        public SimulationUnit UnitAt(int position) => Contains(position) ? occupancy[position] : null;
        public SimulationCity CityAt(int position) => Contains(position) ? cityAt[position] : null;
        public SimulationUnit GetUnit(int id) => units.TryGetValue(id, out SimulationUnit unit) ? unit : null;
        public SimulationCity GetCity(int id) => cities.TryGetValue(id, out SimulationCity city) ? city : null;
        public int GoldForSeat(int seat) => seat >= 0 && seat < SeatCount ? gold[seat] : 0;
        public void SetGold(int seat, int value) { ValidateSeat(seat); gold[seat] = Math.Max(0, value); }
        // Compatibility adapters import existing saves without changing their format.
        public void RestoreOutcome(bool gameOver, int winnerSeat = -1)
        { GameOver = gameOver; WinnerSeat = winnerSeat; }
        public UnitDefinition RecruitType(string type) => type != null && roster.TryGetValue(type, out UnitDefinition definition) ? definition : null;

        public void AddUnit(SimulationUnit unit)
        {
            ValidateSeat(unit.Seat);
            if (!Contains(unit.Position) || occupancy[unit.Position] != null || unit.Health <= 0) throw new ArgumentException("Invalid unit placement.");
            units.Add(unit.Id, unit); occupancy[unit.Position] = unit;
            if (unit.Id >= nextUnitId) nextUnitId = checked(unit.Id + 1);
        }
        public void AddCity(SimulationCity city)
        {
            ValidateSeat(city.Seat);
            if (!Contains(city.Position) || cityAt[city.Position] != null) throw new ArgumentException("Invalid city placement.");
            cities.Add(city.Id, city); cityAt[city.Position] = city;
        }
        internal SimulationUnit Recruit(int seat, int position, UnitDefinition definition)
        {
            while (units.ContainsKey(nextUnitId)) nextUnitId++;
            var unit = new SimulationUnit(nextUnitId++, seat, position, definition); AddUnit(unit); return unit;
        }
        internal void Move(SimulationUnit unit, int destination)
        { occupancy[unit.Position] = null; unit.Position = destination; occupancy[destination] = unit; }
        internal void Remove(SimulationUnit unit) { occupancy[unit.Position] = null; units.Remove(unit.Id); }
        private void ValidateSeat(int seat) { if (seat < 0 || seat >= SeatCount) throw new ArgumentOutOfRangeException(nameof(seat)); }

        public MatchState Copy()
        {
            var copy = new MatchState(Width, Height, SeatCount, Roster, CurrentTurnSeat, Round, CityVision, IncomePerCity, Tiles, Relationships, FirstSeat, RuleProfile)
            { GameOver = GameOver, WinnerSeat = WinnerSeat, CapturedCityId = CapturedCityId };
            for (int seat = 0; seat < SeatCount; seat++) copy.SetGold(seat, gold[seat]);
            foreach (SimulationCity city in Cities) copy.AddCity(city.Copy());
            foreach (SimulationUnit unit in Units) copy.AddUnit(unit.Copy());
            copy.nextUnitId = nextUnitId;
            return copy;
        }
    }
}
