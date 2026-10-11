using System;
using System.Linq;
using BlockNations.Simulation;
using UnityEditor;
using UnityEngine;

// In-memory developer playtest. It does not load a gameplay scene or instantiate an AI.
public sealed class AdvancedEconomyPlaytestWindow : EditorWindow
{
    private AdvancedMatch match;
    private RecruitmentBalance balance;
    private PopulationSex recruitSex;
    private int selectedUnit;
    private bool handoff;
    private string message;
    private Vector2 scroll;

    [MenuItem("Tools/Block Nations/Advanced 7x7 Playtest")]
    public static void Open()
    {
        var window = GetWindow<AdvancedEconomyPlaytestWindow>("Advanced 7x7");
        window.minSize = new Vector2(650, 720);
        window.Show();
    }

    private void OnEnable() { if (match == null) ResetMatch(); }
    private void ResetMatch()
    {
        match = new AdvancedMatch(balance); selectedUnit = 0; handoff = false;
        message = "Select your city to recruit, or a unit and then a highlighted destination. Capture the enemy city to win.";
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Advanced 7x7 — economy prototype", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Two human players. In-memory Editor playtest; closing or recompiling resets the match.", MessageType.Info);
        balance = (RecruitmentBalance)EditorGUILayout.EnumPopup("Balance for next new match", balance);
        if (GUILayout.Button("New match")) ResetMatch();
        if (match == null) return;
        int seat = match.Board.CurrentTurnSeat;
        if (handoff)
        {
            GUILayout.Space(40);
            EditorGUILayout.LabelField($"Pass control to Player {seat + 1}.", EditorStyles.boldLabel);
            if (GUILayout.Button($"Player {seat + 1}: reveal my turn", GUILayout.Height(60))) handoff = false;
            return;
        }
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (match.Board.GameOver)
            EditorGUILayout.HelpBox($"Player {match.Board.WinnerSeat + 1} wins by city capture.", MessageType.Info);
        EditorGUILayout.LabelField($"Round {match.Board.Round} · Player {seat + 1} · Gold {match.Board.GoldForSeat(seat)} · Income {match.GoldIncome(seat)}/turn · Army upkeep {match.GoldUpkeep(seat)}/turn");
        EditorGUILayout.LabelField($"Current balance: {match.Rules.Balance}");
        DrawBoard(seat);
        DrawUnit(seat);
        DrawCity(seat);
        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
        AdvancedTurnReport report = match.LastTurnReport;
        EditorGUILayout.LabelField($"Turn report P{report.Seat + 1}: +{report.GoldProduced} gold, {report.GoldUpkeep} upkeep, {report.Births} births, {report.CivilianDeaths} civilian deaths, {report.UnfedUnits} unfed / {report.UnpaidUnits} unpaid units, {report.UnitDeaths} unit deaths.", EditorStyles.wordWrappedLabel);
        using (new EditorGUI.DisabledScope(match.Board.GameOver))
            if (GUILayout.Button("End turn and pass control", GUILayout.Height(40)))
            {
                if (match.Apply(new MatchCommand(MatchActionKind.EndTurn, seat)).Applied)
                { selectedUnit = 0; handoff = true; message = null; }
            }
        EditorGUILayout.EndScrollView();
    }

    private void DrawBoard(int seat)
    {
        bool[] visible = MatchEngine.Visibility(match.Board, seat);
        var legal = MatchEngine.LegalUnitActions(match.Board, seat, selectedUnit);
        for (int y = 6; y >= 0; y--)
        {
            EditorGUILayout.BeginHorizontal();
            for (int x = 0; x < 7; x++)
            {
                int position = match.Board.Position(x, y);
                var unit = visible[position] ? match.Board.UnitAt(position) : null;
                var city = match.Board.CityAt(position);
                var action = legal.FirstOrDefault(a => a.Command.Destination == position &&
                    (a.Command.Kind == MatchActionKind.Move || a.Command.Kind == MatchActionKind.Attack));
                string label = $"{x},{y}";
                if (city != null) label += $"\nCity P{city.Seat + 1}";
                if (unit != null) label += $"\nP{unit.Seat + 1} {unit.Definition.DisplayName[0]}#{unit.Id}";
                else if (!visible[position]) label += "\nFog";
                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = action != null ? new Color(.5f, .9f, .5f) :
                    unit?.Id == selectedUnit ? new Color(.6f, .8f, 1f) : Color.white;
                if (GUILayout.Button(label, GUILayout.MinWidth(70), GUILayout.Height(62)))
                {
                    if (unit != null && unit.Seat == seat) selectedUnit = unit.Id;
                    else if (action != null)
                    {
                        var result = match.Apply(action.Command);
                        message = !result.Applied ? "That action is no longer legal." : result.HiddenBlocker ?
                            "A hidden enemy blocked the move; remaining attacks are spent." : $"{action.Command.Kind} completed.";
                    }
                    else selectedUnit = 0;
                }
                GUI.backgroundColor = previous;
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawUnit(int seat)
    {
        SimulationUnit unit = match.Board.GetUnit(selectedUnit);
        if (unit == null || unit.Seat != seat) return;
        AdvancedSoldier soldier = match.Soldier(unit.Id);
        EditorGUILayout.LabelField($"{soldier.Sex} {unit.Definition.DisplayName} #{unit.Id} · HP {CombatValues.FormatRatio(unit.Health, unit.Definition.MaxHealthUnits)} · Attack {CombatValues.FormatUnits(unit.Definition.AttackUnits)} · Moves {unit.RemainingMoves} · Attack available: {unit.CanAttack}");
        if (GUILayout.Button("Disband at home city: return one population (no gold refund)"))
        {
            bool success = match.TryDisband(seat, unit.Id, out string reason);
            message = success ? "Surviving soldier returned to the civilian pool." : reason;
            if (success) selectedUnit = 0;
        }
    }

    private void DrawCity(int seat)
    {
        SimulationCity city = match.Board.Cities.First(c => c.Seat == seat);
        AdvancedCityEconomy economy = match.CityEconomy(city.Id);
        AdvancedCityForecast forecast = match.Forecast(city.Id);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("City economy", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Civilians: {economy.Men} male + {economy.Women} female · Soldiers {forecast.Soldiers} · Housing {forecast.Residents}/{forecast.Housing}");
        EditorGUILayout.LabelField($"Food: {economy.StoredFood}/{AdvancedEconomyRules.FoodStorage} stored · +{forecast.FoodProduced} production −{forecast.FoodConsumed} consumption = {forecast.FoodBalance:+0;-0;0}/turn");
        EditorGUILayout.LabelField($"Growth: {economy.GrowthProgress}/{AdvancedEconomyRules.GrowthThreshold} · {economy.Pairs} pairs · Next birth {economy.NextBirthSex}");
        if (forecast.Residents >= forecast.Housing || forecast.FoodBalance <= 0 || forecast.Pairs == 0)
            EditorGUILayout.HelpBox("Growth paused: it needs a civilian male/female pair, spare housing and positive food production balance.", MessageType.Warning);
        if (forecast.FoodBalance < 0 || match.GoldIncome(seat) < match.GoldUpkeep(seat))
            EditorGUILayout.HelpBox("Reserves cover deficits temporarily. Food shortage costs population and damages unfed troops; unpaid troops lose 1 HP per turn.", MessageType.Warning);
        recruitSex = (PopulationSex)EditorGUILayout.EnumPopup("Recruit population", recruitSex);
        if (economy.Available(recruitSex) > 0)
        {
            var next = match.Forecast(city.Id, recruitSex);
            EditorGUILayout.LabelField($"After recruitment: {next.Civilians} civilians, {next.Pairs} pairs, food {next.FoodBalance:+0;-0;0}/turn, gold income {next.GoldProduced}/turn, army upkeep {match.GoldUpkeep(seat) + 1}/turn.");
            if (next.Pairs == 0) EditorGUILayout.HelpBox("This recruit removes the last civilian of one sex: future growth stops until a survivor returns.", MessageType.Warning);
        }
        EditorGUILayout.BeginHorizontal();
        foreach (UnitDefinition definition in match.Board.Roster)
        {
            string reason = match.RecruitBlockReason(seat, city.Id, definition.TypeId, recruitSex);
            var recruited = match.Rules.RecruitDefinition(definition, recruitSex);
            using (new EditorGUI.DisabledScope(reason != null))
                if (GUILayout.Button(new GUIContent($"{definition.DisplayName}\n{definition.RecruitCost} gold + 1 {recruitSex}\nAttack {CombatValues.FormatUnits(recruited.AttackUnits)}", reason), GUILayout.Height(55)))
                    message = match.TryRecruit(seat, city.Id, definition.TypeId, recruitSex, out reason) ? "Recruited from city population." : reason;
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        foreach (CityUpgrade upgrade in Enum.GetValues(typeof(CityUpgrade)))
        {
            string reason = match.UpgradeBlockReason(seat, city.Id, upgrade);
            string effect = upgrade == CityUpgrade.Housing ? "+4 capacity" : upgrade == CityUpgrade.Food ? "+4 food capacity" : "+1 gold capacity";
            if (economy.Level(upgrade) < AdvancedEconomyRules.MaxUpgradeLevel)
            {
                var after = match.Forecast(city.Id, upgrade: upgrade);
                if (upgrade == CityUpgrade.Food) effect += $" (+{after.FoodProduced - forecast.FoodProduced}/turn now)";
                if (upgrade == CityUpgrade.Gold) effect += $" (+{after.GoldProduced - forecast.GoldProduced}/turn now)";
            }
            using (new EditorGUI.DisabledScope(reason != null))
                if (GUILayout.Button(new GUIContent($"Upgrade {upgrade} ({economy.Level(upgrade)}/3)\n{effect} · {match.Rules.UpgradeCost(economy, upgrade)} gold", reason), GUILayout.Height(45)))
                    message = match.TryUpgrade(seat, city.Id, upgrade, out reason) ? $"{upgrade} upgraded. Output also depends on remaining civilian workers." : reason;
        }
        EditorGUILayout.EndHorizontal();
    }
}
