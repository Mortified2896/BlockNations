using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed class HttpTurnTransport : MonoBehaviour, ITurnTransport
{
    [SerializeField]
    [Tooltip("Base URL for the PBp server, e.g. http://127.0.0.1:8080")]
    private string baseUrl = "http://127.0.0.1:8080";

    [SerializeField]
    [Tooltip("UnityWebRequest timeout in seconds (0 = no timeout).")]
    private float timeoutSeconds = 10f;

    private bool initialized;
    private bool isAvailable;
    private string normalizedBaseUrl;

    public string TransportName => "Http";
    public bool IsAvailable => isAvailable;

    public void Initialize()
    {
        if (initialized)
            return;

        initialized = true;
        normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
        isAvailable = !string.IsNullOrWhiteSpace(normalizedBaseUrl);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
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

    private static bool IsValidGameId(string gameId)
    {
        return !string.IsNullOrWhiteSpace(gameId);
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
            req.timeout = GetServerCheckTimeoutSeconds();
            yield return req.SendWebRequest();

            bool reachable = req.responseCode > 0 && req.result != UnityWebRequest.Result.ConnectionError;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"PBp server probe: {url} reachable={reachable} result={req.result} code={req.responseCode}");
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
}
