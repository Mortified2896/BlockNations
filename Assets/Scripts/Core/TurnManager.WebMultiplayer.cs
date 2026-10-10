using System.Collections;

public partial class TurnManager
{
    private IEnumerator StartWebAccountMatch(WebAccountClient.MatchReply launch)
    {
        SetGameMode(GameMode.PlayByPost);
        SetCurrentGameId(launch.match.id);
        PersistCurrentPbpGameIdIfNeeded();
        LocalPlayerSeatStore.SetSeat(launch.match.id, launch.match.seatIndex);
        playByPostAutoSyncEnabled = true;
        playByPostPollSeconds = 10f;
        ResolveTurnTransport();
        yield return WaitForGridReady();
        if (string.IsNullOrWhiteSpace(launch.json)) {
            // Either member can initialize the identical, untouched opening.
            // The durable relay accepts the first initialization atomically.
            InitializeNewGame();
            isPlayByPostWaitingForExport = true;
            RefreshEndTurnButtonInteractable(force: true);
            TryBuildSaveJsonForDisk(out string opening);
            WebAccountClient.MatchReply initialized = null;
            yield return WebAccountClient.Write(launch.match.id, "initialize", opening, 2, value => initialized = value);
            if (initialized?.ok != true) {
                WebAccountClient.ShowStatus("The opening could not be saved online. Return to the lobby and try again.");
                yield break;
            }
            launch = initialized;
        }
        if (!LoadFromJsonString(launch.json)) {
            WebAccountClient.ShowStatus("This match needs a compatible game version. Reload from the lobby to update.");
            yield break;
        }
        pbpCreatorFirstRemoteSubmitPending = false;
        WebAccountClient.ShowStatus(null);
        WebAccountClient.MatchLoaded();
    }

    private bool ApplyAcceptedWebTurn()
    {
        var reply = WebAccountClient.AcceptedMatch;
        if (reply?.match?.id != currentGameId || string.IsNullOrWhiteSpace(reply.json)) return false;
        return LoadFromJsonString(reply.json);
    }
}
