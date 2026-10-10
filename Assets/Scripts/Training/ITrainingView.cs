using UnityEngine;

// Shared presentation contract. A viewer cannot execute a simulated action.
public interface ITrainingView
{
    bool IsTraining { get; }
    bool IsHumanPlaytest { get; }
    bool IsRatingCheck { get; }
    bool CanContinue { get; }
    bool CanEndHumanTurn { get; }
    bool CanStartNewMatch { get; }
    bool CanReturnToTraining { get; }
    bool Paused { get; set; }
    int HumanSeat { get; }
    int Games { get; }
    int Captures { get; }
    int Interruptions { get; }
    int Round { get; }
    int RoundLimit { get; }
    int BoardSize { get; }
    int CurriculumDistance { get; }
    int StartingDistance { get; }
    long Actions { get; }
    long Decisions { get; }
    double Elapsed { get; }
    double TotalTrainingSeconds { get; }
    bool TrainingTimeEstimated { get; }
    bool FullOpening { get; }
    string LastAction { get; }
    string Failure { get; }
    string SimulationVersion { get; }
    string HumanPolicyVersion { get; }
    string TrainingRunDirectory { get; }
    Color SpectatorBackgroundColor { get; }
    TrainingReplayHistory Replay { get; }
    TrainingProgressHistory Progress { get; }
    TrainingEloHistory EloHistory { get; }
    TrainingStorageStatus StorageStatus { get; }
    TrainingPlaytestBridge Playtest { get; }
    int GoldForSeat(int seat);
    void EndHumanTurn();
    void NewHumanMatch();
    void ReturnToTraining();
}

public interface ITrainingSpectator : ITrainingView
{
    int WorkerCount { get; }
    int SelectedWorker { get; set; }
    bool ShowingLive { get; }
    bool ShowingAllLive { get; set; }
    System.Collections.Generic.IReadOnlyList<TrainingLiveBoard> LiveBoards { get; }
    void WatchLive();
    void WatchRecent();
    void StopAndSave();
}
