using UnityEngine;

// Draw recorded state over the live camera; training scene objects stay untouched.
public sealed class TrainingReplayView
{
    private GUIStyle health;
    public void Draw(TrainingReplayHistory replay)
    {
        if (!replay.Inspecting) return;
        if (health == null) health = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter,
            fontSize = 14, normal = { textColor = Color.white } };
        float left = TrainingOverlay.ReservedWidth / TrainingOverlay.Scale;
        float width = Screen.width / TrainingOverlay.Scale - left, height = Screen.height / TrainingOverlay.Scale;
        Fill(new Rect(left, 0, width, height), new Color(0.13f, 0.38f, 0.53f));
        int size = replay.CurrentGame.boardSize;
        float step = Mathf.Min(width, height) / (size + 2);
        float x = left + (width - size * step) / 2, y = (height - size * step) / 2;
        for (int row = 0; row < size; row++) for (int column = 0; column < size; column++)
            Fill(new Rect(x + column * step, y + (size - 1 - row) * step, step * .90f, step * .90f), Color.white);
        foreach (var piece in replay.CurrentFrame.pieces)
        {
            Rect cell = new Rect(x + piece.x * step, y + (size - 1 - piece.y) * step, step * .90f, step * .90f);
            if (piece.sprite != null)
            {
                Rect textureRect = piece.sprite.rect;
                Texture2D texture = piece.sprite.texture;
                Color previous = GUI.color; GUI.color = piece.color;
                GUI.DrawTextureWithTexCoords(cell, texture, new Rect(textureRect.x / texture.width, textureRect.y / texture.height,
                    textureRect.width / texture.width, textureRect.height / texture.height));
                GUI.color = previous;
            }
            else
            {
                Color previous = GUI.color; GUI.color = piece.seat == 0 ? Color.blue : Color.red;
                GUI.Label(cell, piece.city ? "City" : "Unit", health); GUI.color = previous;
            }
            if (!piece.city)
            {
                Rect badge = new Rect(cell.x, cell.y, cell.width, 22);
                Fill(badge, new Color(0, 0, 0, .65f));
                GUI.Label(badge, CombatValues.FormatRatio(piece.health, piece.maxHealth), health);
            }
        }
    }
    private static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color; GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous;
    }
}
