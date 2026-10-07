using System;
using BlockNations.AI;
using NUnit.Framework;
using UnityEngine;

public sealed class AIExternalActionDecisionTests
{
    private sealed class PendingRequest : IAIActionRequest
    {
        public bool Complete { get; set; }
        public bool Cancelled;
        public AIExternalActionResponse Response { get; set; }
        public void Poll() { }
        public void Cancel() { Cancelled = true; }
    }

    private static AIObservation Observation() => new AIObservation {
        Width = 11, Height = 11, Seat = 1,
        LegalActions = new[] {
            new AIAction { Kind = AIActionKind.Move, Actor = 2, Destination = 51, MoveCost = 1 },
            new AIAction { Kind = AIActionKind.Attack, Actor = 2, Target = 0, Destination = 61 },
            new AIAction { Kind = AIActionKind.EndTurn, Actor = -1, Target = -1 } } };

    [Test]
    public void SelectsExactRootActionWithoutSearchingOrMutatingTheObservation()
    {
        AIObservation observation = Observation();
        var decision = new AIExternalActionDecision(observation, new AICompletedActionRequest(
            new AIExternalActionResponse { HasAction = true, ActionId = 1, Summary = "Capture the guard." }));
        decision.AdvanceOnce();
        Assert.That(decision.Complete, Is.True);
        Assert.That(decision.Best.Actions, Is.EqualTo(new[] { observation.LegalActions[1] }));
        Assert.That(decision.Depth, Is.Zero);
        Assert.That(decision.ReplyWorkCompleted, Is.Zero);
        Assert.That(observation.LegalActions.Length, Is.EqualTo(3));
    }

    [TestCase(-1)]
    [TestCase(3)]
    [TestCase(1000)]
    public void InvalidActionIdCannotProduceAnExecutableCandidate(int id)
    {
        var decision = new AIExternalActionDecision(Observation(), new AICompletedActionRequest(
            new AIExternalActionResponse { HasAction = true, ActionId = id }));
        decision.AdvanceOnce();
        Assert.That(decision.Complete, Is.True);
        Assert.That(decision.Best, Is.Null);
    }

    [Test]
    public void MissingChoiceOrTransportErrorCannotSilentlyBecomeActionZero()
    {
        foreach (AIExternalActionResponse response in new[] {
            JsonUtility.FromJson<AIExternalActionResponse>("{}"),
            new AIExternalActionResponse { HasAction = true, ActionId = 0, Error = "Request failed" }, null })
        {
            var decision = new AIExternalActionDecision(Observation(), new AICompletedActionRequest(response));
            decision.AdvanceOnce();
            Assert.That(decision.Complete, Is.True);
            Assert.That(decision.Best, Is.Null);
        }
    }

    [Test]
    public void CancelledPendingRequestCannotExecuteALateResponse()
    {
        var request = new PendingRequest();
        var decision = new AIExternalActionDecision(Observation(), request);
        decision.AdvanceOnce();
        Assert.That(decision.WaitingForExternalResult, Is.True);
        decision.Cancel();
        request.Complete = true;
        request.Response = new AIExternalActionResponse { HasAction = true, ActionId = 0 };
        decision.AdvanceOnce();
        Assert.That(request.Cancelled, Is.True);
        Assert.That(decision.Best, Is.Null);
        Assert.That(decision.StopReason, Does.Contain("cancelled"));
    }

    [Test]
    public void ExternalRecordsPreserveTheChoiceWithoutClaimingDeterministicReinference()
    {
        var decision = new AIExternalActionDecision(Observation(), new AICompletedActionRequest(
            new AIExternalActionResponse { HasAction = true, ActionId = 1, Model = "gpt-6-luna", ReasoningEffort = "max" }));
        decision.AdvanceOnce();
        AIExperienceRecord record = AIExperienceRecord.Create(Observation(), decision, "luna6-max-single-action-v1", 1, AIExecutionResult.Inspection);
        Assert.That(record.Deterministic, Is.False);
        Assert.That(record.SelectedAction, Is.EqualTo(Observation().LegalActions[1]));
        Assert.That(record.ExternalResponse.Model, Is.EqualTo("gpt-6-luna"));
        Assert.Throws<InvalidOperationException>(() => record.BeginReplay(new HardTacticianPolicy()));
    }
}
