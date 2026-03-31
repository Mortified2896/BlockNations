using System;
using UnityEngine;

public static class LocalPlayerProfileStore
{
    private const string PlayerIdKey = "profile_player_id";
    private const string UsernameKey = "profile_username";
    private const string TypedDisplayNameKey = "profile_typed_display_name";

    public struct ProfileData
    {
        public string PlayerId;
        public string Username;
        public string TypedDisplayName;

        public ProfileData(string playerId, string username, string typedDisplayName)
        {
            PlayerId = playerId;
            Username = username;
            TypedDisplayName = typedDisplayName;
        }
    }

    public static ProfileData GetOrCreateProfile()
    {
        bool didChange = false;

        string playerId = PlayerPrefs.GetString(PlayerIdKey, string.Empty);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            playerId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(PlayerIdKey, playerId);
            didChange = true;
        }

        string username = PlayerPrefs.GetString(UsernameKey, string.Empty);
        if (!IsValidUsername(username))
        {
            username = ProfileUsernameGenerator.Generate();
            PlayerPrefs.SetString(UsernameKey, username);
            didChange = true;
        }

        string typedDisplayName = NormalizeTypedDisplayName(PlayerPrefs.GetString(TypedDisplayNameKey, string.Empty));
        string storedTypedDisplayName = PlayerPrefs.GetString(TypedDisplayNameKey, string.Empty);
        if (!string.Equals(storedTypedDisplayName, typedDisplayName, StringComparison.Ordinal))
        {
            PlayerPrefs.SetString(TypedDisplayNameKey, typedDisplayName);
            didChange = true;
        }

        if (didChange)
        {
            PlayerPrefs.Save();
        }

        return new ProfileData(playerId, username, typedDisplayName);
    }

    public static ProfileData RegenerateUsername()
    {
        ProfileData profile = GetOrCreateProfile();
        string regenerated = ProfileUsernameGenerator.GenerateDistinct(profile.Username);

        if (!IsValidUsername(regenerated))
        {
            regenerated = ProfileUsernameGenerator.Generate();
        }

        PlayerPrefs.SetString(UsernameKey, regenerated);
        PlayerPrefs.Save();

        profile.Username = regenerated;
        return profile;
    }

    public static string SetTypedDisplayName(string typedDisplayName)
    {
        string normalized = NormalizeTypedDisplayName(typedDisplayName);
        PlayerPrefs.SetString(TypedDisplayNameKey, normalized);
        PlayerPrefs.Save();
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

    private static bool IsValidUsername(string username)
    {
        return !string.IsNullOrWhiteSpace(username) && username.Length <= ProfileUsernameGenerator.MaxUsernameLength;
    }
}
