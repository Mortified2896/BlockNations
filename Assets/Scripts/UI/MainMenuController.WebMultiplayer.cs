using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class MainMenuController
{
    private IEnumerator StartWebAccountMenu()
    {
        bool initialized = false;
        yield return WebAccountClient.Initialize(ok => initialized = ok);
        if (!initialized) {
            WebAccountClient.ShowStatus("Please sign in with an approved Google account, then reopen the game.");
            yield break;
        }
        string matchId = WebAccountClient.LaunchMatchId;
        if (string.IsNullOrEmpty(matchId)) yield break;
        WebAccountClient.ShowStatus("Opening your online match…");
        WebAccountClient.MatchReply reply = null;
        yield return WebAccountClient.Request("matches/" + matchId, null, value => reply = value);
        if (reply?.ok != true || reply.match?.version != WebAccountClient.MatchVersion || reply.match.seatIndex < 0) {
            WebAccountClient.ShowStatus("This match could not be opened. Return to the lobby to refresh your matches.");
            yield break;
        }
        // The seat comes from authenticated server membership. A URL, local
        // profile or old app seat claim cannot assign it.
        LocalPlayerSeatStore.SetSeat(matchId, reply.match.seatIndex);
        WebAccountClient.PrepareMatch(reply);
        SaveLoadRequest.ClearPending();
        PlayerPrefs.DeleteKey(PlayByPostForceNewKey);
        PlayerPrefs.DeleteKey(PlayByPostPendingNewGameIdKey);
        GameModeSelection.SetPendingMode(TurnManager.GameMode.PlayByPost);
        MapSizeSelection.SetPending(TurnManager.MapSizePreset.Standard7);
        PlayByPostSeatCountSelection.SetPending(2);
        SceneManager.LoadScene(gameplaySceneName);
    }
}
