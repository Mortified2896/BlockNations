using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// The match ID makes orientation and seat assignment reproducible for every
// member initializing the same lobby. No shared Unity random state is consumed.
public static class MultiplayerStartingLayout
{
    public static List<Vector2Int> AssignThreePlayerStarts(List<Vector2Int> anchors, int boardSize, string matchId)
    {
        if (anchors.Count != 3 || string.IsNullOrEmpty(matchId) || matchId.Length < 8 ||
            !uint.TryParse(matchId.Substring(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint seed))
            return anchors;
        var rotated = new List<Vector2Int>(anchors);
        for (int rotation = 0; rotation < seed % 4; rotation++)
            for (int index = 0; index < rotated.Count; index++)
                rotated[index] = new Vector2Int(boardSize - 1 - rotated[index].y, rotated[index].x);
        int[,] orders = { {0,1,2}, {0,2,1}, {1,0,2}, {1,2,0}, {2,0,1}, {2,1,0} };
        int order = (int)((seed / 4) % 6);
        return new List<Vector2Int> { rotated[orders[order,0]], rotated[orders[order,1]], rotated[orders[order,2]] };
    }
}
