using System;
using System.Collections;
using BlockNations.Simulation;
using UnityEngine;

// Explicitly wired only in the owned private build snapshot. The ordinary
// shipping scenes, transport, saves and background trainer do not use this driver.
[DefaultExecutionOrder(-1000)]
public sealed class PrivatePolicyPlaytestController : MonoBehaviour
{
    [SerializeField] private TurnManager turnManager;
    private const int HumanSeat = 0, OpponentSeat = 1;
    private System.Random random;
    private IEnumerator thinking;
    private double nextThinkTime;
    public bool Ready { get; private set; }
    public int MatchNumber { get; private set; }
    public int FirstSeat => turnManager.ExternalFirstSeatIndex;
    public string Failure { get; private set; }
    public TurnManager.AIRecruitVariant Difficulty { get; private set; }

    private void Awake()
    {
        if (turnManager == null || turnManager.gameObject.scene != gameObject.scene ||
            !turnManager.IsExternallyDrivenMatch || turnManager.gridManager == null ||
            turnManager.gridManager.width != 7 || turnManager.gridManager.height != 7)
            throw new InvalidOperationException("The private playtest needs its explicitly wired 7x7 external match.");
        var release = Resources.Load<LearnedPolicyRelease>(LearnedPolicyRelease.ResourcePath);
        if (release == null) throw new InvalidOperationException("The frozen private opponent is missing.");
        turnManager.ConfigureExperimentalRules(release.RulesForBoard(7));
        turnManager.currentMode = TurnManager.GameMode.VsAI;
        AIRecruitVariantSelection.TryConsume(out var selected);
        Difficulty = selected;
        if (!LearnedAIRuntime.IsLearned(Difficulty))
            throw new InvalidOperationException("Choose a learned private difficulty.");
        turnManager.aiRecruitVariant = Difficulty;
        random = new System.Random(Guid.NewGuid().GetHashCode());
        turnManager.ConfigureExternalHumanSeat(HumanSeat, NewGame);
    }

    private IEnumerator Start()
    {
        while (!turnManager.ExternalMatchReady) yield return null;
        Ready = true;
        BeginMatch();
    }

    public void NewGame()
    {
        if (!Ready || !turnManager.gameOver) return;
        BeginMatch();
    }

    private void BeginMatch()
    {
        CloseThinking();
        Failure = null;
        Time.timeScale = 1f;
        // Keep the RNG and frozen difficulty across rematches. Reloading the
        // scene would consume a default difficulty and restart the opening stream.
        int first = random.Next(2);
        turnManager.ResetExternalMatchWithOpening(MatchOpening.StartingGold, first,
            MatchOpening.SecondPlayerGoldBonus);
        turnManager.RecalculatePlayerVisibility();
        MatchNumber++;
        nextThinkTime = Time.realtimeSinceStartupAsDouble + .25;
    }

    private void Update()
    {
        if (!Ready || Failure != null || Time.timeScale <= 0) return;
        if (turnManager.gameOver) { CloseThinking(); return; }
        if (!turnManager.IsTurnOwnedBySeat(OpponentSeat) ||
            Time.realtimeSinceStartupAsDouble < nextThinkTime) return;
        if (thinking == null) thinking = turnManager.RunExternalLearnedTurn(OpponentSeat, Difficulty);
        // The runtime yields after bounded CPU slices/actions. Explicitly own
        // its enumerator so leaving or rematching disposes pending tensors.
        if (thinking.MoveNext()) return;
        CloseThinking();
        if (!string.IsNullOrEmpty(turnManager.LearnedAIStatusForUi))
        { Failure = turnManager.LearnedAIStatusForUi; return; }
        if (!turnManager.gameOver && !turnManager.TryAdvanceExternalMatchTurn(OpponentSeat))
        {
            Failure = "The private opponent could not finish its turn. Return to the menu.";
            Debug.LogError("[Private Policy Playtest] " + Failure);
        }
    }

    private void CloseThinking()
    {
        (thinking as IDisposable)?.Dispose();
        thinking = null;
    }

    private void OnDisable() => CloseThinking();
}
