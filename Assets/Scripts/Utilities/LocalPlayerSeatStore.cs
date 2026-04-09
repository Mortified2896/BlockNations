using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public static class LocalPlayerSeatStore
{
    private const string SeatByGameKeyPrefix = "pbp_seat_";

    public static bool TryGetSeat(string gameId, out int seatOrPlayerIndex)
    {
        seatOrPlayerIndex = 0;
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        string seatKey = BuildSeatKey(gameId);
        if (PlayerPrefs.HasKey(seatKey))
        {
            int storedSeat = NormalizeSeat(PlayerPrefs.GetInt(seatKey, 0));
            seatOrPlayerIndex = storedSeat;
            return true;
        }

        return false;
    }

    public static void SetSeat(string gameId, int seatOrPlayerIndex)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return;
        }

        int seat = NormalizeSeat(seatOrPlayerIndex);
        PlayerPrefs.SetInt(BuildSeatKey(gameId), seat);
        PlayerPrefs.Save();
    }

    public static bool ClearSeat(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        string seatKey = BuildSeatKey(gameId);
        if (!PlayerPrefs.HasKey(seatKey))
        {
            return false;
        }

        PlayerPrefs.DeleteKey(seatKey);
        return true;
    }

    private static string BuildSeatKey(string gameId)
    {
        return DevClientInstanceScope.ScopePlayerPrefsKey(SeatByGameKeyPrefix + Hash128.Compute(gameId).ToString());
    }

    private static int NormalizeSeat(int seatOrPlayerIndex)
    {
        return Mathf.Clamp(seatOrPlayerIndex, 0, PlayByPostSeatUtility.MaxSeatCount - 1);
    }
}

public static class IosPbpBackgroundNotificationExperiment
{
    private const string EnabledPlayerPrefsKeyRaw = "ios_pbp_background_notification_experiment_enabled";
    private static bool notificationAuthorizationRequested;

    [Serializable]
    private sealed class SyncPayload
    {
        public bool enabled;
        public string baseUrl;
        public string apiKey;
        public WatchedGame[] watchedGames;
    }

    [Serializable]
    private sealed class WatchedGame
    {
        public string gameId;
        public string displayName;
        public int knownSeq;
        public int localSeat;
        public bool knownIsLocalTurn;
    }

    private sealed class RuntimeHook : MonoBehaviour
    {
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            SyncFromCurrentRepoState();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                SyncFromCurrentRepoState();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                SyncFromCurrentRepoState();
            }
        }
    }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void BNBackgroundExperimentSyncState(string json);

    [DllImport("__Internal")]
    private static extern void BNBackgroundExperimentRemoveGame(string gameId);

    [DllImport("__Internal")]
    private static extern void BNRequestBackgroundExperimentNotificationAuthorization();
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeHook()
    {
        RuntimeHook existing = UnityEngine.Object.FindFirstObjectByType<RuntimeHook>();
        if (existing != null)
        {
            return;
        }

        GameObject hookObject = new GameObject(nameof(IosPbpBackgroundNotificationExperiment));
        hookObject.hideFlags = HideFlags.HideAndDontSave;
        hookObject.AddComponent<RuntimeHook>();
    }

    public static bool IsEnabled()
    {
#if UNITY_IOS && !UNITY_EDITOR
        if (PlayerPrefs.HasKey(GetEnabledPlayerPrefsKey()))
        {
            return PlayerPrefs.GetInt(GetEnabledPlayerPrefsKey(), 0) == 1;
        }

#if DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
#else
        return false;
#endif
    }

    public static void SetEnabled(bool enabled)
    {
        PlayerPrefs.SetInt(GetEnabledPlayerPrefsKey(), enabled ? 1 : 0);
        PlayerPrefs.Save();
        SyncFromCurrentRepoState();
    }

    public static void SyncFromCurrentRepoState()
    {
        SyncState(
            SaveManifestService.GetActivePlayByPostGames(),
            UnityEngine.Object.FindFirstObjectByType<HttpTurnTransport>());
    }

    public static void SyncState(
        IReadOnlyList<SaveManifestService.ManifestGameSummary> activeGames,
        HttpTurnTransport httpTransport)
    {
        if (!IsEnabled())
        {
            PushPayloadToNative(BuildDisabledPayload());
            return;
        }

        if (httpTransport == null)
        {
            PushPayloadToNative(BuildDisabledPayload());
            return;
        }

        httpTransport.Initialize();
        string baseUrl = httpTransport.BackgroundExperimentBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl) || !httpTransport.IsAvailable)
        {
            PushPayloadToNative(BuildDisabledPayload());
            return;
        }

        List<WatchedGame> watchedGames = BuildWatchedGames(activeGames);
        if (watchedGames.Count <= 0)
        {
            PushPayloadToNative(new SyncPayload
            {
                enabled = true,
                baseUrl = baseUrl,
                apiKey = HttpTurnTransport.BackgroundExperimentApiKey,
                watchedGames = Array.Empty<WatchedGame>()
            });
            return;
        }

        RequestNotificationAuthorizationIfNeeded();
        PushPayloadToNative(new SyncPayload
        {
            enabled = true,
            baseUrl = baseUrl,
            apiKey = HttpTurnTransport.BackgroundExperimentApiKey,
            watchedGames = watchedGames.ToArray()
        });
    }

    public static void RemoveGame(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return;
        }

#if UNITY_IOS && !UNITY_EDITOR
        BNBackgroundExperimentRemoveGame(gameId);
#endif
    }

    private static void RequestNotificationAuthorizationIfNeeded()
    {
        if (notificationAuthorizationRequested)
        {
            return;
        }

        notificationAuthorizationRequested = true;
#if UNITY_IOS && !UNITY_EDITOR
        BNRequestBackgroundExperimentNotificationAuthorization();
#endif
    }

    private static void PushPayloadToNative(SyncPayload payload)
    {
        string json = JsonUtility.ToJson(payload);
#if UNITY_IOS && !UNITY_EDITOR
        BNBackgroundExperimentSyncState(json);
#endif
    }

    private static SyncPayload BuildDisabledPayload()
    {
        return new SyncPayload
        {
            enabled = false,
            baseUrl = string.Empty,
            apiKey = string.Empty,
            watchedGames = Array.Empty<WatchedGame>()
        };
    }

    private static List<WatchedGame> BuildWatchedGames(IReadOnlyList<SaveManifestService.ManifestGameSummary> activeGames)
    {
        List<WatchedGame> watchedGames = new List<WatchedGame>();
        if (activeGames == null)
        {
            return watchedGames;
        }

        HashSet<string> seenGameIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < activeGames.Count; i++)
        {
            SaveManifestService.ManifestGameSummary summary = activeGames[i];
            if (!IsEligible(summary))
            {
                continue;
            }

            if (!seenGameIds.Add(summary.gameId))
            {
                continue;
            }

            if (!LocalPlayerSeatStore.TryGetSeat(summary.gameId, out int localSeat))
            {
                continue;
            }

            int knownSeq = GetKnownTransportSeq(summary);
            if (knownSeq < 0)
            {
                continue;
            }

            bool localIsPlayerOwned = localSeat == 0;
            watchedGames.Add(new WatchedGame
            {
                gameId = summary.gameId,
                displayName = string.IsNullOrWhiteSpace(summary.displayName)
                    ? PbpGameDisplayNameGenerator.BuildForGameId(summary.gameId)
                    : summary.displayName,
                knownSeq = knownSeq,
                localSeat = localSeat,
                knownIsLocalTurn = summary.lastKnownIsPlayerTurn == localIsPlayerOwned
            });
        }

        return watchedGames;
    }

    private static bool IsEligible(SaveManifestService.ManifestGameSummary summary)
    {
        if (summary.isFinished ||
            string.IsNullOrWhiteSpace(summary.gameId) ||
            !summary.hasLastKnownTurnState)
        {
            return false;
        }

        if (!string.Equals(summary.slotType, "PlayByPost", StringComparison.Ordinal))
        {
            return false;
        }

        return !string.Equals(summary.transportType, "File", StringComparison.Ordinal);
    }

    private static int GetKnownTransportSeq(SaveManifestService.ManifestGameSummary summary)
    {
        if (summary.lastKnownTransportSeq > 0)
        {
            return summary.lastKnownTransportSeq;
        }

        return SaveManifestService.ComputePlayByPostTransportSeq(
            summary.lastKnownRoundTurn,
            summary.lastKnownIsPlayerTurn);
    }

    private static string GetEnabledPlayerPrefsKey()
    {
        return DevClientInstanceScope.ScopePlayerPrefsKey(EnabledPlayerPrefsKeyRaw);
    }
}
