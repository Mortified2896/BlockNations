using NUnit.Framework;

public sealed class LocalAIOpponentCompatibilityTests
{
    [TestCase("Default", TurnManager.AIRecruitVariant.Default)]
    [TestCase("RiderFocus", TurnManager.AIRecruitVariant.RiderFocus)]
    [TestCase("HardTactician", TurnManager.AIRecruitVariant.HardTactician)]
    [TestCase("LunaPlaytest", TurnManager.AIRecruitVariant.HardTactician)]
    [TestCase("3", TurnManager.AIRecruitVariant.HardTactician)]
    [TestCase(null, TurnManager.AIRecruitVariant.Default)]
    [TestCase("", TurnManager.AIRecruitVariant.Default)]
    [TestCase("UnknownOpponent", TurnManager.AIRecruitVariant.Default)]
    public void SavedOpponentsResolveWithoutChangingTheSaveFormat(string saved, TurnManager.AIRecruitVariant expected)
    {
        Assert.That(AIRecruitVariantSelection.FromSavedValue(saved), Is.EqualTo(expected));
    }

    [TestCase(0, TurnManager.AIRecruitVariant.Default)]
    [TestCase(1, TurnManager.AIRecruitVariant.RiderFocus)]
    [TestCase(2, TurnManager.AIRecruitVariant.HardTactician)]
    [TestCase(3, TurnManager.AIRecruitVariant.HardTactician)]
    [TestCase(99, TurnManager.AIRecruitVariant.Default)]
    public void SerializedOpponentValuesResolveToLocalPolicies(int value, TurnManager.AIRecruitVariant expected)
    {
        Assert.That(AIRecruitVariantSelection.ForCurrentRuntime((TurnManager.AIRecruitVariant)value), Is.EqualTo(expected));
        Assert.That(System.Enum.GetValues(typeof(TurnManager.AIRecruitVariant)).Length, Is.EqualTo(3));
    }
}
