using System;
using UnityEngine;

public static class LocalPlayerSeatStore
{
    private const string PlayByPostGameIdKey = "pbp_gameId";
    private const string LegacyPlayByPostIsPlayer1Key = "pbp_isPlayer1";
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

        // Backward compatibility for older data that only had one global PBp seat.
        string activeGameId = PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty);
        if (string.Equals(activeGameId, gameId, StringComparison.Ordinal) &&
            PlayerPrefs.HasKey(LegacyPlayByPostIsPlayer1Key))
        {
            bool isPlayer1 = PlayerPrefs.GetInt(LegacyPlayByPostIsPlayer1Key, 1) != 0;
            seatOrPlayerIndex = isPlayer1 ? 0 : 1;
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

        // Keep current-session compatibility with existing PBp flows.
        PlayerPrefs.SetString(PlayByPostGameIdKey, gameId);
        PlayerPrefs.SetInt(LegacyPlayByPostIsPlayer1Key, seat == 0 ? 1 : 0);
        PlayerPrefs.Save();
    }

    private static string BuildSeatKey(string gameId)
    {
        return SeatByGameKeyPrefix + Hash128.Compute(gameId).ToString();
    }

    private static int NormalizeSeat(int seatOrPlayerIndex)
    {
        return seatOrPlayerIndex <= 0 ? 0 : 1;
    }
}
