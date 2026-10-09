using System;
using System.IO;
using NUnit.Framework;

public sealed class SinglePlayerContinueSaveTests
{
    private string root;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "bn-continue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(root, true);

    [Test]
    public void UnfinishedSinglePlayerMatchCanBeContinued()
    {
        string path = Write("save_sp.json", "{\"mode\":\"VsAI\",\"gameOver\":false}");
        Assert.That(SinglePlayerContinueSave.TryGetPath(root, out string selected), Is.True);
        Assert.That(selected, Is.EqualTo(path));
    }

    [TestCase("save_sp.json")]
    [TestCase("save.json")]
    public void FinishedMatchIsHiddenAndRemainsSaved(string fileName)
    {
        string path = Write(fileName, "{\"mode\":\"VsAI\",\"gameOver\":true}");
        Assert.That(SinglePlayerContinueSave.TryGetPath(root, out string selected), Is.False);
        Assert.That(selected, Is.Null);
        Assert.That(File.Exists(path), Is.True);
    }

    [Test]
    public void FinishedPrimaryDoesNotResurrectAnOlderLegacyMatch()
    {
        Write("save_sp.json", "{\"mode\":\"VsAI\",\"gameOver\":true}");
        Write("save.json", "{\"mode\":\"VsAI\",\"gameOver\":false}");
        Assert.That(SinglePlayerContinueSave.TryGetPath(root, out _), Is.False);
    }

    [Test]
    public void LegacySinglePlayerSaveWithoutModeStillWorks()
    {
        string path = Write("save.json", "{\"gameOver\":false}");
        Assert.That(SinglePlayerContinueSave.TryGetPath(root, out string selected), Is.True);
        Assert.That(selected, Is.EqualTo(path));
    }

    [Test]
    public void PlayByPostSaveDoesNotBecomeTheSinglePlayerContinueTarget()
    {
        Write("save.json", "{\"mode\":\"PlayByPost\",\"gameOver\":false}");
        Assert.That(SinglePlayerContinueSave.TryGetPath(root, out _), Is.False);
    }

    [Test]
    public void MissingOrUnreadableSaveDoesNotOfferContinue()
    {
        Assert.That(SinglePlayerContinueSave.TryGetPath(root, out _), Is.False);
        Write("save_sp.json", "invalid save JSON");
        Assert.That(SinglePlayerContinueSave.TryGetPath(root, out _), Is.False);
    }

    private string Write(string fileName, string json)
    {
        string path = Path.Combine(root, fileName);
        File.WriteAllText(path, json);
        return path;
    }
}
