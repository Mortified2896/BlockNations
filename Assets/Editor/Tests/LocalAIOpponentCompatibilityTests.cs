using NUnit.Framework;

public sealed class LocalAIOpponentCompatibilityTests
{
    [TestCase("Default", TurnManager.AIRecruitVariant.Default)]
    [TestCase("RiderFocus", TurnManager.AIRecruitVariant.RiderFocus)]
    [TestCase("HardTactician", TurnManager.AIRecruitVariant.HardTactician)]
    [TestCase("LunaPlaytest", TurnManager.AIRecruitVariant.HardTactician)]
    [TestCase("3", TurnManager.AIRecruitVariant.HardTactician)]
    [TestCase("LearnedEasy", TurnManager.AIRecruitVariant.LearnedEasy)]
    [TestCase("LearnedMedium", TurnManager.AIRecruitVariant.LearnedMedium)]
    [TestCase("LearnedHard", TurnManager.AIRecruitVariant.LearnedHard)]
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
    [TestCase(4, TurnManager.AIRecruitVariant.LearnedEasy)]
    [TestCase(5, TurnManager.AIRecruitVariant.LearnedMedium)]
    [TestCase(6, TurnManager.AIRecruitVariant.LearnedHard)]
    [TestCase(99, TurnManager.AIRecruitVariant.Default)]
    public void SerializedOpponentValuesResolveToLocalPolicies(int value, TurnManager.AIRecruitVariant expected)
    {
        Assert.That(AIRecruitVariantSelection.ForCurrentRuntime((TurnManager.AIRecruitVariant)value), Is.EqualTo(expected));
        Assert.That(System.Enum.GetValues(typeof(TurnManager.AIRecruitVariant)), Has.No.Member((TurnManager.AIRecruitVariant)3),
            "Keep the retired serialized value reserved.");
    }
}
