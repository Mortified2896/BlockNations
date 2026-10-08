using System;
using System.Globalization;

// Read-only subset of the supervisor's shared-root storage telemetry.
[Serializable]
public sealed class TrainingStorageStatus
{
    public string runId;
    public long usedBytes, budgetBytes;

    public bool IsValid(string expectedRunId) => runId == expectedRunId && usedBytes >= 0 && budgetBytes > 0;
    public float Fraction => (float)Math.Min(1, Math.Max(0, usedBytes / (double)budgetBytes));
    public string Summary => string.Format(CultureInfo.InvariantCulture,
        "Shared storage: {0:F3} / {1:0.###} GB ({2:F1}%)", usedBytes / 1_000_000_000d,
        budgetBytes / 1_000_000_000d, 100 * usedBytes / (double)budgetBytes);
}
