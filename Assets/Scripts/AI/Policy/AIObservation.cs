using System;

namespace BlockNations.AI
{
    // Decision data has no Unity objects, private transport data, or viewer-side booleans.
    // IDs are indices within this observation, not persistent unit identities.
    public enum AIActionKind { Move, Attack, Recruit, EndTurn }

    [Serializable]
    public struct AIUnitState
    {
        public int Seat, X, Y, Health, MaxHealth, Attack, Defense, Range, Vision;
        public int MaxMoves, MovesUsed, MaxAttacks, AttacksUsed, Cost;
        public string Type;
        public bool AttackAfterMoving, CommittedMove, AttackEndsMovement;
        public int Position(int width) => Y * width + X;
    }

    [Serializable]
    public struct AICityState
    {
        public int Seat, X, Y;
        public bool Recruited;
        // False means last observed ownership, not a current hidden-state read.
        public bool CurrentlyVisible;
        public int Position(int width) => Y * width + X;
    }

    [Serializable]
    public struct AIAction : IEquatable<AIAction>
    {
        public AIActionKind Kind;
        public int Actor, Target, Destination, MoveCost, RecruitType;
        public bool Equals(AIAction other) => Kind == other.Kind && Actor == other.Actor &&
            Target == other.Target && Destination == other.Destination && RecruitType == other.RecruitType;
        public override bool Equals(object other) => other is AIAction action && Equals(action);
        public override int GetHashCode() => (((int)Kind * 397 ^ Actor) * 397 ^ Target) * 397 ^ Destination ^ RecruitType;
        public string Key => $"{(int)Kind:D1}:{Actor:D4}:{Destination:D4}:{Target:D4}:{RecruitType:D3}";
    }

    [Serializable]
    public sealed class AIObservation
    {
        public const int SchemaVersion = 2;
        public int Width, Height, Seat, Gold, CityVision, Round, IncomePerCity;
        public bool[] Tiles, Seen, Visible;
        public AIUnitState[] Units, RecruitTypes;
        // Optional public roster availability. Null preserves existing observations.
        // Catalog indices remain stable even when a type cannot be purchased.
        public bool[] RecruitEnabled;
        public bool CanRecruitType(int slot) => RecruitEnabled == null || RecruitEnabled[slot];
        public AICityState[] Cities;
        public int[] HostileSeats;
        public bool IsHostileSeat(int seat) => HostileSeats != null && Array.IndexOf(HostileSeats, seat) >= 0;
        // Root action mask comes from the authoritative runtime legal-action service.
        public AIAction[] LegalActions;
    }

    public interface IAIActionPolicy
    {
        string Version { get; }
        IAIDecision BeginDecision(AIObservation observation, int workBudget);
    }

    public interface IAIDecision
    {
        int WorkCompleted { get; }
        int WorkBudget { get; }
        int ReplyWorkCompleted { get; }
        string EvaluatorVersion { get; }
        bool IsRootOnly { get; }
        int Depth { get; }
        bool Complete { get; }
        string StopReason { get; }
        AICandidatePlan Best { get; }
        System.Collections.Generic.IReadOnlyList<AICandidatePlan> Candidates { get; }
        void AdvanceOnce();
        void Cancel();
    }

    // Shared primitive rules: runtime wrappers and prediction both call these functions.
    public static class AIActionRules
    {
        public const string RulesVersion = BlockNations.Simulation.SimulationRules.Version;
        public static int RemainingMoves(bool committedMove, int maximum, int used, bool attackEndsMovement = false, int attacksUsed = 0) =>
            BlockNations.Simulation.SimulationRules.RemainingMoves(committedMove, maximum, used, attackEndsMovement, attacksUsed);
        public static bool CanAttack(bool afterMoving, int maximum, int used, int movesUsed) =>
            BlockNations.Simulation.SimulationRules.CanAttack(afterMoving, maximum, used, movesUsed);
        public static int Damage(int attack, int defense) => BlockNations.Simulation.SimulationRules.Damage(attack, defense);
        public static int Distance(int x1, int y1, int x2, int y2) => BlockNations.Simulation.SimulationRules.Distance(x1, y1, x2, y2);
    }
}
