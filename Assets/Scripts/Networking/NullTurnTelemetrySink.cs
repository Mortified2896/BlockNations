public sealed class NullTurnTelemetrySink : ITurnTelemetrySink
{
    public static readonly NullTurnTelemetrySink Instance = new NullTurnTelemetrySink();

    private NullTurnTelemetrySink()
    {
    }

    public void OnTransportOp(
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
    )
    {
    }

    public void OnEndTurnPressed(string mode, int turnNumber, string gameIdHash)
    {
    }
}
