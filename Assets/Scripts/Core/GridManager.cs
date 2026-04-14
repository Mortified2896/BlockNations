using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    public const int MinSupportedBoardSize = 2;
    private const int StartingCityEdgeInset = 1;
    private const string PlayByPostGameIdKeyRaw = "pbp_gameId";
    private const string PlayByPostPerGameSaveFolderName = "pbp";
    private const string PlayByPostPerGameSavePrefix = "pbp_";
    private static string PlayByPostGameIdKey => DevClientInstanceScope.ScopePlayerPrefsKey(PlayByPostGameIdKeyRaw);

    [Header("Grid Settings")]
    public int width = 15;      // Number of tiles in X direction
    public int height = 15;     // Number of tiles in Y direction
    public float tileSize = 1f; // Distance between tile centers

    [Header("References")]
    public GameObject tilePrefab;   // Prefab for a single tile

    [Header("City Settings")]
    public GameObject cityPrefab;   // Prefab for a city icon

    [HideInInspector]
    public TileVisibility[,] tileGrid;

    private float originX;
    private float originY;

    [Serializable]
    private sealed class MinimalPlayByPostHeader
    {
        public string mode;
        public int seatCount = PlayByPostSeatUtility.MinSeatCount;
    }

    void Start()
    {
        int initialWidth = width;
        int initialHeight = height;
        if (!SaveLoadRequest.HasPendingRequest &&
            MapSizeSelection.TryPeek(out TurnManager.MapSizePreset pendingMapSize))
        {
            TurnManager.GetBoardDimensionsForPreset(pendingMapSize, out initialWidth, out initialHeight);
        }

        RebuildGrid(initialWidth, initialHeight, recalculateVisibility: true);
    }

    public bool HasDimensions(int targetWidth, int targetHeight)
    {
        return width == targetWidth && height == targetHeight;
    }

    public void RebuildGrid(int targetWidth, int targetHeight, bool recalculateVisibility = false)
    {
        width = Mathf.Max(MinSupportedBoardSize, targetWidth);
        height = Mathf.Max(MinSupportedBoardSize, targetHeight);

        ClearGeneratedBoard();

        tileGrid = new TileVisibility[width, height];
        GenerateGrid();
        SpawnStartingCities();

        if (recalculateVisibility && TurnManager.Instance != null)
        {
            TurnManager.Instance.RecalculatePlayerVisibility();
        }
    }

    private void ClearGeneratedBoard()
    {
        for (int index = transform.childCount - 1; index >= 0; index--)
        {
            Transform child = transform.GetChild(index);
            if (child == null)
            {
                continue;
            }

            child.gameObject.SetActive(false);
            child.SetParent(null);
            Destroy(child.gameObject);
        }
    }

    private void SpawnStartingCities()
    {
        if (cityPrefab != null)
        {
            int seatCount = ResolveStartingSeatCount();
            List<Vector2Int> desiredAnchors = BuildDesiredStartingAnchors(seatCount);
            HashSet<int> claimedTiles = new HashSet<int>();
            for (int seatIndex = 0; seatIndex < desiredAnchors.Count; seatIndex++)
            {
                Vector2Int resolvedAnchor = ResolveNearestAvailableStartTile(desiredAnchors[seatIndex], claimedTiles);
                claimedTiles.Add(FlattenTileIndex(resolvedAnchor.x, resolvedAnchor.y));
                SpawnCity(resolvedAnchor.x, resolvedAnchor.y, seatIndex);
            }
        }
        else
        {
            Debug.LogWarning("City prefab not assigned on GridManager, no cities spawned.");
        }
    }

    private int ResolveStartingSeatCount()
    {
        if (GameModeSelection.TryPeek(out TurnManager.GameMode pendingMode))
        {
            if (pendingMode != TurnManager.GameMode.PlayByPost)
            {
                return PlayByPostSeatUtility.MinSeatCount;
            }

            if (PlayByPostSeatCountSelection.TryPeek(out int pendingSeatCount))
            {
                return PlayByPostSeatUtility.NormalizeSeatCount(pendingSeatCount);
            }
        }

        if (TryReadActivePlayByPostSeatCount(out int seatCount))
        {
            return seatCount;
        }

        return PlayByPostSeatUtility.MinSeatCount;
    }

    private bool TryReadActivePlayByPostSeatCount(out int seatCount)
    {
        seatCount = PlayByPostSeatUtility.MinSeatCount;

        string gameId = PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty);
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        string snapshotPath = GetPlayByPostSnapshotPath(gameId);
        if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(snapshotPath);
            MinimalPlayByPostHeader header = JsonUtility.FromJson<MinimalPlayByPostHeader>(json);
            if (header == null ||
                !string.Equals(header.mode, TurnManager.GameMode.PlayByPost.ToString(), StringComparison.Ordinal))
            {
                return false;
            }

            seatCount = PlayByPostSeatUtility.NormalizeSeatCount(header.seatCount);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string GetPlayByPostSnapshotPath(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return null;
        }

        string safeGameId = SanitizeGameIdForFileName(gameId);
        string directory = Path.Combine(
            DevClientInstanceScope.GetScopedPersistentDataPath(),
            PlayByPostPerGameSaveFolderName);
        return Path.Combine(directory, $"{PlayByPostPerGameSavePrefix}{safeGameId}.json");
    }

    private static string SanitizeGameIdForFileName(string gameId)
    {
        if (string.IsNullOrEmpty(gameId))
        {
            return string.Empty;
        }

        char[] chars = gameId.ToCharArray();
        char[] invalidChars = Path.GetInvalidFileNameChars();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalidChars, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private List<Vector2Int> BuildDesiredStartingAnchors(int seatCount)
    {
        int normalizedSeatCount = PlayByPostSeatUtility.NormalizeSeatCount(seatCount);
        int left = Mathf.Clamp(StartingCityEdgeInset, 0, Mathf.Max(0, width - 1));
        int right = Mathf.Clamp(width - 1 - StartingCityEdgeInset, 0, Mathf.Max(0, width - 1));
        int bottom = Mathf.Clamp(StartingCityEdgeInset, 0, Mathf.Max(0, height - 1));
        int top = Mathf.Clamp(height - 1 - StartingCityEdgeInset, 0, Mathf.Max(0, height - 1));
        int centerX = Mathf.Clamp(width / 2, 0, Mathf.Max(0, width - 1));

        var anchors = new List<Vector2Int>(normalizedSeatCount);
        switch (normalizedSeatCount)
        {
            case 3:
                anchors.Add(new Vector2Int(centerX, bottom));
                anchors.Add(new Vector2Int(right, top));
                anchors.Add(new Vector2Int(left, top));
                break;

            case 4:
                anchors.Add(new Vector2Int(left, bottom));
                anchors.Add(new Vector2Int(right, top));
                anchors.Add(new Vector2Int(left, top));
                anchors.Add(new Vector2Int(right, bottom));
                break;

            default:
                anchors.Add(new Vector2Int(left, bottom));
                anchors.Add(new Vector2Int(right, top));
                break;
        }

        return anchors;
    }

    private Vector2Int ResolveNearestAvailableStartTile(Vector2Int desiredAnchor, HashSet<int> claimedTiles)
    {
        Vector2Int clampedDesired = new Vector2Int(
            Mathf.Clamp(desiredAnchor.x, 0, Mathf.Max(0, width - 1)),
            Mathf.Clamp(desiredAnchor.y, 0, Mathf.Max(0, height - 1)));

        bool found = false;
        Vector2Int best = clampedDesired;
        int bestManhattan = int.MaxValue;
        int bestSquaredDistance = int.MaxValue;
        int bestAbsY = int.MaxValue;
        int bestAbsX = int.MaxValue;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int flattened = FlattenTileIndex(x, y);
                if (claimedTiles.Contains(flattened))
                {
                    continue;
                }

                int deltaX = Mathf.Abs(x - clampedDesired.x);
                int deltaY = Mathf.Abs(y - clampedDesired.y);
                int manhattan = deltaX + deltaY;
                int squaredDistance = (deltaX * deltaX) + (deltaY * deltaY);

                bool isBetter = !found ||
                                manhattan < bestManhattan ||
                                (manhattan == bestManhattan && squaredDistance < bestSquaredDistance) ||
                                (manhattan == bestManhattan && squaredDistance == bestSquaredDistance && deltaY < bestAbsY) ||
                                (manhattan == bestManhattan && squaredDistance == bestSquaredDistance && deltaY == bestAbsY && deltaX < bestAbsX) ||
                                (manhattan == bestManhattan && squaredDistance == bestSquaredDistance && deltaY == bestAbsY && deltaX == bestAbsX && y < best.y) ||
                                (manhattan == bestManhattan && squaredDistance == bestSquaredDistance && deltaY == bestAbsY && deltaX == bestAbsX && y == best.y && x < best.x);

                if (!isBetter)
                {
                    continue;
                }

                found = true;
                best = new Vector2Int(x, y);
                bestManhattan = manhattan;
                bestSquaredDistance = squaredDistance;
                bestAbsY = deltaY;
                bestAbsX = deltaX;
            }
        }

        return best;
    }

    private int FlattenTileIndex(int x, int y)
    {
        return (y * width) + x;
    }

    void GenerateGrid()
    {
        if (tilePrefab == null)
        {
            Debug.LogError("Tile prefab not assigned on GridManager!");
            return;
        }

        // Center the grid around (0,0)
        originX = -(width - 1) * tileSize / 2f;
        originY = -(height - 1) * tileSize / 2f;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector3 position = new Vector3(
                    originX + x * tileSize,
                    originY + y * tileSize,
                    0f
                );

                GameObject tile = Instantiate(tilePrefab, position, Quaternion.identity, transform);
                tile.name = $"Tile_{x}_{y}";

                TileVisibility visibility = tile.GetComponent<TileVisibility>();
                if (visibility != null)
                {
                    visibility.Initialize(x, y);
                    tileGrid[x, y] = visibility;
                }
                else
                {
                    Debug.LogWarning("Tile prefab is missing TileVisibility component.", tile);
                }
            }
        }
    }

    void SpawnCity(int x, int y, int ownerSeatIndex)
    {
        if (cityPrefab == null)
        {
            Debug.LogError("Tried to spawn a city, but cityPrefab is not assigned on GridManager!");
            return;
        }

        float offsetX = -(width - 1) * tileSize / 2f;
        float offsetY = -(height - 1) * tileSize / 2f;

        Vector3 position = new Vector3(
            offsetX + x * tileSize,
            offsetY + y * tileSize,
            0f
        );

        GameObject cityObject = Instantiate(cityPrefab, position, Quaternion.identity, transform);
        cityObject.name = $"Seat{ownerSeatIndex + 1}City_{x}_{y}";

        // Set ownership and grid coordinates on the City component
        City city = cityObject.GetComponent<City>();
        if (city != null)
        {
            city.SetOwnerSeatIndex(ownerSeatIndex);
            city.x = x;
            city.y = y;
        }

        // Tell OwnedSprite who owns this city for coloring
        OwnedSprite owned = cityObject.GetComponent<OwnedSprite>();
        if (owned != null)
        {
            owned.SetOwnerSeatIndex(ownerSeatIndex);
        }
    }

    public bool TryGetTile(int x, int y, out TileVisibility tile)
    {
        tile = null;
        if (x < 0 || y < 0 || x >= width || y >= height) return false;
        tile = tileGrid[x, y];
        return tile != null;
    }

    public bool TryGetTileAtWorldPosition(Vector3 worldPosition, out TileVisibility tile)
    {
        tile = null;
        if (tileGrid == null) return false;

        float localX = (worldPosition.x - originX) / tileSize;
        float localY = (worldPosition.y - originY) / tileSize;

        int gridX = Mathf.RoundToInt(localX);
        int gridY = Mathf.RoundToInt(localY);

        return TryGetTile(gridX, gridY, out tile);
    }

    public IEnumerable<TileVisibility> GetAllTiles()
    {
        if (tileGrid == null) yield break;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                TileVisibility tile = tileGrid[x, y];
                if (tile != null)
                    yield return tile;
            }
        }
    }
}
