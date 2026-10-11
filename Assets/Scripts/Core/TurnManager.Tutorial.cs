using System;
using BlockNations.Simulation;

public partial class TurnManager
{
    public bool IsTutorialMatch { get; private set; }

    // The dedicated scene enables the existing external-driver boundary in its
    // serialized wiring. Ordinary gameplay never switches into this boundary.
    public void ConfigureTutorialDriver(Action restart)
    {
        if (!externallyDrivenMatch || gridManager == null || gridManager.width != 7 || gridManager.height != 7)
            throw new InvalidOperationException("The tutorial requires its explicitly configured 7x7 scene.");
        IsTutorialMatch = true;
        ConfigureExternalHumanSeat(TutorialLesson.HumanSeat, restart);
        ConfigureExperimentalRules(MatchRuleProfile.CoreThree);
    }
}
