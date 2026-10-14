using System;
using UnityEngine;

/// <summary>Silence development training players before any scene starts audio.</summary>
public static class TrainingAudioPolicy
{
    public static bool RequiresSilence(string[] arguments)
    {
        if (arguments == null) return false;
        for (int i = 0; i + 1 < arguments.Length; i++)
        {
            if (arguments[i] == "--training-status" && !string.IsNullOrWhiteSpace(arguments[i + 1]))
                return true;
            if ((arguments[i] == "--training-viewer" || arguments[i] == "--training-silent") &&
                bool.TryParse(arguments[i + 1], out bool enabled) && enabled)
                return true;
        }
        return false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        ApplySilence(Environment.GetCommandLineArgs());
    }

    public static bool ApplySilence(string[] arguments)
    {
        if (!RequiresSilence(arguments)) return false;
        AudioListener.pause = true;
        AudioListener.volume = 0f;
        Debug.Log("[ML Training] Development audio muted before scene startup.");
        return true;
    }
}
