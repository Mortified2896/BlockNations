using System;
using System.Collections.Generic;

namespace BlockNations.AI
{
    // Version the tensor contract independently of saves and transport formats.
    // Type names never enter the network: roster slots carry capabilities and costs.
    public static class LearnedActionSchema
    {
        public const int Version = 2;
        public const string BehaviorName = "BlockNationsSeatV2";
        public const int BoardSize = 11;
        public const int Positions = BoardSize * BoardSize;
        public const int RecruitCapacity = 16;
        public const int TileChannels = 24;
        public const int RecruitChannels = 13;
        public const int GlobalChannels = 8;
        public const int ObservationSize = Positions * TileChannels + RecruitCapacity * RecruitChannels + GlobalChannels;
        public const int RecruitOffset = Positions * 2;
        public const int EndTurn = RecruitOffset + RecruitCapacity;
        public const int ActionCount = EndTurn + 1;

        public static void Validate(AIObservation observation)
        {
            if (observation == null || !SupportsBoard(observation.Width) || observation.Height != observation.Width ||
                observation.Seat < 0 || observation.Seat > 1 || observation.Tiles?.Length != observation.Width * observation.Height ||
                observation.Seen?.Length != observation.Width * observation.Height || observation.Visible?.Length != observation.Width * observation.Height ||
                observation.Units == null || observation.Cities == null || observation.RecruitTypes == null ||
                observation.LegalActions == null)
                throw new ArgumentException("Learned schema v2 requires a complete two-seat square board of size 5, 6, 7, 9 or 11.");
            if (observation.RecruitTypes.Length > RecruitCapacity)
                throw new ArgumentException("Recruit roster exceeds learned schema v2; train a new schema instead of dropping actions.");
        }

        public static bool SupportsBoard(int size) => size == 5 || size == 6 || size == 7 || size == 9 || size == 11;

        // Center native board coordinates on a fixed canvas. Padding is unavailable,
        // and action dictionaries retain native actions for authoritative execution.
        public static int CanvasPosition(AIObservation observation, int position)
        {
            if (position < 0 || position >= observation.Width * observation.Height)
                throw new ArgumentOutOfRangeException(nameof(position));
            int padding = BoardSize - observation.Width;
            // On an even board, put the spare padding cell on the opposite side
            // for seat 1, so rotating its canvas produces the same perspective.
            int inset = padding / 2 + (observation.Seat == 1 ? padding % 2 : 0);
            return (position / observation.Width + inset) * BoardSize + position % observation.Width + inset;
        }

        public static int CanonicalPosition(int position, int seat) => seat == 0 ? position : Positions - 1 - position;

        private static int SourcePosition(AIObservation observation, AIAction action) =>
            action.Kind == AIActionKind.Recruit ? CanvasPosition(observation, observation.Cities[action.Actor].Position(observation.Width)) :
            CanvasPosition(observation, observation.Units[action.Actor].Position(observation.Width));

        // selectedSource is in the canonical perspective. -1 is source selection.
        public static Dictionary<int, AIAction> Choices(AIObservation observation, int selectedSource)
        {
            Validate(observation);
            var choices = new Dictionary<int, AIAction>();
            foreach (AIAction action in observation.LegalActions)
            {
                if (action.Kind == AIActionKind.EndTurn) { choices[EndTurn] = action; continue; }
                int source = CanonicalPosition(SourcePosition(observation, action), observation.Seat);
                if (selectedSource < 0) { choices[source] = action; continue; }
                if (source != selectedSource) continue;
                int encoded = action.Kind == AIActionKind.Recruit ? RecruitOffset + action.RecruitType :
                    CanonicalPosition(CanvasPosition(observation, action.Destination), observation.Seat) + (action.Kind == AIActionKind.Attack ? Positions : 0);
                if (choices.TryGetValue(encoded, out AIAction previous) && !previous.Equals(action))
                    throw new InvalidOperationException("Two legal actions alias the same learned action; schema must change.");
                choices[encoded] = action;
            }
            if (!choices.ContainsKey(EndTurn))
                throw new InvalidOperationException("An owning seat observation must include EndTurn.");
            return choices;
        }

        public static float[] Encode(AIObservation observation, int selectedSource)
        {
            Validate(observation);
            float[] values = new float[ObservationSize];
            for (int position = 0; position < observation.Tiles.Length; position++)
            {
                int index = CanonicalPosition(CanvasPosition(observation, position), observation.Seat) * TileChannels;
                values[index] = observation.Tiles[position] ? 1 : 0;
                values[index + 1] = observation.Seen[position] ? 1 : 0;
                values[index + 2] = observation.Visible[position] ? 1 : 0;
            }
            foreach (AICityState city in observation.Cities)
            {
                int index = CanonicalPosition(CanvasPosition(observation, city.Position(observation.Width)), observation.Seat) * TileChannels;
                values[index + 3] = city.Seat == observation.Seat ? 1 : -1;
                values[index + 4] = city.CurrentlyVisible ? 1 : 0;
                // Hidden recruitment is transient private information, even if once remembered.
                values[index + 5] = city.CurrentlyVisible && city.Recruited ? 1 : 0;
            }
            foreach (AIUnitState unit in observation.Units)
            {
                int index = CanonicalPosition(CanvasPosition(observation, unit.Position(observation.Width)), observation.Seat) * TileChannels;
                values[index + 6] = unit.Seat == observation.Seat ? 1 : -1;
                values[index + 7] = unit.Health / 10f;
                values[index + 8] = unit.MaxHealth / 10f;
                values[index + 9] = unit.Attack / 10f;
                values[index + 10] = unit.Defense / 10f;
                values[index + 11] = unit.Range / (float)BoardSize;
                values[index + 12] = unit.Vision / (float)BoardSize;
                values[index + 13] = unit.MaxMoves / (float)BoardSize;
                values[index + 14] = AIActionRules.RemainingMoves(unit.CommittedMove, unit.MaxMoves, unit.MovesUsed, unit.AttackEndsMovement, unit.AttacksUsed) / (float)BoardSize;
                values[index + 15] = unit.MaxAttacks / 10f;
                values[index + 16] = Math.Max(0, unit.MaxAttacks - unit.AttacksUsed) / 10f;
                values[index + 17] = unit.AttackAfterMoving ? 1 : 0;
                values[index + 18] = unit.CommittedMove ? 1 : 0;
                values[index + 19] = unit.Cost / 10f;
            }
            if (selectedSource >= 0) values[selectedSource * TileChannels + 20] = 1;
            foreach (AIAction action in observation.LegalActions)
            {
                if (action.Kind == AIActionKind.EndTurn) continue;
                int source = CanonicalPosition(SourcePosition(observation, action), observation.Seat);
                if (action.Kind == AIActionKind.Recruit) values[source * TileChannels + 23] = 1;
                else if (selectedSource < 0 || source == selectedSource)
                    values[CanonicalPosition(CanvasPosition(observation, action.Destination), observation.Seat) * TileChannels +
                        (action.Kind == AIActionKind.Move ? 21 : 22)] = 1;
            }
            for (int slot = 0; slot < observation.RecruitTypes.Length; slot++)
            {
                AIUnitState type = observation.RecruitTypes[slot];
                int index = Positions * TileChannels + slot * RecruitChannels;
                values[index] = 1;
                values[index + 1] = type.Health / 10f;
                values[index + 2] = type.MaxHealth / 10f;
                values[index + 3] = type.Attack / 10f;
                values[index + 4] = type.Defense / 10f;
                values[index + 5] = type.Range / (float)BoardSize;
                values[index + 6] = type.Vision / (float)BoardSize;
                values[index + 7] = type.MaxMoves / (float)BoardSize;
                values[index + 8] = type.MaxAttacks / 10f;
                values[index + 9] = type.AttackAfterMoving ? 1 : 0;
                values[index + 10] = type.CommittedMove ? 1 : 0;
                values[index + 11] = type.Cost / 10f;
                values[index + 12] = observation.Gold >= type.Cost ? 1 : 0;
            }
            int globals = ObservationSize - GlobalChannels;
            values[globals] = selectedSource < 0 ? 0 : 1;
            values[globals + 1] = observation.Gold / 20f;
            values[globals + 2] = observation.Round / 100f;
            values[globals + 3] = observation.CityVision / (float)BoardSize;
            values[globals + 4] = observation.IncomePerCity / 10f;
            values[globals + 5] = observation.LegalActions.Length / 1000f;
            values[globals + 6] = observation.RecruitTypes.Length / (float)RecruitCapacity;
            values[globals + 7] = Version;
            return values;
        }
    }
}
