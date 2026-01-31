public interface ITurnTelemetrySink
{
    void OnTransportOp(
        string op,
        string transport,
        bool ok,
        string err,
        float durationMs,
        int payloadChars,
        int? seqA,
        int? seqB,
        string mode,
        string gameIdHash
    );

    void OnEndTurnPressed(
        string mode,
        int turnNumber,
        string gameIdHash
    );
}
