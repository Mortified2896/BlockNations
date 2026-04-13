using UnityEngine;

public enum PbpConnectivityState
{
    Normal,
    Offline,
    ServerUnreachable
}

public readonly struct PbpConnectivitySnapshot
{
    public readonly PbpConnectivityState State;
    public readonly bool? LastKnownServerReachable;
    public readonly HttpTurnTransport.ServerStatusProbeClassification? LastKnownServerClassification;

    public PbpConnectivitySnapshot(
        PbpConnectivityState state,
        bool? lastKnownServerReachable,
        HttpTurnTransport.ServerStatusProbeClassification? lastKnownServerClassification)
    {
        State = state;
        LastKnownServerReachable = lastKnownServerReachable;
        LastKnownServerClassification = lastKnownServerClassification;
    }
}

public static class PbpConnectivityStateModel
{
    private static bool? lastKnownServerReachable;
    private static HttpTurnTransport.ServerStatusProbeClassification? lastKnownServerClassification;

    public static bool? LastKnownServerReachable => lastKnownServerReachable;
    public static HttpTurnTransport.ServerStatusProbeClassification? LastKnownServerClassification => lastKnownServerClassification;

    public static void ObserveSubmitResult(bool ok, string err)
    {
        ObserveServerProbeResult(PbpServerStatusText.ClassifyTransportResult(ok, err));
    }

    public static void ObserveFetchResult(bool reachable, string resultOrError)
    {
        ObserveServerProbeResult(PbpServerStatusText.ClassifyFetchResult(reachable, resultOrError));
    }

    public static void ObserveServerProbeResult(bool reachable)
    {
        ObserveServerProbeResult(
            reachable
                ? HttpTurnTransport.ServerStatusProbeClassification.Connected
                : HttpTurnTransport.ServerStatusProbeClassification.Unreachable);
    }

    public static void ObserveServerProbeResult(HttpTurnTransport.ServerStatusProbeClassification classification)
    {
        lastKnownServerClassification = classification;
        lastKnownServerReachable =
            classification != HttpTurnTransport.ServerStatusProbeClassification.Unreachable;
    }

    public static PbpConnectivitySnapshot Resolve(NetworkReachability internetReachability)
    {
        if (internetReachability == NetworkReachability.NotReachable)
        {
            return new PbpConnectivitySnapshot(
                PbpConnectivityState.Offline,
                lastKnownServerReachable,
                lastKnownServerClassification);
        }

        if (lastKnownServerClassification == HttpTurnTransport.ServerStatusProbeClassification.Unreachable)
        {
            return new PbpConnectivitySnapshot(
                PbpConnectivityState.ServerUnreachable,
                lastKnownServerReachable,
                lastKnownServerClassification);
        }

        return new PbpConnectivitySnapshot(
            PbpConnectivityState.Normal,
            lastKnownServerReachable,
            lastKnownServerClassification);
    }

    public static bool IsConnectivityFailure(string err)
    {
        return PbpServerStatusText.IsConnectivityFailure(err);
    }
}
