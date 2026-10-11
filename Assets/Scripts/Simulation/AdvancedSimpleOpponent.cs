using System;
using System.Linq;

namespace BlockNations.Simulation
{
    // Small deterministic human-playtest opponent. No policy model, training, or hidden unit inputs.
    public sealed class AdvancedSimpleOpponent
    {
        public int Seat { get; }
        public AdvancedSimpleOpponent(int seat) { Seat = seat; }

        // Apply one bounded action so the renderer can show the opponent's turn incrementally.
        public bool Step(AdvancedMatch match, out string description)
        {
            description = null;
            MatchState board = match.Board;
            if (!board.IsTurnOwnedBySeat(Seat)) return false;
            var home = board.Cities.FirstOrDefault(c => c.Seat == Seat);
            if (home == null) return End(match, out description);
            var city = match.CityEconomy(home.Id);
            var forecast = match.Forecast(home.Id);
            int treasury = board.GoldForSeat(Seat);

            // Avoid a preventable supply collapse, or restore a depleted pairing pool at home.
            var stationed = board.UnitAt(home.Position);
            if (stationed != null && (forecast.FoodBalance < 0 || match.GoldIncome(Seat) < match.GoldUpkeep(Seat)) &&
                match.TryDisband(Seat, stationed.Id, out _))
            { description = "The opponent demobilized a soldier to recover its economy."; return true; }

            CityUpgrade? upgrade = forecast.FoodBalance < 1 && city.FoodLevel < AdvancedEconomyRules.MaxUpgradeLevel &&
                match.Forecast(home.Id, upgrade: CityUpgrade.Food).FoodProduced > forecast.FoodProduced ? CityUpgrade.Food :
                forecast.Residents >= forecast.Housing - 1 && forecast.Pairs > 0 ? CityUpgrade.Housing :
                city.GoldLevel < AdvancedEconomyRules.MaxUpgradeLevel &&
                match.Forecast(home.Id, upgrade: CityUpgrade.Gold).GoldProduced > forecast.GoldProduced ? CityUpgrade.Gold : (CityUpgrade?)null;
            if (upgrade.HasValue && (treasury >= match.Rules.UpgradeCost(city, upgrade.Value) + 2 || forecast.FoodBalance < 0) &&
                match.TryUpgrade(Seat, home.Id, upgrade.Value, out _))
            { description = $"The opponent upgraded {upgrade.Value.ToString().ToLowerInvariant()}."; return true; }

            // Legal masks contain only current visibility; known starting cities supply the strategic goal.
            var enemyCity = board.Cities.FirstOrDefault(c => board.Relationships.IsHostile(Seat, c.Seat));
            var actions = board.Units.Where(u => u.Seat == Seat)
                .SelectMany(u => MatchEngine.LegalUnitActions(board, Seat, u.Id)).ToArray();
            MatchLegalAction best = null; int bestScore = 0;
            foreach (var action in actions)
            {
                var command = action.Command; var unit = board.GetUnit(command.ActorId);
                int score = 0;
                if (command.Kind == MatchActionKind.Attack)
                {
                    var target = board.GetUnit(command.TargetId);
                    int damage = SimulationRules.Damage(unit.Definition.AttackUnits, target.Definition.DefenseUnits);
                    score = 100 + (damage >= target.Health ? 80 : 0) + (board.CityAt(target.Position) != null ? 20 : 0);
                }
                else if (command.Kind == MatchActionKind.Move && enemyCity != null)
                {
                    int before = Distance(board, unit.Position, enemyCity.Position);
                    int after = Distance(board, command.Destination, enemyCity.Position);
                    score = (before - after) * 10 + (unit.Position == home.Position ? 5 : 0);
                    if (command.Destination == enemyCity.Position) score += 200;
                }
                // Enumeration and explicit tie-breaks make repeated playtests reproducible.
                if (score > bestScore || score == bestScore && score > 0 && best != null &&
                    (command.ActorId < best.Command.ActorId || command.ActorId == best.Command.ActorId && command.Destination < best.Command.Destination))
                { best = action; bestScore = score; }
            }
            if (best != null && match.Apply(best.Command).Applied)
            { description = best.Command.Kind == MatchActionKind.Attack ? "The opponent attacked." : "The opponent moved."; return true; }

            // Sustain recruits with civilian income and food; preserve both civilian sex pools where possible.
            PopulationSex sex = city.Men >= city.Women ? PopulationSex.Male : PopulationSex.Female;
            if (city.Available(sex) > 1)
            {
                var next = match.Forecast(home.Id, sex);
                if (next.FoodBalance >= 0 && next.GoldProduced >= match.GoldUpkeep(Seat) + 1)
                {
                    int count = board.Units.Count(u => u.Seat == Seat);
                    string type = count % 3 == 1 ? UnitRegistry.ArcherTypeId : count % 3 == 2 ? UnitRegistry.RiderTypeId : UnitRegistry.WarriorTypeId;
                    if (match.TryRecruit(Seat, home.Id, type, sex, out _))
                    { description = $"The opponent recruited a {sex.ToString().ToLowerInvariant()} {type}."; return true; }
                }
            }
            return End(match, out description);
        }
        private bool End(AdvancedMatch match, out string description)
        { description = "Your turn."; return match.Apply(new MatchCommand(MatchActionKind.EndTurn, Seat)).Applied; }
        private static int Distance(MatchState board, int a, int b) =>
            SimulationRules.Distance(a % board.Width, a / board.Width, b % board.Width, b / board.Width);
    }
}
