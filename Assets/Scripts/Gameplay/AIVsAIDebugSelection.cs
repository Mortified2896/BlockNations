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

        public static Settings Default => new Settings
        {
            enabled = false,
            sideARecruitVariant = TurnManager.AIRecruitVariant.Default,
            sideBRecruitVariant = TurnManager.AIRecruitVariant.Default
        };
    }

    [System.Serializable]
    private sealed class PersistedSettings
    {
        public bool enabled;
        public string sideARecruitVariant;
        public string sideBRecruitVariant;
    }

    private static Settings pendingSettings = Settings.Default;
    private static bool hasPendingSettings;

    public static void SetPending(
        bool enabled,
        TurnManager.AIRecruitVariant sideARecruitVariant,
        TurnManager.AIRecruitVariant sideBRecruitVariant)
    {
        pendingSettings = new Settings
        {
            enabled = enabled,
            sideARecruitVariant = sideARecruitVariant,
            sideBRecruitVariant = sideBRecruitVariant
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
            sideBRecruitVariant = settings.sideBRecruitVariant.ToString()
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
                sideBRecruitVariant = ParseVariantOrDefault(persisted.sideBRecruitVariant)
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

    private static string BuildPlayerPrefsKey(string gameId)
    {
        return PlayerPrefsKeyPrefix + Hash128.Compute(gameId.Trim()).ToString();
    }
}
