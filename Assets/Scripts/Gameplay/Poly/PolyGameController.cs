using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BlockNations.Simulation.Poly;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed partial class PolyGameController : MonoBehaviour
{
    public const string SceneName = "Poly";
    [SerializeField] private UIDocument document;
    public PolyMatch Match { get; private set; }
    public string Status { get; private set; } = "Opening Poly…";
    private string savePath, saveStatus = "";
    private bool aiRunning, exitAfterSave;
    private PanelSettings runtimePanel;
    private IEnumerator Start()
    {
        if (document == null) throw new InvalidOperationException("Poly needs its serialized UI document.");
        runtimePanel = Instantiate(document.panelSettings);
        document.panelSettings = runtimePanel;
        BuildView();
        if (PublicWebBuild.UsesGoogleAccounts) {
            bool verified = false;
            yield return WebAccountClient.Initialize(ok => verified = ok);
            if (!verified || !WebAccountClient.CanPlayPoly) { Status = "Poly is available only to its owner."; ShowUnavailable(); yield break; }
        } else if (!Application.isEditor) { Status = "Poly requires the account website."; ShowUnavailable(); yield break; }
        savePath = PolySaveStore.PathFor(Application.persistentDataPath, WebAccountClient.PlayerId);
        try {
            Match = PolySaveStore.Load(savePath);
            if (Match == null) NewMatch(); else { Status = "Continued your saved Poly match."; FocusHome(); RefreshView(); ResumeAI(); }
        } catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException) {
            Status = "Could not continue this Poly save. It has been preserved. Choose New match to start again."; ShowUnavailable();
        }
    }
    private void NewMatch()
    {
        Match = new PolyMatch(unchecked((int)DateTime.UtcNow.Ticks)); selected = 12; selectedUnit = 0; tab = "board";
        Status = "Tap your Warrior, then a highlighted tile to move. Develop your city's fixed territory and buy technology with Gold.";
        FocusHome(); SaveGame(); RefreshView();
    }
    private void Act(PolyAction action)
    {
        if (Match == null || aiRunning || !Match.IsTurnOwnedBySeat(0)) return;
        if (Match.TryApply(action, out string message)) { Status = message; SaveGame(); RefreshView(); ResumeAI(); }
        else { Status = message; RefreshView(); }
    }
    private void ResumeAI()
    {
        if (Match != null && Match.IsTurnOwnedBySeat(1) && !aiRunning) StartCoroutine(OpponentTurn());
    }
    private IEnumerator OpponentTurn()
    {
        aiRunning = true; RefreshView();
        for (int i = 0; i < 400 && Match.IsTurnOwnedBySeat(1); i++) {
            var action = PolyOpponent.Choose(PolyObservation.ForSeat(Match, 1));
            if (action == null || !Match.TryApply(action, out _)) break;
            SaveGame(); RefreshView(); yield return new WaitForSecondsRealtime(0.04f);
        }
        if (Match.IsTurnOwnedBySeat(1)) {
            var end = Match.LegalActions(1).FirstOrDefault(a => a.Kind == PolyActionKind.EndTurn);
            if (end != null) Match.TryApply(end, out _);
        }
        aiRunning = false;
        Status = Match.Board.GameOver ? (Match.Board.WinnerSeat == 0 ? "Victory! Every opposing city is yours." : "Defeat. Your last city was captured.") : "Your turn. Gold income collected.";
        SaveGame(); RefreshView();
    }
    private void SaveGame()
    {
        if (Match == null || string.IsNullOrEmpty(savePath)) return;
        try {
            PolySaveStore.Write(savePath, Match); saveStatus = "Saving…";
#if UNITY_WEBGL && !UNITY_EDITOR
            BN_PolySync(gameObject.name);
#else
            OnPolySaved("ok");
#endif
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            saveStatus = "Save failed. Keep this page open and try Save again."; exitAfterSave = false;
        }
    }
    public void OnPolySaved(string result)
    {
        saveStatus = result == "ok" ? "Saved on this browser" : "Save failed. Keep this page open and try Save again.";
        if (result == "ok" && exitAfterSave) SceneManager.LoadScene("MainMenu");
    }
    private void Leave()
    {
        if (Match == null) { SceneManager.LoadScene("MainMenu"); return; }
        exitAfterSave = true; SaveGame();
    }
    private void OnApplicationPause(bool paused) { if (paused) SaveGame(); }
    private void OnApplicationQuit() { SaveGame(); }
    private void OnDestroy() { if (runtimePanel != null) Destroy(runtimePanel); }
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void BN_PolySync(string receiver);
    [DllImport("__Internal")] private static extern void BN_PolyPublish(string json);
    [DllImport("__Internal")] private static extern float BN_PolyPixelRatio();
#endif
}
