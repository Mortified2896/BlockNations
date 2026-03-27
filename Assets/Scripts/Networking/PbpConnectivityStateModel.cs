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

    public PbpConnectivitySnapshot(PbpConnectivityState state, bool? lastKnownServerReachable)
    {
        State = state;
        LastKnownServerReachable = lastKnownServerReachable;
    }
}

public static class PbpConnectivityStateModel
{
    private static bool? lastKnownServerReachable;

    public static bool? LastKnownServerReachable => lastKnownServerReachable;

    public static void ObserveSubmitResult(bool ok, string err)
    {
        if (ok)
        {
            lastKnownServerReachable = true;
            return;
        }

        if (IsConnectivityFailure(err))
        {
            lastKnownServerReachable = false;
        }
    }

    public static void ObserveFetchResult(bool reachable, string resultOrError)
    {
        if (reachable)
        {
            lastKnownServerReachable = true;
            return;
        }

        if (IsConnectivityFailure(resultOrError))
        {
            lastKnownServerReachable = false;
        }
    }

    public static void ObserveServerProbeResult(bool reachable)
    {
        lastKnownServerReachable = reachable;
    }

    public static PbpConnectivitySnapshot Resolve(NetworkReachability internetReachability)
    {
        if (internetReachability == NetworkReachability.NotReachable)
        {
            return new PbpConnectivitySnapshot(PbpConnectivityState.Offline, lastKnownServerReachable);
        }

        if (lastKnownServerReachable == false)
        {
            return new PbpConnectivitySnapshot(PbpConnectivityState.ServerUnreachable, lastKnownServerReachable);
        }

        return new PbpConnectivitySnapshot(PbpConnectivityState.Normal, lastKnownServerReachable);
    }

    public static bool IsConnectivityFailure(string err)
    {
        if (string.IsNullOrEmpty(err))
        {
            return true;
        }

        return string.Equals(err, TurnTelemetryConstants.IoError, System.StringComparison.Ordinal) ||
               string.Equals(err, TurnTelemetryConstants.Unavailable, System.StringComparison.Ordinal) ||
               string.Equals(err, TurnTelemetryConstants.NullTransport, System.StringComparison.Ordinal) ||
               string.Equals(err, TurnTelemetryConstants.Unknown, System.StringComparison.Ordinal);
    }
}
