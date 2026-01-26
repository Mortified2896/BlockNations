using System;
using System.Collections;

public interface ITurnTransport
{
    string TransportName { get; }
    bool IsAvailable { get; }
    void Initialize();
    IEnumerator SubmitTurn(string gameId, int turnNumber, string json, Action<bool, string> done);
    IEnumerator TryFetchNextTurn(string gameId, int afterTurnNumber, Action<bool, string, int, string> done);
}

