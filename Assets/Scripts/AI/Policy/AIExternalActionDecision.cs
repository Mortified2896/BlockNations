using System;
using System.Collections.Generic;

namespace BlockNations.AI
{
    [Serializable]
    public sealed class AIExternalActionResponse
    {
        public bool HasAction;
        public int ActionId = -1;
        public string Summary, Model, ReasoningEffort, Error;
        public double Seconds;
        public int InputTokens, OutputTokens, CallsRemaining;
    }

    // Transport is owned by the Unity/local-service adapter, not the game policy.
    public interface IAIActionRequest
    {
        bool Complete { get; }
        AIExternalActionResponse Response { get; }
        void Poll();
        void Cancel();
    }

    public sealed class AICompletedActionRequest : IAIActionRequest
    {
        public bool Complete => true;
        public AIExternalActionResponse Response { get; }
        public AICompletedActionRequest(AIExternalActionResponse response) => Response = response;
        public void Poll() { }
        public void Cancel() { }
    }

    // One request selects ONE ID from this exact observation's authoritative mask.
    // The runtime rebuilds the observation and mask before the next request.
    public sealed class AIExternalActionDecision : IAIDecision
    {
        private readonly AIObservation observation;
        private readonly IAIActionRequest request;
        private readonly List<AICandidatePlan> candidates = new List<AICandidatePlan>();
        public AIExternalActionResponse Response { get; private set; }
        public string RequestedModel { get; }
        public string RequestedReasoningEffort { get; }
        public int WorkCompleted => Complete ? 1 : 0;
        public int WorkBudget => 1;
        public int ReplyWorkCompleted => 0;
        public string EvaluatorVersion => "external-action-choice-v1";
        public bool IsRootOnly => true;
        public bool WaitingForExternalResult => !Complete;
        public int Depth => 0;
        public bool Complete { get; private set; }
        public string StopReason { get; private set; }
        public AICandidatePlan Best => candidates.Count == 0 ? null : candidates[0];
        public IReadOnlyList<AICandidatePlan> Candidates => candidates;

        public AIExternalActionDecision(AIObservation observation, IAIActionRequest request, string model = null, string effort = null)
        {
            this.observation = observation ?? throw new ArgumentNullException(nameof(observation));
            this.request = request ?? throw new ArgumentNullException(nameof(request));
            RequestedModel = model;
            RequestedReasoningEffort = effort;
        }

        public void AdvanceOnce()
        {
            if (Complete) return;
            request.Poll();
            if (!request.Complete) return;
            Response = request.Response;
            if (Response == null || !string.IsNullOrEmpty(Response.Error))
            { FinishWithoutAction(Response?.Error ?? "External decision returned no response"); return; }
            if (!Response.HasAction || Response.ActionId < 0 || Response.ActionId >= observation.LegalActions.Length)
            { FinishWithoutAction("External decision returned an invalid legal-action ID"); return; }
            AIAction selected = observation.LegalActions[Response.ActionId];
            // No search/evaluation is blended into a model choice. Zero is not a score.
            candidates.Add(new AICandidatePlan(new[] { selected }, default));
            StopReason = "External model selected a legal action";
            Complete = true;
        }

        public void Cancel()
        {
            if (Complete) return;
            request.Cancel();
            FinishWithoutAction("External decision cancelled");
        }

        private void FinishWithoutAction(string reason)
        {
            Complete = true;
            StopReason = reason;
        }
    }
}
