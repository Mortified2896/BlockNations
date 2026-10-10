using System;
using System.Collections.Generic;
using System.Linq;
using BlockNations.AI;
using BlockNations.Simulation;

namespace BlockNations.Training
{
    [Serializable] public sealed class ArenaMatchEvent
    {
        public int version = 1, boardSize, worker, match, firstSeat, winner = -1, blueDecisions, redDecisions;
        public bool interrupted, fullOpening, policyChanged;
        public string kind, session;
        public long sequence;
        public PolicyAssignment context;
        public string[] currentIds;
    }
    public sealed class ArenaAgentStep
    {
        public int agent, seat;
        public float[] observation;
        public int[] available;
        public bool terminal, interrupted;
        public float reward;
    }

    // No Unity loop, object, rendering, physics or Python implementation of game rules.
    public sealed class SelfPlayArena
    {
        public readonly int Worker, BoardSize, Seed;
        private readonly Random random;
        private readonly bool curriculum;
        private readonly int openingEconomyVersion;
        private readonly int fixedFirstSeat;
        private readonly UnitDefinition[] roster;
        private readonly SimulationObservationSource observer = new SimulationObservationSource();
        private readonly int[] source = { -1, -1 }, seatDecisions = new int[2];
        private readonly Queue<bool> recent = new Queue<bool>();
        private int recentCaptures, matchDecisions, turnActions;
        private bool terminalReported, openingRecorded;
        private PolicyAssignment assignment, latestAssignment;
        public MatchState State { get; private set; }
        public TrainingTrace Trace { get; private set; }
        public TrainingTrace CompletedTrace { get; private set; }
        public List<ArenaMatchEvent> Events { get; } = new List<ArenaMatchEvent>();
        public long Decisions { get; private set; }
        public long Actions { get; private set; }
        public int Games { get; private set; }
        public int Captures { get; private set; }
        public int Interruptions { get; private set; }
        public int Resets { get; private set; }
        public int CurriculumDistance { get; private set; }
        public bool FullOpening { get; private set; }
        public bool CanDecide => !terminalReported;
        public int RoundLimit => FullOpening ? 100 : 30;
        public string LastAction { get; private set; }

        public SelfPlayArena(int worker, int size, int seed, bool curriculum, int distance, IEnumerable<UnitDefinition> roster,
            int openingEconomyVersion = MatchOpening.CurrentEconomyVersion, int fixedFirstSeat = -1)
        {
            if (worker < 0 || !LearnedActionSchema.SupportsBoard(size) || (curriculum && size != 11)) throw new ArgumentException("Invalid training arena configuration.");
            Worker = worker; BoardSize = size; Seed = seed; random = new Random(seed);
            if (!MatchOpening.SupportsEconomy(openingEconomyVersion)) throw new ArgumentOutOfRangeException(nameof(openingEconomyVersion));
            this.openingEconomyVersion = openingEconomyVersion;
            if (fixedFirstSeat < -1 || fixedFirstSeat > 1) throw new ArgumentOutOfRangeException(nameof(fixedFirstSeat));
            this.fixedFirstSeat = fixedFirstSeat;
            this.curriculum = curriculum; CurriculumDistance = distance;
            this.roster = roster.OrderBy(t => t.TypeId, StringComparer.Ordinal).ToArray();
            if (this.roster.Length == 0 || this.roster.Length > LearnedActionSchema.RecruitCapacity) throw new ArgumentException("Unsupported roster.");
            Reset();
        }

        public void Reset(bool trainerReset = false)
        {
            if (trainerReset) Resets++;
            source[0] = source[1] = -1; seatDecisions[0] = seatDecisions[1] = 0;
            matchDecisions = turnActions = 0; terminalReported = openingRecorded = false; assignment = latestAssignment = null;
            observer.ResetKnowledge();
            FullOpening = !curriculum || random.Next(5) == 0;
            int first = random.Next(2), low = 1, high = BoardSize - 2;
            if (fixedFirstSeat >= 0) first = fixedFirstSeat;
            bool mirror = false;
            if (!FullOpening)
            {
                int distance = random.Next(4) == 0 ? Math.Max(2, CurriculumDistance - 2) : CurriculumDistance;
                low = (BoardSize - 1 - distance) / 2; high = low + distance; mirror = random.Next(2) == 0;
            }
            State = new MatchState(BoardSize, BoardSize, 2, roster, first, firstSeat: first);
            for (int seat = 0; seat < 2; seat++)
            {
                int coordinate = (seat == 0) != mirror ? low : high, position = State.Position(coordinate, coordinate);
                State.AddCity(new SimulationCity(seat + 1, seat, position));
                State.SetGold(seat, FullOpening ? MatchOpening.GoldBeforeIncome(seat, first, openingEconomyVersion) : 4);
                if (!FullOpening) State.AddUnit(new SimulationUnit(seat + 1, seat, position, roster[random.Next(roster.Length)]));
            }
            MatchEngine.BeginTurn(State, first);
            observer.SetPublicStartingCities(State.Cities.Select(c => new AICityState { Seat = c.Seat, X = c.Position % BoardSize, Y = c.Position / BoardSize }).ToArray());
            LastAction = (first == 0 ? "Blue" : "Red") + " moves first";
            Trace = new TrainingTrace { worker = Worker, match = Games + 1, boardSize = BoardSize, firstSeat = first, rulesVersion = SimulationRules.Version };
            Trace.Record(TrainingTraceState.Capture(State, LastAction));
        }

        public ArenaAgentStep Decision()
        {
            int seat = State.CurrentTurnSeat;
            AIObservation observation = observer.Observe(State, seat).Observation;
            return new ArenaAgentStep { agent = Worker * 2 + seat, seat = seat,
                observation = LearnedActionSchema.Encode(observation, source[seat]), available = LearnedActionSchema.Choices(observation, source[seat]).Keys.ToArray() };
        }

        // Evaluation-only reference, using exactly the seat's fair observation.
        // Optional roster restrictions test strategies; they never constrain the learner.
        public int ReferenceChoice(string recruitType, int workBudget)
        {
            if (terminalReported || workBudget < 1 || workBudget > 2048)
                throw new InvalidOperationException("A reference decision requires an active match and bounded work.");
            int seat = State.CurrentTurnSeat;
            AIObservation observation = observer.Observe(State, seat).Observation;
            var choices = LearnedActionSchema.Choices(observation, source[seat]);
            if (source[seat] >= 0) observation.LegalActions = choices.Values.ToArray();
            if (!string.IsNullOrEmpty(recruitType))
            {
                if (!roster.Any(t => t.TypeId == recruitType)) throw new ArgumentException("Unknown reference recruit type.");
                observation.LegalActions = observation.LegalActions.Where(a => a.Kind != AIActionKind.Recruit ||
                    observation.RecruitTypes[a.RecruitType].Type == recruitType).ToArray();
            }
            var decision = new HardTacticianPolicy().BeginDecision(observation, workBudget);
            while (!decision.Complete) decision.AdvanceOnce();
            AIAction selected = decision.Best.Actions[0];
            if (selected.Kind == AIActionKind.EndTurn) return LearnedActionSchema.EndTurn;
            if (source[seat] >= 0) return choices.Single(pair => pair.Value.Equals(selected)).Key;
            int position = selected.Kind == AIActionKind.Recruit ? observation.Cities[selected.Actor].Position(BoardSize) :
                observation.Units[selected.Actor].Position(BoardSize);
            return LearnedActionSchema.CanonicalPosition(LearnedActionSchema.CanvasPosition(observation, position), seat);
        }

        public ArenaAgentStep[] Advance(int? encoded, PolicyAssignment policy)
        {
            Events.Clear(); CompletedTrace = null;
            if (terminalReported)
            {
                if (encoded.HasValue) throw new InvalidOperationException("Terminal agents must not receive actions.");
                Reset(); return new[] { Decision() };
            }
            if (!encoded.HasValue) throw new InvalidOperationException("Missing action for an owning agent.");
            if (!openingRecorded)
            {
                assignment = policy; Trace.policy = policy; openingRecorded = true;
                Events.Add(Event("begin", policy));
            }
            latestAssignment = policy;
            if (assignment == null || !assignment.SameAs(policy)) Trace.policyChanged = true;
            int seat = State.CurrentTurnSeat;
            var context = observer.Observe(State, seat);
            var choices = LearnedActionSchema.Choices(context.Observation, source[seat]);
            if (!choices.TryGetValue(encoded.Value, out AIAction selected)) throw new InvalidOperationException("Policy produced a masked action.");
            Decisions++; matchDecisions++; seatDecisions[seat]++;
            if (encoded != LearnedActionSchema.EndTurn && source[seat] < 0) source[seat] = encoded.Value;
            else
            {
                source[seat] = -1;
                if (!context.Commands.TryGetValue(selected.Key, out MatchCommand command)) throw new InvalidOperationException("Missing authoritative command.");
                LastAction = Describe(command);
                if (!MatchEngine.Apply(State, command).Applied) throw new InvalidOperationException("Authoritative action rejected.");
                Actions++; turnActions = command.Kind == MatchActionKind.EndTurn ? 0 : turnActions + 1;
                if (!State.GameOver) Trace.Record(TrainingTraceState.Capture(State, LastAction));
            }
            bool interrupted = !State.GameOver && (State.Round > RoundLimit || matchDecisions >= 20000 || turnActions > 4096);
            if (!State.GameOver && !interrupted) return new[] { Decision() };
            terminalReported = true;
            var terminal = new ArenaAgentStep[2];
            for (int owner = 0; owner < 2; owner++)
                terminal[owner] = new ArenaAgentStep { agent = Worker * 2 + owner, seat = owner, terminal = true, interrupted = interrupted,
                    reward = interrupted ? 0 : State.WinnerSeat == owner ? 1 : -1,
                    observation = LearnedActionSchema.Encode(observer.Observe(State, owner).Observation, source[owner]) };
            LastAction = interrupted ? "Match interrupted at turn/action limit" : (State.WinnerSeat == 0 ? "Blue" : "Red") + " captures city and wins";
            var end = Event("end", assignment); end.interrupted = interrupted; end.winner = State.WinnerSeat;
            end.policyChanged = Trace.policyChanged;
            end.currentIds = Trace.policyChanged || policy == null ? null : new[] { policy.learner, policy.opponent };
            Events.Add(end);
            Trace.Record(TrainingTraceState.Capture(State, LastAction, terminal: true)); CompletedTrace = Trace;
            Games++; if (interrupted) Interruptions++; else Captures++;
            recent.Enqueue(!interrupted); if (!interrupted) recentCaptures++;
            if (recent.Count > 100 && recent.Dequeue()) recentCaptures--;
            if (curriculum && recent.Count == 100 && recentCaptures >= 65 && CurriculumDistance < 8)
            { CurriculumDistance += 2; recent.Clear(); recentCaptures = 0; }
            return terminal;
        }

        private ArenaMatchEvent Event(string kind, PolicyAssignment policy) => new ArenaMatchEvent {
            boardSize = BoardSize, worker = Worker, match = Games + 1, firstSeat = State.FirstSeat, kind = kind,
            blueDecisions = seatDecisions[0], redDecisions = seatDecisions[1], context = policy, fullOpening = FullOpening };
        private string Describe(MatchCommand command)
        {
            string side = command.Seat == 0 ? "Blue" : "Red";
            if (command.Kind == MatchActionKind.EndTurn) return side + " ends turn";
            if (command.Kind == MatchActionKind.Recruit) return side + " recruits " + command.RecruitType;
            return side + " " + (State.GetUnit(command.ActorId)?.Definition.TypeId ?? "unit") +
                (command.Kind == MatchActionKind.Attack ? " attacks " : " moves toward ") + $"({command.Destination % BoardSize}, {command.Destination / BoardSize})";
        }
    }
}
