using System;

/// <summary>
/// Small helper that decides whether the PBp top-HUD status should be visible
/// and what text it should show.
///
/// Intent:
/// - keep UITK Top HUD independent from legacy PBpConnectionStatusView
/// - keep formatting/policy in one place
/// - derive from authoritative gameplay/runtime state only
/// - optional offline warning prefix can be added when that state is available
/// </summary>
public static class PbpTopHudStatusProvider
{
    public enum ConnectivityState
    {
        Unknown,
        Connected,
        Unreachable
    }

    public readonly struct StatusResult
    {
        public readonly bool Visible;
        public readonly string Message;

        public StatusResult(bool visible, string message)
        {
            Visible = visible;
            Message = message ?? string.Empty;
        }

        public static StatusResult Hidden => new StatusResult(false, string.Empty);
    }

    /// <summary>
    /// Build the status result to show on the top HUD.
    /// 
    /// - ConnectivityState.Unknown keeps the normal case clean with no prefix.
    /// - ConnectivityState.Unreachable adds an offline warning prefix.
    /// </summary>
    public static StatusResult Build(
        TurnManager turnManager,
        ConnectivityState connectivityState = ConnectivityState.Unknown,
        string opponentDisplayName = null)
    {
        if (turnManager == null)
        {
            return StatusResult.Hidden;
        }

        if (turnManager.currentMode != TurnManager.GameMode.PlayByPost)
        {
            return StatusResult.Hidden;
        }

        if (turnManager.gameOver)
        {
            return StatusResult.Hidden;
        }

        bool isLocalPlayersTurn = turnManager.CanAdvanceTurn();

        string turnOwnershipText = isLocalPlayersTurn
            ? "Your turn"
            : BuildWaitingText(opponentDisplayName);

        string message = PrependConnectivity(turnOwnershipText, connectivityState);
        return new StatusResult(!string.IsNullOrWhiteSpace(message), message);
    }

    private static string BuildWaitingText(string opponentDisplayName)
    {
        if (!string.IsNullOrWhiteSpace(opponentDisplayName))
        {
            return $"Waiting for {opponentDisplayName.Trim()}";
        }

        return "Waiting for opponent";
    }

    private static string PrependConnectivity(string baseText, ConnectivityState connectivityState)
    {
        if (string.IsNullOrWhiteSpace(baseText))
        {
            return string.Empty;
        }

        return connectivityState switch
        {
            ConnectivityState.Unreachable => $"Offline • {baseText}",
            _ => baseText
        };
    }
}