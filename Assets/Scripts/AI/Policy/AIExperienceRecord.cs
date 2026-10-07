using System;

namespace BlockNations.AI
{
    public enum AIExecutionResult { Inspection, Executed, Rejected, EndTurn }

    [Serializable]
    public sealed class AIPredictedReplyRecord
    {
        public int Seat;
        public AIAction[] Actions;
        public AIEvaluation Evaluation;
    }

    [Serializable]
    public sealed class AICandidateRecord
    {
        public AIAction[] Actions;
        public AIPositionFeatures PredictedFeatures;
        public AIEvaluation Evaluation;
        public AIPredictedReplyRecord PredictedReply;
    }

    // A captured decision sample, NOT an RL episode/reward or a gameplay save.
    // Action/unit indices belong to Observation; NextObservation has its own indices.
    [Serializable]
    public sealed class AIExperienceRecord
    {
        public const int CurrentSchemaVersion = 1;
        public int RecordSchemaVersion, ObservationSchemaVersion, FeatureSchemaVersion;
        public string RulesVersion, PolicyVersion, EvaluatorVersion, StopReason;
        public int Turn, WorkBudget, WorkCompleted, ReplyWorkCompleted, SearchDepth;
        public AIObservation Observation, NextObservation;
        public AICandidateRecord[] Candidates;
        public bool HasSelectedAction, TerminalAfterAction, RootOnly, Deterministic;
        public AIAction SelectedAction;
        public AIExecutionResult ExecutionResult;

        public static AIExperienceRecord Create(AIObservation observation, IAIDecision search,
            string policyVersion, int turn, AIExecutionResult execution, AIObservation nextObservation = null, bool terminal = false)
        {
            if (observation == null || search == null) throw new ArgumentNullException();
            AIExperienceRecord record = new AIExperienceRecord {
                RecordSchemaVersion = CurrentSchemaVersion, ObservationSchemaVersion = AIObservation.SchemaVersion,
                FeatureSchemaVersion = AIPositionFeatures.SchemaVersion, RulesVersion = AIActionRules.RulesVersion,
                PolicyVersion = policyVersion, EvaluatorVersion = search.EvaluatorVersion, Turn = turn,
                WorkBudget = search.WorkBudget, WorkCompleted = search.WorkCompleted, ReplyWorkCompleted = search.ReplyWorkCompleted,
                SearchDepth = search.Depth, StopReason = search.StopReason, RootOnly = search.IsRootOnly, Observation = observation,
                Deterministic = true,
                NextObservation = nextObservation, ExecutionResult = execution, TerminalAfterAction = terminal,
                Candidates = new AICandidateRecord[search.Candidates.Count] };
            AICandidatePlan best = search.Best;
            record.HasSelectedAction = best != null && best.Actions.Length > 0;
            if (record.HasSelectedAction) record.SelectedAction = best.Actions[0];
            for (int i = 0; i < record.Candidates.Length; i++)
            {
                AICandidatePlan plan = search.Candidates[i];
                record.Candidates[i] = new AICandidateRecord { Actions = plan.Actions, Evaluation = plan.Evaluation,
                    PredictedFeatures = plan.Features,
                    PredictedReply = plan.Reply == null ? null : new AIPredictedReplyRecord {
                        Seat = plan.Reply.Seat, Actions = plan.Reply.Actions, Evaluation = plan.Reply.Evaluation } };
            }
            return record;
        }

        public IAIDecision BeginReplay(IAIActionPolicy policy)
        {
            if (!Deterministic)
                throw new InvalidOperationException("Only deterministic decision records support replay.");
            if (RecordSchemaVersion != CurrentSchemaVersion || ObservationSchemaVersion != AIObservation.SchemaVersion ||
                FeatureSchemaVersion != AIPositionFeatures.SchemaVersion || RulesVersion != AIActionRules.RulesVersion)
                throw new InvalidOperationException("Decision record schema/rules are incompatible with this replay.");
            if (policy == null || policy.Version != PolicyVersion)
                throw new InvalidOperationException("Replay requires the recorded policy version.");
            IAIDecision replay = policy.BeginDecision(Observation, RootOnly ? 0 : WorkBudget);
            if (replay.EvaluatorVersion != EvaluatorVersion)
                throw new InvalidOperationException("Replay requires the recorded evaluator version.");
            return replay;
        }
    }
}
