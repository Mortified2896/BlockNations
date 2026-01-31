using System;
using System.Collections;
using UnityEngine;

public class TelemetryTurnTransport : ITurnTransport
{
    private readonly ITurnTransport inner;
    private readonly ITurnTelemetrySink sink;
    private readonly Func<string> modeProvider;
    private readonly Func<string> gameIdHashProvider;

    public TelemetryTurnTransport(
        ITurnTransport inner,
        ITurnTelemetrySink sink,
        Func<string> modeProvider,
        Func<string> gameIdHashProvider)
    {
        this.inner = inner;
        this.sink = sink ?? NullTurnTelemetrySink.Instance;
        this.modeProvider = modeProvider;
        this.gameIdHashProvider = gameIdHashProvider;
    }

    public string TransportName => inner != null ? inner.TransportName : "Null";
    public bool IsAvailable => inner != null && inner.IsAvailable;

    public void Initialize()
    {
        inner?.Initialize();
    }

    public IEnumerator SubmitTurn(string gameId, int turnNumber, string json, Action<bool, string> done)
    {
        if (inner == null)
        {
            yield return null;
            done?.Invoke(false, TurnTelemetryConstants.NullTransport);
            yield break;
        }

        float start = Time.realtimeSinceStartup;
        bool ok = false;
        string err = null;

        yield return inner.SubmitTurn(gameId, turnNumber, json, (success, error) =>
        {
            ok = success;
            err = error;
            done?.Invoke(success, error);
        });

        float durationMs = (Time.realtimeSinceStartup - start) * 1000f;
        int payloadChars = json != null ? json.Length : 0;
        string errTelemetry = ok ? null : (string.IsNullOrEmpty(err) ? TurnTelemetryConstants.Unknown : err);

        TryEmitTransportOp(
            TurnTelemetryConstants.Submit,
            TransportName,
            ok,
            errTelemetry,
            durationMs,
            payloadChars,
            turnNumber,
            null);
    }

    public IEnumerator TryFetchNextTurn(string gameId, int afterTurnNumber, Action<bool, string, int, string> done)
    {
        if (inner == null)
        {
            yield return null;
            done?.Invoke(false, TurnTelemetryConstants.NullTransport, 0, null);
            yield break;
        }

        float start = Time.realtimeSinceStartup;
        bool ok = false;
        string err = null;
        int fetchedTurnNumber = 0;
        string fetchedJson = null;

        yield return inner.TryFetchNextTurn(gameId, afterTurnNumber, (success, error, turn, json) =>
        {
            ok = success;
            err = error;
            fetchedTurnNumber = turn;
            fetchedJson = json;
            done?.Invoke(success, error, turn, json);
        });

        float durationMs = (Time.realtimeSinceStartup - start) * 1000f;
        int payloadChars = fetchedJson != null ? fetchedJson.Length : 0;
        int? seqB = ok && fetchedTurnNumber > 0 ? fetchedTurnNumber : (int?)null;
        string errTelemetry = ok ? null : (string.IsNullOrEmpty(err) ? TurnTelemetryConstants.Unknown : err);

        TryEmitTransportOp(
            TurnTelemetryConstants.Fetch,
            TransportName,
            ok,
            errTelemetry,
            durationMs,
            payloadChars,
            afterTurnNumber,
            seqB);
    }

    private void TryEmitTransportOp(
        string op,
        string transport,
        bool ok,
        string err,
        float durationMs,
        int payloadChars,
        int? seqA,
        int? seqB)
    {
        if (sink == null)
            return;

        try
        {
            sink.OnTransportOp(
                op,
                transport,
                ok,
                err,
                durationMs,
                payloadChars,
                seqA,
                seqB,
                modeProvider != null ? modeProvider() : null,
                gameIdHashProvider != null ? gameIdHashProvider() : null);
        }
        catch
        {
        }
    }
}
