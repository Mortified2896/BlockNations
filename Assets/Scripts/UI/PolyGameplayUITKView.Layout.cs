using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class PolyGameplayUITKView
{
    private float lastPublish;
    private void Layout()
    {
        float ratio = 1;
#if UNITY_WEBGL && !UNITY_EDITOR
        ratio = Mathf.Max(1, BN_PolyPixelRatio());
#else
        if (Application.isMobilePlatform && Screen.dpi > 160) ratio = Screen.dpi / 160;
#endif
        runtimePanel.scale = ratio;
        hud.EnableInClassList("compact-short", Screen.height / ratio < 660);
        hud.EnableInClassList("landscape-short", Screen.width > Screen.height && Screen.height / ratio < 500);
        var safe = Screen.safeArea;
        hud.style.left = safe.xMin / ratio; hud.style.right = (Screen.width - safe.xMax) / ratio;
        hud.style.top = (Screen.height - safe.yMax) / ratio; hud.style.bottom = safe.yMin / ratio;
    }
    // Read-only acceptance diagnostics publish only the human observation and actual geometry.
    private void PublishBrowserState()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (!controller.IsReady || Time.unscaledTime - lastPublish < .2f || root.worldBound.width <= 0) return;
        lastPublish = Time.unscaledTime;
        var match = controller.Match; var bounds = root.worldBound;
        var state = new BrowserState { round = match.Board.Round, currentTurnSeatIndex = match.currentTurnSeatIndex, gold = match.Board.GoldForSeat(0), income = match.Income(0),
            gameOver = match.Board.GameOver, saved = controller.SaveStatus, status = controller.Status, screen = screen, cameraX = boardCamera.transform.position.x, cameraY = boardCamera.transform.position.y,
            zoom = boardCamera.orthographicSize, selected = controller.SelectedPosition, selectedUnit = controller.SceneUnits.FirstOrDefault(p=>p.Value==UnitSelectionManager.Instance.SelectedUnit).Key };
        foreach (Button button in root.Query<Button>().ToList()) {
            Rect r = button.worldBound; bool shown = r.width > 0 && r.height > 0 && button.visible;
            for (var parent = button.parent; parent != null; parent = parent.parent) if (parent.resolvedStyle.display == DisplayStyle.None) shown = false;
            if (!shown || !bounds.Contains(r.center)) continue;
            state.controls.Add(new BrowserControl { name = button.name, enabled = button.enabledInHierarchy, x = (r.center.x-bounds.x)/bounds.width, y = (r.center.y-bounds.y)/bounds.height });
        }
        foreach (var tile in controller.Manager.gridManager.GetAllTiles()) {
            Vector3 p = boardCamera.WorldToScreenPoint(tile.transform.position);
            state.tiles.Add(new BrowserPoint { position=tile.gridY*11+tile.gridX, x=p.x/Screen.width, y=1-p.y/Screen.height });
        }
        foreach (var c in match.Cities.Where(c=>c.ownerSeatIndex==0)) state.cities.Add(new BrowserCity { id=c.id,level=c.level,population=c.population,pendingRewards=c.pendingRewards });
        foreach (var u in BlockNations.Simulation.Poly.PolyObservation.ForSeat(match,0).Units) state.units.Add(new BrowserUnit { id=u.Id,seat=u.Seat,position=u.Position,health=u.Health,type=u.Definition.TypeId });
        BN_PolyPublish(JsonUtility.ToJson(state));
#endif
    }
    [Serializable] private sealed class BrowserState
    {
        public string mode="Poly", presentation="native", saved,status,screen;
        public int round,currentTurnSeatIndex,gold,income,selected,selectedUnit;
        public float cameraX,cameraY,zoom;
        public bool gameOver;
        public List<BrowserControl> controls=new List<BrowserControl>();
        public List<BrowserPoint> tiles=new List<BrowserPoint>();
        public List<BrowserCity> cities=new List<BrowserCity>();
        public List<BrowserUnit> units=new List<BrowserUnit>();
    }
    [Serializable] private sealed class BrowserControl { public string name; public bool enabled; public float x,y; }
    [Serializable] private sealed class BrowserPoint { public int position; public float x,y; }
    [Serializable] private sealed class BrowserCity { public int id,level,population,pendingRewards; }
    [Serializable] private sealed class BrowserUnit { public int id,seat,position,health; public string type; }
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern float BN_PolyPixelRatio();
    [DllImport("__Internal")] private static extern void BN_PolyPublish(string json);
#endif
}
