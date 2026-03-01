using UnityEngine;

public class DebugTurnTelemetrySink : MonoBehaviour, ITurnTelemetrySink
{
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
        if (!PbpDebugSettingsLoader.EnableTransportLogs)
            return;

        Debug.Log(
            $"Telemetry TransportOp op={op} transport={transport} ok={ok} err={(err ?? "<null>")} " +
            $"durationMs={durationMs:F1} payloadChars={payloadChars} seqA={(seqA.HasValue ? seqA.Value.ToString() : "<null>")} " +
            $"seqB={(seqB.HasValue ? seqB.Value.ToString() : "<null>")} mode={mode} gameIdHash={gameIdHash}");
    }

    public void OnEndTurnPressed(string mode, int turnNumber, string gameIdHash)
    {
        if (!PbpDebugSettingsLoader.EnableTransportLogs)
            return;

        Debug.Log($"Telemetry EndTurnPressed mode={mode} turnNumber={turnNumber} gameIdHash={gameIdHash}");
    }
}
