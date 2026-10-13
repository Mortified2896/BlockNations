using System.Collections;
using UnityEngine;

public partial class TurnManager
{
    private bool isApplyingAcceptedWebTurn;
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
            yield return WebAccountClient.Write(launch.match.id, "initialize", opening, launch.match.seatCount, value => initialized = value);
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
        WebAccountClient.MatchLoaded(currentGameId);
    }

    private IEnumerator WaitForPlayByPostPoll(float seconds)
    {
        if (!PublicWebBuild.UsesGoogleAccounts) {
            yield return new WaitForSecondsRealtime(seconds);
            yield break;
        }
        float deadline = Time.realtimeSinceStartup + seconds;
        while (isPlayByPostWaitingForExport && Time.realtimeSinceStartup < deadline) {
            if (WebAccountClient.ConsumeMatchUpdate(currentGameId)) yield break;
            yield return null;
        }
    }

    private bool ApplyAcceptedWebTurn()
    {
        var reply = WebAccountClient.AcceptedMatch;
        if (reply?.match?.id != currentGameId || string.IsNullOrWhiteSpace(reply.json)) return false;
        // The submit coroutine starts polling after it saves the accepted echo.
        // Starting it during this load would launch a fetch that the submit
        // coroutine immediately cancels, leaving the fetch-busy flag set.
        isApplyingAcceptedWebTurn = true;
        try { return LoadFromJsonString(reply.json); }
        finally { isApplyingAcceptedWebTurn = false; }
    }
}
