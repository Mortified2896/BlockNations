/// <summary>
/// Holds the chosen AI recruit variant while switching scenes.
/// </summary>
public static class AIRecruitVariantSelection
{
    private static TurnManager.AIRecruitVariant pendingVariant = TurnManager.AIRecruitVariant.LearnedMedium;

    public static void SetPending(TurnManager.AIRecruitVariant variant)
    {
        pendingVariant = variant;
    }

    public static bool TryConsume(out TurnManager.AIRecruitVariant variant)
    {
        variant = pendingVariant;
        pendingVariant = TurnManager.AIRecruitVariant.LearnedMedium;
        return true;
    }

    // Value 3 belonged to a retired development opponent. Never reuse it: old
    // serialized selections resolve to local Hard without changing save fields.
    public static TurnManager.AIRecruitVariant ForCurrentRuntime(TurnManager.AIRecruitVariant variant)
    {
        switch (variant)
        {
            case TurnManager.AIRecruitVariant.Default:
            case TurnManager.AIRecruitVariant.RiderFocus:
            case TurnManager.AIRecruitVariant.HardTactician:
            case TurnManager.AIRecruitVariant.LearnedEasy:
            case TurnManager.AIRecruitVariant.LearnedMedium:
            case TurnManager.AIRecruitVariant.LearnedHard:
                return variant;
            case (TurnManager.AIRecruitVariant)3:
                return TurnManager.AIRecruitVariant.HardTactician;
            default:
                return TurnManager.AIRecruitVariant.Default;
        }
    }

    public static TurnManager.AIRecruitVariant FromSavedValue(string value)
    {
        // Saves stored the opponent's enum name. This compatibility alias is
        // the only remaining reference to the removed model experiment.
        if (string.Equals(value, "LunaPlaytest", System.StringComparison.Ordinal))
            return TurnManager.AIRecruitVariant.HardTactician;
        return System.Enum.TryParse(value, out TurnManager.AIRecruitVariant variant)
            ? ForCurrentRuntime(variant)
            : TurnManager.AIRecruitVariant.Default;
    }
}
