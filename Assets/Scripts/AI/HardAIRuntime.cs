using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using BlockNations.AI;
using UnityEngine;
using Object = UnityEngine.Object;

// Unity adapter owns observation, incremental scheduling, and authoritative execution.
// Policy/simulation code cannot inspect the scene or use human-viewer visibility.
public sealed class HardAIRuntime
{
    private readonly IAIActionPolicy policy = new HardTacticianPolicy();
    private readonly SeatAIObservationSource observationSource = new SeatAIObservationSource();
    private readonly HashSet<string> failedActions = new HashSet<string>(StringComparer.Ordinal);
    private IAIDecision activeDecision;
    public int KnowledgeGeneration { get; private set; }
    public void ResetKnowledge()
    {
        KnowledgeGeneration++;
        activeDecision?.Cancel();
        activeDecision = null;
        observationSource.ResetKnowledge();
        failedActions.Clear();
    }

    public IEnumerator RunTurn(TurnManager manager, int seatIndex) => RunTurnWithPolicy(manager, seatIndex, policy);

    public IEnumerator RunTurnWithPolicy(TurnManager manager, int seatIndex, IAIActionPolicy actionPolicy)
    {
        if (!CanRun(manager, seatIndex)) yield break;
        int generation = KnowledgeGeneration;
        failedActions.Clear();
        foreach (Unit unit in Object.FindObjectsByType<Unit>())
            if (unit.ownerSeatIndex == seatIndex) unit.ResetMovementForTurn();
        int remainingWork = HardTacticianPolicy.TurnWorkBudget;
        // Safety bound scales with current assets; each successful action consumes a resource.
        int actionLimit = 32 + Object.FindObjectsByType<Unit>().Length * 8;
        for (int actionNumber = 0; actionNumber < actionLimit && CanRun(manager, seatIndex) && generation == KnowledgeGeneration; actionNumber++)
        {
            SeatAIObservationSource.Context context = Observe(manager, seatIndex);
            IAIDecision search = actionPolicy.BeginDecision(context.Observation, Math.Min(HardTacticianPolicy.DecisionWorkBudget, remainingWork));
            activeDecision = search;
            Stopwatch elapsed = Stopwatch.StartNew();
            HardAIDiagnostics.Begin(context.Observation, search, actionPolicy.Version, manager.turnNumber);
            while (!search.Complete && CanRun(manager, seatIndex) && generation == KnowledgeGeneration)
            {
                while (manager.IsAIExecutionPaused && CanRun(manager, seatIndex) && generation == KnowledgeGeneration) yield return null;
                if (!CanRun(manager, seatIndex) || generation != KnowledgeGeneration) yield break;
                Stopwatch slice = Stopwatch.StartNew();
                do { search.AdvanceOnce(); }
                while (!search.Complete && slice.Elapsed.TotalMilliseconds < 4);
                HardAIDiagnostics.ElapsedSeconds = elapsed.Elapsed.TotalSeconds;
                if (!search.Complete) yield return null;
            }
            if (!CanRun(manager, seatIndex) || generation != KnowledgeGeneration) yield break;
            remainingWork = Math.Max(0, remainingWork - search.WorkCompleted);
            AICandidatePlan best = search.Best;
            if (best == null || best.Actions.Length == 0 || best.Actions[0].Kind == AIActionKind.EndTurn)
            {
                if (HardAIExperienceRecorder.Enabled) HardAIExperienceRecorder.Append(AIExperienceRecord.Create(
                    context.Observation, search, actionPolicy.Version, manager.turnNumber, AIExecutionResult.EndTurn));
                yield break;
            }
            while ((HardAIDiagnostics.ShouldWait || manager.IsAIExecutionPaused) && CanRun(manager, seatIndex) && generation == KnowledgeGeneration) yield return null;
            if (!CanRun(manager, seatIndex) || generation != KnowledgeGeneration) yield break;
            HardAIDiagnostics.ConsumeStep();
            AIAction selected = best.Actions[0];
            bool executed = context.RuntimeActions.TryGetValue(selected.Key, out LegalTurnAction action) && TryExecute(manager, action);
            if (!executed)
                failedActions.Add(selected.Key);
            else failedActions.Clear();
            HardAIDiagnostics.LastAction = (executed ? "" : "Rejected: ") + Describe(context.Observation, selected);
            manager.RecalculatePlayerVisibility();
            if (HardAIExperienceRecorder.Enabled && generation == KnowledgeGeneration && manager.gridManager != null)
                HardAIExperienceRecorder.Append(AIExperienceRecord.Create(context.Observation, search, actionPolicy.Version,
                    manager.turnNumber, executed ? AIExecutionResult.Executed : AIExecutionResult.Rejected,
                    Observe(manager, seatIndex).Observation, manager.gameOver));
            // Rebuild visibility and the root mask after EVERY action, including recruitment.
            yield return null;
        }
    }

    private static bool CanRun(TurnManager manager, int seat) => manager != null && !manager.gameOver &&
        manager.gridManager != null &&
        manager.currentMode == TurnManager.GameMode.VsAI && manager.currentTurnSeatIndex == seat && manager.IsTurnOwnedBySeat(seat);

    public SeatAIObservationSource.Context Observe(TurnManager manager, int seat) =>
        observationSource.Observe(manager, seat, includeZeroDamageAttacks: false, excludedActions: failedActions);

    // Retained for existing runtime regression callers.
    internal static bool TryExecute(TurnManager manager, LegalTurnAction proposed) =>
        SeatAIActionExecutor.TryExecute(manager, proposed);

    public static string Describe(AIObservation observation, AIAction action)
    {
        string destination = $"({action.Destination % observation.Width},{action.Destination / observation.Width})";
        if (action.Kind == AIActionKind.EndTurn) return "End turn";
        if (action.Kind == AIActionKind.Recruit) return $"Recruit {observation.RecruitTypes[action.RecruitType].Type} at {destination}";
        string actor = action.Actor < observation.Units.Length ? observation.Units[action.Actor].Type : "new recruit";
        return $"{actor} #{action.Actor}: {action.Kind} {destination}";
    }
}

public static class HardAIDiagnostics
{
    public static AIObservation Observation { get; private set; }
    public static IAIDecision Search { get; private set; }
    public static string PolicyVersion { get; private set; }
    public static int Turn { get; private set; }
    public static double ElapsedSeconds;
    public static string LastAction;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static bool PauseBeforeAction;
    private static bool stepRequested;
    public static bool ShouldWait => PauseBeforeAction && !stepRequested;
    public static void RequestStep() => stepRequested = true;
    public static void ConsumeStep() => stepRequested = false;
#else
    public static bool ShouldWait => false;
    public static void ConsumeStep() { }
#endif
    public static void Begin(AIObservation observation, IAIDecision search, string version, int turn)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Observation = observation;
        Search = search;
        PolicyVersion = version;
        Turn = turn;
        ElapsedSeconds = 0;
#endif
    }
}
