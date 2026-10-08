using System;
using System.Collections.Generic;

namespace BlockNations.Simulation
{
    public static class MatchPaths
    {
        private static readonly int[] OffsetX = { 1, 0, -1, 0, 1, 1, -1, -1 };
        private static readonly int[] OffsetY = { 0, 1, 0, -1, 1, -1, 1, -1 };

        // The same path routine accepts an authoritative world or a search belief.
        public static Dictionary<int, int[]> Build(int width, int height, bool[] tiles, int origin, int range, Func<int, bool> blocked)
        {
            var paths = new Dictionary<int, int[]>();
            if (range <= 0) return paths;
            if (width < 1 || height < 1 || tiles == null || tiles.Length != width * height || origin < 0 || origin >= tiles.Length || !tiles[origin])
                throw new ArgumentException("Invalid path board/origin.");
            int[] previous = new int[tiles.Length], distance = new int[tiles.Length];
            for (int i = 0; i < distance.Length; i++) distance[i] = -1;
            var frontier = new Queue<int>(); frontier.Enqueue(origin); distance[origin] = 0;
            while (frontier.Count > 0)
            {
                int position = frontier.Dequeue(), x = position % width, y = position / width;
                if (distance[position] >= range) continue;
                for (int i = 0; i < OffsetX.Length; i++)
                {
                    int nx = x + OffsetX[i], ny = y + OffsetY[i];
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    int next = ny * width + nx;
                    if (!tiles[next] || distance[next] >= 0 || (blocked != null && blocked(next))) continue;
                    distance[next] = distance[position] + 1; previous[next] = position; frontier.Enqueue(next);
                    var path = new int[distance[next]]; int step = next;
                    for (int index = path.Length - 1; index >= 0; index--) { path[index] = step; step = previous[step]; }
                    paths.Add(next, path);
                }
            }
            return paths;
        }
    }
}
