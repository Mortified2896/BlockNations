using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockNations.Training;
using NUnit.Framework;

public sealed class TrainingSnapshotPublisherTests
{
    [Test]
    public void ABlockedOutputNeverBlocksProducersAndPendingSnapshotsStayBounded()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var written = new ConcurrentDictionary<string, int>();
        var publisher = new LatestSnapshotPublisher<int>(4, (key, value) => {
            if (key == "blocked") { entered.Set(); release.Wait(); }
            written[key] = value;
        });
        try
        {
            publisher.Publish("blocked", 0);
            Assert.That(entered.Wait(2000), Is.True);
            Task producer = Task.Run(() => {
                for (int i = 1; i <= 10000; i++) publisher.Publish("live", i);
                publisher.Publish("other", 1);
            });
            Assert.That(producer.Wait(2000), Is.True, "Simulation publishing must finish while the output thread is still blocked.");
            Assert.That(publisher.PendingCount, Is.EqualTo(2));
            Assert.That(publisher.Superseded, Is.EqualTo(9999));
            Assert.That(written.ContainsKey("live"), Is.False);
        }
        finally { release.Set(); publisher.Dispose(); }
        Assert.That(written["live"], Is.EqualTo(10000));
        Assert.That(written["other"], Is.EqualTo(1));
    }

    [Test]
    public void OverflowKeepsNewestKeysAndOutputErrorsRemainOptional()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var written = new ConcurrentDictionary<string, int>();
        var publisher = new LatestSnapshotPublisher<int>(2, (key, value) => {
            if (key == "blocked") { entered.Set(); release.Wait(); throw new IOException("fixture output unavailable"); }
            written[key] = value;
        });
        try
        {
            publisher.Publish("blocked", 0); Assert.That(entered.Wait(2000), Is.True);
            for (int i = 1; i <= 100; i++) publisher.Publish(i.ToString(), i);
            Assert.That(publisher.PendingCount, Is.EqualTo(2));
            Assert.That(publisher.Superseded, Is.EqualTo(98));
        }
        finally { release.Set(); publisher.Dispose(); }
        Assert.That(written.Keys, Is.EquivalentTo(new[] { "99", "100" }));
        Assert.That(publisher.Error, Is.Null, "A later successful write recovers optional output diagnostics.");
    }
}
