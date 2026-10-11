using System.Collections.Generic;
using System.Runtime.InteropServices;
using BlockNations.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class AdvancedGameplayUITKView
{
    // Read-only acceptance evidence: own economy and currently visible units.
    // Never publish the opponent's treasury, population or unseen pieces.
    [System.Serializable] private sealed class BrowserState
    {
        public string mode = "advanced", rules, scene, status, tab, recruitSex;
        public int round, currentTurnSeat, gold, men, women, food, housing, soldiers, goldIncome, upkeep, foodBalance;
        public bool gameOver, cityOpen, unitOpen, menuOpen;
        public List<BrowserPiece> visibleUnits = new List<BrowserPiece>();
        public List<BrowserPoint> tiles = new List<BrowserPoint>();
        public List<BrowserControl> controls = new List<BrowserControl>();
    }
    [System.Serializable] private sealed class BrowserPoint { public int position; public float x, y; }
    [System.Serializable] private sealed class BrowserPiece { public int id, ownerSeatIndex, position, health; public string type, sex; }
    [System.Serializable] private sealed class BrowserControl { public string name; public bool enabled; public float x, y, width, height; }
    private float lastBrowserPublish;
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void AdvancedPublishState(string json);
#endif
    private void PublishBrowserState()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (Time.unscaledTime - lastBrowserPublish < 0.2f) return;
        lastBrowserPublish = Time.unscaledTime;
        AdvancedMatch match = controller.Match; MatchState board = match.Board;
        AdvancedCityEconomy city = match.CityEconomy(1); var forecast = match.Forecast(1);
        var state = new BrowserState { rules = AdvancedEconomyRules.Version, scene = gameObject.scene.name,
            status = controller.Status, round = board.Round, currentTurnSeat = board.CurrentTurnSeat, gold = board.GoldForSeat(0),
            men = city.Men, women = city.Women, food = city.StoredFood, housing = forecast.Housing, soldiers = forecast.Soldiers,
            goldIncome = match.GoldIncome(0), upkeep = match.GoldUpkeep(0), foodBalance = forecast.FoodBalance,
            gameOver = board.GameOver, cityOpen = cityUI.IsPanelOpen, unitOpen = unitUI.IsPanelOpen,
            menuOpen = controller.IsMenuOpen, tab = new[] { "Recruit", "Develop", "Population" }[tab], recruitSex = controller.RecruitSex.ToString() };
        bool[] visible = MatchEngine.Visibility(board, 0);
        foreach (SimulationUnit unit in board.Units)
            if (unit.Seat == 0 || visible[unit.Position]) state.visibleUnits.Add(new BrowserPiece { id = unit.Id,
                ownerSeatIndex = unit.Seat, position = unit.Position, health = unit.Health, type = unit.Definition.TypeId,
                sex = unit.Seat == 0 ? match.Soldier(unit.Id)?.Sex.ToString() : null });
        for (int position = 0; position < board.Tiles.Length; position++)
        {
            controller.Manager.gridManager.TryGetTile(position % 7, position / 7, out TileVisibility tile);
            Vector3 screen = boardCamera.WorldToScreenPoint(tile.transform.position);
            state.tiles.Add(new BrowserPoint { position = position, x = screen.x / Screen.width,
                y = 1 - screen.y / Screen.height });
        }
        foreach (Button button in root.Query<Button>().ToList())
        {
            if (button.resolvedStyle.display == DisplayStyle.None || !button.visible || button.worldBound.width == 0) continue;
            bool shown = true;
            for (VisualElement parent = button.parent; parent != null; parent = parent.parent)
                if (parent.resolvedStyle.display == DisplayStyle.None || parent.resolvedStyle.visibility == Visibility.Hidden) shown = false;
            if (!shown) continue;
            Rect rect = button.worldBound, bounds = root.worldBound;
            state.controls.Add(new BrowserControl { name = button.name, enabled = button.enabledInHierarchy,
                x = (rect.center.x - bounds.xMin) / bounds.width, y = (rect.center.y - bounds.yMin) / bounds.height,
                width = rect.width / bounds.width, height = rect.height / bounds.height });
        }
        AdvancedPublishState(JsonUtility.ToJson(state));
#endif
    }
}
