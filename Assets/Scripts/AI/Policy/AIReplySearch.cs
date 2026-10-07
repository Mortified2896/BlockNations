using System;
using System.Collections.Generic;

namespace BlockNations.AI
{
    public sealed class AIReplyPlan
    {
        public readonly int Seat;
        public readonly AIAction[] Actions;
        public readonly AIEvaluation Evaluation;
        public AIReplyPlan(int seat, AIAction[] actions, AIEvaluation evaluation)
        { Seat = seat; Actions = actions; Evaluation = evaluation; }
    }

    // A bounded worst-response check over the SAME transitions as own-turn lookahead.
    // Different hostile seats are alternatives, never an invented cooperative team.
    // Only units in the fair root observation participate; unknown recruits are not invented.
    public sealed class AIReplySearch
    {
        public const int BeamWidth = 2, DepthLimit = 4, SuccessorsPerNode = 4;
        private sealed class Node
        {
            public int Seat;
            public AITacticalState State;
            public AIReplyPlan Plan;
        }
        private readonly IAIPositionEvaluator evaluator;
        private readonly int workBudget;
        private readonly HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
        private List<Node> frontier = new List<Node>();
        private readonly List<Node> next = new List<Node>();
        private List<AIAction> actions;
        private int nodeIndex, actionIndex, depth;
        public int WorkCompleted { get; private set; }
        public bool Complete { get; private set; }
        public AIReplyPlan Worst { get; private set; }

        public AIReplySearch(AITacticalState state, IAIPositionEvaluator evaluator, int workBudget)
        {
            this.evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
            this.workBudget = Math.Max(1, workBudget);
            AIEvaluation baseline = evaluator.Evaluate(AIPositionFeatureExtractor.Extract(state, estimateThreats: false));
            Worst = new AIReplyPlan(-1, Array.Empty<AIAction>(), baseline);
            List<int> seats = new List<int>();
            foreach (AIUnitState unit in state.Units)
                if (unit.Health > 0 && state.Observation.IsHostileSeat(unit.Seat) && !seats.Contains(unit.Seat)) seats.Add(unit.Seat);
            seats.Sort();
            foreach (int seat in seats)
            {
                AITacticalState reset = state.BeginObservedTurn(seat);
                frontier.Add(new Node { Seat = seat, State = reset,
                    Plan = new AIReplyPlan(seat, Array.Empty<AIAction>(), baseline) });
                visited.Add(seat + ":" + reset.StateKey());
            }
            Complete = frontier.Count == 0 || state.CapturedCity || state.LostCity;
        }

        public void AdvanceOnce()
        {
            if (Complete) return;
            while (nodeIndex < frontier.Count)
            {
                Node parent = frontier[nodeIndex];
                if (actions == null)
                {
                    actions = AIActionOrdering.Order(parent.State, parent.State.ActionsForSeat(parent.Seat),
                        selective: true, successorLimit: SuccessorsPerNode);
                    actionIndex = 0;
                }
                if (actionIndex >= actions.Count) { nodeIndex++; actions = null; continue; }
                AIAction action = actions[actionIndex++];
                AITacticalState state = parent.State.After(action);
                AIAction[] path = new AIAction[parent.Plan.Actions.Length + 1];
                Array.Copy(parent.Plan.Actions, path, parent.Plan.Actions.Length);
                path[path.Length - 1] = action;
                AIReplyPlan plan = new AIReplyPlan(parent.Seat, path,
                    evaluator.Evaluate(AIPositionFeatureExtractor.Extract(state, estimateThreats: false)));
                WorkCompleted++;
                if (Compare(plan, Worst) < 0) Worst = plan;
                if (state.LostCity) { Complete = true; return; }
                if (action.Kind != AIActionKind.EndTurn && visited.Add(parent.Seat + ":" + state.StateKey()))
                {
                    next.Add(new Node { Seat = parent.Seat, State = state, Plan = plan });
                    next.Sort((a, b) => Compare(a.Plan, b.Plan));
                    if (next.Count > BeamWidth) next.RemoveAt(next.Count - 1);
                }
                if (WorkCompleted >= workBudget) Complete = true;
                return;
            }
            depth++;
            if (next.Count == 0 || depth >= DepthLimit) { Complete = true; return; }
            frontier = new List<Node>(next);
            next.Clear();
            nodeIndex = 0;
            actions = null;
        }

        private static int Compare(AIReplyPlan a, AIReplyPlan b)
        {
            int comparison = a.Evaluation.Total.CompareTo(b.Evaluation.Total);
            if (comparison != 0) return comparison;
            comparison = a.Actions.Length.CompareTo(b.Actions.Length);
            if (comparison != 0) return comparison;
            comparison = a.Seat.CompareTo(b.Seat);
            if (comparison != 0) return comparison;
            for (int i = 0; i < a.Actions.Length; i++)
            {
                comparison = string.CompareOrdinal(a.Actions[i].Key, b.Actions[i].Key);
                if (comparison != 0) return comparison;
            }
            return 0;
        }
    }
}
