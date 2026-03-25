using System;
using UnityEngine;

public static class LocalPlayerProfileStore
{
    private const string PlayerIdKey = "profile_player_id";
    private const string UsernameKey = "profile_username";

    public struct ProfileData
    {
        public string PlayerId;
        public string Username;

        public ProfileData(string playerId, string username)
        {
            PlayerId = playerId;
            Username = username;
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

        if (didChange)
        {
            PlayerPrefs.Save();
        }

        return new ProfileData(playerId, username);
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

    private static bool IsValidUsername(string username)
    {
        return !string.IsNullOrWhiteSpace(username) && username.Length <= ProfileUsernameGenerator.MaxUsernameLength;
    }
}
