using System;
using System.Collections.Generic;
using System.Linq;
using BlockNations.Simulation.Poly;
using UnityEngine;

// Normal SpriteRenderers, white tile prefab and fog layers; no UI grid or alternate camera.
public sealed partial class PolyWorldPresentation : MonoBehaviour
{
    [SerializeField] private PolyGameController controller;
    [SerializeField] private GameObject warriorPrefab, riderPrefab, archerPrefab;
    [SerializeField] private Sprite warriorSprite, riderSprite, archerSprite;
    private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
    private readonly SpriteRenderer[] terrain = new SpriteRenderer[121], resource = new SpriteRenderer[121], improvement = new SpriteRenderer[121];
    public GameObject Prefab(string type) => type == "poly-rider" || type == "poly-knight" ? riderPrefab : type == "poly-archer" || type == "poly-catapult" ? archerPrefab : warriorPrefab;
    public Sprite Icon(string type) => type == "poly-warrior" ? warriorSprite : type == "poly-rider" ? riderSprite : type == "poly-archer" ? archerSprite : SpriteFor(type);
    public void ApplyUnitSprite(Unit unit, string type)
    {
        if (type == "poly-warrior" || type == "poly-rider" || type == "poly-archer") return;
        var renderer = unit.GetComponentsInChildren<SpriteRenderer>().First(r => r != unit.moveOutline);
        renderer.sprite = SpriteFor(type);
        unit.GetComponent<UnitPresentationClamp>()?.ApplyClamp();
    }
    private void Awake()
    {
        if (controller == null || warriorPrefab == null || riderPrefab == null || archerPrefab == null || warriorSprite == null || riderSprite == null || archerSprite == null)
            throw new InvalidOperationException("Poly world references must be assigned in the Inspector.");
    }
    public void Initialize()
    {
        for (int p = 0; p < 121; p++) {
            if (terrain[p] != null) continue;
            controller.Manager.gridManager.TryGetTile(p % 11, p / 11, out var tile);
            terrain[p] = CreateRenderer(tile, "Poly terrain", 2, Vector3.zero, .78f);
            improvement[p] = CreateRenderer(tile, "Poly improvement", 3, Vector3.zero, .66f);
            resource[p] = CreateRenderer(tile, "Poly resource", 4, new Vector3(.26f, .26f, 0), .32f);
            foodYield[p] = CreateRenderer(tile, "Poly food yield", 5, new Vector3(-.23f, -.32f, 0), .43f);
            goldYield[p] = CreateRenderer(tile, "Poly Gold yield", 5, new Vector3(.23f, -.32f, 0), .43f);
        }
    }
    private static SpriteRenderer CreateRenderer(TileVisibility tile, string name, int sortingOrder, Vector3 offset, float scale)
    {
        var go = new GameObject(name); go.transform.SetParent(tile.transform, false); go.transform.localPosition = offset;
        go.transform.localScale = Vector3.one * scale;
        var renderer = go.AddComponent<SpriteRenderer>(); renderer.sortingOrder = sortingOrder;
        renderer.sharedMaterial = tile.GetComponent<SpriteRenderer>().sharedMaterial;
        return renderer;
    }
    public void Refresh()
    {
        for (int p = 0; p < 121; p++) {
            var known = controller.Match.KnownTile(0, p);
            terrain[p].enabled = known != null; resource[p].enabled = known != null;
            improvement[p].enabled = known != null && known.improvement != PolyImprovement.None;
            RefreshYields(p, known != null);
            if (known == null) continue;
            string landscape = known.terrain != PolyTerrain.Plains ? known.terrain.ToString() : known.road ? "Road" : null;
            terrain[p].sprite = landscape == null ? null : SpriteFor(landscape);
            terrain[p].color = known.terrain == PolyTerrain.Forest ? new Color(.12f,.43f,.22f) : new Color(.38f,.41f,.44f);
            improvement[p].sprite = known.improvement == PolyImprovement.None ? null : SpriteFor(known.improvement.ToString());
            improvement[p].color = new Color(.45f,.32f,.15f);
            resource[p].sprite = known.resource == PolyResource.None || controller.Match.CityAt(p) != null ? null : SpriteFor(known.resource.ToString());
            resource[p].color = known.resource == PolyResource.Ore ? new Color(.59f,.46f,.16f) : new Color(.77f,.50f,.10f);
        }
        foreach (var city in controller.Match.Cities) {
            var live = controller.SceneCities[city.id]; var renderers = live.GetComponentsInChildren<SpriteRenderer>();
            int owner = controller.Match.KnownCityOwner(0, city.position);
            if (owner >= 0) live.GetComponent<OwnedSprite>()?.SetOwnerSeatIndex(owner);
            else foreach (var r in renderers) r.color = new Color(.52f,.49f,.42f);
            foreach (var r in renderers) r.enabled = controller.Match.KnownTile(0,city.position) != null;
        }
    }
    private Sprite SpriteFor(string key)
    {
        if (sprites.TryGetValue(key, out var value)) return value;
        string[] pixels;
        switch (key) {
            case "Forest": pixels = new[] {"....#...........","...###.....#....","..#####...###...",".#######.#####..","...###..#######.","..#####...###...",".#######.#####..","###############.","....#......#....","....#......#...."}; break;
            case "LumberHut": pixels = new[] {"...#........#...","..###......###..",".#####....#####.","...#....#####...","......#########.",".....###########","......#..##..#..","......#..##..#..","......########..",".####.########..",".####..........."}; break;
            case "HuntingHut": pixels = new[] {"...#.........#..","..###.......###.",".#####..#..#####","...#...###...#..","......#####.....",".....#######....","....#########...",".....#.....#....",".....#..#..#....",".....#..#..#....",".....#######...."}; break;
            case "Orchard": pixels = new[] {"...####..####...","..############..",".##############.",".####.####.####.","..############..","...####..####...","......####......",".......##.......",".......##.......",".....######....."}; break;
            case "Mountain": case "Ore": pixels = new[] {".......#........","......###.......",".....#...#......","....#.....#.....","...#...#...#....","..#...###...#...",".#...#####...#..","################"}; break;
            case "Mine": pixels = new[] {".......#........","......###.......",".....#####......","....#######.....","...#########....","..###.....###...",".####.....####..","#####.....#####.","#####.....#####.","################"}; break;
            case "Farm": case "Crops": pixels = new[] {".#..#..#..#..#..","###.###.###.###.",".#..#..#..#..#..",".#..#..#..#..#..","................",".#..#..#..#..#..","###.###.###.###.",".#..#..#..#..#.."}; break;
            case "Windmill": pixels = new[] {"##..........##..",".##........##...","..##......##....","...##....##.....","....######......","....######......","...##....##.....","..##.####.##....",".##..####..##...","##...####...##..",".....####.......","....######......"}; break;
            case "Sawmill": case "Forge": pixels = new[] {"...######.......","..########......",".##########.....","###......###....","..#..##..#......","..#..##..#......","..#......#......","..########......","..########......"}; break;
            case "Road": pixels = new[] {"................","################","................","################"}; break;
            case "Animals": pixels = new[] {"..#......#......","...#....#.......","....####........","....####........",".###########....",".############...","...##....##.....","...##....##....."}; break;
            case "Fruit": pixels = new[] {".......#........","......##........","...###..###.....","..##########....","..##########....","..##########....","...########.....","....######......"}; break;
            case "poly-defender": pixels = new[] {".....######.....",".....#....#.....",".....######.....","...##########...","...#........#...","...#.######.#...","...#.######.#...","...#.######.#...","....########....",".....######.....","......####......","......####......",".....##..##....."}; break;
            case "poly-swordsman": pixels = new[] {".............#..",".....####...##..","....######.##...","....######.##...",".....####.##....","...########.....","..###.####.###..","..###.####..##..",".....######.....",".....######.....","......####......",".....##..##.....","....###..###...."}; break;
            case "poly-knight": pixels = new[] {"............##..",".....####...##..",".....####...##..","......##...###..","..##########....",".#############..","###############.",".###########....","...########.....","...##...##......","...##...##......","..###..###......"}; break;
            case "poly-catapult": pixels = new[] {"...........###..","..........####..",".........##.....","........##......",".......##.......","......##........",".....##.........","....##..........","..###########...",".#############..",".###.......###..",".###.......###..","..##.......##..."}; break;
            default: pixels = new[] {".....######.....","....########....","....########....",".....######.....","..############..",".##############.","###.########.###","###.########.###","....########....","....########....",".....######.....","....###..###....","...####..####..."}; break;
        }
        var texture = new Texture2D(16,16,TextureFormat.RGBA32,false) { filterMode = FilterMode.Point, name = "Poly " + key };
        var colors = new Color32[256];
        int offset = (16-pixels.Length)/2;
        for(int y=0;y<pixels.Length;y++) for(int x=0;x<Math.Min(16,pixels[y].Length);x++) if(pixels[y][x]=='#') colors[(15-y-offset)*16+x] = new Color32(255,255,255,255);
        texture.SetPixels32(colors); texture.Apply();
        value = Sprite.Create(texture,new Rect(0,0,16,16),new Vector2(.5f,.5f),16); value.name = "Poly " + key; sprites.Add(key,value); return value;
    }
    private void OnDestroy()
    {
        foreach(var sprite in sprites.Values) { Destroy(sprite.texture); Destroy(sprite); }
    }
}
