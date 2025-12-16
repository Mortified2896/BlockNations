using System.Runtime.InteropServices;
using UnityEngine;

public static class ClipboardUtility
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern int CopyToClipboard(string str);
#endif

    public static bool TryCopy(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            return CopyToClipboard(text) == 1;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("ClipboardUtility: Copy failed on WebGL. " + ex.Message);
            return false;
        }
#else
        GUIUtility.systemCopyBuffer = text;
        return true;
#endif
    }
}

