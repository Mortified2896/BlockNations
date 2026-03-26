using System;

/// <summary>
/// Small helper that decides whether the PBp top-HUD status should be visible
/// and what text it should show.
///
/// Intent:
/// - keep UITK Top HUD independent from legacy PBpConnectionStatusView
/// - keep formatting/policy in one place
/// - derive from authoritative gameplay/runtime state only
///
/// Current behavior:
/// - hidden outside Play-by-Post
/// - hidden during game over
/// - shows "Your turn" when local player can act
/// - shows "Waiting for opponent" when local player is waiting
/// - if an opponent name is available later, can show "Waiting for {Name}"
/// - optional connectivity prefix can be added when that state is available
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
    /// Computes the PBp status line for the gameplay Top HUD.
    ///
    /// Notes:
    /// - This method intentionally does NOT derive match metadata such as opponent
    ///   display name from save/network state. Pass it in if/when you have it.
    /// - ConnectivityState.Unknown avoids pretending to know connected/offline state
    ///   if no reliable source is available yet.
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
            ConnectivityState.Connected => $"Connected • {baseText}",
            ConnectivityState.Unreachable => $"Offline • {baseText}",
            _ => baseText
        };
    }
}