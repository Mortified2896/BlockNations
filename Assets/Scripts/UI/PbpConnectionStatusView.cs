using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Shows a small PBp-only connection status banner in gameplay.
/// Inspector wiring:
/// - Assign `statusText` (required) to the label you want to update.
/// - Assign `root` (optional) to the banner root GameObject to show/hide.
/// - Optionally assign `turnManager` and `httpTurnTransport` (auto-resolved if omitted).
/// </summary>
public class PbpConnectionStatusView : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private GameObject root;
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private HttpTurnTransport httpTurnTransport;

    [Header("Checks")]
    [SerializeField, Min(1f)] private float heartbeatSeconds = 60f;
    [SerializeField, Min(0.25f)] private float modeCheckSeconds = 0.5f;
    [SerializeField, Min(0.5f)] private float submitFeedbackSeconds = 3f;

    private const string PlayByPostGameIdKey = "pbp_gameId";
    private const string SubmitFailedFeedback = "Turn not sent. Try again.";

    private enum ServerState
    {
        Checking,
        Connected,
        Unreachable
    }

    private Coroutine modeMonitorRoutine;
    private Coroutine heartbeatRoutine;
    private Coroutine submitFeedbackRoutine;
    private TurnManager subscribedTurnManager;
    private ServerState serverState = ServerState.Checking;
    private bool showSubmitFailureFeedback;

    private void OnEnable()
    {
        TryResolveDependencies();
        TrySubscribeToTurnManagerEvents();
        StartModeMonitor();
    }

    private void OnDisable()
    {
        StopModeMonitor();
        StopHeartbeat();
        StopSubmitFailureFeedback();
        UnsubscribeFromTurnManagerEvents();
    }

    private void StartModeMonitor()
    {
        if (modeMonitorRoutine != null)
        {
            StopCoroutine(modeMonitorRoutine);
        }

        modeMonitorRoutine = StartCoroutine(ModeMonitorLoop());
    }

    private void StopModeMonitor()
    {
        if (modeMonitorRoutine != null)
        {
            StopCoroutine(modeMonitorRoutine);
            modeMonitorRoutine = null;
        }
    }

    private IEnumerator ModeMonitorLoop()
    {
        bool wasPlayByPost = false;

        while (isActiveAndEnabled)
        {
            TryResolveDependencies();
            TrySubscribeToTurnManagerEvents();

            bool isPlayByPost = IsPlayByPostMode();
            if (!isPlayByPost)
            {
                if (wasPlayByPost)
                {
                    StopHeartbeat();
                }

                StopSubmitFailureFeedback();
                wasPlayByPost = false;
                SetVisible(false);
                yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, modeCheckSeconds));
                continue;
            }

            SetVisible(true);
            if (!wasPlayByPost)
            {
                serverState = ServerState.Checking;
                if (httpTurnTransport == null)
                {
                    serverState = ServerState.Unreachable;
                }
                RefreshStatusText();
            }

            if (IsLocalPlayersTurn())
            {
                StartHeartbeatIfNeeded();
            }
            else
            {
                StopHeartbeat();
            }
            wasPlayByPost = true;
            yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, modeCheckSeconds));
        }

        modeMonitorRoutine = null;
    }

    private void StartHeartbeatIfNeeded()
    {
        if (heartbeatRoutine != null)
        {
            return;
        }

        heartbeatRoutine = StartCoroutine(HeartbeatLoop());
    }

    private void StopHeartbeat()
    {
        if (heartbeatRoutine != null)
        {
            StopCoroutine(heartbeatRoutine);
            heartbeatRoutine = null;
        }
    }

    private IEnumerator HeartbeatLoop()
    {
        while (isActiveAndEnabled)
        {
            if (!IsPlayByPostMode() || !IsLocalPlayersTurn())
            {
                break;
            }

            serverState = ServerState.Checking;
            RefreshStatusText();

            TryResolveDependencies();

            bool reachable = false;
            if (httpTurnTransport != null)
            {
                yield return StartCoroutine(httpTurnTransport.CheckServerReachable(result => reachable = result));
            }

            serverState = reachable ? ServerState.Connected : ServerState.Unreachable;
            RefreshStatusText();

            float waitSeconds = Mathf.Max(1f, heartbeatSeconds);
            float elapsed = 0f;
            while (elapsed < waitSeconds && isActiveAndEnabled)
            {
                if (!IsPlayByPostMode() || !IsLocalPlayersTurn())
                {
                    break;
                }

                float step = Mathf.Min(1f, waitSeconds - elapsed);
                yield return new WaitForSecondsRealtime(step);
                elapsed += step;
            }
        }

        heartbeatRoutine = null;
    }

    private void TryResolveDependencies()
    {
        if (turnManager == null)
        {
            turnManager = TurnManager.Instance;
            if (turnManager == null)
            {
                turnManager = FindObjectOfType<TurnManager>();
            }
        }

        if (httpTurnTransport == null && turnManager != null && turnManager.turnTransportComponent is HttpTurnTransport managerHttp)
        {
            httpTurnTransport = managerHttp;
        }

        if (httpTurnTransport == null)
        {
            httpTurnTransport = FindObjectOfType<HttpTurnTransport>();
        }
    }

    private bool IsPlayByPostMode()
    {
        return turnManager != null && turnManager.currentMode == TurnManager.GameMode.PlayByPost;
    }

    private bool IsLocalPlayersTurn()
    {
        if (turnManager == null || turnManager.currentMode != TurnManager.GameMode.PlayByPost)
        {
            return false;
        }

        string gameId = PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty);
        if (string.IsNullOrWhiteSpace(gameId) || !LocalPlayerSeatStore.TryGetSeat(gameId, out int seat))
        {
            // Fallback to Player 1 seat if unavailable.
            return turnManager.isPlayerTurn;
        }

        bool localIsPlayerOne = seat <= 0;
        bool currentSideIsPlayerOne = turnManager.isPlayerTurn;
        return localIsPlayerOne == currentSideIsPlayerOne;
    }

    private void SetVisible(bool visible)
    {
        if (root != null && root != gameObject)
        {
            if (root.activeSelf != visible)
            {
                root.SetActive(visible);
            }
            return;
        }

        if (statusText != null)
        {
            statusText.enabled = visible;
        }
    }

    private void SetStatusText(string message)
    {
        if (statusText != null)
        {
            statusText.text = message ?? string.Empty;
        }
    }

    private void TrySubscribeToTurnManagerEvents()
    {
        if (turnManager == null)
        {
            return;
        }

        if (subscribedTurnManager == turnManager)
        {
            return;
        }

        UnsubscribeFromTurnManagerEvents();
        turnManager.PlayByPostSubmitResult += OnPlayByPostSubmitResult;
        turnManager.PlayByPostFetchResult += OnPlayByPostFetchResult;
        subscribedTurnManager = turnManager;
    }

    private void UnsubscribeFromTurnManagerEvents()
    {
        if (subscribedTurnManager == null)
        {
            return;
        }

        subscribedTurnManager.PlayByPostSubmitResult -= OnPlayByPostSubmitResult;
        subscribedTurnManager.PlayByPostFetchResult -= OnPlayByPostFetchResult;
        subscribedTurnManager = null;
    }

    private void OnPlayByPostSubmitResult(bool ok, string err)
    {
        if (!IsPlayByPostMode())
        {
            return;
        }

        if (ok)
        {
            StopSubmitFailureFeedback();
            serverState = ServerState.Connected;
            RefreshStatusText();
            return;
        }

        if (!IsConnectivityFailure(err))
        {
            return;
        }

        serverState = ServerState.Unreachable;
        StartSubmitFailureFeedback();
        RefreshStatusText();
    }

    private void OnPlayByPostFetchResult(bool reachable, string resultOrError)
    {
        if (!IsPlayByPostMode())
        {
            return;
        }

        if (reachable)
        {
            serverState = ServerState.Connected;
            RefreshStatusText();
            return;
        }

        if (!IsConnectivityFailure(resultOrError))
        {
            return;
        }

        serverState = ServerState.Unreachable;
        RefreshStatusText();
    }

    private void RefreshStatusText()
    {
        if (statusText == null)
        {
            return;
        }

        if (!IsPlayByPostMode())
        {
            SetStatusText(string.Empty);
            return;
        }

        string bannerText = BuildBannerText();
        if (showSubmitFailureFeedback)
        {
            SetStatusText($"{bannerText}\n{SubmitFailedFeedback}");
        }
        else
        {
            SetStatusText(bannerText);
        }
    }

    private string BuildBannerText()
    {
        NetworkReachability reachability = Application.internetReachability;
        if (reachability == NetworkReachability.NotReachable)
        {
            return "No internet connection";
        }

        bool isWifiOrLan = reachability == NetworkReachability.ReachableViaLocalAreaNetwork;
        if (serverState == ServerState.Checking)
        {
            return isWifiOrLan
                ? "Checking server (Wi-Fi/LAN connected)…"
                : "Checking server (Cellular connected)…";
        }

        if (serverState == ServerState.Connected)
        {
            return isWifiOrLan
                ? "Server connected (Wi-Fi/LAN)"
                : "Server connected (Cellular)";
        }

        return isWifiOrLan
            ? "Server unreachable (Wi-Fi/LAN connected)"
            : "Server unreachable (Cellular connected)";
    }

    private void StartSubmitFailureFeedback()
    {
        showSubmitFailureFeedback = true;
        if (submitFeedbackRoutine != null)
        {
            StopCoroutine(submitFeedbackRoutine);
        }

        submitFeedbackRoutine = StartCoroutine(ClearSubmitFeedbackAfterDelay());
    }

    private IEnumerator ClearSubmitFeedbackAfterDelay()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, submitFeedbackSeconds));
        showSubmitFailureFeedback = false;
        submitFeedbackRoutine = null;
        RefreshStatusText();
    }

    private void StopSubmitFailureFeedback()
    {
        showSubmitFailureFeedback = false;
        if (submitFeedbackRoutine != null)
        {
            StopCoroutine(submitFeedbackRoutine);
            submitFeedbackRoutine = null;
        }
    }

    private static bool IsConnectivityFailure(string err)
    {
        if (string.IsNullOrEmpty(err))
        {
            return true;
        }

        return string.Equals(err, TurnTelemetryConstants.IoError, StringComparison.Ordinal) ||
               string.Equals(err, TurnTelemetryConstants.Unavailable, StringComparison.Ordinal) ||
               string.Equals(err, TurnTelemetryConstants.NullTransport, StringComparison.Ordinal) ||
               string.Equals(err, TurnTelemetryConstants.Unknown, StringComparison.Ordinal);
    }
}
