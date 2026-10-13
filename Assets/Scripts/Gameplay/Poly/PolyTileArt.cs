using BlockNations.Simulation.Poly;
using UnityEngine;
using UnityEngine.UIElements;

// Code-native map art keeps terrain and the eight unit roles readable at any zoom.
public sealed class PolyTileArt : VisualElement
{
    public PolyTile Tile;
    public bool Visible, City;
    public int Owner, UnitOwner;
    public string Unit;
    public PolyTileArt()
    {
        style.position = Position.Absolute; style.left = style.right = style.top = style.bottom = 0;
        generateVisualContent += Draw;
    }
    private void Draw(MeshGenerationContext context)
    {
        var p = context.painter2D; float w = contentRect.width, h = contentRect.height;
        if (w <= 0 || h <= 0) return;
        Color bg = Tile == null ? new Color(.10f, .16f, .20f) : Tile.terrain == PolyTerrain.Forest ? new Color(.18f, .39f, .29f) : Tile.terrain == PolyTerrain.Mountain ? new Color(.49f, .52f, .47f) : new Color(.42f, .56f, .32f);
        if (Tile != null && !Visible) bg *= .48f;
        p.fillColor = bg; Rect(p, 0, 0, w, h);
        if (Tile == null) return;
        float s = w / 48;
        if (Tile.terrain == PolyTerrain.Forest) { p.fillColor = new Color(.09f, .25f, .18f); Triangle(p, w * .28f, h * .25f, 13 * s); Triangle(p, w * .69f, h * .37f, 12 * s); }
        if (Tile.terrain == PolyTerrain.Mountain) { p.fillColor = new Color(.73f, .75f, .68f); Triangle(p, w * .5f, h * .2f, 19 * s); }
        if (Tile.road) { p.strokeColor = new Color(.77f, .70f, .52f); p.lineWidth = 3 * s; p.BeginPath(); p.MoveTo(new Vector2(0, h * .58f)); p.LineTo(new Vector2(w, h * .58f)); p.Stroke(); }
        if (Tile.resource != PolyResource.None) { p.fillColor = Tile.resource == PolyResource.Ore ? new Color(.89f, .85f, .54f) : new Color(.97f, .74f, .31f); Circle(p, w * .8f, h * .2f, 4 * s); }
        if (Tile.improvement != PolyImprovement.None) {
            p.fillColor = new Color(.89f, .73f, .46f); Rect(p, w * .13f, h * .45f, w * .73f, h * .2f);
            p.strokeColor = new Color(.39f, .28f, .15f); p.lineWidth = 1;
            for (int i = 1; i < 4; i++) { p.BeginPath(); p.MoveTo(new Vector2(w * (.13f + i * .18f), h * .45f)); p.LineTo(new Vector2(w * (.13f + i * .18f), h * .65f)); p.Stroke(); }
            if (Tile.improvement == PolyImprovement.Windmill || Tile.improvement == PolyImprovement.Sawmill || Tile.improvement == PolyImprovement.Forge) { p.fillColor = Color.white; Triangle(p, w * .5f, h * .24f, 10 * s); }
        }
        if (City) { p.fillColor = Owner == 0 ? new Color(.29f, .65f, .98f) : Owner == 1 ? new Color(.93f, .41f, .33f) : new Color(.92f, .87f, .7f); Rect(p, w * .15f, h * .22f, w * .7f, h * .42f); Triangle(p, w * .5f, h * .04f, 16 * s); }
        if (Unit != null) {
            p.fillColor = UnitOwner == 0 ? new Color(.22f, .60f, .98f) : new Color(.96f, .32f, .29f);
            Circle(p, w * .5f, h * .39f, 12 * s);
            p.strokeColor = Color.white; p.lineWidth = 2 * s; p.BeginPath();
            if (Unit == "poly-archer") { p.Arc(new Vector2(w * .47f, h * .39f), 7 * s, -90, 90); p.MoveTo(new Vector2(w * .47f, h * .23f)); p.LineTo(new Vector2(w * .47f, h * .55f)); }
            else if (Unit == "poly-rider" || Unit == "poly-knight") { p.MoveTo(new Vector2(w * .35f, h * .46f)); p.LineTo(new Vector2(w * .55f, h * .26f)); p.LineTo(new Vector2(w * .64f, h * .44f)); }
            else if (Unit == "poly-defender") { p.MoveTo(new Vector2(w * .34f, h * .28f)); p.LineTo(new Vector2(w * .67f, h * .28f)); p.LineTo(new Vector2(w * .5f, h * .53f)); p.ClosePath(); }
            else if (Unit == "poly-catapult") { p.MoveTo(new Vector2(w * .34f, h * .48f)); p.LineTo(new Vector2(w * .66f, h * .48f)); p.MoveTo(new Vector2(w * .5f, h * .48f)); p.LineTo(new Vector2(w * .63f, h * .24f)); }
            else { p.MoveTo(new Vector2(w * .37f, h * .52f)); p.LineTo(new Vector2(w * .63f, h * .25f)); p.MoveTo(new Vector2(w * .34f, h * .37f)); p.LineTo(new Vector2(w * .54f, h * .51f)); }
            p.Stroke();
        }
        if (!Visible) { p.fillColor = new Color(0, 0, 0, .3f); Rect(p, 0, 0, w, h); }
    }
    private static void Rect(Painter2D p, float x, float y, float w, float h) { p.BeginPath(); p.MoveTo(new Vector2(x,y)); p.LineTo(new Vector2(x+w,y)); p.LineTo(new Vector2(x+w,y+h)); p.LineTo(new Vector2(x,y+h)); p.ClosePath(); p.Fill(); }
    private static void Circle(Painter2D p, float x, float y, float r) { p.BeginPath(); p.Arc(new Vector2(x,y), r, 0, 360); p.Fill(); }
    private static void Triangle(Painter2D p, float x, float y, float size) { p.BeginPath(); p.MoveTo(new Vector2(x,y)); p.LineTo(new Vector2(x-size*.6f,y+size)); p.LineTo(new Vector2(x+size*.6f,y+size)); p.ClosePath(); p.Fill(); }
}
