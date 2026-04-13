using System;
using UnityEngine;

public static class LocalPlayerProfileStore
{
    public const int MinTypedDisplayNameLength = 2;

    private const string PlayerIdKeyRaw = "profile_player_id";
    private const string UsernameKeyRaw = "profile_username";
    private const string TitleKeyRaw = "profile_title";
    private const string TypedDisplayNameKeyRaw = "profile_typed_display_name";

    public enum TypedDisplayNameValidationResult
    {
        Valid,
        TooShort,
        TooLong,
        InvalidCharacters,
        NotRecognizable
    }

    public struct ProfileData
    {
        public string PlayerId;
        public string Username;
        public string Title;
        public string TypedDisplayName;

        public ProfileData(string playerId, string username, string title, string typedDisplayName)
        {
            PlayerId = playerId;
            Username = username;
            Title = title;
            TypedDisplayName = typedDisplayName;
        }
    }

    public static ProfileData GetOrCreateProfile()
    {
        bool didChange = false;

        string playerIdKey = GetScopedKey(PlayerIdKeyRaw);
        string usernameKey = GetScopedKey(UsernameKeyRaw);
        string titleKey = GetScopedKey(TitleKeyRaw);
        string typedDisplayNameKey = GetScopedKey(TypedDisplayNameKeyRaw);

        string playerId = PlayerPrefs.GetString(playerIdKey, string.Empty);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            playerId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(playerIdKey, playerId);
            didChange = true;
        }

        string username = PlayerPrefs.GetString(usernameKey, string.Empty);
        if (!IsValidUsername(username))
        {
            username = ProfileUsernameGenerator.Generate();
            PlayerPrefs.SetString(usernameKey, username);
            didChange = true;
        }

        string storedTitle = PlayerPrefs.GetString(titleKey, string.Empty);
        string title = NormalizeTitle(storedTitle);
        if (!IsValidTitle(title))
        {
            title = ResolveInitialTitle(username);
        }

        if (!string.Equals(storedTitle, title, StringComparison.Ordinal))
        {
            PlayerPrefs.SetString(titleKey, title);
            didChange = true;
        }

        string typedDisplayName = NormalizeTypedDisplayName(PlayerPrefs.GetString(typedDisplayNameKey, string.Empty));
        string storedTypedDisplayName = PlayerPrefs.GetString(typedDisplayNameKey, string.Empty);
        if (!HasRecognizableTypedDisplayName(typedDisplayName))
        {
            if (string.IsNullOrWhiteSpace(storedTypedDisplayName))
            {
                typedDisplayName = NormalizeTypedDisplayName(DevClientInstanceScope.GetInitialTypedProfileName());
            }
            else
            {
                typedDisplayName = string.Empty;
            }
        }

        if (!string.Equals(storedTypedDisplayName, typedDisplayName, StringComparison.Ordinal))
        {
            PlayerPrefs.SetString(typedDisplayNameKey, typedDisplayName);
            didChange = true;
        }

        if (didChange)
        {
            PlayerPrefs.Save();
        }

        return new ProfileData(playerId, username, title, typedDisplayName);
    }

    public static ProfileData RegenerateTitle()
    {
        ProfileData profile = GetOrCreateProfile();
        string regenerated = ProfileTitleGenerator.GenerateDistinct(profile.Title);

        PlayerPrefs.SetString(GetScopedKey(TitleKeyRaw), regenerated);
        PlayerPrefs.Save();

        profile.Title = regenerated;
        return profile;
    }

    public static string SetTypedDisplayName(string typedDisplayName)
    {
        string normalized = NormalizeTypedDisplayName(typedDisplayName);
        if (!HasRecognizableTypedDisplayName(normalized))
        {
            return GetSavedTypedDisplayName();
        }

        PlayerPrefs.SetString(GetScopedKey(TypedDisplayNameKeyRaw), normalized);
        return normalized;
    }

    public static string SetTitle(string title)
    {
        string normalized = NormalizeTitle(title);
        if (!IsValidTitle(normalized))
        {
            return GetSavedTitle();
        }

        PlayerPrefs.SetString(GetScopedKey(TitleKeyRaw), normalized);
        return normalized;
    }

    public static string NormalizeTypedDisplayName(string typedDisplayName)
    {
        if (string.IsNullOrWhiteSpace(typedDisplayName))
        {
            return string.Empty;
        }

        string trimmed = typedDisplayName.Trim();
        int maxLength = ProfileUsernameGenerator.MaxUsernameLength;
        return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength);
    }

    public static string GetTypedDisplayNameLengthRangeText()
    {
        return $"{MinTypedDisplayNameLength}-{ProfileUsernameGenerator.MaxUsernameLength}";
    }

    public static string NormalizeTitle(string title)
    {
        return string.IsNullOrWhiteSpace(title)
            ? string.Empty
            : title.Trim();
    }

    public static bool IsValidTitle(string title)
    {
        return !string.IsNullOrWhiteSpace(NormalizeTitle(title));
    }

    public static string FormatPublicUsername(string typedDisplayName, string title)
    {
        string normalizedTypedDisplayName = NormalizeTypedDisplayName(typedDisplayName);
        string normalizedTitle = NormalizeTitle(title);

        if (string.IsNullOrWhiteSpace(normalizedTypedDisplayName))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return normalizedTypedDisplayName;
        }

        return $"{normalizedTypedDisplayName} {normalizedTitle}";
    }

    public static TypedDisplayNameValidationResult GetTypedDisplayNameValidationResult(string typedDisplayName)
    {
        string trimmed = string.IsNullOrWhiteSpace(typedDisplayName)
            ? string.Empty
            : typedDisplayName.Trim();

        if (trimmed.Length < MinTypedDisplayNameLength)
        {
            return TypedDisplayNameValidationResult.TooShort;
        }

        if (trimmed.Length > ProfileUsernameGenerator.MaxUsernameLength)
        {
            return TypedDisplayNameValidationResult.TooLong;
        }

        if (!ContainsOnlyAllowedTypedDisplayNameCharacters(trimmed))
        {
            return TypedDisplayNameValidationResult.InvalidCharacters;
        }

        if (ProfileUsernameGenerator.IsGeneratedUsername(trimmed))
        {
            return TypedDisplayNameValidationResult.NotRecognizable;
        }

        return TypedDisplayNameValidationResult.Valid;
    }

    public static bool IsValidTypedDisplayName(string typedDisplayName)
    {
        return GetTypedDisplayNameValidationResult(typedDisplayName) == TypedDisplayNameValidationResult.Valid;
    }

    public static bool HasRecognizableTypedDisplayName(string typedDisplayName)
    {
        string normalized = NormalizeTypedDisplayName(typedDisplayName);
        return (IsValidTypedDisplayName(normalized) && !ProfileUsernameGenerator.IsGeneratedUsername(normalized)) ||
               DevClientInstanceScope.IsCurrentDevDefaultTypedProfileName(normalized);
    }

    private static string GetSavedTypedDisplayName()
    {
        string savedTypedDisplayName = NormalizeTypedDisplayName(PlayerPrefs.GetString(GetScopedKey(TypedDisplayNameKeyRaw), string.Empty));
        return HasRecognizableTypedDisplayName(savedTypedDisplayName)
            ? savedTypedDisplayName
            : string.Empty;
    }

    private static string GetSavedTitle()
    {
        string savedTitle = NormalizeTitle(PlayerPrefs.GetString(GetScopedKey(TitleKeyRaw), string.Empty));
        return IsValidTitle(savedTitle)
            ? savedTitle
            : string.Empty;
    }

    public static string GenerateValidTypedDisplayNameFallback()
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string generated = NormalizeTypedDisplayName(ProfileUsernameGenerator.Generate());
            if (generated.Length >= MinTypedDisplayNameLength &&
                generated.Length <= ProfileUsernameGenerator.MaxUsernameLength &&
                ContainsOnlyAllowedTypedDisplayNameCharacters(generated))
            {
                return generated;
            }
        }

        return "PlayerTwo";
    }

    private static bool IsValidUsername(string username)
    {
        return !string.IsNullOrWhiteSpace(username) && username.Length <= ProfileUsernameGenerator.MaxUsernameLength;
    }

    private static string ResolveInitialTitle(string legacyUsername)
    {
        string normalizedLegacyUsername = NormalizeTitle(legacyUsername);
        if (IsValidUsername(normalizedLegacyUsername))
        {
            return normalizedLegacyUsername;
        }

        string generatedTitle = NormalizeTitle(ProfileTitleGenerator.Generate());
        return IsValidTitle(generatedTitle)
            ? generatedTitle
            : "the Bold";
    }

    private static bool ContainsOnlyAllowedTypedDisplayNameCharacters(string typedDisplayName)
    {
        for (int i = 0; i < typedDisplayName.Length; i++)
        {
            char c = typedDisplayName[i];
            if (!char.IsLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetScopedKey(string rawKey)
    {
        return DevClientInstanceScope.ScopePlayerPrefsKey(rawKey);
    }
}
