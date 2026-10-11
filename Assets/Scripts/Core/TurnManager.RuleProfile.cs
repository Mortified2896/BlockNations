using System;
using BlockNations.Simulation;

public partial class TurnManager
{
    public MatchRuleProfile RuleProfile { get; private set; } = MatchRuleProfile.Baseline;

    // Apply a frozen release's explicit rules before recruitment or loaded units
    // are presented. Legacy opponents and PBp keep their supported rules.
    private void ConfigureOrdinaryMatchRules()
    {
        if (IsExternallyDrivenMatch) return;
        RuleProfile = MatchRuleProfile.Baseline;
        if (currentMode != GameMode.VsAI || !LearnedAIRuntime.IsLearned(aiRecruitVariant)) return;
        var release = UnityEngine.Resources.Load<LearnedPolicyRelease>(LearnedPolicyRelease.ResourcePath);
        if (release == null) throw new InvalidOperationException("The frozen learned AI release is missing.");
        RuleProfile = release.RulesForBoard(gridManager.width);
    }

    public void ConfigureExperimentalRules(MatchRuleProfile profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (!IsExternallyDrivenMatch || ExternalMatchReady)
            throw new InvalidOperationException("Select experimental rules before preparing an external match.");
        RuleProfile = profile;
    }
}
