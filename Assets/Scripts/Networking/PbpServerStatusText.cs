using System;

public static class PbpServerStatusText
{
    public static HttpTurnTransport.ServerStatusProbeClassification ClassifyTransportResult(bool ok, string resultOrError)
    {
        if (ok || string.Equals(resultOrError, "OK", StringComparison.OrdinalIgnoreCase))
        {
            return HttpTurnTransport.ServerStatusProbeClassification.Connected;
        }

        if (string.Equals(resultOrError, TurnTelemetryConstants.NoTurn, StringComparison.Ordinal) ||
            string.Equals(resultOrError, "GAME_FULL", StringComparison.Ordinal))
        {
            return HttpTurnTransport.ServerStatusProbeClassification.Connected;
        }

        if (string.Equals(resultOrError, "UNAUTHORIZED", StringComparison.Ordinal))
        {
            return HttpTurnTransport.ServerStatusProbeClassification.AuthenticationFailed;
        }

        if (IsConnectivityFailure(resultOrError))
        {
            return HttpTurnTransport.ServerStatusProbeClassification.Unreachable;
        }

        return HttpTurnTransport.ServerStatusProbeClassification.BadResponse;
    }

    public static HttpTurnTransport.ServerStatusProbeClassification ClassifyFetchResult(bool reachable, string resultOrError)
    {
        if (string.Equals(resultOrError, "OK", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(resultOrError, TurnTelemetryConstants.NoTurn, StringComparison.Ordinal))
        {
            return HttpTurnTransport.ServerStatusProbeClassification.Connected;
        }

        if (string.Equals(resultOrError, "UNAUTHORIZED", StringComparison.Ordinal))
        {
            return HttpTurnTransport.ServerStatusProbeClassification.AuthenticationFailed;
        }

        if (IsConnectivityFailure(resultOrError))
        {
            return HttpTurnTransport.ServerStatusProbeClassification.Unreachable;
        }

        return reachable
            ? HttpTurnTransport.ServerStatusProbeClassification.BadResponse
            : HttpTurnTransport.ServerStatusProbeClassification.Unreachable;
    }

    public static bool IsHealthy(HttpTurnTransport.ServerStatusProbeClassification classification)
    {
        return classification == HttpTurnTransport.ServerStatusProbeClassification.Connected;
    }

    public static string GetStatusText(HttpTurnTransport.ServerStatusProbeClassification classification)
    {
        return classification switch
        {
            HttpTurnTransport.ServerStatusProbeClassification.Connected => "Connected",
            HttpTurnTransport.ServerStatusProbeClassification.AuthenticationFailed => "Authentication failed",
            HttpTurnTransport.ServerStatusProbeClassification.BadResponse => "Bad server response",
            _ => "Server unreachable"
        };
    }

    public static string BuildStatusWithContext(HttpTurnTransport.ServerStatusProbeClassification classification, string context)
    {
        string status = GetStatusText(classification);
        return string.IsNullOrWhiteSpace(context)
            ? status
            : $"{status}. {context.Trim()}";
    }

    public static bool IsConnectivityFailure(string err)
    {
        if (string.IsNullOrEmpty(err))
        {
            return true;
        }

        return string.Equals(err, TurnTelemetryConstants.IoError, StringComparison.Ordinal) ||
               string.Equals(err, TurnTelemetryConstants.Unavailable, StringComparison.Ordinal) ||
               string.Equals(err, TurnTelemetryConstants.NullTransport, StringComparison.Ordinal) ||
               string.Equals(err, TurnTelemetryConstants.Unknown, StringComparison.Ordinal);
    }
}
