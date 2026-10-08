using System;
using System.IO;
using NUnit.Framework;

public sealed class TrainingRunSelectionTests
{
    private string root;
    private static readonly DateTime Now = new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "bn-run-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "runs"));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(root, true);

    [Test]
    public void NewRunUsesSelectedBoardAndSkipsExistingDirectoriesAndFiles()
    {
        string id = TrainingRunSelection.ResolveId(root, null, false, 6, Now);
        Assert.That(id, Is.EqualTo("mac-6x6-20261008T090000Z"));
        Directory.CreateDirectory(Path.Combine(root, "runs", id));
        File.WriteAllText(Path.Combine(root, "runs", id, "checkpoint.pt"), "preserved");
        File.WriteAllText(Path.Combine(root, "runs", id + "-2"), "foreign file");
        Assert.That(TrainingRunSelection.ResolveId(root, " ", false, 6, Now), Is.EqualTo(id + "-3"));
        Assert.That(File.ReadAllText(Path.Combine(root, "runs", id, "checkpoint.pt")), Is.EqualTo("preserved"));
        Assert.That(TrainingRunSelection.ResolveId(root, null, false, 5, Now), Is.EqualTo("mac-5x5-20261008T090000Z"));
    }

    [Test]
    public void ResumeKeepsTheSavedIdentityDespiteAnotherSelectedBoard()
    {
        const string saved = "mac-5x5-fresh-20261008";
        Directory.CreateDirectory(Path.Combine(root, "runs", saved));
        Assert.That(TrainingRunSelection.ResolveId(root, " " + saved + " ", true, 6, Now), Is.EqualTo(saved));
        Assert.Throws<ArgumentException>(() => TrainingRunSelection.ResolveId(root, null, true, 6, Now));
    }

    [Test]
    public void ExplicitNewRunCannotReuseAnExistingIdentity()
    {
        Directory.CreateDirectory(Path.Combine(root, "runs", "saved"));
        Assert.Throws<InvalidOperationException>(() => TrainingRunSelection.ResolveId(root, "saved", false, 6, Now));
        Assert.That(TrainingRunSelection.ResolveId(root, " custom-new ", false, 6, Now), Is.EqualTo("custom-new"));
    }

    [TestCase("../saved")]
    [TestCase("saved/run")]
    [TestCase("run name")]
    public void InvalidSavedIdentityIsRejected(string id)
    {
        Assert.Throws<ArgumentException>(() => TrainingRunSelection.ResolveId(root, id, true, 6, Now));
    }
}
