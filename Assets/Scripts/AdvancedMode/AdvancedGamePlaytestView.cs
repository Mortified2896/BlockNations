using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using BlockNations.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlockNations.AdvancedMode
{
    // Explicitly wired by the dedicated build scene; no gameplay-scene search or auto-wiring.
    public sealed class AdvancedGamePlaytestView : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private VisualTreeAsset layout;
        [SerializeField] private StyleSheet stylesheet;
        [SerializeField] private Texture2D warriorImage, riderImage, archerImage;
        private AdvancedMatch match;
        private readonly AdvancedSimpleOpponent opponent = new AdvancedSimpleOpponent(1);
        private VisualElement board, cityActions, recruitActions;
        private Label turnLabel, goldLabel, populationLabel, foodLabel, growthLabel, selectionLabel, statusLabel, reportLabel;
        private Button endButton;
        private DropdownField sexField, balanceField;
        private int selectedUnit;
        private bool thinking;
        private string status;
        private readonly Button[] tiles = new Button[49];

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void AdvancedPublishState(string json);
#endif

        public void Configure(UIDocument doc, VisualTreeAsset tree, StyleSheet style, Texture2D warrior, Texture2D rider, Texture2D archer)
        { document = doc; layout = tree; stylesheet = style; warriorImage = warrior; riderImage = rider; archerImage = archer; }

        private void OnEnable()
        {
            if (document == null || layout == null || stylesheet == null)
            { Debug.LogError("Advanced playtest needs its serialized UI references."); return; }
            var root = document.rootVisualElement;
            root.Clear(); layout.CloneTree(root); root.styleSheets.Add(stylesheet);
            board = root.Q("Board"); cityActions = root.Q("CityActions"); recruitActions = root.Q("RecruitActions");
            turnLabel = root.Q<Label>("Turn"); goldLabel = root.Q<Label>("Gold"); populationLabel = root.Q<Label>("Population");
            foodLabel = root.Q<Label>("Food"); growthLabel = root.Q<Label>("Growth"); selectionLabel = root.Q<Label>("Selection");
            statusLabel = root.Q<Label>("Status"); reportLabel = root.Q<Label>("Report");
            endButton = root.Q<Button>("EndTurn"); endButton.clicked += EndTurn;
            root.Q<Button>("NewMatch").clicked += NewMatch;
            root.Q<Button>("Disband").clicked += Disband;
            sexField = root.Q<DropdownField>("RecruitSex"); sexField.choices = new List<string> { "Male population", "Female population" }; sexField.index = 0;
            sexField.RegisterValueChangedCallback(_ => Refresh());
            balanceField = root.Q<DropdownField>("Balance"); balanceField.choices = new List<string> { "Equal combat stats", "Class strength experiment" }; balanceField.index = 0;
            BuildBoard(); NewMatch();
        }

        private void BuildBoard()
        {
            board.Clear();
            for (int y = 6; y >= 0; y--)
            {
                var row = new VisualElement(); row.AddToClassList("board-row"); board.Add(row);
                for (int x = 0; x < 7; x++)
                {
                    int position = y * 7 + x;
                    var tile = new Button(() => ClickTile(position)); tile.name = $"Tile{position}"; tile.AddToClassList("tile");
                    row.Add(tile); tiles[position] = tile;
                }
            }
            board.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                float height = evt.newRect.width;
                if (Math.Abs(board.resolvedStyle.height - height) > 1) board.style.height = height;
            });
        }

        private void NewMatch()
        {
            StopAllCoroutines(); thinking = false; selectedUnit = 0;
            match = new AdvancedMatch(balanceField.index == 1 ? RecruitmentBalance.ClassStrengthExperiment : RecruitmentBalance.Equal);
            status = "Recruit at your city, select a unit, then tap a green tile to move or attack. Capture the red city to win.";
            Refresh();
        }

        private void ClickTile(int position)
        {
            if (thinking || !match.Board.IsTurnOwnedBySeat(0)) return;
            var visible = MatchEngine.Visibility(match.Board, 0);
            var unit = visible[position] ? match.Board.UnitAt(position) : null;
            if (unit != null && unit.Seat == 0) { selectedUnit = unit.Id; status = "Green tiles are legal moves or attacks."; }
            else
            {
                var action = MatchEngine.LegalUnitActions(match.Board, 0, selectedUnit)
                    .FirstOrDefault(a => a.Command.Destination == position);
                if (action != null)
                {
                    var result = match.Apply(action.Command);
                    status = result.HiddenBlocker ? "A hidden enemy blocked the move. The unit's attacks are spent." :
                        result.KilledUnitId != 0 ? "Enemy unit defeated. Its population is lost." :
                        result.Applied ? $"{action.Command.Kind} completed." : "That action is no longer legal.";
                }
                else { selectedUnit = 0; status = "Select one of your blue units, or use the city controls below."; }
            }
            Refresh();
        }

        private void Recruit(string type)
        {
            if (thinking) return;
            var sex = sexField.index == 0 ? PopulationSex.Male : PopulationSex.Female;
            if (match.TryRecruit(0, 1, type, sex, out string reason))
            { selectedUnit = match.Board.UnitAt(match.Board.GetCity(1).Position).Id; status = $"Recruited a {sex.ToString().ToLowerInvariant()} {type}: 2 gold and 1 population."; }
            else status = reason;
            Refresh();
        }

        private void Upgrade(CityUpgrade upgrade)
        {
            if (thinking) return;
            status = match.TryUpgrade(0, 1, upgrade, out string reason) ? $"{upgrade} upgraded. One city upgrade per turn." : reason;
            Refresh();
        }

        private void Disband()
        {
            if (thinking) return;
            bool applied = match.TryDisband(0, selectedUnit, out string reason);
            status = applied ? "Soldier returned to its home city's population. No gold refunded." : reason;
            if (applied) selectedUnit = 0;
            Refresh();
        }

        private void EndTurn()
        {
            if (thinking || !match.Apply(new MatchCommand(MatchActionKind.EndTurn, 0)).Applied) return;
            selectedUnit = 0; thinking = true; status = "Opponent's turn…"; Refresh(); StartCoroutine(PlayOpponent());
        }

        private IEnumerator PlayOpponent()
        {
            int budget = 128;
            while (match.Board.IsTurnOwnedBySeat(1) && budget-- > 0)
            {
                yield return new WaitForSeconds(.22f);
                if (!opponent.Step(match, out status)) break;
                Refresh();
            }
            if (match.Board.IsTurnOwnedBySeat(1)) match.Apply(new MatchCommand(MatchActionKind.EndTurn, 1));
            thinking = false; status = "Your turn."; Refresh();
        }

        private void Refresh()
        {
            if (match == null) return;
            var state = match.Board; var city = match.CityEconomy(1); var forecast = match.Forecast(1);
            bool canAct = !thinking && state.IsTurnOwnedBySeat(0);
            turnLabel.text = state.GameOver ? state.WinnerSeat == 0 ? "Victory — enemy city captured" : "Defeat — your city was captured" :
                $"Round {state.Round} · {(canAct ? "Your turn" : "Opponent's turn")}";
            goldLabel.text = $"Gold {state.GoldForSeat(0)}    +{match.GoldIncome(0)} income / −{match.GoldUpkeep(0)} upkeep per turn";
            populationLabel.text = $"{city.Men} male + {city.Women} female civilians · {forecast.Soldiers} soldiers · Housing {forecast.Residents}/{forecast.Housing}";
            foodLabel.text = $"Food {city.StoredFood}/{AdvancedEconomyRules.FoodStorage} stored · {forecast.FoodProduced} produced / {forecast.FoodConsumed} consumed · Net {forecast.FoodBalance:+0;-0;0}";
            bool growthPaused = forecast.Residents >= forecast.Housing || forecast.Pairs == 0 || forecast.FoodBalance <= 0;
            growthLabel.text = $"Growth {city.GrowthProgress}/{AdvancedEconomyRules.GrowthThreshold} · {forecast.Pairs} civilian pairs" +
                (growthPaused ? " · PAUSED: needs pairs, spare housing and food surplus" : $" · Next birth: {city.NextBirthSex}");
            bool[] visible = MatchEngine.Visibility(state, 0);
            var actions = MatchEngine.LegalUnitActions(state, 0, selectedUnit);
            for (int position = 0; position < 49; position++)
            {
                var tile = tiles[position]; tile.ClearClassList(); tile.AddToClassList("tile"); tile.style.backgroundImage = StyleKeyword.None;
                var unit = visible[position] ? state.UnitAt(position) : null; var settlement = state.CityAt(position);
                tile.text = settlement == null ? "" : settlement.Seat == 0 ? "YOUR CITY" : "RED CITY";
                if (!visible[position]) tile.AddToClassList("fog");
                if (settlement != null) tile.AddToClassList("city");
                if (unit != null)
                {
                    tile.AddToClassList(unit.Seat == 0 ? "friendly" : "enemy");
                    var soldier = match.Soldier(unit.Id);
                    tile.text = $"{unit.Definition.DisplayName[0]}·{(soldier?.Sex == PopulationSex.Female ? "F" : "M")}\n{CombatValues.FormatUnits(unit.Health)} HP";
                    Texture2D image = unit.Definition.TypeId == UnitRegistry.WarriorTypeId ? warriorImage :
                        unit.Definition.TypeId == UnitRegistry.RiderTypeId ? riderImage : archerImage;
                    if (image != null) tile.style.backgroundImage = new StyleBackground(image);
                }
                if (unit?.Id == selectedUnit) tile.AddToClassList("selected");
                if (canAct && actions.Any(a => a.Command.Destination == position)) tile.AddToClassList("legal");
                tile.SetEnabled(canAct);
            }
            var selected = state.GetUnit(selectedUnit);
            selectionLabel.text = selected == null ? "Select a blue unit to see its actions." :
                $"{match.Soldier(selected.Id).Sex} {selected.Definition.DisplayName} · HP {CombatValues.FormatRatio(selected.Health, selected.Definition.MaxHealthUnits)} · Attack {CombatValues.FormatUnits(selected.Definition.AttackUnits)} · Moves {selected.RemainingMoves} · Attack {(selected.CanAttack ? "ready" : "spent")}";
            document.rootVisualElement.Q<Button>("Disband").SetEnabled(canAct && selected != null);
            var sex = sexField.index == 0 ? PopulationSex.Male : PopulationSex.Female;
            recruitActions.Clear();
            foreach (var definition in state.Roster)
            {
                string type = definition.TypeId;
                var button = new Button(() => Recruit(type)) { text = $"{definition.DisplayName}\n2 gold + 1 {(sex == PopulationSex.Male ? "male" : "female")}" };
                string reason = match.RecruitBlockReason(0, 1, type, sex);
                button.AddToClassList("purchase"); button.tooltip = reason;
                button.SetEnabled(canAct && reason == null); recruitActions.Add(button);
            }
            document.rootVisualElement.Q<Label>("RecruitForecast").text = city.Available(sex) == 0 ? "No population available for this choice." :
                $"After recruitment: {match.Forecast(1, sex).Pairs} pairs · Food net {match.Forecast(1, sex).FoodBalance:+0;-0;0} · Army upkeep {match.GoldUpkeep(0) + 1}/turn";
            cityActions.Clear();
            foreach (CityUpgrade upgrade in Enum.GetValues(typeof(CityUpgrade)))
            {
                CityUpgrade chosen = upgrade; int level = city.Level(chosen);
                string effect = chosen == CityUpgrade.Housing ? "+4 housing" : chosen == CityUpgrade.Food ? "+4 farm capacity" : "+1 market capacity";
                if (level < AdvancedEconomyRules.MaxUpgradeLevel)
                {
                    var after = match.Forecast(1, upgrade: chosen);
                    if (chosen == CityUpgrade.Food) effect += $"\n+{after.FoodProduced - forecast.FoodProduced} food/turn now";
                    if (chosen == CityUpgrade.Gold) effect += $"\n+{after.GoldProduced - forecast.GoldProduced} gold/turn now";
                }
                var button = new Button(() => Upgrade(chosen)) { text = $"{chosen} {level}/3 · {match.Rules.UpgradeCost(city, chosen)} gold\n{effect}" };
                string reason = match.UpgradeBlockReason(0, 1, chosen);
                button.AddToClassList("purchase"); button.tooltip = reason;
                button.SetEnabled(canAct && reason == null); cityActions.Add(button);
            }
            string supplyWarning = forecast.FoodBalance < 0 || match.GoldIncome(0) < match.GoldUpkeep(0) ?
                " Reserves cover deficits temporarily; food shortage costs population, and unfed/unpaid troops lose HP." : "";
            statusLabel.text = state.GameOver ? (state.WinnerSeat == 0 ? "You captured the red city. Start a new match to try another economy." : "The opponent captured your city. Start a new match to try a different opening.") : (thinking ? "Opponent is playing…" : status) + supplyWarning;
            endButton.text = thinking ? "Opponent is playing…" : "End turn"; endButton.SetEnabled(canAct);
            var report = match.LastTurnReport;
            reportLabel.text = report.Seat == 0 ? $"This turn: +{report.GoldProduced} gold, −{report.GoldUpkeep} upkeep, {report.Births} births, {report.CivilianDeaths} civilian deaths, {report.UnitDeaths} unit deaths." : "The opponent has its own population and economy.";
            Publish(visible);
        }

        [Serializable] private sealed class PublicState
        {
            public string rules, balance, turn; public int round, gold, men, women, food, housing, pairs, soldiers, selectedUnit;
            public bool gameOver, thinking; public int winnerSeat; public string status;
            public PublicUnit[] visibleUnits;
        }
        [Serializable] private sealed class PublicUnit
        { public int id, seat, x, y, health, moves; public string type, sex; public bool canAttack; }
        private void Publish(bool[] visible)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var city = match.CityEconomy(1); var forecast = match.Forecast(1);
            var snapshot = new PublicState { rules = AdvancedEconomyRules.Version, balance = match.Rules.Balance.ToString(),
                turn = match.Board.CurrentTurnSeat == 0 ? "human" : "scripted-ai", round = match.Board.Round,
                gold = match.Board.GoldForSeat(0), men = city.Men, women = city.Women, food = city.StoredFood,
                housing = forecast.Housing, pairs = city.Pairs, soldiers = forecast.Soldiers, selectedUnit = selectedUnit,
                gameOver = match.Board.GameOver, winnerSeat = match.Board.WinnerSeat, thinking = thinking, status = thinking ? "Opponent is playing…" : status,
                visibleUnits = match.Board.Units.Where(u => visible[u.Position]).Select(u => new PublicUnit {
                    id = u.Id, seat = u.Seat, x = u.Position % 7, y = u.Position / 7, health = u.Health,
                    moves = u.RemainingMoves, canAttack = u.CanAttack, type = u.Definition.TypeId,
                    sex = match.Soldier(u.Id).Sex.ToString() }).ToArray() };
            AdvancedPublishState(JsonUtility.ToJson(snapshot));
#endif
        }
    }
}
