using System;
using System.IO;
using System.Text;
using UnityEngine;

public static class DevClientInstanceScope
{
    private const string DevPersistentRootFolderName = "dev_clients";
    private const string DefaultAppStorageNamespace = "app";

    private static bool resolved;
    private static string storageNamespace;
    private static string initialTypedProfileName;
    private static string legacyInitialTypedProfileName;

    public static bool IsEnabled
    {
        get
        {
            EnsureResolved();
            return !string.IsNullOrWhiteSpace(storageNamespace);
        }
    }

    public static string StorageNamespace
    {
        get
        {
            EnsureResolved();
            return storageNamespace;
        }
    }

    public static string ScopePlayerPrefsKey(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return rawKey;
        }

        EnsureResolved();
        return string.IsNullOrWhiteSpace(storageNamespace)
            ? rawKey
            : $"dev_{storageNamespace}_{rawKey}";
    }

    public static string GetScopedPersistentDataPath()
    {
        EnsureResolved();
        return string.IsNullOrWhiteSpace(storageNamespace)
            ? Application.persistentDataPath
            : Path.Combine(Application.persistentDataPath, DevPersistentRootFolderName, storageNamespace);
    }

    public static string GetInitialTypedProfileName()
    {
        EnsureResolved();
        return initialTypedProfileName ?? string.Empty;
    }

    public static bool IsCurrentDevDefaultTypedProfileName(string typedProfileName)
    {
        string normalizedInput = LocalPlayerProfileStore.NormalizeTypedDisplayName(typedProfileName);
        string normalizedDefault = LocalPlayerProfileStore.NormalizeTypedDisplayName(GetInitialTypedProfileName());
        if (!string.IsNullOrWhiteSpace(normalizedDefault) &&
            string.Equals(normalizedInput, normalizedDefault, StringComparison.Ordinal))
        {
            return true;
        }

        string normalizedLegacyDefault = LocalPlayerProfileStore.NormalizeTypedDisplayName(legacyInitialTypedProfileName);
        return !string.IsNullOrWhiteSpace(normalizedLegacyDefault) &&
               string.Equals(normalizedInput, normalizedLegacyDefault, StringComparison.Ordinal);
    }

    private static void EnsureResolved()
    {
        if (resolved)
        {
            return;
        }

        resolved = true;
        storageNamespace = string.Empty;
        initialTypedProfileName = string.Empty;
        legacyInitialTypedProfileName = string.Empty;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Application.isEditor)
        {
            storageNamespace = "editor";
            initialTypedProfileName = "Editor";
            return;
        }

#if UNITY_STANDALONE_OSX
        string appBundleName = TryGetCurrentAppBundleName();
        if (TryParseMacDevAppSuffix(appBundleName, out string suffix))
        {
            storageNamespace = "mac" + suffix;
            initialTypedProfileName = "Mac" + suffix;
            legacyInitialTypedProfileName = suffix;
            return;
        }

        storageNamespace = "mac";
        initialTypedProfileName = "Mac";
#endif
#endif

        if (string.IsNullOrWhiteSpace(storageNamespace))
        {
            storageNamespace = DefaultAppStorageNamespace;
        }
    }

    private static string TryGetCurrentAppBundleName()
    {
        try
        {
            DirectoryInfo current = new DirectoryInfo(Application.dataPath);
            while (current != null)
            {
                if (current.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                {
                    return current.Name;
                }

                current = current.Parent;
            }
        }
        catch
        {
            // Best-effort only. Empty result keeps release/default behavior.
        }

        return string.Empty;
    }

    private static bool TryParseMacDevAppSuffix(string appBundleName, out string suffix)
    {
        suffix = string.Empty;

        if (string.IsNullOrWhiteSpace(appBundleName))
        {
            return false;
        }

        string appStem = Path.GetFileNameWithoutExtension(appBundleName).Trim();
        const string expectedPrefix = "BlockNations";
        if (!appStem.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string remainder = appStem.Substring(expectedPrefix.Length).Trim();
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        string sanitized = SanitizeToken(remainder);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return false;
        }

        suffix = sanitized;
        return true;
    }

    private static string SanitizeToken(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder(rawValue.Length);
        for (int i = 0; i < rawValue.Length; i++)
        {
            char c = rawValue[i];
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
