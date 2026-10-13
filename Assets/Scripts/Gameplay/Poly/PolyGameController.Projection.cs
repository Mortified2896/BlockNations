using System.Collections.Generic;
using System.Linq;
using BlockNations.Simulation.Poly;
using UnityEngine;

public sealed partial class PolyGameController
{
    public int UnitId(Unit unit) => sceneUnits.FirstOrDefault(p => p.Value == unit).Key;
    public int CityId(City city) => sceneCities.FirstOrDefault(p => p.Value == city).Key;
    private void PrepareWorld()
    {
        foreach (var unit in sceneUnits.Values) if (unit != null) { unit.gameObject.SetActive(false); Destroy(unit.gameObject); }
        foreach (var city in turnManager.gridManager.GetComponentsInChildren<City>()) { city.gameObject.SetActive(false); Destroy(city.gameObject); }
        sceneUnits.Clear(); sceneCities.Clear();
        foreach (var city in Match.Cities) {
            var tile = Tile(city.position);
            var go = Instantiate(turnManager.gridManager.cityPrefab, tile.transform.position, Quaternion.identity, turnManager.gridManager.transform);
            var live = go.GetComponent<City>(); live.x = city.position % 11; live.y = city.position / 11;
            go.name = "Poly City " + city.id; sceneCities.Add(city.id, live);
        }
        foreach (var tile in turnManager.gridManager.GetAllTiles()) {
            int p = tile.gridY * 11 + tile.gridX;
            tile.SetSeenSeatIndices(Enumerable.Range(0, 2).Where(seat => Match.KnownTile(seat, p) != null), 0);
        }
        world.Initialize();
    }
    private TileVisibility Tile(int position)
    {
        turnManager.gridManager.TryGetTile(position % 11, position / 11, out var tile); return tile;
    }
    private void Project()
    {
        foreach (int id in sceneUnits.Keys.Where(id => Match.Board.GetUnit(id) == null).ToArray()) {
            var live = sceneUnits[id]; if (unitSelection.SelectedUnit == live) CloseInspection();
            live.SetCurrentHealthUnits(0); live.Die(); sceneUnits.Remove(id);
        }
        foreach (var unit in Match.Board.Units) {
            if (!sceneUnits.TryGetValue(unit.Id, out Unit live)) {
                var prefab = world.Prefab(unit.Definition.TypeId);
                live = Instantiate(prefab, Tile(unit.Position).transform.position, Quaternion.identity).GetComponent<Unit>();
                live.ApplyResolvedDefinition(unit.Definition, false); live.SetOwnerSeatIndex(unit.Seat);
                live.GetComponent<OwnedSprite>()?.SetOwnerSeatIndex(unit.Seat);
                world.ApplyUnitSprite(live, unit.Definition.TypeId);
                sceneUnits.Add(unit.Id, live);
            }
            live.SetCurrentHealthUnits(unit.Health);
            var state = Match.UnitState(unit.Id);
            live.movesUsedThisTurn = state.moved || state.rested ? live.maxMovesPerTurn : 0;
            live.attacksUsedThisTurn = state.attacked || state.rested ? live.maxAttacksPerTurn : 0;
            Vector3 position = Tile(unit.Position).transform.position; position.z = live.transform.position.z;
            live.transform.position = position; live.currentCity = null;
        }
        foreach (var city in Match.Cities) {
            var live = sceneCities[city.id]; live.ownerSeatIndex = city.ownerSeatIndex; live.SyncLegacyOwnershipBridge();
            live.hasRecruitedThisTurn = city.recruited;
            var occupant = Match.Board.UnitAt(city.position);
            live.stationedUnit = occupant == null ? null : sceneUnits[occupant.Id].gameObject;
            if (occupant != null) sceneUnits[occupant.Id].currentCity = live;
        }
        turnManager.ProjectPolyTurn(Match.Board);
        turnManager.RecalculatePlayerVisibility(); world.Refresh();
        unitSelection.RefreshMoveOutlinesForCurrentTurn(); unitSelection.RefreshSelectedHighlights(); Revision++;
    }
    public int RemainingMoveRange(Unit unit)
    {
        if (!IsReady || unit == null || !Match.IsTurnOwnedBySeat(unit.ownerSeatIndex)) return 0;
        var s = Match.UnitState(UnitId(unit));
        return s == null || s.rested || (s.moved || s.attackStarted) && !(unit.UnitTypeId == "poly-rider" && s.attacked && !s.retreatUsed) ? 0 : unit.maxMovesPerTurn;
    }
    public bool CanAttack(Unit unit)
    {
        if (!IsReady || unit == null || unit.UnitTypeId == "poly-builder" || !Match.IsTurnOwnedBySeat(unit.ownerSeatIndex)) return false;
        var s = Match.UnitState(UnitId(unit));
        return s != null && !s.rested && !s.attacked && (unit.CanAttackAfterMoving || !s.moved);
    }
    public List<LegalTurnAction> GetSceneLegalActions(int seatIndex, Unit onlyUnit, bool unitsOnly)
    {
        var result = new List<LegalTurnAction>(); if (!IsReady) return result;
        foreach (var action in Match.LegalActions(seatIndex)) {
            if (action.Kind != PolyActionKind.Move && action.Kind != PolyActionKind.Attack && action.Kind != PolyActionKind.Recruit && action.Kind != PolyActionKind.EndTurn) continue;
            if (unitsOnly && action.Kind != PolyActionKind.Move && action.Kind != PolyActionKind.Attack) continue;
            sceneUnits.TryGetValue(action.ActorId, out var live);
            if (onlyUnit != null && (live != onlyUnit || action.Kind == PolyActionKind.Recruit)) continue;
            var origin = live != null && action.Kind != PolyActionKind.Recruit ? Tile(Match.Board.GetUnit(action.ActorId).Position) : null;
            switch (action.Kind) {
                case PolyActionKind.Move:
                    var path = Match.Paths(Match.Board.GetUnit(action.ActorId))[action.Destination].Select(Tile).ToArray();
                    result.Add(new LegalTurnAction(LegalActionType.UnitMove, seatIndex, live, origin, Tile(action.Destination), null, path)); break;
                case PolyActionKind.Attack:
                    var target = sceneUnits[Match.Board.UnitAt(action.Destination).Id];
                    result.Add(new LegalTurnAction(LegalActionType.UnitAttack, seatIndex, live, origin, Tile(action.Destination), target, null)); break;
                case PolyActionKind.Recruit:
                    var city = sceneCities[action.ActorId]; var cityTile = Tile(Match.City(action.ActorId).position);
                    result.Add(new LegalTurnAction(LegalActionType.CityRecruit, seatIndex, null, cityTile, cityTile, null, null, city, action.Choice, action.Cost)); break;
                default: result.Add(new LegalTurnAction(LegalActionType.EndTurn, seatIndex, null, null, null, null, null)); break;
            }
        }
        return result;
    }
    public bool TryApply(LegalTurnAction action)
    {
        if (action.SeatIndex != 0) return false;
        var legal = GetSceneLegalActions(action.SeatIndex, action.Unit, false).FirstOrDefault(a => a.ActionType == action.ActionType &&
            a.Unit == action.Unit && a.TargetTile == action.TargetTile && a.City == action.City && a.RecruitUnitTypeId == action.RecruitUnitTypeId);
        if (legal.TargetTile == null && action.ActionType != LegalActionType.EndTurn) return false;
        var kind = action.ActionType == LegalActionType.UnitMove ? PolyActionKind.Move : action.ActionType == LegalActionType.UnitAttack ? PolyActionKind.Attack :
            action.ActionType == LegalActionType.CityRecruit ? PolyActionKind.Recruit : PolyActionKind.EndTurn;
        return Act(new PolyAction(kind, action.SeatIndex, action.ActionType == LegalActionType.CityRecruit ? CityId(action.City) : UnitId(action.Unit),
            kind == PolyActionKind.Move || kind == PolyActionKind.Attack ? action.TargetTile.gridY * 11 + action.TargetTile.gridX : -1, action.RecruitUnitTypeId));
    }
}
