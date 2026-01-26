using System;
using System.Collections;

public class NullTurnTransport : ITurnTransport
{
    public string TransportName => "Null";
    public bool IsAvailable => false;

    public void Initialize()
    {
    }

    public IEnumerator SubmitTurn(string gameId, int turnNumber, string json, Action<bool, string> done)
    {
        yield return null;
        done?.Invoke(false, "UNAVAILABLE");
    }

    public IEnumerator TryFetchNextTurn(string gameId, int afterTurnNumber, Action<bool, string, int, string> done)
    {
        yield return null;
        done?.Invoke(false, "UNAVAILABLE", 0, null);
    }
}

