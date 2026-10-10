using System;

// Build compatibility is separate from the supported protocol migration window.
// A v1.0.4 client can import the preceding build's snapshots and writes 1.0.4 on
// its next export. A v1.0.3 client rejects that newer snapshot through its gate.
public static class PbpAppVersionPolicy
{
    public const string BalanceRelease = "1.0.4";
    public const string PreviousRelease = "1.0.3";

    public static bool Supports(string current, string incoming)
    {
        if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(incoming)) return false;
        current = current.Trim(); incoming = incoming.Trim();
        return string.Equals(current, incoming, StringComparison.Ordinal) ||
            (current == BalanceRelease && incoming == PreviousRelease);
    }
}
