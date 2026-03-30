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
#if UNITY_IOS || UNITY_ANDROID || UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }

    public static bool IsReminderShareSupported()
    {
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }

    public static bool TryPresentDefaultReminderShareSheet()
    {
        return TryPresentReminderShareSheet(DefaultReminderShareText);
    }

    public static bool TryPresentReminderShareSheetForTurn(int turnNumber)
    {
        return TryPresentReminderShareSheet(BuildReminderShareText(turnNumber));
    }

    public static string BuildReminderShareText(int? turnNumber = null)
    {
        if (!turnNumber.HasValue || turnNumber.Value <= 0)
        {
            return DefaultReminderShareText;
        }

        return $"It’s your turn in Block Nations. Turn {turnNumber.Value}.";
    }

    public static bool TryPresentReminderShareSheet(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

#if UNITY_IOS && !UNITY_EDITOR
        return BNPresentReminderShareSheet(text);
#elif UNITY_ANDROID && !UNITY_EDITOR
        return TryPresentAndroidReminderShareSheet(text);
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

#if UNITY_ANDROID && !UNITY_EDITOR
    private static bool TryPresentAndroidReminderShareSheet(string text)
    {
        try
        {
            using AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            if (currentActivity == null)
            {
                return false;
            }

            using AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent");
            intent.Call<AndroidJavaObject>("setAction", "android.intent.action.SEND");
            intent.Call<AndroidJavaObject>("setType", "text/plain");
            intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.TEXT", text);

            using AndroidJavaClass intentClass = new AndroidJavaClass("android.content.Intent");
            using AndroidJavaObject chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, "Send Reminder");
            chooser.Call<AndroidJavaObject>("addFlags", 0x10000000);
            currentActivity.Call("startActivity", chooser);
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("PlayByPostReminderShareAdapter: Android reminder share failed. " + ex.Message);
            return false;
        }
    }
#endif
}
