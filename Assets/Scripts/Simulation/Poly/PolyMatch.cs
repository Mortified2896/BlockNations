using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockNations.Simulation.Poly
{
    // Owns Poly transitions and economy. The established kernel supplies live visibility only.
    public sealed partial class PolyMatch
    {
        public MatchState Board { get; private set; }
        public PolyTile[] Tiles { get; private set; }
        public List<PolyCity> Cities { get; private set; }
        private readonly Dictionary<int, PolyUnitSave> unitState = new Dictionary<int, PolyUnitSave>();
        private readonly HashSet<string>[] technologies = { new HashSet<string>(), new HashSet<string>() };
        private readonly PolyTile[][] memory = { new PolyTile[121], new PolyTile[121] };
        private readonly int[][] knownOwners = { new int[121], new int[121] }, knownLevels = { new int[121], new int[121] };
        public int Seed { get; private set; }
        public int currentTurnSeatIndex => Board.CurrentTurnSeat;
        public bool IsTurnOwnedBySeat(int seatIndex) => Board.IsTurnOwnedBySeat(seatIndex);
        public bool HasTechnology(int seatIndex, string id) => seatIndex >= 0 && seatIndex < 2 && (id == null || technologies[seatIndex].Contains(id));
        public int Income(int seatIndex) => Cities.Where(c => c.ownerSeatIndex == seatIndex).Sum(c => c.Income);
        public PolyCity City(int id) => Cities.FirstOrDefault(c => c.id == id);
        public PolyCity CityAt(int position) => Cities.FirstOrDefault(c => c.position == position);
        public PolyUnitSave UnitState(int id) => unitState.TryGetValue(id, out var value) ? value : null;
        public bool[] Visibility(int seatIndex) => MatchEngine.Visibility(Board, seatIndex);
        public PolyTile KnownTile(int seatIndex, int position) => memory[seatIndex][position]?.Copy();
        public int KnownCityOwner(int seatIndex, int position) => knownOwners[seatIndex][position];
        public int KnownCityLevel(int seatIndex, int position) => knownLevels[seatIndex][position];

        private PolyMatch() { }
        public PolyMatch(int seed)
        {
            Seed = seed; var random = new Random(seed);
            Board = new MatchState(11, 11, 2, PolyRules.Units, incomePerCity: 0);
            Tiles = new PolyTile[121]; Cities = new List<PolyCity>();
            for (int p = 0; p < 121; p++) {
                int roll = random.Next(100);
                var terrain = roll < 25 ? PolyTerrain.Forest : roll < 40 ? PolyTerrain.Mountain : PolyTerrain.Plains;
                Tiles[p] = new PolyTile { terrain = terrain, resource = terrain == PolyTerrain.Forest ? PolyResource.Animals :
                    terrain == PolyTerrain.Mountain ? PolyResource.Ore : random.Next(2) == 0 ? PolyResource.Fruit : PolyResource.Crops };
                knownOwners[0][p] = knownOwners[1][p] = -2;
            }
            int[] positions = { 12, 108, 56, 16, 60, 64, 104 };
            for (int i = 0; i < positions.Length; i++) {
                var city = new PolyCity { id = i + 1, position = positions[i], ownerSeatIndex = i < 2 ? i : -1 };
                Cities.Add(city);
                for (int y = city.position / 11 - 1; y <= city.position / 11 + 1; y++)
                    for (int x = city.position % 11 - 1; x <= city.position % 11 + 1; x++) Tiles[y * 11 + x].cityId = city.id;
                Tiles[city.position].terrain = PolyTerrain.Plains; Tiles[city.position].resource = PolyResource.None;
                // Every opening has all three economic terrain branches and a clear escape route.
                Tiles[city.position + 1].terrain = PolyTerrain.Plains; Tiles[city.position + 1].resource = PolyResource.Fruit;
                Tiles[city.position - 1].terrain = PolyTerrain.Plains; Tiles[city.position - 1].resource = PolyResource.Crops;
                Tiles[city.position + 11].terrain = PolyTerrain.Forest; Tiles[city.position + 11].resource = PolyResource.Animals;
                Tiles[city.position - 11].terrain = PolyTerrain.Mountain; Tiles[city.position - 11].resource = PolyResource.Ore;
                if (city.ownerSeatIndex >= 0) {
                    Board.AddCity(new SimulationCity(city.id, city.ownerSeatIndex, city.position));
                    Spawn(city.ownerSeatIndex, city.position, "poly-warrior", city.id, false);
                }
            }
            Board.SetGold(0, 7); Board.SetGold(1, 6); UpdateMemory();
        }
        private void Spawn(int seatIndex, int position, string type, int cityId, bool exhausted = true)
        {
            SimulationUnit unit = Board.Recruit(seatIndex, position, PolyRules.Definition(type));
            unitState.Add(unit.Id, new PolyUnitSave { id = unit.Id, homeCityId = cityId, moved = exhausted, attacked = exhausted, rested = exhausted });
        }
        private void UpdateMemory()
        {
            for (int seat = 0; seat < 2; seat++) {
                bool[] visible = Visibility(seat);
                for (int p = 0; p < 121; p++) if (visible[p]) {
                    memory[seat][p] = Tiles[p].Copy();
                    var city = CityAt(p); if (city != null) { knownOwners[seat][p] = city.ownerSeatIndex; knownLevels[seat][p] = city.level; }
                }
            }
        }
        public int ResearchCost(int seatIndex, string id)
        {
            var tech = PolyRules.Technology(id);
            return tech == null ? int.MaxValue : tech.Tier * (4 + Cities.Count(c => c.ownerSeatIndex == seatIndex));
        }
        public List<PolyAction> LegalActions(int seatIndex)
        {
            var actions = new List<PolyAction>();
            if (!IsTurnOwnedBySeat(seatIndex)) return actions;
            var pending = Cities.Where(c => c.ownerSeatIndex == seatIndex && c.pendingRewards > 0).OrderBy(c => c.id).FirstOrDefault();
            if (pending != null) {
                int earnedLevel = pending.level - pending.pendingRewards + 1;
                string[] choices = earnedLevel >= 5 ? new[] { "giant", "treasury" } : earnedLevel == 3 ? new[] { "gold", "population" } : new[] { "workshop", "walls" };
                foreach (string choice in choices) if (choice != "giant" || GiantPosition(pending) >= 0)
                    actions.Add(new PolyAction(PolyActionKind.Reward, seatIndex, pending.id, choice: choice) { Label = RewardLabel(choice) });
                return actions;
            }
            bool[] visible = Visibility(seatIndex); int gold = Board.GoldForSeat(seatIndex);
            foreach (SimulationUnit unit in Board.Units.Where(u => u.Seat == seatIndex).OrderBy(u => u.Id)) {
                var info = UnitState(unit.Id); if (info.rested) continue;
                foreach (int destination in Paths(unit, visible).Keys.OrderBy(p => p)) actions.Add(new PolyAction(PolyActionKind.Move, seatIndex, unit.Id, destination));
                if (!info.attacked && (unit.Definition.CanAttackAfterMoving || !info.moved)) {
                    foreach (SimulationUnit target in Board.Units.Where(u => Board.Relationships.IsHostile(seatIndex, u.Seat) && visible[u.Position]).OrderBy(u => u.Id))
                        if (Distance(unit.Position, target.Position) <= unit.Definition.AttackRange) {
                            int damage = Damage(unit, target);
                            actions.Add(new PolyAction(PolyActionKind.Attack, seatIndex, unit.Id, target.Position) {
                                Damage = Math.Min(target.Health, damage), Retaliation = target.Health <= damage || target.Definition.TypeId == "poly-catapult" || Distance(unit.Position, target.Position) > target.Definition.AttackRange ? 0 : Damage(target, unit, target.Health - damage) });
                        }
                }
                if (!info.moved && !info.attackStarted && !info.attacked) {
                    var city = CityAt(unit.Position);
                    if (city != null && city.ownerSeatIndex != seatIndex) actions.Add(new PolyAction(PolyActionKind.Capture, seatIndex, unit.Id));
                    if (unit.Health < unit.Definition.MaxHealthUnits) actions.Add(new PolyAction(PolyActionKind.Heal, seatIndex, unit.Id));
                }
            }
            foreach (PolyCity city in Cities.Where(c => c.ownerSeatIndex == seatIndex).OrderBy(c => c.id)) {
                if (!city.recruited && Board.UnitAt(city.position) == null && Board.Units.Count(u => u.Seat == seatIndex && UnitState(u.Id).homeCityId == city.id) < city.level + 2)
                    foreach (var unit in PolyRules.Units.Where(u => u.TypeId != "poly-giant" && HasTechnology(seatIndex, PolyRules.UnitTechnology(u.TypeId)) && u.RecruitCost <= gold))
                        actions.Add(new PolyAction(PolyActionKind.Recruit, seatIndex, city.id, choice: unit.TypeId) { Cost = unit.RecruitCost, Label = unit.DisplayName });
            }
            for (int p = 0; p < 121; p++) if (visible[p] && City(Tiles[p].cityId)?.ownerSeatIndex == seatIndex && CityAt(p) == null &&
                (Board.UnitAt(p) == null || Board.UnitAt(p).Seat == seatIndex)) {
                foreach (PolyAction action in DevelopActions(seatIndex, p)) if (action.Cost <= gold) actions.Add(action);
            }
            foreach (var tech in PolyRules.Technologies) if (!HasTechnology(seatIndex, tech.Id) && HasTechnology(seatIndex, tech.Parent) && ResearchCost(seatIndex, tech.Id) <= gold)
                actions.Add(new PolyAction(PolyActionKind.Research, seatIndex, choice: tech.Id) { Cost = ResearchCost(seatIndex, tech.Id), Label = tech.Name });
            actions.Add(new PolyAction(PolyActionKind.EndTurn, seatIndex));
            return actions;
        }
        public bool TryApply(PolyAction request, out string message)
        {
            message = "That action is unavailable. Check ownership, technology, Gold and remaining actions.";
            if (request == null) return false;
            PolyAction action = LegalActions(request.SeatIndex).FirstOrDefault(a => a.Same(request));
            if (action == null) return false;
            int seat = action.SeatIndex;
            switch (action.Kind) {
                case PolyActionKind.EndTurn:
                    int next = 1 - seat; Board.CurrentTurnSeat = next; if (next == 0) Board.Round++;
                    foreach (var u in Board.Units.Where(u => u.Seat == next)) { var s = UnitState(u.Id); s.moved = s.attacked = s.rested = s.retreatUsed = s.attackStarted = false; }
                    foreach (var c in Cities.Where(c => c.ownerSeatIndex == next)) c.recruited = false;
                    Board.SetGold(next, checked(Board.GoldForSeat(next) + Income(next))); message = next == 0 ? "Your turn." : "Opponent's turn."; break;
                case PolyActionKind.Research:
                    Pay(seat, action.Cost); technologies[seat].Add(action.Choice); message = action.Label + " researched."; break;
                case PolyActionKind.Develop:
                    Pay(seat, action.Cost); Develop(action); message = action.Label + ": +" + action.Population + " population."; break;
                case PolyActionKind.Recruit:
                    var city = City(action.ActorId); Pay(seat, action.Cost); Spawn(seat, city.position, action.Choice, city.id); city.recruited = true;
                    message = action.Label + " recruited. Ready next turn."; break;
                case PolyActionKind.Reward:
                    Reward(action); message = action.Label + " chosen."; break;
                case PolyActionKind.Move:
                    Move(action); message = "Moved. Capture a village or enemy city at the start of your next turn."; break;
                case PolyActionKind.Attack:
                    Attack(action); message = "Attack: " + (action.Damage / 10f).ToString("0.#") + " damage."; break;
                case PolyActionKind.Heal:
                    var heal = Board.GetUnit(action.ActorId); heal.Health = Math.Min(heal.Definition.MaxHealthUnits, heal.Health + (City(Tiles[heal.Position].cityId)?.ownerSeatIndex == seat ? 40 : 20));
                    UnitState(heal.Id).rested = true; message = "Rested and recovered health."; break;
                case PolyActionKind.Capture:
                    var captor = Board.GetUnit(action.ActorId); var captured = CityAt(captor.Position); captured.ownerSeatIndex = seat; captured.recruited = true;
                    var kernelCity = Board.GetCity(captured.id); if (kernelCity == null) Board.AddCity(new SimulationCity(captured.id, seat, captured.position)); else kernelCity.Seat = seat;
                    UnitState(captor.Id).rested = true;
                    if (!Cities.Any(c => c.ownerSeatIndex == 1 - seat)) Board.RestoreOutcome(true, seat);
                    message = Board.GameOver ? (seat == 0 ? "Victory! Every opposing city is yours." : "Defeat. Your last city was captured.") : "City captured. Its fixed territory and income are now yours."; break;
            }
            UpdateMemory(); return true;
        }
        private void Pay(int seat, int amount) => Board.SetGold(seat, Board.GoldForSeat(seat) - amount);
        public static int Distance(int a, int b) => Math.Max(Math.Abs(a % 11 - b % 11), Math.Abs(a / 11 - b / 11));
    }
}
