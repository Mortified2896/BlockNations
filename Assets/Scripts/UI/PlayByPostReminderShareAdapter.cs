using System.Runtime.InteropServices;
using UnityEngine;

public static class PlayByPostReminderShareAdapter
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern bool BNPresentReminderShareSheet(string text);
#endif

    public static bool TryPresentReminderShareSheet(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

#if UNITY_IOS && !UNITY_EDITOR
        return BNPresentReminderShareSheet(text);
#else
        return false;
#endif
    }
}
