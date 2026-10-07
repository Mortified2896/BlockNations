using System.Collections.Generic;
using BlockNations.AI;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(Unity.MLAgents.Policies.BehaviorParameters))]
public sealed class TrainingSeatAgent : Agent
{
    [SerializeField] private TrainingArena arena;
    [SerializeField] private int seatIndex;
    private SeatAIObservationSource.Context context;
    private Dictionary<int, AIAction> choices;
    private float[] observations;
    private int selectedSource = -1;
    private bool awaitingDecision;
    public int SeatIndex => seatIndex;
    public int SelectedSource => selectedSource;

    public override void OnEpisodeBegin()
    {
        selectedSource = -1;
        context = null;
        choices = null;
        observations = null;
        awaitingDecision = false;
    }

    public void PrepareDecision(SeatAIObservationSource.Context snapshot)
    {
        context = snapshot;
        choices = LearnedActionSchema.Choices(snapshot.Observation, selectedSource);
        observations = LearnedActionSchema.Encode(snapshot.Observation, selectedSource);
        awaitingDecision = true;
        RequestDecision();
    }

    public void PrepareTerminalObservation(AIObservation snapshot) =>
        observations = LearnedActionSchema.Encode(snapshot, selectedSource);

    public override void CollectObservations(VectorSensor sensor) =>
        sensor.AddObservation(observations ?? new float[LearnedActionSchema.ObservationSize]);

    public override void WriteDiscreteActionMask(IDiscreteActionMask mask)
    {
        for (int action = 0; action < LearnedActionSchema.ActionCount; action++)
            mask.SetActionEnabled(0, action, choices != null ? choices.ContainsKey(action) : action == LearnedActionSchema.EndTurn);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // Academy can reset both agents while an RPC decision is in flight.
        // A cleared/replayed buffer after that reset is not a game action.
        if (!awaitingDecision || arena == null || !arena.CanAct(seatIndex)) return;
        awaitingDecision = false;
        int encoded = actions.DiscreteActions[0];
        if (choices == null || !choices.TryGetValue(encoded, out AIAction selected))
        {
            arena.RejectAction("Policy produced a masked action.");
            return;
        }
        if (encoded != LearnedActionSchema.EndTurn && selectedSource < 0)
        {
            selectedSource = encoded;
            arena.RecordDecision();
            return;
        }
        selectedSource = -1;
        bool executed = context.RuntimeActions.TryGetValue(selected.Key, out LegalTurnAction action) &&
            arena.Execute(action);
        if (!executed) arena.RejectAction("Authoritative action revalidation failed.");
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // Explicit random baseline for environment verification; never advertised as learned AI.
        int choice = LearnedActionSchema.EndTurn;
        if (choices != null && choices.Count > 0)
        {
            int offset = arena.NextRandom(choices.Count);
            foreach (int candidate in choices.Keys) if (offset-- == 0) { choice = candidate; break; }
        }
        ActionSegment<int> discrete = actionsOut.DiscreteActions;
        discrete[0] = choice;
    }
}
