using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using BlockNations.AI;
using Unity.InferenceEngine;
using UnityEngine;

// Unity supplies filtered observations and executes commands; Inference Engine only
// receives tensors. The same CPU model runs locally in native and browser players.
public sealed class LearnedAIRuntime
{
    private readonly SeatAIObservationSource observer = new SeatAIObservationSource();
    private readonly HashSet<string> rejected = new HashSet<string>(StringComparer.Ordinal);
    private Worker worker;
    private ModelAsset loadedAsset;
    private LearnedPolicyRelease.Manifest manifest;
    private System.Random random;
    private int generation;
    private bool knowsStartingCities;
    public string LastFailure { get; private set; }
    public double LastDecisionMilliseconds { get; private set; }
    public int CompletedDecisions { get; private set; }

    public static bool IsLearned(TurnManager.AIRecruitVariant variant) =>
        variant == TurnManager.AIRecruitVariant.LearnedEasy || variant == TurnManager.AIRecruitVariant.LearnedMedium ||
        variant == TurnManager.AIRecruitVariant.LearnedHard;

    public static LearnedDifficulty DifficultyFor(TurnManager.AIRecruitVariant variant)
    {
        switch (variant)
        {
            case TurnManager.AIRecruitVariant.LearnedEasy: return LearnedDifficulty.Easy;
            case TurnManager.AIRecruitVariant.LearnedMedium: return LearnedDifficulty.Medium;
            case TurnManager.AIRecruitVariant.LearnedHard: return LearnedDifficulty.Hard;
            default: throw new ArgumentOutOfRangeException(nameof(variant));
        }
    }

    public void ResetKnowledge()
    {
        generation++;
        observer.ResetKnowledge();
        rejected.Clear();
        knowsStartingCities = false;
        random = null;
        LastFailure = null;
        CompletedDecisions = 0;
        worker?.Dispose(); worker = null; loadedAsset = null; manifest = null;
    }

    private void Prepare(TurnManager manager, LearnedDifficulty difficulty)
    {
        LearnedPolicyRelease release = Resources.Load<LearnedPolicyRelease>(LearnedPolicyRelease.ResourcePath);
        if (release == null) throw new InvalidOperationException("The learned AI release is missing.");
        manifest = release.Validate(manager.gridManager.width);
        if (manager.gridManager.height != manifest.boardSize)
            throw new InvalidOperationException("The learned AI release requires a square board.");
        ModelAsset asset = release.ModelFor(difficulty);
        if (worker == null || asset != loadedAsset)
        {
            worker?.Dispose();
            worker = new Worker(ModelLoader.Load(asset), BackendType.CPU);
            loadedAsset = asset;
        }
        if (random == null) random = new System.Random(Guid.NewGuid().GetHashCode());
        if (!knowsStartingCities)
        {
            // In the two-seat rules a capture ends the match, so an ongoing match's
            // city locations/owners are also the publicly known starting locations.
            var startingCities = new List<AICityState>();
            foreach (City city in UnityEngine.Object.FindObjectsByType<City>(FindObjectsSortMode.None))
            {
                if (city.gameObject.scene != manager.gameObject.scene || city.ownerSeatIndex < 0 || city.ownerSeatIndex > 1 ||
                    !manager.gridManager.TryGetTile(city.x, city.y, out _)) continue;
                startingCities.Add(new AICityState { Seat = city.ownerSeatIndex, X = city.x, Y = city.y });
            }
            observer.SetPublicStartingCities(startingCities.ToArray());
            knowsStartingCities = true;
        }
    }

    public IEnumerator RunTurn(TurnManager manager, int seat, TurnManager.AIRecruitVariant variant)
    {
        IEnumerator turn = RunTurnCore(manager, seat, variant);
        try
        {
            while (true)
            {
                bool advanced = false;
                object pending = null;
                Exception failure = null;
                try { advanced = turn.MoveNext(); if (advanced) pending = turn.Current; }
                catch (Exception ex) { failure = ex; }
                if (failure != null) { Fail(failure); yield break; }
                if (!advanced) yield break;
                yield return pending;
            }
        }
        finally { (turn as IDisposable)?.Dispose(); }
    }

    private IEnumerator RunTurnCore(TurnManager manager, int seat, TurnManager.AIRecruitVariant variant)
    {
        if (!SeatAIActionExecutor.CanAct(manager, seat)) yield break;
        int currentGeneration = generation;
        LearnedDifficulty difficulty = DifficultyFor(variant);
        try { Prepare(manager, difficulty); }
        catch (Exception ex) { Fail(ex); yield break; }
        rejected.Clear();
        SeatAIObservationSource.Context first = observer.Observe(manager, seat);
        // Only own public resources determine this safeguard, never hidden unit counts.
        int maximumCommands = CommandBudget(first.Observation);
        for (int command = 0; command < maximumCommands && CanContinue(manager, seat, currentGeneration); command++)
        {
            while (manager.IsAIExecutionPaused && CanContinue(manager, seat, currentGeneration)) yield return null;
            if (!CanContinue(manager, seat, currentGeneration)) yield break;
            SeatAIObservationSource.Context context = observer.Observe(manager, seat, excludedActions: rejected);
            int source = -1;
            AIAction selected = default;
            Stopwatch elapsed = Stopwatch.StartNew();
            for (int stage = 0; stage < 2; stage++)
            {
                Dictionary<int, AIAction> choices = LearnedActionSchema.Choices(context.Observation, source);
                using (var input = new Tensor<float>(new TensorShape(1, LearnedActionSchema.ObservationSize),
                    LearnedActionSchema.Encode(context.Observation, source)))
                using (var mask = new Tensor<float>(new TensorShape(1, LearnedActionSchema.ActionCount), LearnedActionSampling.Mask(choices.Keys)))
                {
                    worker.SetInput("obs_0", input);
                    worker.SetInput("action_masks", mask);
                    IEnumerator schedule = worker.ScheduleIterable();
                    bool finished = false;
                    while (!finished && CanContinue(manager, seat, currentGeneration))
                    {
                        while (manager.IsAIExecutionPaused && CanContinue(manager, seat, currentGeneration)) yield return null;
                        if (!CanContinue(manager, seat, currentGeneration)) yield break;
                        Stopwatch slice = Stopwatch.StartNew();
                        try
                        {
                            do { finished = !schedule.MoveNext(); }
                            while (!finished && slice.Elapsed.TotalMilliseconds < 4);
                        }
                        catch (Exception ex) { Fail(ex); yield break; }
                        if (!finished) yield return null;
                    }
                    if (!CanContinue(manager, seat, currentGeneration)) yield break;
                    Tensor<float> output = worker.PeekOutput(manifest.probabilityOutput) as Tensor<float>;
                    if (output == null) { Fail(new InvalidOperationException("The learned AI has no policy output.")); yield break; }
                    output.ReadbackRequest();
                    while (CanContinue(manager, seat, currentGeneration) && !output.IsReadbackRequestDone()) yield return null;
                    if (!CanContinue(manager, seat, currentGeneration)) yield break;
                    int choice;
                    try { choice = LearnedActionSampling.Choose(output.DownloadToArray(), choices.Keys, difficulty, random); }
                    catch (Exception ex) { Fail(ex); yield break; }
                    selected = choices[choice];
                    if (source < 0 && choice != LearnedActionSchema.EndTurn) { source = choice; continue; }
                }
                break;
            }
            LastDecisionMilliseconds = elapsed.Elapsed.TotalMilliseconds;
            CompletedDecisions++;
            if (selected.Kind == AIActionKind.EndTurn) yield break;
            bool applied = context.RuntimeActions.TryGetValue(selected.Key, out LegalTurnAction action) &&
                SeatAIActionExecutor.TryExecute(manager, action);
            if (applied) rejected.Clear(); else rejected.Add(selected.Key);
            manager.RecalculatePlayerVisibility();
            // Every actual action gets a new seat observation, including surprise stops.
            yield return null;
        }
    }

    private bool CanContinue(TurnManager manager, int seat, int expectedGeneration) =>
        generation == expectedGeneration && SeatAIActionExecutor.CanAct(manager, seat);

    private static int CommandBudget(AIObservation observation)
    {
        int budget = 8, recruitBudget = 1;
        foreach (AIUnitState type in observation.RecruitTypes)
            recruitBudget = Math.Max(recruitBudget, 1 + type.MaxMoves + type.MaxAttacks);
        foreach (AIUnitState unit in observation.Units)
            if (unit.Seat == observation.Seat) budget += 1 + unit.MaxMoves + unit.MaxAttacks;
        foreach (AICityState city in observation.Cities)
            if (city.Seat == observation.Seat) budget += recruitBudget;
        return budget;
    }

    private void Fail(Exception exception)
    {
        LastFailure = "Learned AI unavailable. Return to the menu and try again.";
        UnityEngine.Debug.LogError("[Learned AI] " + exception.Message);
    }
}
