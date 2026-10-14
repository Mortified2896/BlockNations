using NUnit.Framework;
using UnityEngine;

public class TrainingAudioPolicyTests
{
    [TestCase("--training-viewer", "true", true)]
    [TestCase("--training-viewer", "false", false)]
    [TestCase("--training-status", "/tmp/arena.json", true)]
    [TestCase("--training-silent", "true", true)]
    [TestCase("--training-silent", "false", false)]
    [TestCase("--training-silent", "invalid", false)]
    [TestCase("--ordinary-game", "true", false)]
    public void OnlyTrainingLaunchesRequireSilence(string option, string value, bool expected)
    {
        Assert.That(TrainingAudioPolicy.RequiresSilence(new[] { "BlockNations", option, value }), Is.EqualTo(expected));
    }

    [Test]
    public void MissingOrIncompleteArgumentsLeaveOrdinaryAudioAvailable()
    {
        Assert.That(TrainingAudioPolicy.RequiresSilence(null), Is.False);
        Assert.That(TrainingAudioPolicy.RequiresSilence(new[] { "BlockNations", "--training-silent" }), Is.False);
    }

    [Test]
    public void TrainingMutesTheListenerWhileOrdinaryLaunchPreservesVolume()
    {
        bool paused = AudioListener.pause;
        float volume = AudioListener.volume;
        try
        {
            AudioListener.pause = false;
            AudioListener.volume = 0.4f;
            Assert.That(TrainingAudioPolicy.ApplySilence(new[] { "BlockNations" }), Is.False);
            Assert.That(AudioListener.pause, Is.False);
            Assert.That(AudioListener.volume, Is.EqualTo(0.4f));
            Assert.That(TrainingAudioPolicy.ApplySilence(new[] { "BlockNations", "--training-viewer", "true" }), Is.True);
            Assert.That(AudioListener.pause, Is.True);
            Assert.That(AudioListener.volume, Is.Zero);
        }
        finally
        {
            AudioListener.pause = paused;
            AudioListener.volume = volume;
        }
    }
}
