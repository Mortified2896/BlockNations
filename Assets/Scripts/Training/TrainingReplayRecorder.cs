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
            pieces.Add(new TrainingReplayHistory.Piece { city = true, x = city.x, y = city.y, seat = city.ownerSeatIndex,
                sprite = renderer != null ? renderer.sprite : null, color = renderer != null ? renderer.color : Color.white });
        }
        foreach (Unit unit in Object.FindObjectsByType<Unit>())
        {
            if (unit.gameObject.scene != manager.gameObject.scene || unit.currentHealthUnits <= 0 ||
                !manager.gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile)) continue;
            SpriteRenderer renderer = unit.PrimarySpriteRenderer;
            pieces.Add(new TrainingReplayHistory.Piece { x = tile.gridX, y = tile.gridY, seat = unit.ownerSeatIndex,
                health = unit.currentHealthUnits, maxHealth = unit.maxHealthUnits,
                sprite = renderer != null ? renderer.sprite : null, color = renderer != null ? renderer.color : Color.white });
        }
        int size = manager.gridManager.width;
        var blueVision = new bool[size * size];
        var redVision = new bool[size * size];
        foreach (TileVisibility tile in manager.ComputeVisibilityForSeat(0))
            blueVision[tile.gridY * size + tile.gridX] = true;
        foreach (TileVisibility tile in manager.ComputeVisibilityForSeat(1))
            redVision[tile.gridY * size + tile.gridX] = true;
        return new TrainingReplayHistory.Frame { round = manager.turnNumber, seat = manager.currentTurnSeatIndex,
            blueGold = manager.GetGoldForSeat(0), redGold = manager.GetGoldForSeat(1), description = description,
            visionWidth = size, blueVision = blueVision, redVision = redVision, pieces = pieces.ToArray() };
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
