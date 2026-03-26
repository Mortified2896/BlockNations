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

    private enum ServerState
    {
        Checking,
        Connected,
        Unreachable
    }

    private Coroutine modeMonitorRoutine;
    private Coroutine heartbeatRoutine;
    private TurnManager subscribedTurnManager;
    private ServerState serverState = ServerState.Checking;
    private string currentStatusMessage = string.Empty;
    private bool isStatusVisible;


    public string CurrentStatusMessage => currentStatusMessage;
    public bool IsStatusVisible => isStatusVisible;
    public event Action<string, bool> StatusChanged;

    private void OnEnable()
    {
        SyncExposedStatusState();
        TryResolveDependencies();
        TrySubscribeToTurnManagerEvents();
        StartModeMonitor();
    }

    private void OnDisable()
    {
        StopModeMonitor();
        StopHeartbeat();
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

            if (turnManager != null && turnManager.gameOver)
            {
                StopHeartbeat();
                SetStatusText(string.Empty);
                SetVisible(false);
                wasPlayByPost = true;
                yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, modeCheckSeconds));
                continue;
            }

            if (IsLocalPlayersTurn())
            {
                StartHeartbeatIfNeeded();
            }
            else
            {
                StopHeartbeat();
            }

            // Keep the HUD status in sync when PBp turn ownership flips after load/fetch.
            RefreshStatusText();
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
                turnManager = UnityEngine.Object.FindFirstObjectByType<TurnManager>();
            }
        }

        if (httpTurnTransport == null && turnManager != null && turnManager.turnTransportComponent is HttpTurnTransport managerHttp)
        {
            httpTurnTransport = managerHttp;
        }

        if (httpTurnTransport == null)
        {
            httpTurnTransport = UnityEngine.Object.FindFirstObjectByType<HttpTurnTransport>();
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

        // Mirror the same gating used by the Next/End Turn button in PBp.
        return turnManager.CanAdvanceTurn();
    }

    private void SetVisible(bool visible)
    {
        if (root != null && root != gameObject)
        {
            if (root.activeSelf != visible)
            {
                root.SetActive(visible);
            }

            if (isStatusVisible != visible)
            {
                isStatusVisible = visible;
                NotifyStatusChanged();
            }
            return;
        }

        if (statusText != null)
        {
            statusText.enabled = visible;
        }

        if (isStatusVisible != visible)
        {
            isStatusVisible = visible;
            NotifyStatusChanged();
        }
    }

    private void SetStatusText(string message)
    {
        string resolved = message ?? string.Empty;
        if (statusText != null)
        {
            statusText.text = resolved;
        }

        if (!string.Equals(currentStatusMessage, resolved, StringComparison.Ordinal))
        {
            currentStatusMessage = resolved;
            NotifyStatusChanged();
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
            serverState = ServerState.Connected;
            RefreshStatusText();
            return;
        }

        if (!IsConnectivityFailure(err))
        {
            return;
        }

        serverState = ServerState.Unreachable;
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
        if (!IsPlayByPostMode())
        {
            SetStatusText(string.Empty);
            return;
        }

        if (turnManager != null && turnManager.gameOver)
        {
            SetStatusText(string.Empty);
            return;
        }

        bool serverOnline = IsServerOnline();
        bool isYourTurn = IsLocalPlayersTurn();
        SetStatusText(BuildPbpHudStatus(serverOnline, isYourTurn));
    }

    private bool IsServerOnline()
    {
        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            return false;
        }

        return serverState == ServerState.Connected;
    }

    // PBp HUD status matrix:
    // 1) Connected + your turn
    // 2) Connected + waiting for opponent
    // 3) Offline + your turn
    // 4) Offline + waiting for opponent
    private static string BuildPbpHudStatus(bool serverOnline, bool isYourTurn)
    {
        if (serverOnline)
        {
            return isYourTurn
                ? "Connected • Your turn"
                : "Connected • Waiting for opponent";
        }

        return isYourTurn
            ? "Offline • Your turn"
            : "Offline • Waiting for opponent";
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

    private void SyncExposedStatusState()
    {
        currentStatusMessage = statusText != null ? statusText.text ?? string.Empty : string.Empty;
        isStatusVisible = GetCurrentVisibility();
    }

    private bool GetCurrentVisibility()
    {
        if (root != null && root != gameObject)
        {
            return root.activeSelf;
        }

        return statusText != null && statusText.enabled;
    }


    private void NotifyStatusChanged()
    {
        if (StatusChanged == null)
        {
            return;
        }

        try
        {
            StatusChanged.Invoke(currentStatusMessage, isStatusVisible);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }
}
