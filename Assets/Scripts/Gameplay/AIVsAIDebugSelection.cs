using UnityEngine;

/// <summary>
/// Holds dev-only AI-vs-AI settings while switching scenes and when resuming a saved debug match.
/// </summary>
public static class AIVsAIDebugSelection
{
    private const string PlayerPrefsKeyPrefix = "debug_ai_vs_ai_";

    [System.Serializable]
    public struct Settings
    {
        public bool enabled;
        public TurnManager.AIRecruitVariant sideARecruitVariant;
        public TurnManager.AIRecruitVariant sideBRecruitVariant;
        public AILocalDecisionFeatures sideAFeatures;
        public AILocalDecisionFeatures sideBFeatures;
        public TurnManager.AIDebugProfile sideAProfile;
        public TurnManager.AIDebugProfile sideBProfile;
        public TurnManager.AIVsAIBatchSpeedPreset batchSpeedPreset;

        public static Settings Default => new Settings
        {
            enabled = false,
            sideARecruitVariant = TurnManager.AIRecruitVariant.Default,
            sideBRecruitVariant = TurnManager.AIRecruitVariant.Default,
            sideAFeatures = AILocalDecisionFeatures.None,
            sideBFeatures = AILocalDecisionFeatures.None,
            sideAProfile = TurnManager.AIDebugProfile.Baseline,
            sideBProfile = TurnManager.AIDebugProfile.Baseline,
            batchSpeedPreset = TurnManager.AIVsAIBatchSpeedPreset.Normal
        };
    }

    [System.Serializable]
    private sealed class PersistedSettings
    {
        public bool enabled;
        public string sideARecruitVariant;
        public string sideBRecruitVariant;
        public string sideAFeatures;
        public string sideBFeatures;
        public string sideAProfile;
        public string sideBProfile;
        public string batchSpeedPreset;
    }

    private static Settings pendingSettings = Settings.Default;
    private static bool hasPendingSettings;

    public static void SetPending(
        bool enabled,
        TurnManager.AIRecruitVariant sideARecruitVariant,
        TurnManager.AIRecruitVariant sideBRecruitVariant,
        AILocalDecisionFeatures sideAFeatures,
        AILocalDecisionFeatures sideBFeatures,
        TurnManager.AIDebugProfile sideAProfile,
        TurnManager.AIDebugProfile sideBProfile,
        TurnManager.AIVsAIBatchSpeedPreset batchSpeedPreset)
    {
        pendingSettings = new Settings
        {
            enabled = enabled,
            sideARecruitVariant = sideARecruitVariant,
            sideBRecruitVariant = sideBRecruitVariant,
            sideAFeatures = sideAFeatures,
            sideBFeatures = sideBFeatures,
            sideAProfile = sideAProfile,
            sideBProfile = sideBProfile,
            batchSpeedPreset = batchSpeedPreset
        };
        hasPendingSettings = true;
    }

    public static bool TryConsume(out Settings settings)
    {
        settings = pendingSettings;
        pendingSettings = Settings.Default;
        bool hadPendingSettings = hasPendingSettings;
        hasPendingSettings = false;
        return hadPendingSettings;
    }

    public static bool TryPeek(out Settings settings)
    {
        settings = pendingSettings;
        return hasPendingSettings;
    }

    public static void SaveForGame(string gameId, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return;
        }

        string key = BuildPlayerPrefsKey(gameId);
        if (!settings.enabled)
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            return;
        }

        PersistedSettings persisted = new PersistedSettings
        {
            enabled = true,
            sideARecruitVariant = settings.sideARecruitVariant.ToString(),
            sideBRecruitVariant = settings.sideBRecruitVariant.ToString(),
            sideAFeatures = settings.sideAFeatures.ToString(),
            sideBFeatures = settings.sideBFeatures.ToString(),
            sideAProfile = settings.sideAProfile.ToString(),
            sideBProfile = settings.sideBProfile.ToString(),
            batchSpeedPreset = settings.batchSpeedPreset.ToString()
        };

        PlayerPrefs.SetString(key, JsonUtility.ToJson(persisted));
        PlayerPrefs.Save();
    }

    public static bool TryLoadForGame(string gameId, out Settings settings)
    {
        settings = Settings.Default;
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        string json = PlayerPrefs.GetString(BuildPlayerPrefsKey(gameId), string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            PersistedSettings persisted = JsonUtility.FromJson<PersistedSettings>(json);
            if (persisted == null || !persisted.enabled)
            {
                return false;
            }

            settings = new Settings
            {
                enabled = true,
                sideARecruitVariant = ParseVariantOrDefault(persisted.sideARecruitVariant),
                sideBRecruitVariant = ParseVariantOrDefault(persisted.sideBRecruitVariant),
                sideAFeatures = ParseFeaturesOrDefault(persisted.sideAFeatures),
                sideBFeatures = ParseFeaturesOrDefault(persisted.sideBFeatures),
                sideAProfile = ParseProfileOrDefault(persisted.sideAProfile),
                sideBProfile = ParseProfileOrDefault(persisted.sideBProfile),
                batchSpeedPreset = ParseBatchSpeedPresetOrDefault(persisted.batchSpeedPreset)
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static TurnManager.AIRecruitVariant ParseVariantOrDefault(string rawVariant)
    {
        if (!string.IsNullOrWhiteSpace(rawVariant) &&
            System.Enum.TryParse(rawVariant, out TurnManager.AIRecruitVariant parsedVariant))
        {
            return parsedVariant;
        }

        return TurnManager.AIRecruitVariant.Default;
    }

    private static TurnManager.AIVsAIBatchSpeedPreset ParseBatchSpeedPresetOrDefault(string rawPreset)
    {
        if (!string.IsNullOrWhiteSpace(rawPreset) &&
            System.Enum.TryParse(rawPreset, out TurnManager.AIVsAIBatchSpeedPreset parsedPreset))
        {
            return parsedPreset;
        }

        return TurnManager.AIVsAIBatchSpeedPreset.Normal;
    }

    private static AILocalDecisionFeatures ParseFeaturesOrDefault(string rawFeatures)
    {
        if (!string.IsNullOrWhiteSpace(rawFeatures) &&
            System.Enum.TryParse(rawFeatures, out AILocalDecisionFeatures parsedFeatures))
        {
            return parsedFeatures;
        }

        return AILocalDecisionFeatures.None;
    }

    private static TurnManager.AIDebugProfile ParseProfileOrDefault(string rawProfile)
    {
        if (!string.IsNullOrWhiteSpace(rawProfile) &&
            System.Enum.TryParse(rawProfile, out TurnManager.AIDebugProfile parsedProfile))
        {
            return parsedProfile;
        }

        return TurnManager.AIDebugProfile.Baseline;
    }

    private static string BuildPlayerPrefsKey(string gameId)
    {
        return PlayerPrefsKeyPrefix + Hash128.Compute(gameId.Trim()).ToString();
    }
}
