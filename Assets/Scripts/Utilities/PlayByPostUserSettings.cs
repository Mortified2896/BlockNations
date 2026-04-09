using UnityEngine;

public static class PlayByPostUserSettings
{
    private const string MessageAfterTurnEndEnabledKey = "pbp_message_after_turn_end_enabled";

    public static bool IsMessageAfterTurnEndEnabled()
    {
        return PlayerPrefs.GetInt(MessageAfterTurnEndEnabledKey, 1) == 1;
    }

    public static void SetMessageAfterTurnEndEnabled(bool enabled)
    {
        PlayerPrefs.SetInt(MessageAfterTurnEndEnabledKey, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }
}
