// Immutable presentation copy for one worker. Its frame never drives an action.
public sealed class TrainingLiveBoard
{
    public int Worker { get; }
    public int RoundLimit { get; }
    public TrainingReplayHistory.Game Game { get; }
    public TrainingReplayHistory.Frame Opening => Game?.frames[0];
    public TrainingReplayHistory.Frame Current => Game?.frames[Game.frames.Count - 1];
    public TrainingLiveBoard(int worker, int roundLimit, TrainingReplayHistory.Game game)
    { Worker = worker; RoundLimit = roundLimit; Game = game; }
}
