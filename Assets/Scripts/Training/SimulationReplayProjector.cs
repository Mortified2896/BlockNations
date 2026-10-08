using System;
using System.Collections.Generic;
using BlockNations.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

// Cache authored sprite geometry once. Watching training needs no live Unit,
// TileVisibility, or health Canvas for each simulated piece/action.
public sealed class SimulationReplayProjector
{
    private sealed class UnitTemplate
    {
        public TrainingReplayHistory.Piece Piece;
        public Unit.ActionOutlinePresentation Move, Attack;
        public UnitHealthLabel.PresentationTemplate Health;
        public bool HasHealthTemplate;
    }
    private readonly Dictionary<string, UnitTemplate> units = new Dictionary<string, UnitTemplate>(StringComparer.Ordinal);
    private readonly Dictionary<int, TrainingReplayHistory.Piece> cities = new Dictionary<int, TrainingReplayHistory.Piece>();
    private readonly float spacing;
    private readonly Vector2 tileSize;

    public SimulationReplayProjector(TurnManager manager, MatchState opening)
    {
        GridManager grid = manager.gridManager;
        spacing = grid.tileSize;
        SpriteRenderer tileRenderer = grid.tileGrid[0, 0].GetComponent<SpriteRenderer>();
        tileSize = tileRenderer != null ? (Vector2)tileRenderer.bounds.size : Vector2.one * .9f;
        foreach (City city in Object.FindObjectsByType<City>(FindObjectsSortMode.None))
        {
            if (city.gameObject.scene != manager.gameObject.scene) continue;
            var piece = TrainingReplayRecorder.CaptureSprite(city.GetComponent<SpriteRenderer>(), city.x, city.y, city.ownerSeatIndex);
            piece.city = true;
            piece.bounds.position -= (Vector2)city.transform.position;
            cities[city.ownerSeatIndex] = piece;
        }
        for (int seat = 0; seat < opening.SeatCount; seat++) foreach (UnitDefinition definition in opening.Roster)
        {
            GameObject sample = manager.InstantiateConfiguredUnit(definition.TypeId, manager.GetUnitPrefabForType(definition.TypeId),
                Vector3.zero, seat, null, resetTurnState: true);
            if (sample == null || !sample.TryGetComponent(out Unit unit)) throw new InvalidOperationException("Missing unit presentation: " + definition.TypeId);
            var template = new UnitTemplate { Piece = TrainingReplayRecorder.CaptureSprite(unit.PrimarySpriteRenderer, 0, 0, seat) };
            var health = sample.GetComponent<UnitHealthLabel>();
            if (health != null) template.HasHealthTemplate = health.TryGetPresentationTemplate(out template.Health);
            unit.TryGetActionOutlinePresentation(true, false, out template.Move);
            unit.movesUsedThisTurn = unit.maxMovesPerTurn;
            unit.TryGetActionOutlinePresentation(true, true, out template.Attack);
            units.Add(Key(seat, definition.TypeId), template);
            sample.SetActive(false); Object.Destroy(sample);
        }
    }

    public TrainingReplayHistory.Frame Capture(MatchState state, string description)
    {
        var pieces = new List<TrainingReplayHistory.Piece>();
        foreach (SimulationCity city in state.Cities)
        {
            if (!cities.TryGetValue(city.Seat, out var template)) throw new InvalidOperationException("Missing city presentation for seat.");
            template.x = city.Position % state.Width; template.y = city.Position / state.Width;
            template.bounds.position += Center(state, template.x, template.y);
            pieces.Add(template);
        }
        bool[] blueVision = MatchEngine.Visibility(state, 0), redVision = MatchEngine.Visibility(state, 1);
        foreach (SimulationUnit unit in state.Units)
        {
            UnitTemplate template = units[Key(unit.Seat, unit.Definition.TypeId)];
            var piece = template.Piece;
            piece.x = unit.Position % state.Width; piece.y = unit.Position / state.Width;
            piece.health = unit.Health; piece.maxHealth = unit.Definition.MaxHealthUnits;
            Vector2 center = Center(state, piece.x, piece.y);
            piece.bounds.position += center;
            piece.hasHealthPresentation = template.HasHealthTemplate &&
                template.Health.Project(unit.Health, unit.Definition.MaxHealthUnits, center, out piece.healthPresentation);
            if (!state.GameOver && unit.Seat == state.CurrentTurnSeat)
            {
                Unit.ActionOutlinePresentation outline = default;
                if (unit.RemainingMoves > 0) outline = template.Move;
                else if (unit.CanAttack)
                    foreach (MatchLegalAction action in MatchEngine.LegalUnitActions(state, unit.Seat, unit.Id,
                        unit.Seat == 0 ? blueVision : redVision))
                        if (action.Command.Kind == MatchActionKind.Attack) { outline = template.Attack; break; }
                piece.outlineSprite = outline.sprite; piece.outlineColor = outline.color;
                piece.outlineBounds = outline.bounds; piece.outlineBounds.position += center;
            }
            pieces.Add(piece);
        }
        return new TrainingReplayHistory.Frame { round = state.Round, seat = state.CurrentTurnSeat, blueGold = state.GoldForSeat(0),
            redGold = state.GoldForSeat(1), description = description, visionWidth = state.Width,
            tileSpacing = spacing, tileWorldSize = tileSize, blueVision = blueVision, redVision = redVision, pieces = pieces.ToArray() };
    }
    private Vector2 Center(MatchState state, int x, int y) =>
        new Vector2(x - (state.Width - 1) / 2f, y - (state.Height - 1) / 2f) * spacing;
    private static string Key(int seat, string type) => seat + ":" + type;

    public static string Describe(MatchState state, MatchCommand command)
    {
        string side = command.Seat == 0 ? "Blue" : "Red";
        if (command.Kind == MatchActionKind.EndTurn) return side + " ends turn";
        if (command.Kind == MatchActionKind.Recruit) return side + " recruits " + command.RecruitType;
        string type = state.GetUnit(command.ActorId)?.Definition.TypeId ?? "unit";
        return side + " " + type + (command.Kind == MatchActionKind.Attack ? " attacks " : " moves toward ") +
            $"({command.Destination % state.Width}, {command.Destination / state.Width})";
    }
}
