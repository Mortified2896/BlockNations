using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockNations.Simulation.Poly
{
    public sealed partial class PolyMatch
    {
        public Dictionary<int, int[]> Paths(SimulationUnit unit, bool[] visible = null)
        {
            var paths = new Dictionary<int, int[]>();
            if (unit == null || !IsTurnOwnedBySeat(unit.Seat)) return paths;
            var info = UnitState(unit.Id);
            bool retreat = unit.Definition.TypeId == "poly-rider" && info.attacked && !info.retreatUsed;
            if (info.rested || (!retreat && (info.moved || info.attacked || info.attackStarted))) return paths;
            visible = visible ?? Visibility(unit.Seat);
            int budget = unit.Definition.MaxMovesPerTurn;
            int[] cost = Enumerable.Repeat(int.MaxValue, 121).ToArray(), previous = new int[121];
            cost[unit.Position] = 0; var frontier = new List<int> { unit.Position };
            while (frontier.Count > 0) {
                frontier.Sort((a, b) => cost[a] != cost[b] ? cost[a].CompareTo(cost[b]) : a.CompareTo(b));
                int p = frontier[0]; frontier.RemoveAt(0);
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) {
                    int x = p % 11 + dx, y = p / 11 + dy;
                    if ((dx == 0 && dy == 0) || x < 0 || y < 0 || x >= 11 || y >= 11) continue;
                    int next = y * 11 + x; var occupant = Board.UnitAt(next);
                    if (occupant != null && (occupant.Seat == unit.Seat || visible[next])) continue;
                    // Unknown terrain is a hypothesis, never a hidden-state legality hint.
                    var known = memory[unit.Seat][next];
                    if (known?.terrain == PolyTerrain.Mountain && !HasTechnology(unit.Seat, "climbing")) continue;
                    int terrainCost = known != null && known.terrain != PolyTerrain.Plains && !known.road ? 2 : 1;
                    // One tile is always possible on passable rough terrain.
                    int candidate = cost[p] + (p == unit.Position ? Math.Min(budget, terrainCost) : terrainCost);
                    if (candidate > budget || candidate >= cost[next]) continue;
                    cost[next] = candidate; previous[next] = p; if (!frontier.Contains(next)) frontier.Add(next);
                }
            }
            for (int p = 0; p < 121; p++) if (p != unit.Position && cost[p] <= budget) {
                var path = new List<int>(); int step = p;
                while (step != unit.Position) { path.Add(step); step = previous[step]; }
                path.Reverse(); paths.Add(p, path.ToArray());
            }
            return paths;
        }
        private void Move(PolyAction action)
        {
            var unit = Board.GetUnit(action.ActorId); var info = UnitState(unit.Id);
            int[] path = Paths(unit)[action.Destination]; int spent = 0;
            foreach (int p in path) {
                int terrainCost = Tiles[p].terrain != PolyTerrain.Plains && !Tiles[p].road ? 2 : 1;
                terrainCost = spent == 0 ? Math.Min(terrainCost, unit.Definition.MaxMovesPerTurn) : terrainCost;
                if (Board.UnitAt(p) != null || (Tiles[p].terrain == PolyTerrain.Mountain && !HasTechnology(unit.Seat, "climbing"))) {
                    info.attacked = true; break;
                }
                if (spent + terrainCost > unit.Definition.MaxMovesPerTurn) break;
                spent += terrainCost; Board.Move(unit, p);
            }
            if (info.attacked) info.retreatUsed = true;
            info.moved = true;
        }
        private double DefenseBonus(SimulationUnit unit)
        {
            var city = CityAt(unit.Position);
            if (city?.ownerSeatIndex == unit.Seat) return 1.5 + .5 * Math.Min(3, city.walls);
            return Tiles[unit.Position].terrain == PolyTerrain.Forest && HasTechnology(unit.Seat, "archery") ||
                Tiles[unit.Position].terrain == PolyTerrain.Mountain && HasTechnology(unit.Seat, "climbing") ? 1.5 : 1;
        }
        public int Damage(SimulationUnit attacker, SimulationUnit defender, int healthOverride = -1)
        {
            double attack = attacker.Definition.AttackUnits / 10.0;
            double force = attack * (healthOverride < 0 ? attacker.Health : healthOverride) / attacker.Definition.MaxHealthUnits;
            double defense = defender.Definition.DefenseUnits / 10.0 * defender.Health / defender.Definition.MaxHealthUnits * DefenseBonus(defender);
            return Math.Max(10, (int)Math.Round(4.5 * attack * force / Math.Max(0.01, force + defense), MidpointRounding.AwayFromZero) * 10);
        }
        private void Remove(SimulationUnit unit) { Board.Remove(unit); unitState.Remove(unit.Id); }
        private void Attack(PolyAction action)
        {
            var attacker = Board.GetUnit(action.ActorId); var target = Board.UnitAt(action.Destination); var info = UnitState(attacker.Id);
            info.attacked = true; info.attackStarted = true;
            target.Health = Math.Max(0, target.Health - action.Damage);
            if (target.Health == 0) {
                int destination = target.Position; Remove(target); info.kills++;
                if (attacker.Definition.AttackRange == 1) Board.Move(attacker, destination);
                if (attacker.Definition.TypeId == "poly-knight") info.attacked = false;
            } else {
                attacker.Health = Math.Max(0, attacker.Health - action.Retaliation);
                if (attacker.Health == 0) Remove(attacker);
            }
        }
    }
}
