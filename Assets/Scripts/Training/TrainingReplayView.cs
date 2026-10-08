using UnityEngine;

// A single board presentation for every live/replay vision mode. It draws
// recorded copies; scene visibility, policy observations and actions stay untouched.
public sealed class TrainingReplayView
{
    private GUIStyle health;

    public void Draw(TrainingReplayHistory.Frame frame, TrainingReplayHistory.Frame opening, int size,
        TrainingVision vision, Color backgroundColor, bool showActionMarkers)
    {
        if (frame == null) return;
        if (health == null) health = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, padding = new RectOffset() };
        float left = TrainingOverlay.ReservedWidth / TrainingOverlay.Scale;
        float width = Screen.width / TrainingOverlay.Scale - left, height = Screen.height / TrainingOverlay.Scale;
        Fill(new Rect(left, 0, width, height), backgroundColor);
        Vector2 center = new Vector2(left + width / 2, height / 2);
        // Match the arena camera's orthographic projection, with tiles centered
        // at their native world positions rather than at a GUI rectangle corner.
        float pixelsPerWorldUnit = Mathf.Min(width, height) / (size + 2);
        for (int row = 0; row < size; row++) for (int column = 0; column < size; column++)
        {
            Vector2 tileCenter = new Vector2(column - (size - 1) / 2f, row - (size - 1) / 2f) * frame.tileSpacing;
            Rect tileBounds = new Rect(tileCenter - frame.tileWorldSize / 2, frame.tileWorldSize);
            Fill(Project(tileBounds, center, pixelsPerWorldUnit),
                frame.Visible(column, row, vision) ? Color.white : new Color(.055f, .085f, .12f));
        }
        // Starting locations are public. Hidden current ownership stays concealed.
        if (opening != null) foreach (var city in opening.pieces)
        {
            if (!city.city || frame.Visible(city.x, city.y, vision)) continue;
            health.fontSize = Mathf.Max(10, Mathf.RoundToInt(pixelsPerWorldUnit * .15f));
            health.normal.textColor = GuiColor(Color.white);
            GUI.Label(Project(city.bounds, center, pixelsPerWorldUnit), "City", health);
        }
        foreach (var piece in frame.pieces)
        {
            if (!frame.Visible(piece.x, piece.y, vision)) continue;
            if (showActionMarkers && piece.outlineSprite != null)
                DrawSprite(piece.outlineSprite, piece.outlineBounds, piece.outlineColor, false, false, center, pixelsPerWorldUnit);
            if (piece.sprite != null)
                DrawSprite(piece.sprite, piece.bounds, piece.color, piece.flipX, piece.flipY, center, pixelsPerWorldUnit);
        }
        // World health canvases sort above every sprite; keep the same ordering.
        foreach (var piece in frame.pieces)
        {
            if (!piece.hasHealthPresentation || !frame.Visible(piece.x, piece.y, vision)) continue;
            var presentation = piece.healthPresentation;
            Rect badge = Project(presentation.bounds, center, pixelsPerWorldUnit);
            Fill(badge, presentation.badgeColor);
            health.fontSize = Mathf.Max(1, Mathf.RoundToInt(presentation.fontWorldSize * pixelsPerWorldUnit));
            health.normal.textColor = GuiColor(presentation.textColor);
            GUI.Label(badge, presentation.text, health);
        }
    }

    private static Rect Project(Rect world, Vector2 center, float pixelsPerWorldUnit) => new Rect(
        center.x + world.xMin * pixelsPerWorldUnit, center.y - world.yMax * pixelsPerWorldUnit,
        world.width * pixelsPerWorldUnit, world.height * pixelsPerWorldUnit);

    private static void DrawSprite(Sprite sprite, Rect world, Color color, bool flipX, bool flipY,
        Vector2 center, float pixelsPerWorldUnit)
    {
        Rect source = sprite.rect;
        Texture2D texture = sprite.texture;
        Rect uv = new Rect(source.x / texture.width, source.y / texture.height,
            source.width / texture.width, source.height / texture.height);
        if (flipX) { uv.x += uv.width; uv.width = -uv.width; }
        if (flipY) { uv.y += uv.height; uv.height = -uv.height; }
        Color previous = GUI.color;
        GUI.color = GuiColor(color);
        GUI.DrawTextureWithTexCoords(Project(world, center, pixelsPerWorldUnit), texture, uv);
        GUI.color = previous;
    }

    // Sprite and camera colours are authored in sRGB, while IMGUI tint uses the
    // active rendering space. Convert every authored tint, not just the background.
    private static Color GuiColor(Color color) => QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;

    private static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = GuiColor(color);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
