using System;
using System.Collections.Generic;
using System.Linq;
using BlockNations.Simulation.Poly;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class PolyGameController
{
    private VisualElement root, board, inspector, body;
    private ScrollView boardScroll, details;
    private Label resources, statusLabel, savedLabel;
    private readonly Button[] cells = new Button[121];
    private int selected = -1, selectedUnit;
    private string tab = "board";
    private float tileSize = 48, publishAt;
    private bool focusQueued;
    private Button endTurn;
    private static Label Text(string value, string css = null) { var l = new Label(value); if (css != null) l.AddToClassList(css); return l; }
    private Button Button(VisualElement parent, string name, string label, Action click, bool enabled = true)
    {
        var b = new Button(click) { name = name, text = label }; b.AddToClassList("poly-button"); b.SetEnabled(enabled); parent.Add(b); return b;
    }
    private void BuildView()
    {
        root = document.rootVisualElement.Q("PolyRoot"); resources = root.Q<Label>("Resources"); statusLabel = root.Q<Label>("Status"); savedLabel = root.Q<Label>("Saved");
        boardScroll = root.Q<ScrollView>("BoardScroll"); board = root.Q("Board"); details = root.Q<ScrollView>("Details"); inspector = details.contentContainer; body = root.Q("Body");
        root.Q<Button>("Tech").clicked += () => { selectedUnit = 0; tab = tab == "tech" ? "board" : "tech"; RefreshView(); };
        root.Q<Button>("Cities").clicked += () => { selectedUnit = 0; tab = "cities"; RefreshView(); };
        root.Q<Button>("Help").clicked += () => { selectedUnit = 0; tab = "help"; RefreshView(); };
        root.Q<Button>("Save").clicked += SaveGame;
        root.Q<Button>("Menu").clicked += Leave;
        root.Q<Button>("New").clicked += ConfirmNew;
        root.Q<Button>("ZoomIn").clicked += () => SetTileSize(Math.Min(80, tileSize + 8));
        root.Q<Button>("ZoomOut").clicked += () => SetTileSize(Math.Max(28, tileSize - 8));
        endTurn = root.Q<Button>("EndTurn"); endTurn.clicked += () => Act(new PolyAction(PolyActionKind.EndTurn, 0));
        for (int y = 10; y >= 0; y--) {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; board.Add(row);
            for (int x = 0; x < 11; x++) {
                int position = y * 11 + x;
                var b = new Button(() => Select(position)) { name = "Tile" + position }; b.AddToClassList("poly-tile");
                b.Add(new PolyTileArt { name = "Art", pickingMode = PickingMode.Ignore });
                b.Add(Text("", "tile-label")); cells[position] = b; row.Add(b);
            }
        }
        root.RegisterCallback<GeometryChangedEvent>(_ => {
            bool narrow = root.resolvedStyle.width < 850;
            body.style.flexDirection = narrow ? FlexDirection.Column : FlexDirection.Row;
            details.style.width = narrow ? new StyleLength(Length.Percent(100)) : new StyleLength(340);
            details.style.maxHeight = narrow ? new StyleLength(Length.Percent(44)) : new StyleLength(StyleKeyword.None);
            if (Match != null) { RefreshView(); QueueMapFocus(); }
        });
        SetTileSize(48);
    }
    private void SetTileSize(float size)
    {
        tileSize = size; board.style.width = 11 * size;
        foreach (var cell in cells) if (cell != null) { cell.style.width = size; cell.style.height = size; }
    }
    private void FocusHome()
    {
        selected = Match.Cities.FirstOrDefault(c => c.ownerSeatIndex == 0)?.position ?? Match.Board.Units.FirstOrDefault(u => u.Seat == 0)?.Position ?? 12;
        QueueMapFocus();
    }
    private void QueueMapFocus()
    {
        if (focusQueued) return;
        focusQueued = true;
        root.schedule.Execute(() => {
            focusQueued = false;
            if (selected >= 0 && selected < 121) boardScroll.ScrollTo(cells[Math.Max(0, selected - 12)]);
        }).StartingIn(300);
    }
    private void Select(int position)
    {
        if (Match == null) return;
        if (selectedUnit != 0 && Match.IsTurnOwnedBySeat(0) && !aiRunning) {
            var action = Match.LegalActions(0).FirstOrDefault(a => a.ActorId == selectedUnit && a.Destination == position && (a.Kind == PolyActionKind.Attack || a.Kind == PolyActionKind.Move));
            if (action != null) { selected = position; Act(action); return; }
        }
        selected = position; tab = "board";
        var unit = Match.Board.UnitAt(position); selectedUnit = unit?.Seat == 0 ? unit.Id : 0; RefreshView();
    }
    private void RefreshView()
    {
        if (Match == null || root == null) return;
        bool humanTurn = Match.IsTurnOwnedBySeat(0) && !aiRunning;
        resources.text = $"Gold {Match.Board.GoldForSeat(0)}   +{Match.Income(0)}/turn   ·   Round {Match.Board.Round}   ·   {(Match.Board.GameOver ? "Finished" : humanTurn ? "Your turn" : "AI turn")}";
        statusLabel.text = Status; savedLabel.text = saveStatus;
        var actions = Match.LegalActions(0); endTurn.SetEnabled(humanTurn && actions.Any(a => a.Kind == PolyActionKind.EndTurn));
        bool[] visible = Match.Visibility(0);
        for (int p = 0; p < 121; p++) {
            var known = Match.KnownTile(0, p); var unit = visible[p] || Match.Board.UnitAt(p)?.Seat == 0 ? Match.Board.UnitAt(p) : null;
            var city = known == null ? null : Match.CityAt(p);
            var art = cells[p].Q<PolyTileArt>("Art"); art.Tile = known; art.Visible = visible[p]; art.City = city != null;
            art.Owner = city == null ? -1 : Match.KnownCityOwner(0, p); art.Unit = unit?.Definition.TypeId; art.UnitOwner = unit?.Seat ?? -1;
            art.MarkDirtyRepaint();
            cells[p].Q<Label>().text = unit != null ? unit.Definition.DisplayName.Substring(0, Math.Min(3, unit.Definition.DisplayName.Length)) + " " + (unit.Health / 10f).ToString("0.#") :
                city != null ? (art.Owner < 0 ? "Village" : "L" + Match.KnownCityLevel(0, p)) :
                known?.improvement != PolyImprovement.None && known != null ? known.improvement.ToString().Substring(0, Math.Min(4, known.improvement.ToString().Length)) :
                known?.resource != PolyResource.None && known != null ? known.resource.ToString().Substring(0, Math.Min(4, known.resource.ToString().Length)) : "";
            cells[p].Q<Label>().style.display = string.IsNullOrEmpty(cells[p].Q<Label>().text) ? DisplayStyle.None : DisplayStyle.Flex;
            cells[p].EnableInClassList("friendly-territory", known != null && Match.City(known.cityId)?.ownerSeatIndex == 0);
            cells[p].EnableInClassList("selected", selected == p);
            cells[p].EnableInClassList("reachable", actions.Any(a => a.ActorId == selectedUnit && a.Destination == p && a.Kind == PolyActionKind.Move));
            cells[p].EnableInClassList("target", actions.Any(a => a.ActorId == selectedUnit && a.Destination == p && a.Kind == PolyActionKind.Attack));
            cells[p].tooltip = known == null ? "Unexplored" : known.terrain + (known.improvement != PolyImprovement.None ? " · " + known.improvement : known.resource != PolyResource.None ? " · " + known.resource : "") + (visible[p] ? "" : " · remembered terrain");
        }
        inspector.Clear();
        if (Match.Board.GameOver) { inspector.Add(Text(Match.Board.WinnerSeat == 0 ? "Victory" : "Defeat", "sheet-title")); inspector.Add(Text(Status)); Button(inspector, "PlayAgain", "New Poly match", ConfirmNew); return; }
        var rewards = actions.Where(a => a.Kind == PolyActionKind.Reward).ToArray();
        if (rewards.Length > 0) {
            var city = Match.City(rewards[0].ActorId); inspector.Add(Text("City " + city.id + " · Level " + (city.level - city.pendingRewards + 1) + " reward", "sheet-title"));
            inspector.Add(Text("Income increased. Choose your city reward."));
            foreach (var action in rewards) { var a = action; Button(inspector, "Reward_" + a.Choice, a.Label, () => Act(a)); }
            return;
        }
        if (tab == "tech") { DrawTechnologies(actions, humanTurn); return; }
        if (tab == "help") { DrawHelp(); return; }
        if (tab == "cities") {
            inspector.Add(Text("Your cities", "sheet-title"));
            foreach (var c in Match.Cities.Where(c => c.ownerSeatIndex == 0)) { var city = c; Button(inspector, "City_" + c.id, $"City {c.id} · Level {c.level} · +{c.Income} Gold", () => Select(city.position)); }
            return;
        }
        if (selected < 0 || Match.KnownTile(0, selected) == null) { inspector.Add(Text("Explore the map", "sheet-title")); inspector.Add(Text("Tap a blue unit, then a green tile to move. Yellow resources grow the city that owns their fixed 3×3 territory.")); return; }
        var selectedPiece = selectedUnit == 0 ? null : Match.Board.GetUnit(selectedUnit);
        if (selectedPiece != null) {
            inspector.Add(Text(selectedPiece.Definition.DisplayName, "sheet-title"));
            var d = selectedPiece.Definition; inspector.Add(Text($"HP {selectedPiece.Health / 10f:0.#}/{d.MaxHealthUnits / 10} · Attack {d.AttackUnits / 10} · Defense {d.DefenseUnits / 10}\nMove {d.MaxMovesPerTurn} · Range {d.AttackRange}"));
            inspector.Add(Text(PolyRules.UnitAbility(d.TypeId))); var info = Match.UnitState(selectedPiece.Id);
            inspector.Add(Text(info.rested ? "Done this turn" : $"{(info.moved ? "Moved" : "Movement ready")} · {(info.attacked ? "Attacked" : "Attack ready")}"));
            foreach (var a in actions.Where(a => a.ActorId == selectedPiece.Id && (a.Kind == PolyActionKind.Heal || a.Kind == PolyActionKind.Capture))) { var action = a; Button(inspector, action.Kind.ToString(), action.Kind == PolyActionKind.Heal ? "Rest and heal (uses this turn)" : "Capture this city", () => Act(action)); }
        }
        var selectedCity = Match.CityAt(selected);
        if (selectedCity?.ownerSeatIndex == 0) DrawCity(selectedCity, actions, humanTurn);
        else DrawTile(selected, actions, humanTurn);
    }
    private void DrawCity(PolyCity city, List<PolyAction> actions, bool humanTurn)
    {
        inspector.Add(Text($"City {city.id} · Level {city.level}", "sheet-title"));
        inspector.Add(Text($"Population {city.population}/{city.PopulationNeeded} · +{city.Income} Gold/turn\nTerritory stays 3×3. Tap nearby resources to develop them."));
        int supported = Match.Board.Units.Count(u => u.Seat == 0 && Match.UnitState(u.Id).homeCityId == city.id);
        inspector.Add(Text($"Units supported {supported}/{city.level + 2}"));
        foreach (var unit in PolyRules.Units.Where(u => u.TypeId != "poly-giant")) {
            var definition = unit; var a = actions.FirstOrDefault(x => x.Kind == PolyActionKind.Recruit && x.ActorId == city.id && x.Choice == unit.TypeId);
            string tech = PolyRules.UnitTechnology(unit.TypeId); bool unlocked = Match.HasTechnology(0, tech);
            Button(inspector, "Recruit_" + unit.TypeId, unit.DisplayName + " · " + unit.RecruitCost + " Gold" + (unlocked ? "" : " · needs " + PolyRules.Technology(tech).Name) +
                $"\nHP {unit.MaxHealthUnits / 10} · Atk {unit.AttackUnits / 10} · Def {unit.DefenseUnits / 10} · Range {unit.AttackRange}", () => Act(a), humanTurn && a != null);
        }
        if (Match.Board.UnitAt(city.position) != null) inspector.Add(Text("Move the stationed unit to make room for recruitment."));
        else if (city.recruited) inspector.Add(Text("This city has recruited or was captured this turn."));
    }
    private void DrawTile(int p, List<PolyAction> actions, bool humanTurn)
    {
        var tile = Match.KnownTile(0, p); var city = Match.City(tile.cityId);
        inspector.Add(Text(tile.terrain.ToString(), "sheet-title"));
        inspector.Add(Text((tile.improvement != PolyImprovement.None ? tile.improvement.ToString() : tile.resource != PolyResource.None ? tile.resource.ToString() : "Open land") + (tile.road ? " · Road" : "")));
        if (Match.Visibility(0)[p]) {
            var enemy = Match.Board.UnitAt(p);
            if (enemy != null && enemy.Seat != 0) inspector.Add(Text($"{enemy.Definition.DisplayName} · HP {enemy.Health / 10f:0.#}/{enemy.Definition.MaxHealthUnits / 10}\nAttack {enemy.Definition.AttackUnits / 10} · Defense {enemy.Definition.DefenseUnits / 10} · Range {enemy.Definition.AttackRange}"));
            var town = Match.CityAt(p);
            if (town != null && town.ownerSeatIndex >= 0 && town.ownerSeatIndex != 0) inspector.Add(Text($"Opposing city · Level {town.level} · Fortifications {town.walls}"));
        }
        if (city != null && city.ownerSeatIndex == 0) {
            inspector.Add(Text($"City {city.id} · Level {city.level} · Population {city.population}/{city.PopulationNeeded}"));
            var options = Match.DevelopActions(0, p).ToArray();
            foreach (var option in options) {
                var a = option; bool available = actions.Any(x => x.Same(a));
                Button(inspector, "Develop_" + a.Choice, $"{a.Label} · {a.Cost} Gold · +{a.Population} population", () => Act(a), humanTurn && available);
            }
            if (options.Length == 0) inspector.Add(Text("Research the relevant technology to unlock this tile's improvement. Completed improvements stay in place."));
            Button(inspector, "InspectCity", "Open City " + city.id, () => { selected = city.position; selectedUnit = 0; RefreshView(); });
        } else inspector.Add(Text(Match.CityAt(p) != null ? "Move onto the city, then capture it on your next turn." : "Tile development requires your city's fixed territory."));
    }
    private void DrawTechnologies(List<PolyAction> actions, bool humanTurn)
    {
        inspector.Add(Text("Technology", "sheet-title")); inspector.Add(Text("Buy research with Gold. Higher tiers and more cities increase the price."));
        foreach (var tech in PolyRules.Technologies) {
            bool owned = Match.HasTechnology(0, tech.Id); var action = actions.FirstOrDefault(a => a.Kind == PolyActionKind.Research && a.Choice == tech.Id);
            string prerequisite = tech.Parent == null ? "" : "← " + PolyRules.Technology(tech.Parent).Name + "\n";
            var t = tech;
            var card = Button(inspector, "Research_" + t.Id, (owned ? "✓ " : "") + prerequisite + t.Name + (owned ? " · unlocked" : " · " + Match.ResearchCost(0, t.Id) + " Gold"), () => Act(action), humanTurn && !owned && action != null);
            card.style.marginLeft = 3 + (t.Tier - 1) * 12;
            inspector.Add(Text(t.Description, "tech-description"));
        }
    }
    private void DrawHelp()
    {
        inspector.Add(Text("How to play Poly", "sheet-title"));
        inspector.Add(Text("1. Tap a blue unit, then a green tile to move or a red target to attack. Melee units advance after a kill. Surviving defenders retaliate within range.\n\n2. Buy technology with Gold. Tap fruit, animals, crops, forest or ore in your territory to harvest or build. Improvements add city population.\n\n3. Each city level increases Gold income and offers a reward. Territories stay 3×3. Windmills, sawmills and forges benefit from adjacent matching improvements.\n\n4. Move onto a village or enemy city, then use Capture on your following turn. Win by taking every AI city.\n\n5. New recruits are ready next turn. Cities support level + 2 units. Rest unused units to heal. Riders can retreat after attacking; Knights attack again after kills. Catapults must stand still to fire.\n\nFog follows current visibility. Explored terrain is remembered; unseen enemies are hidden. Use + / − to zoom and drag/scroll the map.\n\nPoly saves automatically in this browser/device for your account. Save and Menu waits for storage to finish. New match replaces only your Poly save."));
    }
    private void ConfirmNew()
    {
        if (aiRunning) return;
        inspector.Clear(); inspector.Add(Text("Start a new Poly match?", "sheet-title"));
        inspector.Add(Text("This replaces your current Poly continuation slot."));
        Button(inspector, "ConfirmNew", "Start new 11×11 match", NewMatch);
        Button(inspector, "CancelNew", "Keep current match", RefreshView);
    }
    private void ShowUnavailable()
    {
        statusLabel.text = Status; inspector.Clear(); inspector.Add(Text(Status));
        Button(inspector, "Back", "Main menu", Leave);
        if (!string.IsNullOrEmpty(savePath)) Button(inspector, "NewAfterInvalidSave", "New Poly match", ConfirmNew);
        endTurn.SetEnabled(false);
    }
    private void LateUpdate()
    {
        if (runtimePanel != null) {
            float scale = 1;
#if UNITY_WEBGL && !UNITY_EDITOR
            scale = Mathf.Max(1, BN_PolyPixelRatio());
#endif
            if (Mathf.Abs(runtimePanel.scale - scale) > .01f) runtimePanel.scale = scale;
        }
        if (savedLabel != null) savedLabel.text = saveStatus;
#if UNITY_WEBGL && !UNITY_EDITOR
        if (Match == null || root.worldBound.width <= 0 || Time.unscaledTime < publishAt) return;
        publishAt = Time.unscaledTime + 0.25f;
        var state = new BrowserState { round = Match.Board.Round, gold = Match.Board.GoldForSeat(0), income = Match.Income(0), currentTurnSeatIndex = Match.currentTurnSeatIndex,
            gameOver = Match.Board.GameOver, status = Status, saved = saveStatus, tab = tab, selected = selected, selectedUnit = selectedUnit };
        Rect bounds = root.worldBound;
        foreach (var button in root.Query<Button>().ToList()) {
            Rect r = button.worldBound;
            bool shown = r.width > 0 && r.height > 0 && button.visible;
            for (var parent = button.parent; parent != null; parent = parent.parent) {
                if (parent.resolvedStyle.display == DisplayStyle.None) shown = false;
                if ((parent == boardScroll.contentViewport || parent == details.contentViewport) && !parent.worldBound.Contains(r.center)) shown = false;
            }
            if (!shown || r.center.x < bounds.xMin || r.center.x > bounds.xMax || r.center.y < bounds.yMin || r.center.y > bounds.yMax) continue;
            state.controls.Add(new BrowserControl { name = button.name, enabled = button.enabledInHierarchy, x = (r.center.x - bounds.x) / bounds.width, y = (r.center.y - bounds.y) / bounds.height });
        }
        foreach (var c in Match.Cities.Where(c => c.ownerSeatIndex == 0)) state.cities.Add(new BrowserCity { id = c.id, level = c.level, population = c.population, pendingRewards = c.pendingRewards });
        foreach (var u in PolyObservation.ForSeat(Match, 0).Units) state.units.Add(new BrowserUnit { id = u.Id, seat = u.Seat, position = u.Position, health = u.Health, type = u.Definition.TypeId });
        BN_PolyPublish(JsonUtility.ToJson(state));
#endif
    }
    [Serializable] private sealed class BrowserState
    {
        public string mode = "Poly", status, saved, tab;
        public int round, gold, income, currentTurnSeatIndex, selected, selectedUnit;
        public bool gameOver;
        public List<BrowserControl> controls = new List<BrowserControl>();
        public List<BrowserCity> cities = new List<BrowserCity>();
        public List<BrowserUnit> units = new List<BrowserUnit>();
    }
    [Serializable] private sealed class BrowserControl { public string name; public bool enabled; public float x, y; }
    [Serializable] private sealed class BrowserCity { public int id, level, population, pendingRewards; }
    [Serializable] private sealed class BrowserUnit { public int id, seat, position, health; public string type; }
}
