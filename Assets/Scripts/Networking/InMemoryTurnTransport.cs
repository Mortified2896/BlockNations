using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InMemoryTurnTransport : ITurnTransport
{
    private static readonly Dictionary<string, SortedDictionary<int, string>> TurnsByGameId =
        new Dictionary<string, SortedDictionary<int, string>>();

    private static readonly object Sync = new object();

    public string TransportName => "InMemory";
    public bool IsAvailable => true;

    public void Initialize()
    {
    }

    public IEnumerator SubmitTurn(string gameId, int turnNumber, string json, Action<bool, string> done)
    {
        yield return new WaitForSecondsRealtime(0.1f);

        if (string.IsNullOrWhiteSpace(gameId))
        {
            done?.Invoke(false, "INVALID_GAME_ID");
            yield break;
        }

        if (turnNumber <= 0)
        {
            done?.Invoke(false, "INVALID_TURN");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            done?.Invoke(false, "EMPTY_JSON");
            yield break;
        }

        lock (Sync)
        {
            if (!TurnsByGameId.TryGetValue(gameId, out var turns))
            {
                turns = new SortedDictionary<int, string>();
                TurnsByGameId[gameId] = turns;
            }

            turns[turnNumber] = json;
        }

        done?.Invoke(true, null);
    }

    public IEnumerator TryFetchNextTurn(string gameId, int afterTurnNumber, Action<bool, string, int, string> done)
    {
        yield return new WaitForSecondsRealtime(0.1f);

        if (string.IsNullOrWhiteSpace(gameId))
        {
            done?.Invoke(false, "INVALID_GAME_ID", 0, null);
            yield break;
        }

        int foundTurn = 0;
        string foundJson = null;

        lock (Sync)
        {
            if (TurnsByGameId.TryGetValue(gameId, out var turns))
            {
                foreach (var kvp in turns)
                {
                    if (kvp.Key > afterTurnNumber)
                    {
                        foundTurn = kvp.Key;
                        foundJson = kvp.Value;
                        break;
                    }
                }
            }
        }

        if (foundTurn <= 0 || string.IsNullOrWhiteSpace(foundJson))
        {
            done?.Invoke(false, "NO_TURN", 0, null);
            yield break;
        }

        done?.Invoke(true, null, foundTurn, foundJson);
    }
}

