using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Same-origin requests use the browser's HttpOnly Google-linked game session.
// This client has no access to cookies, OAuth tokens, email addresses or API keys.
public static class WebAccountClient
{
    public const string MatchVersion = "web-pbp-2-core3";
    public static bool SupportsMatchVersion(string version) => version == MatchVersion || version == "web-pbp-1";
    public static string PlayerId { get; private set; } = string.Empty;
    public static string DisplayName { get; private set; } = string.Empty;
    public static bool IsAdmin { get; private set; }
    public static bool CanPlayPoly { get; private set; }
    public static MatchReply PendingMatch { get; private set; }
    public static MatchReply AcceptedMatch { get; private set; }
    public static string LastError { get; private set; }
    private static bool launchConsumed;
    private static bool returnToLobby;

    [Serializable] public sealed class Player { public string id; public string name; public int seat; }
    [Serializable] public sealed class Match {
        public string id; public string version; public string status; public int seatIndex;
        public int seq; public int revision; public int currentTurnSeatIndex; public int seatCount = 2; public bool ready; public Player[] players;
    }
    [Serializable] public sealed class MatchReply {
        public bool ok; public bool canAdminister; public bool canPlayPoly; public string error; public Player player; public Match match; public string json; public int seq;
    }
    [Serializable] private sealed class SnapshotWrite {
        public string json; public int baseSeq;
    }

    public static string Origin => new Uri(Application.absoluteURL).GetLeftPart(UriPartial.Authority);
    public static string LaunchMatchId {
        get {
            if (launchConsumed || !PublicWebBuild.UsesGoogleAccounts) return null;
            var url = new Uri(Application.absoluteURL);
            foreach (string item in url.Query.TrimStart('?').Split('&')) {
                string[] pair = item.Split(new[] { '=' }, 2);
                if (pair.Length == 2 && pair[0] == "match" && System.Text.RegularExpressions.Regex.IsMatch(pair[1], "^[a-f0-9]{32}$"))
                    return pair[1];
            }
            return null;
        }
    }

    public static IEnumerator Initialize(Action<bool> done)
    {
        IsAdmin = false;
        CanPlayPoly = false;
        MatchReply reply = null;
        yield return Request("session", null, value => reply = value);
        if (reply?.ok == true && !string.IsNullOrEmpty(reply.player?.id)) {
            PlayerId = reply.player.id;
            DisplayName = reply.player.name;
            IsAdmin = reply.canAdminister;
            CanPlayPoly = reply.canPlayPoly;
            done(true);
        } else done(false);
    }
    public static void PrepareMatch(MatchReply reply) {
        PendingMatch = reply; AcceptedMatch = reply; launchConsumed = true;
    }
    public static string ConsumeLaunchMatchId() {
        string matchId = LaunchMatchId;
        if (!string.IsNullOrEmpty(matchId)) launchConsumed = true;
        return matchId;
    }
    public static void QueueLobbyReturn() { returnToLobby = true; }
    public static bool ConsumeLobbyReturn() {
        bool pending = returnToLobby; returnToLobby = false; return pending;
    }
    public static void RegisterMenuReceiver(string objectName) {
#if UNITY_WEBGL && !UNITY_EDITOR
        BN_WebRegisterMenu(objectName);
#endif
    }
    public static void RegisterExitReceiver(string objectName) {
#if UNITY_WEBGL && !UNITY_EDITOR
        BN_WebRegisterExit(objectName);
#endif
    }
    public static MatchReply ConsumePendingMatch() {
        MatchReply reply = PendingMatch; PendingMatch = null; return reply;
    }
    public static IEnumerator Write(string matchId, string operation, string json, int baseSeq, Action<MatchReply> done) {
        yield return Request("matches/" + matchId + "/" + operation, JsonUtility.ToJson(new SnapshotWrite { json = json, baseSeq = baseSeq }), done);
    }
    public static IEnumerator Request(string path, string body, Action<MatchReply> done)
    {
        using (var request = new UnityWebRequest(Origin + "/api/multiplayer/" + path, body == null ? "GET" : "POST")) {
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 30;
            request.SetRequestHeader("Accept", "application/json");
            if (body != null) {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("X-BlockNations-Web", "1");
            }
            yield return request.SendWebRequest();
            MatchReply reply = null;
            try { reply = JsonUtility.FromJson<MatchReply>(request.downloadHandler.text); } catch { }
            if (reply == null || request.result == UnityWebRequest.Result.ConnectionError || request.responseCode >= 500)
                reply = new MatchReply { ok = false, error = TurnTelemetryConstants.IoError };
            LastError = reply.ok ? null : reply.error;
            if (reply.ok && reply.match != null) AcceptedMatch = reply;
            done(reply);
        }
    }
    public static void OpenLobby() {
#if UNITY_WEBGL && !UNITY_EDITOR
        BN_WebNavigate("/multiplayer");
#endif
    }
    public static void ShowStatus(string message) {
#if UNITY_WEBGL && !UNITY_EDITOR
        BN_WebStatus(message ?? string.Empty);
#endif
    }
    public static void MatchLoaded(string matchId) {
#if UNITY_WEBGL && !UNITY_EDITOR
        BN_WebMatchLoaded(matchId);
#endif
    }
    public static bool ConsumeMatchUpdate(string matchId) {
#if UNITY_WEBGL && !UNITY_EDITOR
        return BN_WebConsumeMatchUpdate(matchId) != 0;
#else
        return false;
#endif
    }
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void BN_WebNavigate(string path);
    [DllImport("__Internal")] private static extern void BN_WebRegisterMenu(string objectName);
    [DllImport("__Internal")] private static extern void BN_WebRegisterExit(string objectName);
    [DllImport("__Internal")] private static extern void BN_WebStatus(string message);
    [DllImport("__Internal")] private static extern void BN_WebMatchLoaded(string matchId);
    [DllImport("__Internal")] private static extern int BN_WebConsumeMatchUpdate(string matchId);
#endif
}
