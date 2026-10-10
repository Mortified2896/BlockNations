using System;
using System.Collections;

public sealed class WebAccountTurnTransport : ITurnTransport
{
    public string TransportName => "CloudflareGoogle";
    public bool IsAvailable => PublicWebBuild.UsesGoogleAccounts && !string.IsNullOrEmpty(WebAccountClient.PlayerId);
    public void Initialize() { }

    public IEnumerator SubmitTurn(string gameId, int turnNumber, string json, Action<bool, string> done)
    {
        var accepted = WebAccountClient.AcceptedMatch;
        if (accepted?.match?.id != gameId) { done(false, TurnTelemetryConstants.Unavailable); yield break; }
        WebAccountClient.MatchReply reply = null;
        yield return WebAccountClient.Write(gameId, "turn", json, accepted.seq, value => reply = value);
        if (reply.ok) WebAccountClient.ShowStatus(null);
        else if (reply.error == "APPROVED_ACCOUNT_REQUIRED")
            WebAccountClient.ShowStatus("Your Google session needs to be renewed. Return to the lobby and sign in again.");
        done(reply.ok, reply.error);
    }
    public IEnumerator TryFetchNextTurn(string gameId, int afterTurnNumber, Action<bool, string, int, string> done)
    {
        WebAccountClient.MatchReply reply = null;
        yield return WebAccountClient.Request("matches/" + gameId + "/next?afterSeq=" + afterTurnNumber, null, value => reply = value);
        if (!reply.ok && reply.error == "APPROVED_ACCOUNT_REQUIRED")
            WebAccountClient.ShowStatus("Your Google session needs to be renewed. Return to the lobby and sign in again.");
        done(reply.ok, reply.error, reply.seq, reply.json);
    }
}
