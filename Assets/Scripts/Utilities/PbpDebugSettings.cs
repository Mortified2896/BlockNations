using UnityEngine;

[CreateAssetMenu(fileName = "PbpDebugSettings", menuName = "BlockNations/PBp Debug Settings")]
public class PbpDebugSettings : ScriptableObject
{
    public bool enableInputLogs = false;
}

public static class PbpDebugSettingsLoader
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private const string ResourcePath = "PbpDebugSettings";
    private static bool hasLoaded;
    private static bool cachedEnableInputLogs;

    public static bool EnableInputLogs
    {
        get
        {
            if (hasLoaded)
                return cachedEnableInputLogs;

            hasLoaded = true;
            PbpDebugSettings settings = Resources.Load<PbpDebugSettings>(ResourcePath);
            cachedEnableInputLogs = settings != null && settings.enableInputLogs;
            return cachedEnableInputLogs;
        }
    }
#else
    public static bool EnableInputLogs => false;
#endif
}
