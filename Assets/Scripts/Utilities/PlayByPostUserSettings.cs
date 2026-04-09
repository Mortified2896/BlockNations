using UnityEngine;

public static class PlayByPostUserSettings
{
    private const string MessageAfterTurnEndEnabledKeyRaw = "pbp_message_after_turn_end_enabled";

    public static bool IsMessageAfterTurnEndEnabled()
    {
        return PlayerPrefs.GetInt(GetMessageAfterTurnEndEnabledKey(), 1) == 1;
    }

    public static void SetMessageAfterTurnEndEnabled(bool enabled)
    {
        PlayerPrefs.SetInt(GetMessageAfterTurnEndEnabledKey(), enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    private static string GetMessageAfterTurnEndEnabledKey()
    {
        return DevClientInstanceScope.ScopePlayerPrefsKey(MessageAfterTurnEndEnabledKeyRaw);
    }
}
