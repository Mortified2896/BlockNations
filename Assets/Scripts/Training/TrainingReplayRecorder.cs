using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public static class TrainingReplayRecorder
{
    public static TrainingReplayHistory.Frame Capture(TurnManager manager, string description)
    {
        var pieces = new List<TrainingReplayHistory.Piece>();
        foreach (City city in Object.FindObjectsByType<City>())
        {
            if (city.gameObject.scene != manager.gameObject.scene) continue;
            SpriteRenderer renderer = city.GetComponent<SpriteRenderer>();
            var piece = CaptureSprite(renderer, city.x, city.y, city.ownerSeatIndex);
            piece.city = true;
            pieces.Add(piece);
        }
        foreach (Unit unit in Object.FindObjectsByType<Unit>())
        {
            if (unit.gameObject.scene != manager.gameObject.scene || unit.currentHealthUnits <= 0 ||
                !manager.gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile)) continue;
            SpriteRenderer renderer = unit.PrimarySpriteRenderer;
            var piece = CaptureSprite(renderer, tile.gridX, tile.gridY, unit.ownerSeatIndex);
            piece.health = unit.currentHealthUnits; piece.maxHealth = unit.maxHealthUnits;
            UnitHealthLabel label = unit.GetComponent<UnitHealthLabel>();
            if (label != null) piece.hasHealthPresentation = label.TryGetPresentation(out piece.healthPresentation);
            if (unit.moveOutline != null && unit.moveOutline.enabled)
            {
                piece.outlineSprite = unit.moveOutline.sprite;
                piece.outlineBounds = WorldBounds(unit.moveOutline);
                piece.outlineColor = unit.moveOutline.color;
            }
            pieces.Add(piece);
        }
        int size = manager.gridManager.width;
        var blueVision = new bool[size * size];
        var redVision = new bool[size * size];
        foreach (TileVisibility tile in manager.ComputeVisibilityForSeat(0))
            blueVision[tile.gridY * size + tile.gridX] = true;
        foreach (TileVisibility tile in manager.ComputeVisibilityForSeat(1))
            redVision[tile.gridY * size + tile.gridX] = true;
        SpriteRenderer tileRenderer = manager.gridManager.tileGrid[0, 0].GetComponent<SpriteRenderer>();
        return new TrainingReplayHistory.Frame { round = manager.turnNumber, seat = manager.currentTurnSeatIndex,
            blueGold = manager.GetGoldForSeat(0), redGold = manager.GetGoldForSeat(1), description = description,
            visionWidth = size, tileSpacing = manager.gridManager.tileSize,
            tileWorldSize = tileRenderer != null ? (Vector2)tileRenderer.bounds.size : Vector2.one * .9f,
            blueVision = blueVision, redVision = redVision, pieces = pieces.ToArray() };
    }

    private static TrainingReplayHistory.Piece CaptureSprite(SpriteRenderer renderer, int x, int y, int seat) =>
        new TrainingReplayHistory.Piece { x = x, y = y, seat = seat,
            sprite = renderer != null ? renderer.sprite : null, color = renderer != null ? renderer.color : Color.white,
            bounds = renderer != null ? WorldBounds(renderer) : default,
            flipX = renderer != null && renderer.flipX, flipY = renderer != null && renderer.flipY };

    private static Rect WorldBounds(SpriteRenderer renderer)
    {
        Bounds bounds = renderer.bounds;
        return Rect.MinMaxRect(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y);
    }
    public static string Describe(LegalTurnAction action)
    {
        string side = action.SeatIndex == 0 ? "Blue" : "Red";
        if (action.ActionType == LegalActionType.EndTurn) return side + " ends turn";
        if (action.ActionType == LegalActionType.CityRecruit) return side + " recruits " + action.RecruitUnitTypeId;
        string type = action.Unit != null ? action.Unit.UnitTypeId : "unit";
        return side + " " + type + (action.ActionType == LegalActionType.UnitAttack ? " attacks " : " moves toward ") +
            $"({action.TargetTile.gridX}, {action.TargetTile.gridY})";
    }
}
