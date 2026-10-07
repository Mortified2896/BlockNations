using System;

// Scene transitions must carry the current scheduled pairing, including seat swaps.
// Selecting the first pairing is appropriate only when beginning a new run.
public static class AIVsAIMatchHandoff
{
    public static void Queue(AIVsAIBatchRunController.SimulationSettings simulation,
        AIVsAIDebugSelection.Settings fallback, bool startingNewRun)
    {
        simulation = AIVsAIBatchRunController.SanitizeSimulationSettings(simulation);
        AIVsAIDebugSelection.Settings resolved = fallback;
        bool found = false;
        if (!startingNewRun)
        {
            found = AIVsAIBatchRunController.TryGetUpcomingMatchSettings(
                out resolved.sideARecruitVariant, out resolved.sideBRecruitVariant,
                out resolved.sideAFeatures, out resolved.sideBFeatures,
                out resolved.sideAProfile, out resolved.sideBProfile);
            if (!found) throw new InvalidOperationException("The active AI run has no upcoming scheduled match.");
        }
        else if (simulation.mode == AIVsAIBatchRunController.SimulationMode.Tournament)
        {
            found = AIVsAIBatchRunController.TryGetInitialTournamentMatchSettings(simulation,
                out resolved.sideARecruitVariant, out resolved.sideBRecruitVariant,
                out resolved.sideAFeatures, out resolved.sideBFeatures,
                out resolved.sideAProfile, out resolved.sideBProfile);
            if (!found) throw new InvalidOperationException("The AI tournament has no initial scheduled match.");
        }
        AIVsAIBatchRunController.SetPendingSimulationSettings(simulation);
        AIVsAIDebugSelection.SetPending(resolved.enabled,
            resolved.sideARecruitVariant, resolved.sideBRecruitVariant,
            resolved.sideAFeatures, resolved.sideBFeatures,
            resolved.sideAProfile, resolved.sideBProfile, resolved.batchSpeedPreset);
    }

    public static bool MatchesUpcoming(AIVsAIDebugSelection.Settings actual)
    {
        return actual.enabled && AIVsAIBatchRunController.TryGetUpcomingMatchSettings(
            out var sideA, out var sideB, out var featuresA, out var featuresB,
            out var profileA, out var profileB) &&
            actual.sideARecruitVariant == sideA && actual.sideBRecruitVariant == sideB &&
            actual.sideAFeatures == featuresA && actual.sideBFeatures == featuresB &&
            actual.sideAProfile == profileA && actual.sideBProfile == profileB;
    }
}
