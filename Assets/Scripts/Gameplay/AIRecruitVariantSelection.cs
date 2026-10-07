/// <summary>
/// Holds the chosen AI recruit variant while switching scenes.
/// </summary>
public static class AIRecruitVariantSelection
{
    private static TurnManager.AIRecruitVariant pendingVariant = TurnManager.AIRecruitVariant.Default;

    public static void SetPending(TurnManager.AIRecruitVariant variant)
    {
        pendingVariant = variant;
    }

    public static bool TryConsume(out TurnManager.AIRecruitVariant variant)
    {
        variant = pendingVariant;
        pendingVariant = TurnManager.AIRecruitVariant.Default;
        return true;
    }

    // A development-only opponent can appear in a local VsAI save. Shipping
    // builds resolve it to the local policy without adding/changing save fields.
    public static TurnManager.AIRecruitVariant ForCurrentBuild(TurnManager.AIRecruitVariant variant)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return variant;
#else
        return variant == TurnManager.AIRecruitVariant.LunaPlaytest ? TurnManager.AIRecruitVariant.HardTactician : variant;
#endif
    }
}
