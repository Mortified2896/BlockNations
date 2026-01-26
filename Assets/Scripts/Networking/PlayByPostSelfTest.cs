#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

public class PlayByPostSelfTest : MonoBehaviour
{
    [ContextMenu("PBp Self Test - Two Turn Exchange")]
    private void TwoTurnExchange()
    {
        StartCoroutine(RunTwoTurnExchange());
    }

    private IEnumerator RunTwoTurnExchange()
    {
        TurnManager tm = TurnManager.Instance != null ? TurnManager.Instance : UnityEngine.Object.FindFirstObjectByType<TurnManager>();
        if (tm == null)
        {
            Debug.LogError("PBp Self Test: No TurnManager found in scene.");
            yield break;
        }

        if (tm.currentMode != TurnManager.GameMode.PlayByPost)
        {
            Debug.LogWarning("PBp Self Test: TurnManager is not in PlayByPost mode; aborting to avoid side effects.");
            yield break;
        }

        var impl = RunTwoTurnExchangeImpl(tm);
        while (true)
        {
            object yielded;
            try
            {
                if (!impl.MoveNext())
                {
                    yield break;
                }
                yielded = impl.Current;
            }
            catch (Exception ex)
            {
                Debug.LogError("PBp Self Test: Exception: " + ex);
                yield break;
            }

            yield return yielded;
        }
    }

    private IEnumerator RunTwoTurnExchangeImpl(TurnManager tm)
    {
        var transport = new InMemoryTurnTransport();
        transport.Initialize();

        if (!tm.TryBuildPlayByPostExportJson(out int turnA, out string jsonA))
        {
            Debug.LogError("PBp Self Test: Failed to build export JSON (A).");
            yield break;
        }

        string gameId = TryGetPrivateString(tm, "currentGameId");
        if (string.IsNullOrWhiteSpace(gameId))
        {
            gameId = "PBpSelfTest-" + Guid.NewGuid().ToString("N");
        }

        bool submitAOk = false;
        string submitAErr = null;
        yield return transport.SubmitTurn(gameId, turnA, jsonA, (ok, err) =>
        {
            submitAOk = ok;
            submitAErr = err;
        });

        Debug.Log($"PBp Self Test: submit A (turn={turnA}) => ok={submitAOk}, err={submitAErr}");

        bool fetchAOk = false;
        string fetchAErr = null;
        int fetchedATurn = 0;
        string fetchedAJson = null;
        yield return transport.TryFetchNextTurn(gameId, afterTurnNumber: 0, (ok, err, turn, json) =>
        {
            fetchAOk = ok;
            fetchAErr = err;
            fetchedATurn = turn;
            fetchedAJson = json;
        });

        Debug.Log($"PBp Self Test: fetch A => ok={fetchAOk}, err={fetchAErr}, turn={fetchedATurn}, chars={(fetchedAJson != null ? fetchedAJson.Length : 0)}");

        if (!fetchAOk)
        {
            Debug.LogError("PBp Self Test: Failed to fetch A.");
            yield break;
        }

        bool loadAOk = tm.LoadFromJsonString(fetchedAJson);
        Debug.Log($"PBp Self Test: load A => ok={loadAOk}");
        if (!loadAOk)
        {
            Debug.LogError("PBp Self Test: Failed to load A.");
            yield break;
        }

        if (!tm.TryBuildPlayByPostExportJson(out int turnB, out string jsonB))
        {
            Debug.LogError("PBp Self Test: Failed to build export JSON (B).");
            yield break;
        }

        bool submitBOk = false;
        string submitBErr = null;
        yield return transport.SubmitTurn(gameId, turnB, jsonB, (ok, err) =>
        {
            submitBOk = ok;
            submitBErr = err;
        });
        Debug.Log($"PBp Self Test: submit B (turn={turnB}) => ok={submitBOk}, err={submitBErr}");

        bool fetchBOk = false;
        string fetchBErr = null;
        int fetchedBTurn = 0;
        string fetchedBJson = null;
        yield return transport.TryFetchNextTurn(gameId, afterTurnNumber: turnA, (ok, err, turn, json) =>
        {
            fetchBOk = ok;
            fetchBErr = err;
            fetchedBTurn = turn;
            fetchedBJson = json;
        });
        Debug.Log($"PBp Self Test: fetch B => ok={fetchBOk}, err={fetchBErr}, turn={fetchedBTurn}, chars={(fetchedBJson != null ? fetchedBJson.Length : 0)}");

        if (!fetchBOk)
        {
            Debug.LogError("PBp Self Test: Failed to fetch B.");
            yield break;
        }

        bool loadBOk = tm.LoadFromJsonString(fetchedBJson);
        Debug.Log($"PBp Self Test: load B => ok={loadBOk}");
        if (!loadBOk)
        {
            Debug.LogError("PBp Self Test: Failed to load B.");
            yield break;
        }

        Debug.Log("PBp Self Test: Two turn exchange completed successfully.");
    }

    private static string TryGetPrivateString(object instance, string fieldName)
    {
        try
        {
            var f = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            return f != null ? f.GetValue(instance) as string : null;
        }
        catch
        {
            return null;
        }
    }
}
#endif
