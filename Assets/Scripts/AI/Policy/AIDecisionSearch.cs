using System;
using System.Collections.Generic;

namespace BlockNations.AI
{
    public sealed class HardTacticianPolicy : IAIActionPolicy
    {
        public const int TurnWorkBudget = 8192;
        public const int DecisionWorkBudget = 2048;
        public const int BeamWidth = 24;
        public const int DepthLimit = 8;
        public const int SuccessorsPerNode = 12;
        public const string PolicyVersion = "hard-tactician-v2";
        public string Version => PolicyVersion;
        public IAIDecision BeginDecision(AIObservation observation, int workBudget) =>
            workBudget > 0 ? new AIDecisionSearch(observation, workBudget) :
            new AIDecisionSearch(observation, Math.Max(1, observation.LegalActions.Length), 1);
    }

    public sealed class AICandidatePlan
    {
        public readonly AIAction[] Actions;
        public readonly AIEvaluation Evaluation;
        public AICandidatePlan(AIAction[] actions, AIEvaluation evaluation)
        {
            Actions = actions;
            Evaluation = evaluation;
        }
        public int Score => Evaluation.Total - Actions.Length;
    }

    // No wall-clock calls, device detection, random traversal, or mutable runtime objects.
    // The scheduler can call AdvanceOnce in any chunk sizes without changing the result.
    public sealed class AIDecisionSearch : IAIDecision
    {
        private sealed class Node
        {
            public AITacticalState State;
            public AICandidatePlan Plan;
        }

        private readonly AIObservation observation;
        private readonly int workBudget;
        private readonly int depthLimit;
        private readonly HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
        private List<Node> frontier = new List<Node>();
        private readonly List<Node> next = new List<Node>();
        private readonly List<AICandidatePlan> candidates = new List<AICandidatePlan>();
        private List<AIAction> actions;
        private int nodeIndex, actionIndex;
        public int WorkCompleted { get; private set; }
        public int WorkBudget => workBudget;
        public int Depth { get; private set; }
        public bool Complete { get; private set; }
        public string StopReason { get; private set; }
        public IReadOnlyList<AICandidatePlan> Candidates => candidates;
        public AICandidatePlan Best => candidates.Count > 0 ? candidates[0] : null;

        public AIDecisionSearch(AIObservation observation, int workBudget, int depthLimit = HardTacticianPolicy.DepthLimit)
        {
            this.observation = observation ?? throw new ArgumentNullException(nameof(observation));
            this.workBudget = Math.Max(1, workBudget);
            this.depthLimit = Math.Max(1, depthLimit);
            AITacticalState state = new AITacticalState(observation);
            frontier.Add(new Node { State = state, Plan = new AICandidatePlan(Array.Empty<AIAction>(), state.Evaluate()) });
            visited.Add(state.StateKey());
        }

        public void AdvanceOnce()
        {
            if (Complete) return;
            while (nodeIndex < frontier.Count)
            {
                Node parent = frontier[nodeIndex];
                if (actions == null)
                {
                    bool isRoot = parent.Plan.Actions.Length == 0;
                    actions = isRoot
                        ? new List<AIAction>(observation.LegalActions)
                        : parent.State.Actions();
                    actions = AIActionOrdering.Order(parent.State, actions, selective: !isRoot);
                    actionIndex = 0;
                }
                if (actionIndex >= actions.Count) { nodeIndex++; actions = null; continue; }
                AIAction action = actions[actionIndex++];
                WorkCompleted++;
                AITacticalState state = parent.State.After(action);
                AIAction[] path = new AIAction[parent.Plan.Actions.Length + 1];
                Array.Copy(parent.Plan.Actions, path, parent.Plan.Actions.Length);
                path[path.Length - 1] = action;
                AICandidatePlan plan = new AICandidatePlan(path, state.Evaluate());
                AddCandidate(plan);
                if (state.CapturedCity) { Finish("Observed winning capture"); return; }
                if (action.Kind != AIActionKind.EndTurn && visited.Add(state.StateKey()))
                {
                    next.Add(new Node { State = state, Plan = plan });
                    next.Sort((a, b) => Compare(a.Plan, b.Plan));
                    if (next.Count > HardTacticianPolicy.BeamWidth) next.RemoveAt(next.Count - 1);
                }
                if (WorkCompleted >= workBudget) Finish("Fixed work budget completed");
                return;
            }
            Depth++;
            if (next.Count == 0 || Depth >= depthLimit)
            {
                Finish(next.Count == 0 ? "Search frontier exhausted" : "Depth limit completed");
                return;
            }
            frontier = new List<Node>(next);
            next.Clear();
            nodeIndex = 0;
            actions = null;
        }

        private void AddCandidate(AICandidatePlan plan)
        {
            // Retain one best continuation per first action; inspection stays bounded.
            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i].Actions[0].Equals(plan.Actions[0]))
                {
                    if (Compare(plan, candidates[i]) >= 0) return;
                    candidates.RemoveAt(i);
                    break;
                }
            candidates.Add(plan);
            candidates.Sort(Compare);
            if (candidates.Count > 8) candidates.RemoveAt(candidates.Count - 1);
        }

        private static int Compare(AICandidatePlan a, AICandidatePlan b)
        {
            int score = b.Score.CompareTo(a.Score);
            if (score != 0) return score;
            int length = a.Actions.Length.CompareTo(b.Actions.Length);
            if (length != 0) return length;
            for (int i = 0; i < a.Actions.Length; i++)
            {
                int order = string.CompareOrdinal(a.Actions[i].Key, b.Actions[i].Key);
                if (order != 0) return order;
            }
            return 0;
        }

        private void Finish(string reason) { Complete = true; StopReason = reason; }
    }
}
