using System;
using BlockNations.Simulation;

public partial class TurnManager
{
    public MatchRuleProfile RuleProfile { get; private set; } = MatchRuleProfile.Baseline;

    // Experimental native training/playtests only. Ordinary games and supported
    // saves/PBp retain the baseline until a release/migration decision is made.
    public void ConfigureExperimentalRules(MatchRuleProfile profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (!IsExternallyDrivenMatch || ExternalMatchReady)
            throw new InvalidOperationException("Select experimental rules before preparing an external match.");
        RuleProfile = profile;
    }
}
