using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    public const int MinSupportedBoardSize = 2;

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
            // Player city near bottom-left
            SpawnCity(1, 1, true);

            // AI city near top-right
            SpawnCity(width - 2, height - 2, false);
        }
        else
        {
            Debug.LogWarning("City prefab not assigned on GridManager, no cities spawned.");
        }
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

    void SpawnCity(int x, int y, bool isPlayerOwned)
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
        cityObject.name = (isPlayerOwned ? "PlayerCity_" : "AICity_") + x + "_" + y;

        // Set ownership and grid coordinates on the City component
        City city = cityObject.GetComponent<City>();
        if (city != null)
        {
            city.isPlayerOwned = isPlayerOwned;
            city.x = x;
            city.y = y;
        }

        // Tell OwnedSprite who owns this city for coloring
        OwnedSprite owned = cityObject.GetComponent<OwnedSprite>();
        if (owned != null)
        {
            owned.SetOwner(isPlayerOwned);
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
