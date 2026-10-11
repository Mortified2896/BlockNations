using System;
using System.Collections;
using BlockNations.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Only the dedicated, explicitly wired Tutorial scene owns this driver. It
// cannot load/save a normal match or ask the learned opponent to take a turn.
public sealed class TutorialController : MonoBehaviour
{
    public const string SceneName = "Tutorial";
    public const string CompletionKey = "BlockNations.Tutorial.CoreThree.v1.Completed";
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private UnitSelectionManager unitSelection;
    [SerializeField] private CityUIManager cityUI;
    [SerializeField] private UnitUIManager unitUI;
    [SerializeField] private UIDocument tutorialDocument;
    [SerializeField] private UIDocument topHudDocument;
    [SerializeField] private UIDocument bottomHudDocument;
    [SerializeField] private UIDocument unitPanelDocument;
    [SerializeField] private UIDocument cityPanelDocument;
    [SerializeField] private Camera boardCamera;
    [SerializeField] private GameplayTopHudUITKView topHud;
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    private TutorialUITKView view;
    private Coroutine opponentRoutine;
    private Coroutine demonstrationRoutine;
    private Unit practiceUnit;
    private bool ready, completed, demonstrating;
    public TutorialStep Step { get; private set; }
    public bool LessonCompleted => completed;
    public bool IsReady => ready;
    public bool IsDemonstrating => demonstrating;
    public string FeedbackScreen => "Tutorial / " + TutorialLesson.Id(Step);

    private void Awake()
    {
        if (turnManager == null || unitSelection == null || cityUI == null || unitUI == null ||
            tutorialDocument == null || topHudDocument == null || bottomHudDocument == null ||
            unitPanelDocument == null || cityPanelDocument == null || boardCamera == null || topHud == null)
            throw new InvalidOperationException("Tutorial scene references must be assigned in the Inspector.");
        turnManager.ConfigureTutorialDriver(Restart);
    }

    private IEnumerator Start()
    {
        while (!turnManager.ExternalMatchReady) yield return null;
        view = new TutorialUITKView(tutorialDocument, topHudDocument, bottomHudDocument, unitPanelDocument, cityPanelDocument, boardCamera,
            Continue, ShowDemonstration, Exit);
        turnManager.SimulationCommandCompleted += OnCommandCompleted;
        ready = true;
        BeginLesson(TutorialStep.Welcome);
    }

    private void LateUpdate()
    {
        if (!ready) return;
        view.Layout(turnManager.gridManager, practiceUnit, Step, completed,
            practiceUnit != null && unitSelection.SelectedUnit == practiceUnit, cityUI.IsPanelOpen);
    }

    private void BeginLesson(TutorialStep step)
    {
        if (opponentRoutine != null) { StopCoroutine(opponentRoutine); opponentRoutine = null; }
        if (demonstrationRoutine != null) { StopCoroutine(demonstrationRoutine); demonstrationRoutine = null; }
        demonstrating = false;
        view.SetDemonstrating(false);
        Step = step;
        completed = false;
        unitSelection.ClearSelection();
        cityUI.ClosePanel();
        unitUI.ClosePanel();
        GameplayInputOrchestrator.ResetTransientInputState();
        if (step == TutorialStep.Complete)
        {
            view.Show(step, false);
            PlayerPrefs.SetInt(CompletionKey, 1);
            PlayerPrefs.Save();
            return;
        }
        MatchState board = TutorialLesson.CreateBoard(step);
        turnManager.ResetExternalMatch(initialGold: 2, firstSeat: TutorialLesson.HumanSeat);
        practiceUnit = null;
        foreach (SimulationUnit fixture in board.Units)
        {
            turnManager.gridManager.TryGetTile(fixture.Position % 7, fixture.Position / 7, out TileVisibility tile);
            City city = null;
            foreach (City candidate in turnManager.gridManager.GetComponentsInChildren<City>())
                if (candidate.x == tile.gridX && candidate.y == tile.gridY) city = candidate;
            GameObject spawned = turnManager.InstantiateConfiguredUnit(fixture.Definition.TypeId,
                turnManager.GetUnitPrefabForType(fixture.Definition.TypeId), tile.transform.position, fixture.Seat, city, true);
            Unit unit = spawned.GetComponent<Unit>();
            unit.SetCurrentHealthUnits(fixture.Health);
            unit.movesUsedThisTurn = fixture.MovesUsed;
            unit.attacksUsedThisTurn = fixture.AttacksUsed;
            if (fixture.Id == TutorialLesson.PracticeUnitId) practiceUnit = unit;
        }
        turnManager.RecalculatePlayerVisibility();
        view.Show(step, completed);
    }

    private void OnCommandCompleted(MatchState state, MatchCommand command, MatchTransition transition)
    {
        if (!ready || completed || !transition.Applied) return;
        // A successful action by a different unit is not the requested exercise.
        if (Step != TutorialStep.Recruit && Step != TutorialStep.EndTurn &&
            (practiceUnit == null || command.ActorId != practiceUnit.GetInstanceID())) return;
        if (TutorialLesson.Completes(Step, state, command, transition))
        {
            completed = true;
            view.Show(Step, true);
        }
        else if (Step == TutorialStep.EndTurn && command.Kind == MatchActionKind.EndTurn && command.Seat == TutorialLesson.HumanSeat)
        {
            view.SetStatus("The practice opponent is taking its turn…");
            opponentRoutine = StartCoroutine(PlayOpponentTurn());
        }
        else if (command.Seat == TutorialLesson.HumanSeat && Step != TutorialStep.Welcome)
            view.SetStatus("Use Show me to see the marked action.");
    }

    private IEnumerator PlayOpponentTurn()
    {
        yield return new WaitForSecondsRealtime(0.7f);
        while (topHud.IsFeedbackOpen) yield return null;
        if (!turnManager.IsTurnOwnedBySeat(TutorialLesson.OpponentSeat)) yield break;
        foreach (LegalTurnAction action in LegalActionService.GetLegalActionsForSeat(turnManager, TutorialLesson.OpponentSeat,
            turnManager.ComputeVisibilityForSeat(TutorialLesson.OpponentSeat)))
        {
            if (action.ActionType != LegalActionType.UnitAttack || action.TargetUnit != practiceUnit) continue;
            new SceneSimulationAdapter(turnManager).TryApply(action);
            turnManager.RecalculatePlayerVisibility();
            break;
        }
        yield return new WaitForSecondsRealtime(0.7f);
        while (topHud.IsFeedbackOpen) yield return null;
        turnManager.TryAdvanceExternalMatchTurn(TutorialLesson.OpponentSeat);
        opponentRoutine = null;
    }

    public void Continue()
    {
        if (!ready || topHud.IsFeedbackOpen || demonstrating) return;
        if (Step == TutorialStep.Complete) { Exit(); return; }
        if (Step == TutorialStep.Welcome || completed) BeginLesson(Step + 1);
        else StartDemonstration(continueAfter: true);
    }

    public void ShowDemonstration()
    {
        if (!ready || topHud.IsFeedbackOpen || demonstrating) return;
        if (Step == TutorialStep.Complete) { topHud.OpenFeedback(); return; }
        if (Step == TutorialStep.Welcome) return;
        if (completed) { Retry(); return; }
        StartDemonstration(continueAfter: false);
    }

    private void StartDemonstration(bool continueAfter)
    {
        // Restore a legal, predictable position even after a wrong move or
        // recruit. The demonstration uses the ordinary scene command adapter.
        BeginLesson(Step);
        demonstrating = true;
        view.SetDemonstrating(true);
        demonstrationRoutine = StartCoroutine(Demonstrate(continueAfter));
    }

    private IEnumerator Demonstrate(bool continueAfter)
    {
        view.SetStatus("Watch the highlighted action.");
        yield return DemoPause(0.6f);
        bool hasAction = TryFindLessonAction(out LegalTurnAction action);
        if (Step == TutorialStep.Recruit && hasAction) cityUI.OnCityClicked(action.City);
        else if (Step != TutorialStep.EndTurn) unitSelection.SelectUnit(practiceUnit);
        yield return DemoPause(0.9f);
        bool applied = Step == TutorialStep.EndTurn
            ? turnManager.TryAdvanceExternalMatchTurn(TutorialLesson.HumanSeat)
            : hasAction && new SceneSimulationAdapter(turnManager).TryApply(action);
        turnManager.RecalculatePlayerVisibility();
        if (applied && Step == TutorialStep.EndTurn)
            while (!completed) yield return null;
        if (!applied) view.SetStatus("Use Show me to try again.");
        yield return DemoPause(1.2f);
        demonstrating = false;
        demonstrationRoutine = null;
        view.SetDemonstrating(false);
        if (continueAfter && completed) BeginLesson(Step + 1);
    }

    private IEnumerator DemoPause(float seconds)
    {
        float remaining = seconds;
        while (remaining > 0)
        {
            if (!topHud.IsFeedbackOpen) remaining -= Time.unscaledDeltaTime;
            yield return null;
        }
        while (topHud.IsFeedbackOpen) yield return null;
    }

    private bool TryFindLessonAction(out LegalTurnAction result)
    {
        LegalActionType kind = Step == TutorialStep.Recruit ? LegalActionType.CityRecruit :
            Step == TutorialStep.WarriorAttack || Step == TutorialStep.RiderAttack || Step == TutorialStep.ArcherAttack
            ? LegalActionType.UnitAttack : LegalActionType.UnitMove;
        foreach (var action in LegalActionService.GetLegalActionsForSeat(turnManager, TutorialLesson.HumanSeat,
            turnManager.ComputeVisibilityForSeat(TutorialLesson.HumanSeat)))
        {
            if (action.ActionType != kind || action.TargetTile == null ||
                action.TargetTile.gridX + action.TargetTile.gridY * TutorialLesson.BoardSize != TutorialLesson.Target(Step)) continue;
            if (kind == LegalActionType.CityRecruit ? action.RecruitUnitTypeId != UnitRegistry.WarriorTypeId : action.Unit != practiceUnit) continue;
            result = action;
            return true;
        }
        result = default;
        return false;
    }

    public void Retry() { if (ready && !topHud.IsFeedbackOpen && !demonstrating) BeginLesson(Step); }
    public void Restart() { if (ready && !topHud.IsFeedbackOpen && !demonstrating) BeginLesson(TutorialStep.Welcome); }
    public void Exit()
    {
        if (topHud.IsFeedbackOpen) return;
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private void OnDestroy()
    {
        if (turnManager != null) turnManager.SimulationCommandCompleted -= OnCommandCompleted;
        view?.Dispose();
    }
}
