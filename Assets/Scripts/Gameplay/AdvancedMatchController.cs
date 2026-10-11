using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BlockNations.Simulation;
using UnityEngine;

// The advanced scene uses the normal world/input presentation. Only this
// driver owns its temporary economy, commands and deterministic opponent.
public sealed class AdvancedMatchController : MonoBehaviour
{
    public const string SceneName = "Advanced";
    public const int HumanSeat = 0;
    public const int OpponentSeat = 1;
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private UnitSelectionManager unitSelection;
    [SerializeField] private CityUIManager cityUI;
    [SerializeField] private UnitUIManager unitUI;
    [SerializeField] private GameplayTopHudUITKView feedbackHud;
    private readonly Dictionary<int, Unit> sceneUnits = new Dictionary<int, Unit>();
    private readonly Dictionary<int, City> sceneCities = new Dictionary<int, City>();
    private readonly AdvancedSimpleOpponent opponent = new AdvancedSimpleOpponent(OpponentSeat);
    private Coroutine opponentRoutine;
    public AdvancedMatch Match { get; private set; }
    public bool IsReady => Match != null;
    public bool IsMenuOpen { get; set; }
    public PopulationSex RecruitSex { get; set; } = PopulationSex.Male;
    public RecruitmentBalance Balance { get; private set; } = RecruitmentBalance.Equal;
    public string Status { get; private set; } = "Tap your city to recruit or develop it.";
    public IReadOnlyDictionary<int, Unit> SceneUnits => sceneUnits;
    public IReadOnlyDictionary<int, City> SceneCities => sceneCities;
    public TurnManager Manager => turnManager;

    private void Awake()
    {
        if (turnManager == null || unitSelection == null || cityUI == null || unitUI == null || feedbackHud == null ||
            turnManager.AdvancedController != this)
            throw new InvalidOperationException("Advanced scene references must be assigned in the Inspector.");
        turnManager.ConfigureAdvancedDriver(Restart);
        GameplayInputOrchestrator.ResetTransientInputState();
    }

    private IEnumerator Start()
    {
        while (!turnManager.ExternalMatchReady) yield return null;
        CreateMatch();
    }

    public int UnitId(Unit unit)
    {
        foreach (var pair in sceneUnits) if (pair.Value == unit) return pair.Key;
        return 0;
    }
    public int CityId(City city)
    {
        foreach (var pair in sceneCities) if (pair.Value == city) return pair.Key;
        return 0;
    }
    public City HumanCity => sceneCities.Values.FirstOrDefault(c => c.ownerSeatIndex == HumanSeat);

    public void Restart() => RestartWithBalance(Balance);
    public void RestartWithBalance(RecruitmentBalance balance)
    {
        if (!turnManager.ExternalMatchReady || feedbackHud.IsFeedbackOpen) return;
        Balance = balance;
        if (opponentRoutine != null) { StopCoroutine(opponentRoutine); opponentRoutine = null; }
        CloseSelection();
        Match = null;
        foreach (Unit unit in sceneUnits.Values)
        {
            unit.gameObject.SetActive(false);
            Destroy(unit.gameObject);
        }
        sceneUnits.Clear(); sceneCities.Clear();
        // Reset the scene through its existing external boundary while the
        // advanced state is absent; then install the authoritative new match.
        turnManager.ResetExternalMatch(initialGold: 0, firstSeat: HumanSeat);
        CreateMatch();
    }

    private void CreateMatch()
    {
        var match = new AdvancedMatch(Balance);
        foreach (SimulationCity city in match.Board.Cities)
        {
            City sceneCity = turnManager.gridManager.GetComponentsInChildren<City>().Single(c =>
                c.x == city.Position % 7 && c.y == city.Position / 7);
            sceneCities.Add(city.Id, sceneCity);
        }
        Match = match;
        RecruitSex = PopulationSex.Male;
        Status = "Tap your city to recruit or develop it.";
        Project();
    }

    public bool TryApply(LegalTurnAction action)
    {
        // UI/control readiness never determines simulation legality. Seat,
        // owner and the wrapper's current state validate every human command.
        if (!IsReady || feedbackHud.IsFeedbackOpen || action.SeatIndex != HumanSeat ||
            !Match.Board.IsTurnOwnedBySeat(action.SeatIndex)) return false;
        var adapter = new SceneSimulationAdapter(turnManager);
        if (action.ActionType == LegalActionType.CityRecruit)
        {
            if (action.City == null || action.City.ownerSeatIndex != action.SeatIndex) return false;
            GameObject prefab = turnManager.GetUnitPrefabForType(action.RecruitUnitTypeId);
            if (prefab == null || prefab.GetComponent<Unit>() == null) return false;
            bool recruited = Match.TryRecruit(action.SeatIndex, CityId(action.City), action.RecruitUnitTypeId, RecruitSex, out string reason);
            Status = recruited ? $"Recruited a {RecruitSex.ToString().ToLowerInvariant()} {action.RecruitUnitTypeId}." : reason;
            if (recruited) Project();
            return recruited;
        }
        if (action.Unit == null || action.Unit.ownerSeatIndex != action.SeatIndex ||
            (action.ActionType != LegalActionType.UnitMove && action.ActionType != LegalActionType.UnitAttack)) return false;
        MatchCommand command = adapter.Command(action);
        turnManager.NotifySimulationCommandPreparing(Match.Board, command);
        MatchTransition transition = Match.Apply(command);
        if (transition.Applied)
        {
            Status = action.ActionType == LegalActionType.UnitAttack ? "Attack complete." : "Unit moved.";
            Project();
            if (SoundManager.Instance != null)
            {
                if (action.ActionType == LegalActionType.UnitAttack) SoundManager.Instance.PlayAttack();
                else SoundManager.Instance.PlayMove();
            }
        }
        turnManager.NotifySimulationCommandCompleted(Match.Board, command, transition);
        return transition.Applied;
    }

    public bool TryUpgrade(City city, CityUpgrade upgrade)
    {
        if (!IsReady || city == null || city.ownerSeatIndex != HumanSeat || feedbackHud.IsFeedbackOpen) return false;
        bool applied = Match.TryUpgrade(HumanSeat, CityId(city), upgrade, out string reason);
        Status = applied ? $"{UpgradeName(upgrade)} upgraded." : reason;
        if (applied) Project();
        return applied;
    }

    public bool TryDisband(Unit unit)
    {
        if (!IsReady || unit == null || unit.ownerSeatIndex != HumanSeat || feedbackHud.IsFeedbackOpen) return false;
        bool applied = Match.TryDisband(HumanSeat, UnitId(unit), out string reason);
        Status = applied ? "Soldier returned to the civilian population." : reason;
        if (applied) { CloseSelection(); Project(); }
        return applied;
    }

    public bool TryEndTurn(int seat)
    {
        if (!IsReady || seat != HumanSeat || feedbackHud.IsFeedbackOpen || !Match.Board.IsTurnOwnedBySeat(seat)) return false;
        CloseSelection();
        if (!Match.Apply(new MatchCommand(MatchActionKind.EndTurn, seat)).Applied) return false;
        Status = "Opponent's turn…";
        if (SoundManager.Instance != null) SoundManager.Instance.PlayTurnEnd();
        Project();
        opponentRoutine = StartCoroutine(PlayOpponent());
        return true;
    }

    private IEnumerator PlayOpponent()
    {
        for (int remaining = 128; remaining > 0 && Match.Board.IsTurnOwnedBySeat(OpponentSeat); remaining--)
        {
            yield return new WaitForSecondsRealtime(0.25f);
            while (feedbackHud.IsFeedbackOpen || IsMenuOpen) yield return null;
            if (!opponent.Step(Match, out _)) break;
            Project();
        }
        if (Match.Board.IsTurnOwnedBySeat(OpponentSeat))
        {
            Match.Apply(new MatchCommand(MatchActionKind.EndTurn, OpponentSeat));
            Project();
        }
        if (!Match.Board.GameOver)
        {
            AdvancedTurnReport report = Match.LastTurnReport;
            Status = report.CivilianDeaths + report.UnitDeaths > 0 ? "Supply shortages caused losses. Check your city." :
                report.UnpaidUnits + report.UnfedUnits > 0 ? "Some soldiers lack supplies. Check food and upkeep." :
                report.Births > 0 ? "Your population grew. Your turn." : "Your turn.";
            if (SoundManager.Instance != null) SoundManager.Instance.PlayTurnStart();
        }
        opponentRoutine = null;
    }

    private void CloseSelection()
    {
        unitSelection.ClearSelection(); cityUI.ClosePanel(); unitUI.ClosePanel();
        if (TileHoverManager.Instance != null) TileHoverManager.Instance.ClearSelection();
    }

    // Stable simulation IDs are mapped to the existing scene objects. No
    // command or population state is reconstructed from rendered objects.
    private void Project()
    {
        MatchState board = Match.Board;
        foreach (int id in sceneUnits.Keys.Where(id => board.GetUnit(id) == null).ToArray())
        {
            Unit unit = sceneUnits[id];
            if (unitSelection.SelectedUnit == unit) CloseSelection();
            unit.SetCurrentHealthUnits(0); unit.Die(); sceneUnits.Remove(id);
        }
        foreach (SimulationUnit unit in board.Units)
        {
            turnManager.gridManager.TryGetTile(unit.Position % board.Width, unit.Position / board.Width, out TileVisibility tile);
            if (!sceneUnits.TryGetValue(unit.Id, out Unit live))
            {
                GameObject prefab = turnManager.GetUnitPrefabForType(unit.Definition.TypeId);
                GameObject spawned = turnManager.InstantiateConfiguredUnit(unit.Definition.TypeId, prefab,
                    tile.transform.position, unit.Seat, null, resetTurnState: true);
                live = spawned.GetComponent<Unit>();
                live.ApplyResolvedDefinition(unit.Definition, preserveCurrentHealth: false);
                sceneUnits.Add(unit.Id, live);
            }
            live.SetCurrentHealthUnits(unit.Health);
            live.movesUsedThisTurn = unit.MovesUsed; live.attacksUsedThisTurn = unit.AttacksUsed;
            live.SetSurprisedRound(unit.SurprisedRound, board.Round);
            Vector3 position = tile.transform.position; position.z = live.transform.position.z;
            live.transform.position = position; live.currentCity = null;
        }
        foreach (SimulationCity city in board.Cities)
        {
            City live = sceneCities[city.Id];
            live.SetOwnerSeatIndex(city.Seat);
            OwnedSprite visual = live.GetComponent<OwnedSprite>();
            if (visual != null) visual.SetOwnerSeatIndex(city.Seat);
            live.hasRecruitedThisTurn = city.Recruited;
            SimulationUnit occupant = board.UnitAt(city.Position);
            live.stationedUnit = occupant == null ? null : sceneUnits[occupant.Id].gameObject;
            if (occupant != null) sceneUnits[occupant.Id].currentCity = live;
        }
        turnManager.ProjectAdvancedTurn(board);
        if (board.GameOver && !turnManager.gameOver)
            turnManager.OnCityCaptured(board.WinnerSeat, sceneCities[board.CapturedCityId]);
        turnManager.RecalculatePlayerVisibility();
        unitSelection.RefreshMoveOutlinesForCurrentTurn();
    }

    public static string UpgradeName(CityUpgrade upgrade) => upgrade == CityUpgrade.Housing ? "Housing" :
        upgrade == CityUpgrade.Food ? "Farm" : "Market";
}
