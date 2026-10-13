using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class MainMenuController
{
    private bool webMatchOpening;

    private IEnumerator StartWebAccountMenu()
    {
        WebAccountClient.RegisterMenuReceiver(gameObject.name);
        bool initialized = false;
        yield return WebAccountClient.Initialize(ok => initialized = ok);
        if (!initialized) {
            WebAccountClient.ShowStatus("Please sign in with an approved Google account, then reopen the game.");
            yield break;
        }
        if (WebAccountClient.ConsumeLobbyReturn()) {
            WebAccountClient.OpenLobby();
            yield break;
        }
        string matchId = WebAccountClient.ConsumeLaunchMatchId();
        if (string.IsNullOrEmpty(matchId)) yield break;
        yield return OpenWebMatch(matchId);
    }

    // Registered by this existing scene component; no hierarchy search or
    // browser-supplied object/seat can select the receiver or match ownership.
    public void OpenWebMatchFromBrowser(string matchId)
    {
        if (!PublicWebBuild.UsesGoogleAccounts || webMatchOpening ||
            !System.Text.RegularExpressions.Regex.IsMatch(matchId ?? "", "^[a-f0-9]{32}$")) return;
        StartCoroutine(RefreshAndOpenWebMatch(matchId));
    }

    private IEnumerator RefreshAndOpenWebMatch(string matchId)
    {
        webMatchOpening = true;
        bool verified = false;
        yield return WebAccountClient.Initialize(ok => verified = ok);
        if (verified) yield return OpenWebMatch(matchId);
        else WebAccountClient.ShowStatus("Please sign in again with an approved Google account.");
        webMatchOpening = false;
    }

    private IEnumerator OpenWebMatch(string matchId)
    {
        WebAccountClient.ShowStatus("Opening your online match…");
        WebAccountClient.MatchReply reply = null;
        yield return WebAccountClient.Request("matches/" + matchId, null, value => reply = value);
        if (reply?.ok != true || reply.match == null || !WebAccountClient.SupportsMatchVersion(reply.match.version) || reply.match.seatIndex < 0) {
            WebAccountClient.ShowStatus("This match could not be opened. Return to the lobby to refresh your matches.");
            yield break;
        }
        if (reply.match.status == "waiting" || reply.match.players == null ||
            (reply.match.status != "finished" && reply.match.players.Length < reply.match.seatCount)) {
            WebAccountClient.OpenLobby();
            yield break;
        }
        // Prepare only a playable match. A waiting lobby must not leave a
        // pending online launch that could override a later single-player game.
        // Seats still come exclusively from authenticated server membership.
        LocalPlayerSeatStore.SetSeat(matchId, reply.match.seatIndex);
        WebAccountClient.PrepareMatch(reply);
        SaveLoadRequest.ClearPending();
        CameraController.ClearPendingRestoreState();
        AIVsAIBatchRunController.ClearAll();
        AIRecruitVariantSelection.TryConsume(out _);
        SnapshotHistorySelection.TryConsume(out _);
        AIVsAIDebugSelection.TryConsume(out _);
        PlayerPrefs.DeleteKey(PlayByPostForceNewKey);
        PlayerPrefs.DeleteKey(PlayByPostPendingNewGameIdKey);
        GameModeSelection.SetPendingMode(TurnManager.GameMode.PlayByPost);
        MapSizeSelection.SetPending(TurnManager.MapSizePreset.Standard7);
        PlayByPostSeatCountSelection.SetPending(reply.match.seatCount);
        SceneManager.LoadScene(gameplaySceneName);
    }
}
