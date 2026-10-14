using System;
using BlockNations.Simulation;

public partial class TurnManager
{
    public MatchRuleProfile RuleProfile { get; private set; } = MatchRuleProfile.Baseline;

    // Ordinary single-player and PBp use the current three-unit roster. The
    // stable catalog still loads supported older saves containing Scouts.
    private void ConfigureOrdinaryMatchRules()
    {
        if (IsExternallyDrivenMatch) return;
        RuleProfile = MatchRuleProfile.CoreThree;
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

    public void ConfigureExperimentalRulesForViewer(MatchRuleProfile profile)
    {
        if (profile == null || !IsExternallyDrivenMatch || externalHumanSeatIndex >= 0)
            throw new InvalidOperationException("Only an isolated spectator can switch its displayed rules.");
        RuleProfile = profile;
    }
}
