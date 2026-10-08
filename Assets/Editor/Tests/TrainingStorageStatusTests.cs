using NUnit.Framework;
using UnityEngine;

public sealed class TrainingStorageStatusTests
{
    [Test]
    public void SupervisorBytesAreDisplayedAsTheSharedDecimalGigabyteBudget()
    {
        var status = JsonUtility.FromJson<TrainingStorageStatus>(
            "{\"runId\":\"run\",\"usedBytes\":253256951,\"budgetBytes\":20000000000,\"state\":\"running\"}");
        Assert.That(status.IsValid("run"), Is.True);
        Assert.That(status.Summary, Is.EqualTo("Shared storage: 0.253 / 20 GB (1.3%)"));
        Assert.That(status.Fraction, Is.EqualTo(.01266284755f).Within(.000001f));
    }

    [Test]
    public void ForeignOrIncompleteTelemetryIsNotAccepted()
    {
        var status = new TrainingStorageStatus { runId = "other", usedBytes = 1, budgetBytes = 20 };
        Assert.That(status.IsValid("run"), Is.False);
        status.runId = "run"; status.usedBytes = -1;
        Assert.That(status.IsValid("run"), Is.False);
        status.usedBytes = 0; status.budgetBytes = 0;
        Assert.That(status.IsValid("run"), Is.False);
    }

    [Test]
    public void UsageBarStaysBoundedWithoutHidingTheMeasuredOverage()
    {
        var status = new TrainingStorageStatus { runId = "run", usedBytes = 21_000_000_000, budgetBytes = 20_000_000_000 };
        Assert.That(status.Fraction, Is.EqualTo(1));
        Assert.That(status.Summary, Does.Contain("105.0%"));
    }
}
