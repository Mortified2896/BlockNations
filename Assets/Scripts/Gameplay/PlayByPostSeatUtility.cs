using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class PlayByPostSeatMetadata
{
    public int seatIndex;
    public string state = PlayByPostSeatUtility.SeatStateUnclaimed;
    public string claimedPlayerId = string.Empty;
    public string typedDisplayName = string.Empty;
}

public static class PlayByPostSeatUtility
{
    public const int MinSeatCount = 2;
    public const int MaxSeatCount = 4;
    public const string SeatStateUnclaimed = "Unclaimed";
    public const string SeatStateActive = "Active";
    public const string SeatStateEliminated = "Eliminated";
    public const string SeatStateResigned = "Resigned";

    public static int NormalizeSeatCount(int seatCount)
    {
        if (seatCount <= 0)
        {
            return MinSeatCount;
        }

        return Mathf.Clamp(seatCount, MinSeatCount, MaxSeatCount);
    }

    public static int NormalizeSeatIndex(int seatIndex, int seatCount = MaxSeatCount)
    {
        int normalizedSeatCount = NormalizeSeatCount(seatCount);
        return Mathf.Clamp(seatIndex, 0, normalizedSeatCount - 1);
    }

    public static string NormalizeSeatState(string state)
    {
        if (string.Equals(state, SeatStateActive, StringComparison.Ordinal))
        {
            return SeatStateActive;
        }

        if (string.Equals(state, SeatStateEliminated, StringComparison.Ordinal))
        {
            return SeatStateEliminated;
        }

        if (string.Equals(state, SeatStateResigned, StringComparison.Ordinal))
        {
            return SeatStateResigned;
        }

        return SeatStateUnclaimed;
    }

    public static string BuildPlayerLabel(int seatIndex)
    {
        return $"Player {NormalizeSeatIndex(seatIndex) + 1}";
    }

    public static string ResolveSeatDisplayNameOrFallback(int seatIndex, string typedDisplayName)
    {
        string normalized = LocalPlayerProfileStore.NormalizeTypedDisplayName(typedDisplayName);
        return string.IsNullOrWhiteSpace(normalized)
            ? BuildPlayerLabel(seatIndex)
            : normalized;
    }

    public static bool IsSeatEligibleForTurnProgression(PlayByPostSeatMetadata seatMetadata)
    {
        string normalizedState = NormalizeSeatState(seatMetadata != null ? seatMetadata.state : null);
        return !string.Equals(normalizedState, SeatStateResigned, StringComparison.Ordinal) &&
               !string.Equals(normalizedState, SeatStateEliminated, StringComparison.Ordinal);
    }

    public static int ResolveEffectiveWaitingSeatIndex(
        IReadOnlyList<PlayByPostSeatMetadata> seats,
        int currentTurnSeatIndex,
        int seatCount)
    {
        if (currentTurnSeatIndex < 0)
        {
            return -1;
        }

        int normalizedSeatCount = NormalizeSeatCount(seatCount);
        int normalizedCurrentSeatIndex = NormalizeSeatIndex(currentTurnSeatIndex, normalizedSeatCount);
        PlayByPostSeatMetadata currentSeatMetadata =
            seats != null && normalizedCurrentSeatIndex < seats.Count
                ? seats[normalizedCurrentSeatIndex]
                : null;
        if (IsSeatEligibleForTurnProgression(currentSeatMetadata))
        {
            return normalizedCurrentSeatIndex;
        }

        for (int step = 1; step <= normalizedSeatCount; step++)
        {
            int candidateSeatIndex = (normalizedCurrentSeatIndex + step) % normalizedSeatCount;
            PlayByPostSeatMetadata candidateSeatMetadata =
                seats != null && candidateSeatIndex < seats.Count
                    ? seats[candidateSeatIndex]
                    : null;
            if (IsSeatEligibleForTurnProgression(candidateSeatMetadata))
            {
                return candidateSeatIndex;
            }
        }

        return normalizedCurrentSeatIndex;
    }
}
