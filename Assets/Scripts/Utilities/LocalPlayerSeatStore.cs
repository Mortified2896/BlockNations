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
        return SeatByGameKeyPrefix + Hash128.Compute(gameId).ToString();
    }

    private static int NormalizeSeat(int seatOrPlayerIndex)
    {
        return seatOrPlayerIndex <= 0 ? 0 : 1;
    }
}
