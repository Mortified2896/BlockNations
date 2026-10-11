using System;
using System.Collections;
using System.Collections.Generic;
using BlockNations.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

// Presentation only. The original board/input components continue to handle
// tile taps, selection, movement, attacks, fog and action markers.
public sealed partial class AdvancedGameplayUITKView : MonoBehaviour
{
    [SerializeField] private AdvancedMatchController controller;
    [SerializeField] private UIDocument document;
    [SerializeField] private UIDocument feedbackDocument;
    [SerializeField] private GameplayTopHudUITKView feedbackHud;
    [SerializeField] private CityUIManager cityUI;
    [SerializeField] private UnitUIManager unitUI;
    [SerializeField] private UnitSelectionManager unitSelection;
    [SerializeField] private GameMenuActions menuActions;
    [SerializeField] private Camera boardCamera;
    [SerializeField] private Texture2D warriorIcon, riderIcon, archerIcon;
    private VisualElement root, hud, economyBar, bottom, citySheet, unitSheet, resultOverlay, menuOverlay;
    private PanelSettings runtimePanel;
    private DropdownField strengthField;
    private bool ready;
    private int tab;
    private readonly string[] unitTypes = { UnitRegistry.WarriorTypeId, UnitRegistry.RiderTypeId, UnitRegistry.ArcherTypeId };
    private readonly string[] unitNames = { "Warrior", "Rider", "Archer" };
    private readonly CityUpgrade[] upgrades = { CityUpgrade.Housing, CityUpgrade.Food, CityUpgrade.Gold };
    private readonly string[] upgradeNames = { "Housing", "Food", "Gold" };

    private void Awake()
    {
        if (controller == null || document == null || feedbackDocument == null || feedbackHud == null ||
            cityUI == null || unitUI == null || unitSelection == null || menuActions == null || boardCamera == null ||
            warriorIcon == null || riderIcon == null || archerIcon == null || document.panelSettings == null)
            throw new InvalidOperationException("Advanced gameplay UI references must be assigned in the Inspector.");
        runtimePanel = Instantiate(document.panelSettings);
        document.panelSettings = runtimePanel;
    }

    private IEnumerator Start()
    {
        while (!controller.IsReady) yield return null;
        root = document.rootVisualElement;
        hud = Required<VisualElement>("AdvancedHudRoot");
        economyBar = Required<VisualElement>("EconomyBar"); bottom = Required<VisualElement>("AdvancedBottom");
        citySheet = Required<VisualElement>("CitySheet"); unitSheet = Required<VisualElement>("UnitSheet");
        resultOverlay = Required<VisualElement>("AdvancedResult"); menuOverlay = Required<VisualElement>("AdvancedMenuOverlay");
        root.pickingMode = PickingMode.Ignore;
        foreach (Label label in root.Query<Label>().ToList()) label.pickingMode = PickingMode.Ignore;
        foreach (Button button in root.Query<Button>().ToList()) button.pickingMode = PickingMode.Position;
        var icons = new[] { warriorIcon, riderIcon, archerIcon };
        for (int i = 0; i < unitTypes.Length; i++)
        {
            string type = unitTypes[i];
            Required<VisualElement>(unitNames[i] + "Icon").style.backgroundImage = new StyleBackground(icons[i]);
            Required<Button>("AdvancedRecruit" + unitNames[i]).clicked += () => cityUI.OnRecruitUnitButton(type);
        }
        for (int i = 0; i < upgrades.Length; i++)
        {
            CityUpgrade upgrade = upgrades[i];
            Required<Button>("Upgrade" + upgradeNames[i]).clicked += () => controller.TryUpgrade(cityUI.CurrentCity, upgrade);
        }
        Required<Button>("RecruitTab").clicked += () => tab = 0;
        Required<Button>("DevelopTab").clicked += () => tab = 1;
        Required<Button>("PopulationTab").clicked += () => tab = 2;
        Required<Button>("RecruitMale").clicked += () => controller.RecruitSex = PopulationSex.Male;
        Required<Button>("RecruitFemale").clicked += () => controller.RecruitSex = PopulationSex.Female;
        Required<Button>("CloseCity").clicked += cityUI.ClosePanel;
        Required<Button>("CloseUnit").clicked += unitSelection.ClearSelection;
        Required<Button>("DisbandUnit").clicked += () => controller.TryDisband(unitUI.CurrentUnit);
        Required<Button>("OpenCity").clicked += () => cityUI.OnCityClicked(controller.HumanCity);
        Required<Button>("AdvancedEndTurn").clicked += () => controller.TryEndTurn(AdvancedMatchController.HumanSeat);
        Required<Button>("AdvancedFeedback").clicked += feedbackHud.OpenFeedback;
        Required<Button>("AdvancedMenu").clicked += () => ShowMenu(true);
        Required<Button>("AdvancedResume").clicked += () => ShowMenu(false);
        Required<Button>("AdvancedExit").clicked += menuActions.QuitToMainMenu;
        Required<Button>("AdvancedResultMenu").clicked += menuActions.QuitToMainMenu;
        Required<Button>("AdvancedPlayAgain").clicked += () => { controller.Restart(); tab = 0; refitBoard = true; };
        strengthField = Required<DropdownField>("StrengthBalance");
        strengthField.choices = new List<string> { "Equal strength", "Class strength experiment" };
        strengthField.index = (int)controller.Balance;
        Required<Button>("AdvancedNewMatch").clicked += () =>
        {
            ShowMenu(false);
            controller.RestartWithBalance((RecruitmentBalance)strengthField.index);
            tab = 0; refitBoard = true;
        };
        ready = true;
    }

    private T Required<T>(string name) where T : VisualElement => root.Q<T>(name) ??
        throw new InvalidOperationException("Missing advanced gameplay UXML reference: " + name);
    private void Text(string name, string text) => Required<Label>(name).text = text;
    private static string Rate(int value) => (value >= 0 ? "+" : "") + value;
    private static string Value(int value) => CombatValues.FormatUnits(value);

    private void ShowMenu(bool show)
    {
        controller.IsMenuOpen = show;
        menuOverlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (show) strengthField.index = (int)controller.Balance;
    }

    private void LateUpdate()
    {
        if (!ready || !controller.IsReady) return;
        // Retain the existing feedback form, without duplicating the standard
        // turn/gold bar or Feedback control underneath the advanced HUD.
        var oldRoot = feedbackDocument.rootVisualElement;
        var oldBar = oldRoot.Q<VisualElement>("TopBar");
        if (oldBar != null) oldBar.style.display = DisplayStyle.None;
        var oldFeedback = oldRoot.Q<Button>("FeedbackButton");
        if (oldFeedback != null) oldFeedback.style.display = DisplayStyle.None;
        Refresh();
        Layout();
        PublishBrowserState();
    }

    private void Refresh()
    {
        AdvancedMatch match = controller.Match;
        MatchState board = match.Board;
        const int seat = AdvancedMatchController.HumanSeat;
        AdvancedCityEconomy home = match.CityEconomy(1);
        AdvancedCityForecast forecast = match.Forecast(1);
        int income = match.GoldIncome(seat), upkeep = match.GoldUpkeep(seat);
        bool humanTurn = board.IsTurnOwnedBySeat(seat);
        Text("AdvancedTurn", $"Round {board.Round} · " + (board.GameOver ? "Game over" : humanTurn ? "Your turn" : "Opponent's turn"));
        Text("AdvancedGold", board.GoldForSeat(seat).ToString());
        Text("GoldRate", $"{Rate(income - upkeep)} / turn");
        Required<Label>("AdvancedGold").tooltip = $"City income {income} gold · Army upkeep {upkeep} gold per turn";
        Text("AdvancedPopulation", home.Civilians.ToString());
        Text("PopulationSplit", $"{home.Men} M · {home.Women} F");
        Text("AdvancedHousing", $"{forecast.Residents}/{forecast.Housing}");
        Text("HousingDetail", $"{Math.Max(0, forecast.Housing - forecast.Residents)} free");
        Text("AdvancedFood", home.StoredFood.ToString());
        Text("FoodRate", $"{Rate(forecast.FoodBalance)} / turn");
        Required<Label>("AdvancedFood").tooltip = $"{forecast.FoodProduced} produced · {forecast.FoodConsumed} consumed per turn";
        Text("AdvancedStatus", controller.Status);
        Required<Button>("AdvancedEndTurn").SetEnabled(humanTurn);
        Required<Button>("AdvancedEndTurn").text = humanTurn ? "End turn" : "AI turn…";
        Required<Button>("OpenCity").SetEnabled(humanTurn && controller.HumanCity != null);
        bool cityOpen = cityUI.IsPanelOpen && cityUI.CurrentCity != null && humanTurn;
        bool unitOpen = unitUI.IsPanelOpen && unitUI.CurrentUnit != null && humanTurn && !cityOpen;
        hud.EnableInClassList("advanced-inspecting", cityOpen || unitOpen);
        citySheet.style.display = cityOpen ? DisplayStyle.Flex : DisplayStyle.None;
        unitSheet.style.display = unitOpen ? DisplayStyle.Flex : DisplayStyle.None;
        if (cityOpen) RefreshCity(match, controller.CityId(cityUI.CurrentCity));
        if (unitOpen) RefreshUnit(match, unitUI.CurrentUnit);
        Text("StrengthDescription", strengthField.index == 1
            ? "Female attack: Warrior −20%, Rider and Archer −10%. Health, defence and upkeep are equal."
            : "Male and female recruits have the same combat stats. Both draw from their chosen population pool.");
        resultOverlay.style.display = board.GameOver ? DisplayStyle.Flex : DisplayStyle.None;
        if (board.GameOver)
        {
            Text("AdvancedResultTitle", board.WinnerSeat == seat ? "Victory" : "Defeat");
            Text("AdvancedResultMessage", controller.Manager.GameOverUiMessage);
        }
    }

    private void RefreshCity(AdvancedMatch match, int cityId)
    {
        AdvancedCityEconomy city = match.CityEconomy(cityId);
        AdvancedCityForecast current = match.Forecast(cityId);
        Text("CityPopulationSummary", $"{city.Civilians} civilians · {current.Soldiers} soldiers");
        string[] tabs = { "Recruit", "Develop", "Population" };
        for (int i = 0; i < tabs.Length; i++)
        {
            Required<Button>(tabs[i] + "Tab").EnableInClassList("selected", tab == i);
            Required<VisualElement>(tabs[i] + "TabContent").style.display = tab == i ? DisplayStyle.Flex : DisplayStyle.None;
        }
        Required<Button>("RecruitMale").text = $"Male · {city.Men}";
        Required<Button>("RecruitFemale").text = $"Female · {city.Women}";
        Required<Button>("RecruitMale").EnableInClassList("selected", controller.RecruitSex == PopulationSex.Male);
        Required<Button>("RecruitFemale").EnableInClassList("selected", controller.RecruitSex == PopulationSex.Female);
        string recruitmentBlock = null;
        for (int i = 0; i < unitTypes.Length; i++)
        {
            string reason = match.RecruitBlockReason(0, cityId, unitTypes[i], controller.RecruitSex);
            Button button = Required<Button>("AdvancedRecruit" + unitNames[i]);
            button.SetEnabled(reason == null); button.tooltip = reason;
            recruitmentBlock = reason;
            UnitDefinition definition = match.Rules.RecruitDefinition(match.Board.RecruitType(unitTypes[i]), controller.RecruitSex);
            Text(unitNames[i] + "Stats", $"ATK {Value(definition.AttackUnits)} · Range {definition.AttackRange}");
        }
        if (recruitmentBlock != null) Text("RecruitConsequence", recruitmentBlock);
        else
        {
            var next = match.Forecast(cityId, controller.RecruitSex);
            Text("RecruitConsequence", $"Upkeep: 1 gold + 1 food / turn · {next.Pairs} pairs remain for growth");
        }
        for (int i = 0; i < upgrades.Length; i++)
        {
            CityUpgrade upgrade = upgrades[i]; string name = upgradeNames[i];
            bool max = city.Level(upgrade) >= AdvancedEconomyRules.MaxUpgradeLevel;
            string reason = match.UpgradeBlockReason(0, cityId, upgrade);
            Button button = Required<Button>("Upgrade" + name); button.SetEnabled(reason == null); button.tooltip = reason;
            Text(name + "Level", $"Level {city.Level(upgrade)} / {AdvancedEconomyRules.MaxUpgradeLevel}");
            Text(name + "Cost", max ? "Complete" : $"{match.Rules.UpgradeCost(city, upgrade)} gold");
            int gain = 0;
            if (!max)
            {
                var next = match.Forecast(cityId, upgrade: upgrade);
                gain = upgrade == CityUpgrade.Housing ? next.Housing - current.Housing : upgrade == CityUpgrade.Food
                    ? next.FoodProduced - current.FoodProduced : next.GoldProduced - current.GoldProduced;
            }
            Text(name + "Gain", max ? "Max level" : $"+{gain} " + (upgrade == CityUpgrade.Housing ? "space" : upgrade == CityUpgrade.Food ? "food / turn" : "gold / turn"));
        }
        Text("UpgradeNote", city.UpgradedThisTurn ? "City upgraded this turn. Build again next turn." :
            "One upgrade per turn. Farms and markets need civilian workers.");
        Text("PopulationDetail", $"{city.Men} men · {city.Women} women · {current.Soldiers} soldiers");
        string growth = current.Residents >= current.Housing ? "Growth paused: housing is full." : city.Pairs == 0
            ? "Growth paused: no male/female pairs." : current.FoodBalance <= 0 ? "Growth paused: a food surplus is needed." :
            $"{city.Pairs} pairs · {city.GrowthProgress} / {AdvancedEconomyRules.GrowthThreshold} growth";
        Text("GrowthDetail", growth);
        var progress = Required<ProgressBar>("GrowthProgress"); progress.value = city.GrowthProgress;
        progress.title = $"{city.GrowthProgress} / {AdvancedEconomyRules.GrowthThreshold}";
        Text("SupplyDetail", $"Food: {current.FoodProduced} produced − {current.FoodConsumed} used / turn\n" +
            $"Army upkeep: {match.GoldUpkeep(0)} gold / turn. Recruits stop working and forming pairs.");
    }

    private void RefreshUnit(AdvancedMatch match, Unit unit)
    {
        int id = controller.UnitId(unit);
        SimulationUnit model = match.Board.GetUnit(id); AdvancedSoldier soldier = match.Soldier(id);
        if (model == null || soldier == null) return;
        Text("AdvancedUnitName", $"{soldier.Sex} {model.Definition.DisplayName}");
        Text("AdvancedUnitStats", $"HP {CombatValues.FormatRatio(model.Health, model.Definition.MaxHealthUnits)}   ·   " +
            $"ATK {Value(model.Definition.AttackUnits)}   ·   DEF {Value(model.Definition.DefenseUnits)}");
        bool atHome = model.Position == match.Board.GetCity(soldier.HomeCityId).Position &&
            match.Board.GetCity(soldier.HomeCityId).Seat == model.Seat;
        bool unused = model.MovesUsed == 0 && model.AttacksUsed == 0;
        Text("UnitSupply", "Upkeep: 1 gold + 1 food / turn");
        var disband = Required<Button>("DisbandUnit"); disband.SetEnabled(atHome && unused);
        disband.tooltip = !atHome ? "Return to your home city to disband." : !unused
            ? "Disband before moving or attacking." : "Return this soldier to the civilian population.";
    }

    private void OnDestroy()
    {
        if (runtimePanel != null) Destroy(runtimePanel);
    }
}
