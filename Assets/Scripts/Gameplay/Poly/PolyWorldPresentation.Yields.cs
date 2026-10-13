using UnityEngine;

public sealed partial class PolyWorldPresentation
{
    private readonly SpriteRenderer[] foodYield = new SpriteRenderer[121], goldYield = new SpriteRenderer[121];
    private static readonly string[] Digits = {
        "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
        "111100111001111", "111100111101111", "111001010010010", "111101111101111", "111101111001111"
    };
    private void RefreshYields(int p, bool known)
    {
        var yield = known ? controller.Match.KnownYield(0, p) : new BlockNations.Simulation.Poly.PolyYield();
        foodYield[p].enabled = known && yield.Food > 0; goldYield[p].enabled = known && yield.Gold > 0;
        if (yield.Food > 0) foodYield[p].sprite = YieldSprite(yield.Food, true);
        if (yield.Gold > 0) goldYield[p].sprite = YieldSprite(yield.Gold, false);
    }
    private Sprite YieldSprite(decimal amount, bool food)
    {
        string key = (food ? "FoodYield" : "GoldYield") + amount;
        if (sprites.TryGetValue(key, out var value)) return value;
        string number = amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); int width = 10 + number.Length * 4, height = 9;
        var pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(22, 40, 49, 240);
        string[] icon = food ? new[] { "..#.#..", ".####..", "..###..", ".####..", "...#..." } : new[] { "..###..", ".#####.", ".##.##.", ".#####.", "..###.." };
        var color = food ? new Color32(175, 222, 116, 255) : new Color32(255, 207, 91, 255);
        for (int y = 0; y < 5; y++) for (int x = 0; x < 7; x++) if (icon[y][x] == '#') pixels[(height - 3 - y) * width + 1 + x] = color;
        for (int digit = 0; digit < number.Length; digit++) {
            if(number[digit]=='.') { pixels[(height-7)*width+9+digit*4+1]=new Color32(255,255,255,255); continue; }
            string glyph = Digits[number[digit] - '0'];
            for (int y = 0; y < 5; y++) for (int x = 0; x < 3; x++) if (glyph[y * 3 + x] == '1')
                pixels[(height - 3 - y) * width + 9 + digit * 4 + x] = new Color32(255, 255, 255, 255);
        }
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Poly " + key };
        texture.SetPixels32(pixels); texture.Apply();
        value = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), width); value.name = "Poly " + key;
        sprites.Add(key, value); return value;
    }
}
