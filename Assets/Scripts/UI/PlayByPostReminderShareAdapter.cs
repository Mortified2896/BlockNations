using System.Runtime.InteropServices;
using UnityEngine;

public static class PlayByPostReminderShareAdapter
{
    private const string DefaultReminderShareText = "It’s your turn in Block Nations!";

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern bool BNPresentReminderShareSheet(string text);
#endif

    public static bool ShouldShowReminderShareUi()
    {
#if UNITY_IOS || UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }

    public static bool IsReminderShareSupported()
    {
#if UNITY_IOS && !UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }

    public static bool TryPresentDefaultReminderShareSheet()
    {
        return TryPresentReminderShareSheet(DefaultReminderShareText);
    }

    public static bool TryPresentReminderShareSheet(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

#if UNITY_IOS && !UNITY_EDITOR
        return BNPresentReminderShareSheet(text);
#elif UNITY_EDITOR
        if (!ClipboardUtility.TryCopy(text))
        {
            GUIUtility.systemCopyBuffer = text;
        }

        Debug.Log($"[ReminderSharePreview] Copied reminder text for editor preview: {text}");
        return true;
#else
        return false;
#endif
    }
}
