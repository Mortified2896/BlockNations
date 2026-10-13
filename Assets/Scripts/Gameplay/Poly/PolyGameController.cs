using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BlockNations.Simulation.Poly;
using UnityEngine;
using UnityEngine.SceneManagement;

// Poly owns the rules and checkpoint; the normal scene owns the board, camera and input.
public sealed partial class PolyGameController : MonoBehaviour
{
    public const string SceneName = "Poly";
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private UnitSelectionManager unitSelection;
    [SerializeField] private CityUIManager cityUI;
    [SerializeField] private UnitUIManager unitUI;
    [SerializeField] private GameplayTopHudUITKView feedbackHud;
    [SerializeField] private PolyWorldPresentation world;
    [SerializeField] private PolyGameplayUITKView view;
    public PolyMatch Match { get; private set; }
    public bool IsReady => Match != null;
    public bool IsFeedbackOpen => feedbackHud.IsFeedbackOpen;
    public void OpenFeedback() => feedbackHud.OpenFeedback();
    public bool IsMenuOpen { get; set; }
    public bool IsOpponentRunning { get; private set; }
    public TurnManager Manager => turnManager;
    public string Status { get; private set; } = "Opening Poly…";
    public string SaveStatus { get; private set; } = "";
    public int SelectedPosition { get; private set; } = -1;
    public enum InspectKind { None, Unit, City, Tile }
    public InspectKind Inspection { get; private set; }
    private string savePath;
    private bool exitAfterSave;
    private Coroutine opponentRoutine;
    private readonly Dictionary<int, Unit> sceneUnits = new Dictionary<int, Unit>();
    private readonly Dictionary<int, City> sceneCities = new Dictionary<int, City>();
    public IReadOnlyDictionary<int, Unit> SceneUnits => sceneUnits;
    public IReadOnlyDictionary<int, City> SceneCities => sceneCities;
    public int Revision { get; private set; }

    private void Awake()
    {
        if (turnManager == null || unitSelection == null || cityUI == null || unitUI == null || feedbackHud == null ||
            world == null || view == null || turnManager.PolyController != this)
            throw new InvalidOperationException("Poly native scene references must be assigned in the Inspector.");
        turnManager.ConfigurePolyDriver(NewMatch);
        GameplayInputOrchestrator.ResetTransientInputState();
    }
    private IEnumerator Start()
    {
        while (!turnManager.ExternalMatchReady) yield return null;
        if (PublicWebBuild.UsesGoogleAccounts) {
            bool verified = false;
            yield return WebAccountClient.Initialize(ok => verified = ok);
            if (!verified || !WebAccountClient.CanPlayPoly) { Status = "Poly is available only to its owner."; Revision++; yield break; }
        } else if (!Application.isEditor) { Status = "Poly requires the account website."; Revision++; yield break; }
        string saveRoot = Application.persistentDataPath;
#if UNITY_EDITOR
        // Isolated Editor acceptance runs must never touch the owner's continuation slot.
        string testRoot = Environment.GetEnvironmentVariable("BLOCKNATIONS_POLY_TEST_SAVE_ROOT");
        if (!string.IsNullOrEmpty(testRoot)) saveRoot = testRoot;
#endif
        savePath = PolySaveStore.PathFor(saveRoot, WebAccountClient.PlayerId);
        try {
            Match = PolySaveStore.Load(savePath);
            if (Match == null) NewMatch();
            else { Status = "Continued your Poly match."; PrepareWorld(); Project(); ResumeAI(); }
        } catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException) {
            Status = "This Poly save could not be opened. It has been preserved."; Revision++;
        }
    }
    public void NewMatch()
    {
        if (IsOpponentRunning || feedbackHud.IsFeedbackOpen) return;
        Match = new PolyMatch(unchecked((int)DateTime.UtcNow.Ticks));
        CloseInspection(); IsMenuOpen = false; Status = "Tap a unit to move, or a resource to develop your city.";
        PrepareWorld(); Project(); view.RefocusBoard(); SaveGame();
    }
    public void CloseInspection()
    {
        unitSelection.ClearSelection(); cityUI.ClosePanel(); unitUI.ClosePanel();
        if (TileHoverManager.Instance != null) TileHoverManager.Instance.ClearSelection();
        Inspection = InspectKind.None; SelectedPosition = -1; Revision++;
    }
    public void OpenCity(int id)
    {
        if (!IsReady || !sceneCities.TryGetValue(id, out City city) || Match.City(id).ownerSeatIndex != 0) return;
        CloseInspection(); cityUI.OnCityClicked(city); SelectedPosition = Match.City(id).position; Inspection = InspectKind.City; Revision++;
    }
    // Called by the established world tap handler after its UI/drag/pinch gates.
    public void HandleBoardTap(TileHighlighter tile, Unit hitUnit, City hitCity)
    {
        if (!IsReady || !Match.IsTurnOwnedBySeat(0) || IsMenuOpen || feedbackHud.IsFeedbackOpen) return;
        if (tile == null) { CloseInspection(); return; }
        var visibility = tile.GetComponent<TileVisibility>();
        int p = visibility.gridY * 11 + visibility.gridX;
        Unit selected = unitSelection.SelectedUnit;
        if (selected != null && GetSceneLegalActions(0, selected, true).Any(a => a.TargetTile == visibility)) {
            unitSelection.TryMoveOrAttackAtPosition(visibility.transform.position);
            Inspection = unitSelection.SelectedUnit != null ? InspectKind.Unit : InspectKind.None;
            SelectedPosition = Inspection == InspectKind.None ? -1 : Match.Board.GetUnit(UnitId(unitSelection.SelectedUnit)).Position;
            Revision++; return;
        }
        if (hitUnit != null && hitUnit.ownerSeatIndex == 0) {
            cityUI.ClosePanel(); unitSelection.SelectUnit(hitUnit);
            Inspection = unitSelection.SelectedUnit != null ? InspectKind.Unit : InspectKind.None;
            SelectedPosition = Inspection == InspectKind.None ? -1 : p; Revision++; return;
        }
        CloseInspection();
        if (Match.KnownTile(0, p) == null) return;
        SelectedPosition = p;
        if (hitCity != null && Match.CityAt(p)?.ownerSeatIndex == 0) { cityUI.OnCityClicked(hitCity); Inspection = InspectKind.City; }
        else Inspection = InspectKind.Tile;
        Revision++;
    }
    public bool Act(PolyAction action)
    {
        if (!IsReady || action == null || action.SeatIndex != 0 || !Match.IsTurnOwnedBySeat(0) || IsOpponentRunning || feedbackHud.IsFeedbackOpen) return false;
        if (!Match.TryApply(action, out string message)) { Status = message; Revision++; return false; }
        Status = message;
        if (action.Kind == PolyActionKind.EndTurn) CloseInspection();
        if (SoundManager.Instance != null) {
            if (action.Kind == PolyActionKind.Move) SoundManager.Instance.PlayMove();
            else if (action.Kind == PolyActionKind.Attack) SoundManager.Instance.PlayAttack();
            else if (action.Kind == PolyActionKind.Recruit) SoundManager.Instance.PlayRecruit();
            else if (action.Kind == PolyActionKind.EndTurn) SoundManager.Instance.PlayTurnEnd();
        }
        Project(); SaveGame(); ResumeAI(); return true;
    }
    public bool TryEndTurn(int seatIndex) => seatIndex == 0 && Act(new PolyAction(PolyActionKind.EndTurn, seatIndex));
    private void ResumeAI()
    {
        if (Match.IsTurnOwnedBySeat(1) && !IsOpponentRunning) opponentRoutine = StartCoroutine(OpponentTurn());
    }
    private IEnumerator OpponentTurn()
    {
        IsOpponentRunning = true; Revision++;
        for (int i = 0; i < 400 && Match.IsTurnOwnedBySeat(1); i++) {
            yield return new WaitForSecondsRealtime(.12f);
            while (IsMenuOpen || feedbackHud.IsFeedbackOpen) yield return null;
            var action = PolyOpponent.Choose(PolyObservation.ForSeat(Match, 1));
            if (action == null || !Match.TryApply(action, out _)) break;
            Project(); SaveGame();
        }
        if (Match.IsTurnOwnedBySeat(1)) {
            var end = Match.LegalActions(1).FirstOrDefault(a => a.Kind == PolyActionKind.EndTurn);
            if (end != null) Match.TryApply(end, out _);
        }
        IsOpponentRunning = false; opponentRoutine = null;
        Status = Match.Board.GameOver ? (Match.Board.WinnerSeat == 0 ? "Victory! Every opposing city is yours." : "Your last city was captured.") : "Your turn.";
        Project(); SaveGame();
        if (!Match.Board.GameOver && SoundManager.Instance != null) SoundManager.Instance.PlayTurnStart();
    }
    public void SaveGame()
    {
        if (!IsReady || string.IsNullOrEmpty(savePath)) return;
        try {
            PolySaveStore.Write(savePath, Match); SaveStatus = "Saving…";
#if UNITY_WEBGL && !UNITY_EDITOR
            BN_PolySync(gameObject.name);
#else
            OnPolySaved("ok");
#endif
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            SaveStatus = "Save failed. Keep this page open and try again."; exitAfterSave = false;
        }
    }
    public void OnPolySaved(string result)
    {
        SaveStatus = result == "ok" ? "Saved" : "Save failed. Keep this page open and try again.";
        Revision++;
        if (result == "ok" && exitAfterSave) SceneManager.LoadScene("MainMenu");
    }
    public void Leave()
    {
        IsMenuOpen = true;
        if (!IsReady) { SceneManager.LoadScene("MainMenu"); return; }
        exitAfterSave = true; SaveGame();
    }
    private void OnApplicationPause(bool paused) { if (paused) SaveGame(); }
    private void OnApplicationQuit() { SaveGame(); }
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void BN_PolySync(string receiver);
#endif
}
