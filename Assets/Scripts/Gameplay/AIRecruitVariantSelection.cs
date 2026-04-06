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
}
