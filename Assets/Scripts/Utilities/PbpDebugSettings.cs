using UnityEngine;

[CreateAssetMenu(fileName = "PbpDebugSettings", menuName = "BlockNations/PBp Debug Settings")]
public class PbpDebugSettings : ScriptableObject
{
    [Tooltip("Shared PBp base URL used by HttpTurnTransport when set. Leave blank to fall back to the scene component value.")]
    public string playByPostBaseUrl = string.Empty;
    public bool enableInputLogs = false;
    public bool enableTransportLogs = false;
    public bool enableSaveLoadLogs = false;
}

public static class PbpDebugSettingsLoader
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private const string ResourcePath = "PbpDebugSettings";
    private static bool hasLoaded;
    private static bool cachedEnableInputLogs;
    private static bool cachedEnableTransportLogs;
    private static bool cachedEnableSaveLoadLogs;

    private static void EnsureLoaded()
    {
        if (hasLoaded)
            return;

        hasLoaded = true;
        PbpDebugSettings settings = Resources.Load<PbpDebugSettings>(ResourcePath);
        cachedEnableInputLogs = settings != null && settings.enableInputLogs;
        cachedEnableTransportLogs = settings != null && settings.enableTransportLogs;
        cachedEnableSaveLoadLogs = settings != null && settings.enableSaveLoadLogs;
    }

    public static void ResetCache()
    {
        hasLoaded = false;
        cachedEnableInputLogs = false;
        cachedEnableTransportLogs = false;
        cachedEnableSaveLoadLogs = false;
    }

    public static bool EnableInputLogs
    {
        get
        {
            EnsureLoaded();
            return cachedEnableInputLogs;
        }
    }

    public static bool EnableTransportLogs
    {
        get
        {
            EnsureLoaded();
            return cachedEnableTransportLogs;
        }
    }

    public static bool EnableSaveLoadLogs
    {
        get
        {
            EnsureLoaded();
            return cachedEnableSaveLoadLogs;
        }
    }
#else
    public static bool EnableInputLogs => false;
    public static bool EnableTransportLogs => false;
    public static bool EnableSaveLoadLogs => false;
#endif
}
