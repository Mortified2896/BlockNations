using System;

namespace BlockNations.Simulation
{
    public enum MatchActionKind { Move, Attack, Recruit, EndTurn }

    public readonly struct MatchCommand : IEquatable<MatchCommand>
    {
        public readonly MatchActionKind Kind;
        public readonly int Seat, ActorId, Destination, TargetId;
        public readonly string RecruitType;
        public MatchCommand(MatchActionKind kind, int seat, int actorId = 0, int destination = -1, int targetId = 0, string recruitType = null)
        { Kind = kind; Seat = seat; ActorId = actorId; Destination = destination; TargetId = targetId; RecruitType = recruitType; }
        public bool Equals(MatchCommand other) => Kind == other.Kind && Seat == other.Seat && ActorId == other.ActorId &&
            Destination == other.Destination && TargetId == other.TargetId && RecruitType == other.RecruitType;
        public override bool Equals(object other) => other is MatchCommand command && Equals(command);
        public override int GetHashCode() => (((int)Kind * 397 ^ Seat) * 397 ^ ActorId) * 397 ^ Destination ^ TargetId;
    }

    public sealed class MatchLegalAction
    {
        public MatchCommand Command { get; }
        public int[] Path { get; }
        public MatchLegalAction(MatchCommand command, int[] path = null) { Command = command; Path = path ?? Array.Empty<int>(); }
    }

    public readonly struct MatchTransition
    {
        public readonly bool Applied, HiddenBlocker;
        public readonly int MovedSteps, KilledUnitId, RecruitedUnitId, CapturedCityId;
        public MatchTransition(bool applied, int movedSteps = 0, bool hiddenBlocker = false, int killedUnitId = 0, int recruitedUnitId = 0, int capturedCityId = 0)
        { Applied = applied; MovedSteps = movedSteps; HiddenBlocker = hiddenBlocker; KilledUnitId = killedUnitId;
          RecruitedUnitId = recruitedUnitId; CapturedCityId = capturedCityId; }
    }
}
