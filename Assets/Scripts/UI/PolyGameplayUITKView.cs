using System;
using System.Collections.Generic;
using System.Linq;
using BlockNations.Simulation.Poly;
using UnityEngine;
using UnityEngine.UIElements;

// Compact sheets over the normal board. Every list is paged; no ScrollView is used.
public sealed partial class PolyGameplayUITKView : MonoBehaviour
{
    [SerializeField] private PolyGameController controller;
    [SerializeField] private UIDocument document;
    [SerializeField] private UIDocument feedbackDocument;
    [SerializeField] private PolyWorldPresentation world;
    [SerializeField] private Camera boardCamera;
    private VisualElement root, hud, sheet, choices, tabs, overlay, overlayChoices, overlayTabs, overlayPages;
    private PanelSettings runtimePanel;
    private Label title, summary, hint, pageLabel, overlayTitle, overlaySummary, overlayHint;
    private Button endTurn, previous, following;
    private int page, pageCount, cityTab, branch, lastRevision = -1, lastPosition = -2;
    private string selectedTechnology = "organization";
    private PolyGameController.InspectKind lastInspection;
    private string screen = "";
    private bool ready;
    public UIDocument Document => document;
    private void Awake()
    {
        if (controller == null || document == null || feedbackDocument == null || world == null || boardCamera == null || document.panelSettings == null)
            throw new InvalidOperationException("Poly gameplay view needs its serialized scene references.");
        runtimePanel = Instantiate(document.panelSettings); document.panelSettings = runtimePanel;
    }
    private void Start()
    {
        root = document.rootVisualElement; root.StretchToParentSize(); root.pickingMode = PickingMode.Ignore;
        hud = root.Q("PolyHudRoot"); sheet = root.Q("Sheet"); choices = root.Q("Choices"); tabs = root.Q("SheetTabs");
        title = root.Q<Label>("SheetTitle"); summary = root.Q<Label>("Summary"); hint = root.Q<Label>("Hint"); pageLabel = root.Q<Label>("PageLabel");
        overlay = root.Q("Overlay"); overlayChoices = root.Q("OverlayChoices"); overlayTabs = root.Q("OverlayTabs"); overlayPages = root.Q("OverlayPages");
        overlayTitle = root.Q<Label>("OverlayTitle"); overlaySummary = root.Q<Label>("OverlaySummary"); overlayHint = root.Q<Label>("OverlayHint");
        root.Q<Button>("Menu").clicked += () => ShowScreen("menu");
        root.Q<Button>("Tech").clicked += () => ShowScreen("tech");
        endTurn = root.Q<Button>("EndTurn"); endTurn.clicked += () => controller.TryEndTurn(0);
        root.Q<Button>("CloseSheet").clicked += controller.CloseInspection;
        root.Q<Button>("CloseOverlay").clicked += () => ShowScreen("");
        previous = root.Q<Button>("Previous"); following = root.Q<Button>("Following");
        previous.clicked += () => { page = Math.Max(0, page - 1); Refresh(); };
        following.clicked += () => { page = Math.Min(pageCount - 1, page + 1); Refresh(); };
        ready = true; Refresh();
    }
    private void ShowScreen(string next)
    {
        screen = next; controller.IsMenuOpen = next.Length > 0; Refresh();
    }
    private void LateUpdate()
    {
        if (!ready) return;
        hud.style.display = controller.IsFeedbackOpen ? DisplayStyle.None : DisplayStyle.Flex;
        var nativeHud = feedbackDocument.rootVisualElement;
        bool showNativeHeader = screen.Length == 0 || controller.IsFeedbackOpen;
        bool landscapeSheet = Screen.width > Screen.height && Screen.height / runtimePanel.scale < 500 && screen.Length == 0 && sheet.resolvedStyle.display != DisplayStyle.None;
        var nativeBar = nativeHud.Q("TopBar");
        if (nativeBar != null) { nativeBar.style.display = showNativeHeader ? DisplayStyle.Flex : DisplayStyle.None; nativeBar.style.width = Length.Percent(landscapeSheet ? 48 : 100); }
        var nativeFeedback = nativeHud.Q<Button>("FeedbackButton");
        if (nativeFeedback != null) nativeFeedback.style.display = showNativeHeader && !landscapeSheet ? DisplayStyle.Flex : DisplayStyle.None;
        Layout();
        if (lastRevision != controller.Revision) {
            if (lastPosition != controller.SelectedPosition || lastInspection != controller.Inspection) page = cityTab = 0;
            lastPosition = controller.SelectedPosition; lastInspection = controller.Inspection;
            Refresh(); lastRevision = controller.Revision;
        }
        PublishBrowserState();
    }
    private static Label Text(string value, string css = null)
    {
        var l = new Label(value) { pickingMode = PickingMode.Ignore }; if (css != null) l.AddToClassList(css); return l;
    }
    private Button Button(VisualElement parent, string name, string text, Action clicked, string css = "menu-button", bool enabled = true)
    {
        var button = new Button(clicked) { name = name, text = text }; button.AddToClassList(css); button.SetEnabled(enabled); parent.Add(button); return button;
    }
    private void Card(VisualElement parent, string name, string label, string detail, Action clicked, bool enabled, Sprite icon = null)
    {
        var button = Button(parent, name, "", clicked, "choice", enabled);
        if (icon != null) { var image = new Image { sprite = icon, pickingMode = PickingMode.Ignore }; image.AddToClassList("unit-icon"); button.Add(image); }
        button.Add(Text(label)); button.Add(Text(detail, "price"));
    }
    private bool CanAct => controller.IsReady && controller.Match.IsTurnOwnedBySeat(0) && !controller.IsOpponentRunning;
    private List<PolyAction> Actions => controller.IsReady ? controller.Match.LegalActions(0) : new List<PolyAction>();
    private void Refresh()
    {
        if (!ready) return;
        choices.Clear(); tabs.Clear(); overlayChoices.Clear(); overlayTabs.Clear(); overlayPages.Clear();
        choices.RemoveFromClassList("tile-choices");
        root.Q("Growth").style.display = DisplayStyle.None;
        overlayChoices.EnableInClassList("tech-choices", screen == "tech");
        root.Q("OverlayCard").EnableInClassList("technology-card", screen == "tech");
        sheet.style.display = DisplayStyle.None; overlay.style.display = DisplayStyle.None;
        root.Q("Pages").style.display = DisplayStyle.None;
        title.text = summary.text = hint.text = overlayTitle.text = overlaySummary.text = overlayHint.text = "";
        endTurn.SetEnabled(CanAct && Actions.Any(a => a.Kind == PolyActionKind.EndTurn));
        endTurn.text = controller.IsOpponentRunning ? "AI turn…" : "End turn";
        if (!controller.IsReady) {
            if (controller.Status != "Opening Poly…") {
                overlay.style.display = DisplayStyle.Flex; overlayTitle.text = "Poly"; overlaySummary.text = controller.Status;
                Button(overlayChoices,"BackToMenu","Main menu",controller.Leave);
                Button(overlayChoices,"NewAfterInvalidSave","New match",()=>ShowScreen("confirm"));
            }
            return;
        }
        if (screen.Length > 0) { DrawOverlay(); return; }
        if (controller.Match.Board.GameOver) { screen = "result"; DrawOverlay(); return; }
        var rewards = Actions.Where(a => a.Kind == PolyActionKind.Reward).ToArray();
        if (rewards.Length > 0) { DrawRewards(rewards); return; }
        int p = controller.SelectedPosition;
        if (p < 0 || controller.Inspection == PolyGameController.InspectKind.None) return;
        sheet.style.display = DisplayStyle.Flex;
        if (controller.Inspection == PolyGameController.InspectKind.Unit) DrawUnit(p);
        else if (controller.Inspection == PolyGameController.InspectKind.City) DrawCity(p);
        else DrawTile(p);
    }
    private void DrawUnit(int p)
    {
        var unit = controller.Match.Board.UnitAt(p); if (unit == null) { sheet.style.display = DisplayStyle.None; return; }
        var d = unit.Definition; title.text = d.DisplayName;
        summary.text = $"HP {unit.Health / 10f:0.#}/{d.MaxHealthUnits / 10}   ATK {d.AttackUnits / 10}   DEF {d.DefenseUnits / 10}\nMove {d.MaxMovesPerTurn} · Range {d.AttackRange}";
        if(d.TypeId=="poly-builder") summary.text=$"HP {unit.Health/10f:0.#}/{d.MaxHealthUnits/10} · DEF {d.DefenseUnits/10}\nMove {d.MaxMovesPerTurn} · 1 construction/turn";
        hint.text = PolyRules.UnitAbility(d.TypeId);
        foreach (var action in Actions.Where(a => a.ActorId == unit.Id && (a.Kind == PolyActionKind.Heal || a.Kind == PolyActionKind.Capture || a.Kind == PolyActionKind.ClaimImprovement))) {
            var a = action; Card(choices,a.Kind.ToString(),a.Kind == PolyActionKind.Heal ? "Heal" : "Capture",a.Kind == PolyActionKind.Heal ? "Rest this turn" : a.Kind == PolyActionKind.Capture ? "Claim this city" : "Claim this improvement",()=>controller.Act(a),CanAct);
        }
        if(d.TypeId=="poly-builder" && controller.Match.CityAt(p)==null)
            Button(tabs,"BuildHere","Build here",()=>controller.OpenTile(p),"tab",CanAct);
        var city = controller.Match.CityAt(p);
        if (city != null) hint.text += " Tap this unit again to inspect the city.";
        if (choices.childCount == 0) choices.style.display = DisplayStyle.None; else choices.style.display = DisplayStyle.Flex;
    }
    private void DrawCity(int p)
    {
        choices.style.display = DisplayStyle.Flex;
        var city = controller.Match.CityAt(p); var stationed = controller.Match.Board.UnitAt(p);
        title.text = $"City {city.id} · Level {city.level}";
        if (city.ownerSeatIndex != 0) {
            title.text = city.ownerSeatIndex < 0 ? "Village" : "AI city · Level " + controller.Match.KnownCityLevel(0,p);
            summary.text = "Fixed 3×3 territory · " + (city.ownerSeatIndex < 0 ? "Unclaimed" : "Opponent controlled");
            if (stationed?.Seat == 0) {
                Button(tabs,"InspectUnit","Unit",()=>InspectStationedUnit(p),"tab",CanAct);
                var capture=Actions.FirstOrDefault(a=>a.Kind==PolyActionKind.Capture && a.ActorId==stationed.Id);
                if(capture!=null) Card(choices,"Capture","Capture","Claim this city",()=>controller.Act(capture),CanAct);
                hint.text=capture!=null ? "Claim this city and its territory." : "Your unit can capture at the start of its next turn.";
            }
            return;
        }
        decimal food = controller.Match.CityFood(city.id);
        summary.text = $"Population {city.population}/{city.PopulationNeeded} · +{food:0.##} Food/turn · +{controller.Match.CityIncome(city.id):0.##} Gold/turn";
        root.Q("Growth").style.display = DisplayStyle.Flex;
        int turns=food==0 ? 0 : (int)Math.Ceiling((city.FoodNeeded-city.StoredFood)/food);
        root.Q<Label>("GrowthLabel").text = $"Food {city.StoredFood:0.##}/{city.FoodNeeded} · " + (food == 0 ? "Build food improvements to grow" : $"Next population in {turns} " + (turns==1 ? "turn" : "turns"));
        root.Q("GrowthFill").style.width = Length.Percent((float)(100m * city.StoredFood / city.FoodNeeded));
        var recruitTab = Button(tabs,"RecruitTab","Recruit",()=>{cityTab=0;page=0;Refresh();},"tab"); recruitTab.EnableInClassList("selected",cityTab==0);
        var developTab = Button(tabs,"DevelopTab","Tiles",()=>{cityTab=1;page=0;Refresh();},"tab"); developTab.EnableInClassList("selected",cityTab==1);
        if (stationed?.Seat == 0) Button(tabs,"InspectUnit","Unit",()=>InspectStationedUnit(p),"tab",CanAct);
        if (cityTab == 0) {
            if(stationed!=null) {
                choices.style.display=DisplayStyle.None;
                hint.text=stationed.Seat==0 ? stationed.Definition.DisplayName+" stationed · Tap it again to inspect the unit. Move it to recruit." : "Defeat the enemy occupying this city to recruit.";
                return;
            }
            string[] roster = { "poly-builder", "poly-warrior", "poly-rider", "poly-archer", "poly-defender", "poly-swordsman", "poly-knight", "poly-catapult" };
            pageCount = 3; page = Mathf.Clamp(page,0,pageCount-1);
            foreach (var id in roster.Skip(page*3).Take(3)) {
                var d = PolyRules.Definition(id); var action = Actions.FirstOrDefault(a=>a.Kind==PolyActionKind.Recruit && a.ActorId==city.id && a.Choice==id);
                bool unlocked = controller.Match.HasTechnology(0,PolyRules.UnitTechnology(id));
                Card(choices,"Recruit_"+id,d.DisplayName,unlocked ? d.RecruitCost+" Gold" : "Needs "+PolyRules.Technology(PolyRules.UnitTechnology(id)).Name,
                    ()=>controller.Act(action),CanAct && action!=null,world.Icon(id));
            }
            int supported = controller.Match.Board.Units.Count(u=>u.Seat==0 && controller.Match.UnitState(u.Id).homeCityId==city.id);
            hint.text = controller.Match.Board.UnitAt(p)!=null ? "Move the stationed unit to recruit." : city.recruited ? "Recruitment is available next turn." : $"Units {supported}/{city.level+2} · New recruits are ready next turn.";
            ShowPages();
        } else {
            var tiles = Enumerable.Range(0,121).Where(n=>n!=p && controller.Match.KnownTile(0,n)!=null && controller.Match.CityAt(n)==null &&
                (controller.Match.KnownTile(0,n).improvementOwnerSeatIndex==0 ? controller.Match.KnownConnection(0,n).CityId==city.id :
                    controller.Match.KnownTile(0,n).improvement==PolyImprovement.None && controller.Match.ConstructionConnection(0,n).CityId==city.id)).ToArray();
            pageCount = (tiles.Length+2)/3; page = Mathf.Clamp(page,0,Math.Max(0,pageCount-1));
            foreach(var n in tiles.Skip(page*3).Take(3)) {
                int position=n; var tile = controller.Match.KnownTile(0,n);
                Card(choices,"InspectTile"+n,TileName(tile),YieldLabel(controller.Match.KnownYield(0,n)),()=>InspectTile(position),true,world.Icon(TileIcon(tile)));
            }
            hint.text = "Builders can develop reachable land. Each improvement supplies its nearest owned city."; ShowPages();
        }
    }
    private void InspectStationedUnit(int p)
    {
        var unit=controller.Match.Board.UnitAt(p); if(unit?.Seat!=0) return;
        controller.Manager.gridManager.TryGetTile(p%11,p/11,out var tile);
        controller.HandleBoardTap(tile.GetComponent<TileHighlighter>(),controller.SceneUnits[unit.Id],controller.SceneCities[controller.Match.CityAt(p).id]);
    }
    private void InspectTile(int p)
    {
        controller.Manager.gridManager.TryGetTile(p%11,p/11,out var tile);
        controller.HandleBoardTap(tile.GetComponent<TileHighlighter>(),null,null);
    }
    private static string TileName(PolyTile tile)
    {
        if (tile.improvement == PolyImprovement.HuntingHut) return "Hunting hut";
        if (tile.improvement == PolyImprovement.LumberHut) return "Lumber hut";
        return tile.improvement != PolyImprovement.None ? tile.improvement.ToString() : tile.resource != PolyResource.None ? tile.resource.ToString() : tile.terrain.ToString();
    }
    private static string TileIcon(PolyTile tile) => tile.improvement != PolyImprovement.None ? tile.improvement.ToString() : tile.resource != PolyResource.None ? tile.resource.ToString() : tile.terrain.ToString();
    private static string YieldLabel(PolyYield yield) => $"{yield.Food:0.##} Food · {yield.Gold:0.##} Gold/turn";
    private void DrawTile(int p)
    {
        choices.style.display = DisplayStyle.Flex;
        choices.AddToClassList("tile-choices");
        var known = controller.Match.KnownTile(0,p); title.text = TileName(known);
        var connection = known.improvementOwnerSeatIndex>=0 || known.roadOwnerSeatIndex>=0 ? controller.Match.KnownConnection(0,p) : controller.Match.ConstructionConnection(0,p);
        var city = controller.Match.City(connection.CityId);
        summary.text = YieldLabel(controller.Match.KnownYield(0,p));
        if(connection.CityId>0) summary.text += $"\nCity {connection.CityId} · Distance {connection.Distance} · {connection.Efficiency*100:0.#}% yield";
        bool visible = controller.Match.Visibility(0)[p];
        var potential = visible ? controller.Match.DevelopmentOptions(0,p).ToArray() : new PolyAction[0];
        pageCount = (potential.Length+1)/2; page = Mathf.Clamp(page,0,Math.Max(0,pageCount-1));
        foreach(var option in potential.Skip(page*2).Take(2)) {
            var action=option;
            string label = action.Technologies.Length==0 ? action.Label : "Research + " + action.Label.Substring(6);
            string detail = $"+{action.Food:0.##} Food · +{action.GoldYield:0.##} Gold/turn\n{action.Cost} Gold";
            if(action.Technologies.Length>0) detail += $" ({action.TechnologyCost} tech + {action.BuildCost} build)\n" + string.Join(" → ",action.Technologies.Select(id=>PolyRules.Technology(id).Name));
            Card(choices,"Develop_"+action.Choice,label,detail,()=>controller.Act(action),CanAct && Actions.Any(a=>a.Same(action)),world.Icon(DevelopmentIcon(action.Choice)));
        }
        if (potential.Length==0) {
            choices.style.display = DisplayStyle.None;
            hint.text = !visible ? "Remembered terrain and yields. Bring a Builder closer to construct here." :
                known.improvement!=PolyImprovement.None ? "This permanent improvement supplies its nearest owned city each turn." : "Construction needs a Builder and an unoccupied land tile.";
        } else {
            var locked = potential.FirstOrDefault(a=>a.Technologies.Length>0);
            var builder=controller.Match.ReadyBuilder(0,p);
            var occupant=controller.Match.Board.UnitAt(p);
            hint.text = builder==null ? occupant?.Seat==0 && occupant.Definition.TypeId=="poly-builder" ? "This Builder has finished its turn. Construction is available next turn." : "Move a ready Builder onto this tile to construct here. You can buy its technology below." :
                locked!=null ? "Research + build includes every prerequisite. Construction ends this Builder's turn." : "Construction ends this Builder's turn. Food and Gold arrive each owner turn.";
            if(locked!=null) {
                string id=locked.Technologies[0];
                var research=Actions.FirstOrDefault(a=>a.Kind==PolyActionKind.Research && a.Choice==id);
                Button(tabs,"TileResearch_"+id,$"Research {PolyRules.Technology(id).Name}\n{controller.Match.ResearchCost(0,id)} Gold",()=>controller.Act(research),"tile-research",CanAct && research!=null);
            }
        }
        var claim=Actions.FirstOrDefault(a=>a.Kind==PolyActionKind.ClaimImprovement && a.Destination==p);
        if(claim!=null) Button(tabs,"ClaimImprovement","Capture improvement",()=>controller.Act(claim),"tab",CanAct);
        if (city?.ownerSeatIndex==0) Button(tabs,"InspectCity","City "+city.id,()=>controller.OpenCity(city.id),"tab");
        ShowPages();
        var unit = visible ? controller.Match.Board.UnitAt(p) : null;
        if (unit!=null && unit.Seat!=0) hint.text = $"{unit.Definition.DisplayName} · HP {unit.Health/10f:0.#}/{unit.Definition.MaxHealthUnits/10}";
    }
    private static string DevelopmentIcon(string choice)
    {
        switch(choice) { case "fruit": return "Orchard"; case "hunt": return "HuntingHut"; case "lumber": return "LumberHut"; default: return char.ToUpperInvariant(choice[0])+choice.Substring(1); }
    }
    private void DrawRewards(PolyAction[] rewards)
    {
        sheet.style.display = DisplayStyle.Flex; choices.style.display = DisplayStyle.Flex;
        var city=controller.Match.City(rewards[0].ActorId); title.text=$"City {city.id} · Level {city.level-city.pendingRewards+1}";
        summary.text=$"Income increased to +{controller.Match.CityIncome(city.id):0.##} Gold/turn. Choose a reward.";
        foreach(var reward in rewards) { var a=reward; Card(choices,"Reward_"+a.Choice,a.Choice=="workshop" ? "Workshop" : a.Choice=="walls" ? "Fortifications" : a.Choice=="giant" ? "Giant" : a.Choice=="population" ? "Population" : "Gold",a.Label.Split(':').Last().Trim(),()=>controller.Act(a),CanAct); }
    }
    private void ShowPages()
    {
        root.Q("Pages").style.display=pageCount>1 ? DisplayStyle.Flex : DisplayStyle.None;
        previous.SetEnabled(page>0); following.SetEnabled(page+1<pageCount); pageLabel.text=$"{page+1} / {pageCount}";
    }
    private void DrawOverlay()
    {
        overlay.style.display=DisplayStyle.Flex;
        switch(screen) {
            case "tech": DrawTechnology(); break;
            case "confirm":
                overlayTitle.text="New Poly match?"; overlaySummary.text="This replaces your current Poly continuation slot.";
                Button(overlayChoices,"ConfirmNew","Start new 11×11 match",()=>{controller.NewMatch();ShowScreen("");});
                Button(overlayChoices,"CancelNew","Keep current match",()=>ShowScreen("menu")); break;
            case "result":
                overlayTitle.text=controller.Match.Board.WinnerSeat==0 ? "Victory" : "Defeat"; overlaySummary.text=controller.Status;
                Button(overlayChoices,"PlayAgain","New match",()=>ShowScreen("confirm")); Button(overlayChoices,"ResultMenu","Main menu",controller.Leave); break;
            case "help":
                overlayTitle.text="How to play"; overlaySummary.text="Drag to move the map. Pinch to zoom. Tap a unit, then a yellow tile or red target.";
                overlayHint.text="Tap a stationed unit again to switch to its city. Recruit a reusable Builder for 3 Gold. Move it onto reachable land, then choose Build here. Improvements connect to your nearest city; yields are divided by tile distance. Food grows population and Gold increases income. Buy missing technology directly from the tile.\n\nEnter a village or enemy city, then Capture on your following turn. Take every AI city to win. Rest units to heal. Riders retreat after attacking; Knights attack again after kills.\n\nYour Poly match saves automatically in this browser.";
                Button(overlayChoices,"HelpBack","Back to game",()=>ShowScreen("")); break;
            default:
                overlayTitle.text="Poly"; overlaySummary.text=controller.SaveStatus;
                Button(overlayChoices,"Resume","Back to game",()=>ShowScreen(""));
                Button(overlayChoices,"Save","Save",()=>{controller.SaveGame();Refresh();});
                Button(overlayChoices,"Help","How to play",()=>ShowScreen("help"));
                Button(overlayChoices,"Feedback","Feedback",()=>{ShowScreen("");controller.OpenFeedback();});
                Button(overlayChoices,"New","New match",()=>ShowScreen("confirm"),enabled:!controller.IsOpponentRunning);
                Button(overlayChoices,"SaveMenu","Save & main menu",controller.Leave); break;
        }
    }
    private void OnDestroy() { if(runtimePanel!=null) Destroy(runtimePanel); }
    public void RefocusBoard() => boardCamera.GetComponent<CameraController>().RefocusBoard();
}
