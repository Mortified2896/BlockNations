using System;
using BlockNations.Simulation;

// Lesson fixtures use the same rules as the three-unit public game. Progress
// depends on successful commands, never button labels or a simulated click.
public enum TutorialStep
{
    Welcome, WarriorMove, Recruit, WarriorAttack, EndTurn,
    RiderMove, RiderAttack, ArcherAttack, ArcherRetreat, Capture, Complete
}

public static class TutorialLesson
{
    public const int BoardSize = 7;
    public const int HumanSeat = 0;
    public const int OpponentSeat = 1;
    public const int PracticeUnitId = 101;
    public const int OpponentUnitId = 102;
    public const int LessonCount = 9;

    public static string Id(TutorialStep step) => step.ToString();

    public static string Title(TutorialStep step)
    {
        switch (step)
        {
            case TutorialStep.Welcome: return "Learn to play";
            case TutorialStep.WarriorMove: return "Move and explore";
            case TutorialStep.Recruit: return "Recruit from your city";
            case TutorialStep.WarriorAttack: return "Attack with a Warrior";
            case TutorialStep.EndTurn: return "Take turns";
            case TutorialStep.RiderMove: return "Ride farther";
            case TutorialStep.RiderAttack: return "Move, then attack";
            case TutorialStep.ArcherAttack: return "Shoot from a distance";
            case TutorialStep.ArcherRetreat: return "Shoot, then retreat";
            case TutorialStep.Capture: return "Capture to win";
            default: return "Ready for a match";
        }
    }

    public static string Instruction(TutorialStep step)
    {
        switch (step)
        {
            case TutorialStep.Welcome:
                return "You are blue. Capture the enemy city to win. Follow the signs, or watch with Show me.";
            case TutorialStep.WarriorMove:
                return "Select your Warrior, then move to the marked tile.";
            case TutorialStep.Recruit:
                return "Open your city and recruit a Warrior for 2 gold.";
            case TutorialStep.WarriorAttack:
                return "Select your Warrior, then attack the marked enemy.";
            case TutorialStep.EndTurn:
                return "Next ends your turn. Your next turn adds 1 gold.";
            case TutorialStep.RiderMove:
                return "Riders can move two tiles in one tap.";
            case TutorialStep.RiderAttack:
                return "Your Rider has moved. It can still attack.";
            case TutorialStep.ArcherAttack:
                return "Archers shoot two tiles away. Shoot before moving.";
            case TutorialStep.ArcherRetreat:
                return "Your Archer has fired. It can still move away.";
            case TutorialStep.Capture:
                return "Move onto the enemy city to win.";
            default:
                return "You are ready to play. Confused by anything? Use Feedback.";
        }
    }

    public static string OriginCue(TutorialStep step) => step == TutorialStep.RiderMove ||
        step == TutorialStep.RiderAttack || step == TutorialStep.Capture ? "1 · Tap Rider" :
        step == TutorialStep.ArcherAttack || step == TutorialStep.ArcherRetreat ? "1 · Tap Archer" : "1 · Tap Warrior";

    public static string TargetCue(TutorialStep step)
    {
        switch (step)
        {
            case TutorialStep.Recruit: return "2 · Recruit · 2 gold";
            case TutorialStep.WarriorAttack:
            case TutorialStep.RiderAttack: return "2 · Attack · −1 HP";
            case TutorialStep.ArcherAttack: return "2 · Shoot · range 2";
            case TutorialStep.RiderMove: return "2 · Move 2 tiles";
            case TutorialStep.ArcherRetreat: return "2 · Retreat";
            case TutorialStep.Capture: return "2 · Capture city";
            default: return "2 · Move 1 tile";
        }
    }

    public static string Result(TutorialStep step)
    {
        switch (step)
        {
            case TutorialStep.Recruit: return "+1 Warrior · −2 gold";
            case TutorialStep.WarriorAttack:
            case TutorialStep.RiderAttack: return "Hit! · −1 HP";
            case TutorialStep.ArcherAttack: return "Hit from 2 tiles away!";
            case TutorialStep.EndTurn: return "Your turn · +1 gold";
            case TutorialStep.ArcherRetreat: return "Shot, then moved";
            case TutorialStep.Capture: return "City captured · Victory!";
            default: return "Moved · fog revealed";
        }
    }

    public static MatchState CreateBoard(TutorialStep step)
    {
        var tiles = new bool[BoardSize * BoardSize];
        for (int i = 0; i < tiles.Length; i++) tiles[i] = true;
        var state = new MatchState(BoardSize, BoardSize, 2, MatchRuleProfile.CoreThree.PolicyCatalog,
            currentTurnSeat: 0, round: 1, cityVision: 1, incomePerCity: 1, tiles: tiles, ruleProfile: MatchRuleProfile.CoreThree);
        state.SetGold(0, 3);
        state.SetGold(1, 3);
        state.AddCity(new SimulationCity(1, 0, state.Position(1, 1)));
        state.AddCity(new SimulationCity(2, 1, state.Position(5, 5)));
        string type = step >= TutorialStep.ArcherAttack && step <= TutorialStep.ArcherRetreat
            ? UnitRegistry.ArcherTypeId : step >= TutorialStep.RiderMove
            ? UnitRegistry.RiderTypeId : UnitRegistry.WarriorTypeId;
        state.RuleProfile.TryGetDefinition(type, out UnitDefinition definition);
        int x = 1, y = 1, moves = 0, attacks = 0;
        if (step == TutorialStep.Recruit || step == TutorialStep.WarriorAttack || step == TutorialStep.EndTurn ||
            step == TutorialStep.ArcherAttack || step == TutorialStep.ArcherRetreat) x = y = 2;
        if (step == TutorialStep.EndTurn) moves = attacks = 1;
        if (step == TutorialStep.RiderAttack) { x = 3; y = 2; moves = 2; }
        if (step == TutorialStep.ArcherRetreat) attacks = 1;
        if (step == TutorialStep.Capture) { x = 4; y = 5; }
        state.AddUnit(new SimulationUnit(PracticeUnitId, 0, state.Position(x, y), definition,
            movesUsed: moves, attacksUsed: attacks));
        if (step == TutorialStep.WarriorAttack || step == TutorialStep.EndTurn ||
            step == TutorialStep.RiderMove || step == TutorialStep.RiderAttack ||
            step == TutorialStep.ArcherAttack || step == TutorialStep.ArcherRetreat)
        {
            int enemyX = step <= TutorialStep.EndTurn ? 3 : 4;
            int health = step == TutorialStep.EndTurn || step == TutorialStep.ArcherRetreat
                ? CombatValues.FromDisplay(2) : UnitRegistry.Warrior.MaxHealthUnits;
            state.AddUnit(new SimulationUnit(OpponentUnitId, 1, state.Position(enemyX, 2), UnitRegistry.Warrior,
                health: health));
        }
        return state;
    }

    public static int Target(TutorialStep step)
    {
        switch (step)
        {
            case TutorialStep.WarriorMove: return 2 + 2 * BoardSize;
            case TutorialStep.Recruit: return 1 + BoardSize;
            case TutorialStep.WarriorAttack: return 3 + 2 * BoardSize;
            case TutorialStep.RiderMove: return 3 + 2 * BoardSize;
            case TutorialStep.RiderAttack:
            case TutorialStep.ArcherAttack: return 4 + 2 * BoardSize;
            case TutorialStep.ArcherRetreat: return 1 + 2 * BoardSize;
            case TutorialStep.Capture: return 5 + 5 * BoardSize;
            default: return -1;
        }
    }

    public static bool Completes(TutorialStep step, MatchState state, MatchCommand command, MatchTransition transition)
    {
        if (!transition.Applied) return false;
        if (step == TutorialStep.EndTurn)
            return command.Kind == MatchActionKind.EndTurn && command.Seat == OpponentSeat &&
                state.CurrentTurnSeat == HumanSeat && state.Round > 1;
        if (command.Seat != HumanSeat) return false;
        if (step == TutorialStep.Recruit)
            return command.Kind == MatchActionKind.Recruit && command.RecruitType == UnitRegistry.WarriorTypeId &&
                transition.RecruitedUnitId != 0;
        if (step == TutorialStep.Capture)
            return transition.CapturedCityId != 0 && state.WinnerSeat == HumanSeat;
        bool attack = step == TutorialStep.WarriorAttack || step == TutorialStep.RiderAttack || step == TutorialStep.ArcherAttack;
        if (step == TutorialStep.Welcome || step == TutorialStep.Complete) return false;
        return command.Kind == (attack ? MatchActionKind.Attack : MatchActionKind.Move) && command.Destination == Target(step);
    }
}
