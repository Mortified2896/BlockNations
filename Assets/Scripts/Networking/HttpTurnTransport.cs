using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed class HttpTurnTransport : MonoBehaviour, ITurnTransport
{
    private const string ApiKeyHeaderName = "X-BlockNations-Api-Key";
    private const string ApiKeyPlayerPrefsKey = "pbp_api_key";
    private const string ApiKeyEnvVarName = "PBP_SHARED_SECRET";
    private const string DefaultPbpApiKey = "wlrwnDxyIynqTumpdywh_5_5bfIj1wf7RndV_2toTPw";

    [SerializeField]
    [Tooltip("UnityWebRequest timeout in seconds (0 = no timeout).")]
    private float timeoutSeconds = 10f;

    private bool initialized;
    private bool isAvailable;
    private string normalizedBaseUrl;

    public string TransportName => "Http";
    public bool IsAvailable => isAvailable;
    public string EffectiveBaseUrl
    {
        get
        {
            Initialize();
            return normalizedBaseUrl;
        }
    }
    public string BackgroundExperimentBaseUrl => ResolveConfiguredBaseUrl();
    public static string BackgroundExperimentApiKey => GetConfiguredPbpApiKey();
    public static string NormalizeConfiguredBaseUrl(string url) => NormalizeBaseUrl(url);

    public void Initialize()
    {
        if (initialized)
            return;

        initialized = true;
        normalizedBaseUrl = ResolveConfiguredBaseUrl();
        isAvailable = !string.IsNullOrWhiteSpace(normalizedBaseUrl);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        normalizedBaseUrl = ResolveConfiguredBaseUrl();
        isAvailable = !string.IsNullOrWhiteSpace(normalizedBaseUrl);
    }
#endif

    public IEnumerator SubmitTurn(string gameId, int turnNumber, string json, Action<bool, string> done)
    {
        if (!IsValidGameId(gameId))
        {
            done?.Invoke(false, "INVALID_GAME_ID");
            yield break;
        }

        if (turnNumber <= 0)
        {
            done?.Invoke(false, "INVALID_TURN");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            done?.Invoke(false, "EMPTY_JSON");
            yield break;
        }

        if (!IsAvailable)
        {
            done?.Invoke(false, TurnTelemetryConstants.Unavailable);
            yield break;
        }

        // Predictable timing: for non-validation paths, always yield at least once before invoking callback.
        yield return null;

        string url = BuildUrl("pbp/turn");
        var payload = new SubmitRequest
        {
            gameId = gameId,
            seq = turnNumber,
            json = json
        };

        string body = JsonUtility.ToJson(payload);
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            ApplyPbpApiKeyHeader(req);
            req.timeout = GetTimeoutSeconds();

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                done?.Invoke(false, TurnTelemetryConstants.IoError);
                yield break;
            }

            long status = req.responseCode;
            string text = req.downloadHandler != null ? req.downloadHandler.text : null;

            if (status == 200)
            {
                if (TryParseSubmitOk(text))
                {
                    done?.Invoke(true, null);
                }
                else
                {
                    done?.Invoke(false, TurnTelemetryConstants.IoError);
                }
                yield break;
            }

            if (status == 409)
            {
                if (HasError(text, "SEQ_CONFLICT"))
                {
                    done?.Invoke(false, TurnTelemetryConstants.Conflict);
                }
                else
                {
                    done?.Invoke(false, TurnTelemetryConstants.IoError);
                }
                yield break;
            }

            if (status == 400)
            {
                string mapped = MapInvalidSubmitInput(gameId, turnNumber, json);
                done?.Invoke(false, mapped);
                yield break;
            }

            if (status >= 500)
            {
                done?.Invoke(false, TurnTelemetryConstants.IoError);
                yield break;
            }

            done?.Invoke(false, TurnTelemetryConstants.IoError);
        }
    }

    public IEnumerator TryFetchNextTurn(string gameId, int afterTurnNumber, Action<bool, string, int, string> done)
    {
        if (!IsValidGameId(gameId))
        {
            done?.Invoke(false, "INVALID_GAME_ID", 0, null);
            yield break;
        }

        if (afterTurnNumber < -1)
        {
            done?.Invoke(false, "INVALID_TURN", 0, null);
            yield break;
        }

        if (!IsAvailable)
        {
            done?.Invoke(false, TurnTelemetryConstants.Unavailable, 0, null);
            yield break;
        }

        // Predictable timing: for non-validation paths, always yield at least once before invoking callback.
        yield return null;

        string url = BuildUrl($"pbp/turn/next?gameId={Uri.EscapeDataString(gameId)}&after={afterTurnNumber}");

        using (var req = UnityWebRequest.Get(url))
        {
            ApplyPbpApiKeyHeader(req);
            req.timeout = GetTimeoutSeconds();

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                done?.Invoke(false, TurnTelemetryConstants.IoError, 0, null);
                yield break;
            }

            long status = req.responseCode;
            string text = req.downloadHandler != null ? req.downloadHandler.text : null;

            if (status == 200)
            {
                if (IsPlainNoTurn(text) || HasError(text, "NO_TURN"))
                {
                    done?.Invoke(false, TurnTelemetryConstants.NoTurn, 0, null);
                    yield break;
                }

                if (TryParseFetchOk(text, out int seq, out string json))
                {
                    done?.Invoke(true, null, seq, json);
                }
                else
                {
                    done?.Invoke(false, TurnTelemetryConstants.IoError, 0, null);
                }
                yield break;
            }

            if (status == 400)
            {
                string mapped = MapInvalidFetchInput(gameId, afterTurnNumber);
                done?.Invoke(false, mapped, 0, null);
                yield break;
            }

            if (status >= 500)
            {
                done?.Invoke(false, TurnTelemetryConstants.IoError, 0, null);
                yield break;
            }

            done?.Invoke(false, TurnTelemetryConstants.IoError, 0, null);
        }
    }

    public IEnumerator CheckServerReachable(Action<bool> done)
    {
        Initialize();

        if (!IsAvailable)
        {
            done?.Invoke(false);
            yield break;
        }

        bool reachable = false;

        // Prefer a dedicated health endpoint if the server has one.
        yield return ProbeReachability(BuildUrl("health"), ok => reachable = ok);

        // Fallback to a safe read endpoint to avoid requiring /health support.
        if (!reachable)
        {
            string probeGameId = Guid.NewGuid().ToString();
            string probeUrl = BuildUrl($"pbp/turn/next?gameId={Uri.EscapeDataString(probeGameId)}&after=0");
            yield return ProbeReachability(probeUrl, ok => reachable = ok);
        }

        done?.Invoke(reachable);
    }

    public readonly struct TurnStatusQuery
    {
        public readonly string GameId;
        public readonly int KnownSeq;

        public TurnStatusQuery(string gameId, int knownSeq)
        {
            GameId = gameId;
            KnownSeq = knownSeq;
        }
    }

    public readonly struct TurnStatusItem
    {
        public readonly string GameId;
        public readonly int KnownSeq;
        public readonly bool HasAnyTurn;
        public readonly int LatestSeq;
        public readonly int NextSeqAfterKnown;
        public readonly bool HasNewerThanKnown;
        public readonly int TurnSeat;

        public TurnStatusItem(
            string gameId,
            int knownSeq,
            bool hasAnyTurn,
            int latestSeq,
            int nextSeqAfterKnown,
            bool hasNewerThanKnown,
            int turnSeat)
        {
            GameId = gameId;
            KnownSeq = knownSeq;
            HasAnyTurn = hasAnyTurn;
            LatestSeq = latestSeq;
            NextSeqAfterKnown = nextSeqAfterKnown;
            HasNewerThanKnown = hasNewerThanKnown;
            TurnSeat = turnSeat;
        }
    }

    public IEnumerator FetchTurnStatuses(TurnStatusQuery[] games, Action<bool, string, TurnStatusItem[]> done)
    {
        if (games == null || games.Length == 0)
        {
            done?.Invoke(false, "INVALID_INPUT", null);
            yield break;
        }

        for (int i = 0; i < games.Length; i++)
        {
            string gameId = games[i].GameId;
            int knownSeq = games[i].KnownSeq;

            if (!IsValidGameId(gameId))
            {
                done?.Invoke(false, "INVALID_GAME_ID", null);
                yield break;
            }

            if (!IsValidKnownSeq(knownSeq))
            {
                done?.Invoke(false, "INVALID_TURN", null);
                yield break;
            }
        }

        if (!IsAvailable)
        {
            done?.Invoke(false, TurnTelemetryConstants.Unavailable, null);
            yield break;
        }

        yield return null;

        string url = BuildUrl("pbp/turn/status");
        var payload = new TurnStatusRequest
        {
            games = new TurnStatusRequestItem[games.Length]
        };

        for (int i = 0; i < games.Length; i++)
        {
            payload.games[i] = new TurnStatusRequestItem
            {
                gameId = games[i].GameId,
                knownSeq = games[i].KnownSeq
            };
        }

        string body = JsonUtility.ToJson(payload);
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            ApplyPbpApiKeyHeader(req);
            req.timeout = GetTimeoutSeconds();

            yield return req.SendWebRequest();

            long status = req.responseCode;
            bool hasHttpResponse = status > 0;
            if (req.result == UnityWebRequest.Result.ConnectionError ||
                req.result == UnityWebRequest.Result.DataProcessingError ||
                (!hasHttpResponse && req.result != UnityWebRequest.Result.Success))
            {
                done?.Invoke(false, TurnTelemetryConstants.IoError, null);
                yield break;
            }

            string text = req.downloadHandler != null ? req.downloadHandler.text : null;

            if (status == 200)
            {
                if (TryParseStatusOk(text, out TurnStatusItem[] items))
                {
                    done?.Invoke(true, null, items);
                }
                else
                {
                    done?.Invoke(false, TurnTelemetryConstants.IoError, null);
                }
                yield break;
            }

            if (status == 400)
            {
                done?.Invoke(false, "INVALID_INPUT", null);
                yield break;
            }

            if (status == 401)
            {
                done?.Invoke(false, "UNAUTHORIZED", null);
                yield break;
            }

            if (status == 429)
            {
                done?.Invoke(false, "RATE_LIMITED", null);
                yield break;
            }

            if (status >= 500)
            {
                done?.Invoke(false, TurnTelemetryConstants.IoError, null);
                yield break;
            }

            done?.Invoke(false, TurnTelemetryConstants.IoError, null);
        }
    }

    public IEnumerator ClaimSeat(
        string gameId,
        string playerId,
        string typedDisplayName,
        Action<bool, string, int, bool> done)
    {
        if (!IsValidGameId(gameId) || string.IsNullOrWhiteSpace(playerId))
        {
            done?.Invoke(false, "INVALID_INPUT", 0, false);
            yield break;
        }

        if (!IsAvailable)
        {
            done?.Invoke(false, TurnTelemetryConstants.Unavailable, 0, false);
            yield break;
        }

        yield return null;

        string url = BuildUrl("pbp/game/claim");
        var payload = new SeatClaimRequest
        {
            gameId = gameId,
            playerId = playerId,
            typedDisplayName = LocalPlayerProfileStore.NormalizeTypedDisplayName(typedDisplayName)
        };

        string body = JsonUtility.ToJson(payload);
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            ApplyPbpApiKeyHeader(req);
            req.timeout = GetTimeoutSeconds();

            yield return req.SendWebRequest();

            long status = req.responseCode;
            string text = req.downloadHandler != null ? req.downloadHandler.text : null;

            if (req.result == UnityWebRequest.Result.ConnectionError ||
                req.result == UnityWebRequest.Result.DataProcessingError ||
                (status <= 0 && req.result != UnityWebRequest.Result.Success))
            {
                done?.Invoke(false, TurnTelemetryConstants.IoError, 0, false);
                yield break;
            }

            if (status == 200)
            {
                if (TryParseSeatClaimOk(text, out int seatIndex, out bool alreadyClaimed))
                {
                    done?.Invoke(true, null, seatIndex, alreadyClaimed);
                }
                else
                {
                    done?.Invoke(false, TurnTelemetryConstants.IoError, 0, false);
                }
                yield break;
            }

            if (status == 400)
            {
                done?.Invoke(false, "INVALID_INPUT", 0, false);
                yield break;
            }

            if (status == 401)
            {
                done?.Invoke(false, "UNAUTHORIZED", 0, false);
                yield break;
            }

            if (status == 409)
            {
                if (HasError(text, "GAME_FULL"))
                {
                    done?.Invoke(false, "GAME_FULL", 0, false);
                }
                else
                {
                    done?.Invoke(false, TurnTelemetryConstants.IoError, 0, false);
                }
                yield break;
            }

            if (status >= 500)
            {
                done?.Invoke(false, TurnTelemetryConstants.IoError, 0, false);
                yield break;
            }

            done?.Invoke(false, TurnTelemetryConstants.IoError, 0, false);
        }
    }

    private static bool IsValidGameId(string gameId)
    {
        return !string.IsNullOrWhiteSpace(gameId);
    }

    private static bool IsValidKnownSeq(int knownSeq)
    {
        if (knownSeq < -1)
        {
            return false;
        }

        if (knownSeq >= 0 && knownSeq.ToString().Length > 12)
        {
            return false;
        }

        return true;
    }

    private static string GetConfiguredPbpApiKey()
    {
        string fromEnv = Environment.GetEnvironmentVariable(ApiKeyEnvVarName);
        if (!string.IsNullOrEmpty(fromEnv))
        {
            return fromEnv;
        }

        string fromPrefs = PlayerPrefs.GetString(ApiKeyPlayerPrefsKey, string.Empty);
        if (!string.IsNullOrEmpty(fromPrefs))
        {
            return fromPrefs;
        }

        return DefaultPbpApiKey;
    }

    private static void ApplyPbpApiKeyHeader(UnityWebRequest req)
    {
        if (req == null)
            return;

        string apiKey = GetConfiguredPbpApiKey();
        if (string.IsNullOrEmpty(apiKey))
            return;

        req.SetRequestHeader(ApiKeyHeaderName, apiKey);
    }

    private static string NormalizeBaseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        string trimmed = url.Trim();
        while (trimmed.EndsWith("/", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1);
        }

        return trimmed.Length == 0 ? null : trimmed;
    }

    private string ResolveConfiguredBaseUrl()
    {
        PbpDebugSettings sharedSettings = Resources.Load<PbpDebugSettings>("PbpDebugSettings");
        return sharedSettings != null
            ? NormalizeBaseUrl(sharedSettings.playByPostBaseUrl)
            : null;
    }

    private string BuildUrl(string path)
    {
        if (string.IsNullOrWhiteSpace(normalizedBaseUrl))
            return null;

        if (string.IsNullOrEmpty(path))
            return normalizedBaseUrl;

        if (path[0] == '/')
            path = path.Substring(1);

        return $"{normalizedBaseUrl}/{path}";
    }

    private int GetTimeoutSeconds()
    {
        if (timeoutSeconds <= 0f)
            return 0;

        int seconds = Mathf.CeilToInt(timeoutSeconds);
        return Mathf.Clamp(seconds, 1, 120);
    }

    private int GetServerCheckTimeoutSeconds()
    {
        int configured = GetTimeoutSeconds();
        if (configured <= 0)
            return 3;

        return Mathf.Clamp(configured, 1, 3);
    }

    private IEnumerator ProbeReachability(string url, Action<bool> done)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            done?.Invoke(false);
            yield break;
        }

        using (var req = UnityWebRequest.Get(url))
        {
            ApplyPbpApiKeyHeader(req);
            req.timeout = GetServerCheckTimeoutSeconds();
            yield return req.SendWebRequest();

            bool reachable = req.responseCode > 0 && req.result != UnityWebRequest.Result.ConnectionError;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (PbpDebugSettingsLoader.EnableTransportLogs)
            {
                Debug.Log($"PBp server probe: {url} reachable={reachable} result={req.result} code={req.responseCode}");
            }
#endif

            done?.Invoke(reachable);
        }
    }

    private static string MapInvalidSubmitInput(string gameId, int turnNumber, string json)
    {
        if (!IsValidGameId(gameId))
            return "INVALID_GAME_ID";
        if (turnNumber <= 0)
            return "INVALID_TURN";
        if (string.IsNullOrWhiteSpace(json))
            return "EMPTY_JSON";
        return "IO_ERROR";
    }

    private static string MapInvalidFetchInput(string gameId, int afterTurnNumber)
    {
        if (!IsValidGameId(gameId))
            return "INVALID_GAME_ID";
        if (afterTurnNumber < -1)
            return "INVALID_TURN";
        return "IO_ERROR";
    }

    private static bool IsPlainNoTurn(string text)
    {
        return string.Equals(text != null ? text.Trim() : null, TurnTelemetryConstants.NoTurn, StringComparison.Ordinal);
    }

    private static bool TryParseSubmitOk(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        SubmitResponse resp;
        try
        {
            resp = JsonUtility.FromJson<SubmitResponse>(text);
        }
        catch
        {
            return false;
        }

        return resp != null && resp.ok;
    }

    private static bool TryParseFetchOk(string text, out int seq, out string json)
    {
        seq = 0;
        json = null;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        FetchResponse resp;
        try
        {
            resp = JsonUtility.FromJson<FetchResponse>(text);
        }
        catch
        {
            return false;
        }

        if (resp == null || resp.seq <= 0 || string.IsNullOrWhiteSpace(resp.json))
            return false;

        seq = resp.seq;
        json = resp.json;
        return true;
    }

    private static bool HasError(string text, string error)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        ErrorResponse resp;
        try
        {
            resp = JsonUtility.FromJson<ErrorResponse>(text);
        }
        catch
        {
            return false;
        }

        return resp != null && string.Equals(resp.error, error, StringComparison.Ordinal);
    }

    private static bool TryParseStatusOk(string text, out TurnStatusItem[] items)
    {
        items = null;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        TurnStatusResponse response;
        try
        {
            response = JsonUtility.FromJson<TurnStatusResponse>(text);
        }
        catch
        {
            return false;
        }

        if (response == null || !response.ok || response.games == null)
            return false;

        items = new TurnStatusItem[response.games.Length];
        for (int i = 0; i < response.games.Length; i++)
        {
            TurnStatusResponseItem g = response.games[i];
            if (g == null || !IsValidGameId(g.gameId) || !IsValidKnownSeq(g.knownSeq))
                return false;

            items[i] = new TurnStatusItem(
                g.gameId,
                g.knownSeq,
                g.hasAnyTurn,
                g.latestSeq,
                g.nextSeqAfterKnown,
                g.hasNewerThanKnown,
                g.turnSeat);
        }

        return true;
    }

    private static bool TryParseSeatClaimOk(string text, out int seatIndex, out bool alreadyClaimed)
    {
        seatIndex = 0;
        alreadyClaimed = false;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        SeatClaimResponse response;
        try
        {
            response = JsonUtility.FromJson<SeatClaimResponse>(text);
        }
        catch
        {
            return false;
        }

        if (response == null || !response.ok)
        {
            return false;
        }

        seatIndex = PlayByPostSeatUtility.NormalizeSeatIndex(response.seatIndex);
        alreadyClaimed = response.alreadyClaimed;
        return true;
    }

    [Serializable]
    private class SubmitRequest
    {
        public string gameId;
        public int seq;
        public string json;
    }

    [Serializable]
    private class SubmitResponse
    {
        public bool ok;
        public bool alreadyHad;
        public string error;
    }

    [Serializable]
    private class FetchResponse
    {
        public int seq;
        public string json;
        public string error;
        public bool ok;
    }

    [Serializable]
    private class ErrorResponse
    {
        public string error;
    }

    [Serializable]
    private class TurnStatusRequest
    {
        public TurnStatusRequestItem[] games;
    }

    [Serializable]
    private class SeatClaimRequest
    {
        public string gameId;
        public string playerId;
        public string typedDisplayName;
    }

    [Serializable]
    private class SeatClaimResponse
    {
        public bool ok;
        public string error;
        public int seatIndex;
        public bool alreadyClaimed;
    }

    [Serializable]
    private class TurnStatusRequestItem
    {
        public string gameId;
        public int knownSeq;
    }

    [Serializable]
    private class TurnStatusResponse
    {
        public bool ok;
        public TurnStatusResponseItem[] games;
    }

    [Serializable]
    private class TurnStatusResponseItem
    {
        public string gameId;
        public int knownSeq;
        public bool hasAnyTurn;
        public int latestSeq;
        public int nextSeqAfterKnown;
        public bool hasNewerThanKnown;
        public int turnSeat;
    }
}
