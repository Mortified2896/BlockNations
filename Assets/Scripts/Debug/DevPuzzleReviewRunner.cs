// Dev Puzzle Review runner.
//
// Scope: dev-only helper. Creates a clean 7x7 puzzle review scene at runtime
// (Puzzle #001 — adjacent empty enemy city capture) without touching the normal
// SampleScene / VsAI / PBp paths. Intended for the Main Menu's "Dev Puzzle Review"
// entry; the runner itself is gated by UNITY_EDITOR so it is not present in
// release builds.
//
// What it does:
//   1. Creates a brand new empty scene (no natural map, no natural cities, no
//      SampleScene HUD, no AI, no PBp transport).
//   2. Adds an orthographic camera framed to the 7x7 board, a directional
//      light, an EventSystem (so UITK works in the new scene), a GridManager
//      configured 7x7, and a TurnManager wired to the GridManager.
//   3. Calls GridManager.RebuildGrid(7, 7) so tile prefabs spawn naturally.
//   4. Spawns one enemy city at (3, 3) and one friendly unit at (3, 4) using
//      the prefab GUIDs from Assets/Scenes/SampleScene.unity.
//   5. Sets TurnManager.currentTurnSeatIndex = 0, isPlayerTurn = true, and
//      marks both the source and target tiles visible for seat 0 so the
//      reviewer can see them.
//   6. Logs a one-line setup status.
//
// What it does NOT do:
//   - Touch LegalActionService, TurnManager.StartupSequence, InitializeNewGame,
//     AI turn flow, recruitment, income, save/load, PBp transport, or PlayerPrefs.
//   - Modify SampleScene.unity, the City/Unit prefabs, or the MainMenu UXML/USS.
//   - Persist anything.
//
// The class itself is #if UNITY_EDITOR gated because it depends on
// UnityEditor.AssetDatabase. The MainMenuUITKView Launch handler invokes the
// runner via reflection so the menu compiles in release builds too.
#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class DevPuzzleReviewRunner
{
    private const string PuzzleLogTag = "[DevPuzzleReview]";
    private const string PuzzleId = "001";
    private const string PuzzleSituationType = "AdjacentEmptyEnemyCityCapture";

    // Prefab GUIDs sourced from Assets/Scenes/SampleScene.unity.
    private const string CityPrefabGuid = "cec95ee826b53544887c1077a20dcf4f";
    private const string UnitPrefabGuid = "201cae50cac1013489cb65af371b9783";
    private const string TilePrefabGuid = "870fbaf2418f95743be8b7f9563606b5";

    private const int BoardSize = 7;
    private const int CityX = 3;
    private const int CityY = 3;
    private const int UnitX = 3;
    private const int UnitY = 4;
    private const int ActingSeatIndex = 0;
    private const int EnemySeatIndex = 1;

    // Public entry point. Returns true if the puzzle scene was created
    // successfully; false on any failure (logged).
    public static bool Run()
    {
        try
        {
            return RunInternal();
        }
        catch (Exception ex)
        {
            Debug.LogError($"{PuzzleLogTag} puzzle={PuzzleId} verdict=SetupFailed error={ex.GetType().Name}:{ex.Message}");
            return false;
        }
    }

    private static bool RunInternal()
    {
        // 1. Create a fresh, empty scene. No natural map, no HUD, no AI.
        Scene puzzleScene = SceneManager.CreateScene($"DevPuzzleReview_Puzzle{PuzzleId}");
        SceneManager.SetActiveScene(puzzleScene);

        // 2. Camera, light, event system. Camera is framed to fit the 7x7 board
        //    with a little padding so the reviewer can see all tiles.
        GameObject cameraGo = new GameObject("DevPuzzleReview_MainCamera");
        Camera camera = cameraGo.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = (BoardSize / 2f) + 1f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.18f, 0.22f, 0.28f, 1f);
        cameraGo.tag = "MainCamera";

        GameObject lightGo = new GameObject("DevPuzzleReview_DirectionalLight");
        Light directional = lightGo.AddComponent<Light>();
        directional.type = LightType.Directional;
        directional.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject eventSystemGo = new GameObject("DevPuzzleReview_EventSystem");
        eventSystemGo.AddComponent<EventSystem>();
        eventSystemGo.AddComponent<StandaloneInputModule>();

        // 3. GridManager — 7x7, with tile/city prefab references resolved from
        //    the project's SampleScene prefab GUIDs.
        GameObject gridGo = new GameObject("DevPuzzleReview_GridManager");
        GridManager gridManager = gridGo.AddComponent<GridManager>();
        gridManager.width = BoardSize;
        gridManager.height = BoardSize;
        gridManager.tileSize = 1f;
        gridManager.tilePrefab = LoadPrefabByGuid(TilePrefabGuid, "BasicTile");
        gridManager.cityPrefab = LoadPrefabByGuid(CityPrefabGuid, "City");
        if (gridManager.tilePrefab == null)
        {
            Debug.LogError($"{PuzzleLogTag} puzzle={PuzzleId} verdict=SetupFailed reason=TilePrefabNotFound");
            return false;
        }
        if (gridManager.cityPrefab == null)
        {
            Debug.LogError($"{PuzzleLogTag} puzzle={PuzzleId} verdict=SetupFailed reason=CityPrefabNotFound");
            return false;
        }

        // RebuildGrid(7, 7) will: clear the grid, instantiate tile prefabs in
        // a 7x7 pattern, then call SpawnStartingCities() — which will spawn
        // a single seat-0 city at the natural start anchor. We destroy any
        // cities it spawns below before placing the puzzle pieces.
        gridManager.RebuildGrid(BoardSize, BoardSize, recalculateVisibility: false);
        DestroyAllCities(gridManager);

        // 4. TurnManager — minimal, no AI, no PBp, just the unit prefab.
        GameObject turnManagerGo = new GameObject("DevPuzzleReview_TurnManager");
        TurnManager turnManager = turnManagerGo.AddComponent<TurnManager>();
        turnManager.gridManager = gridManager;
        turnManager.unitPrefab = LoadPrefabByGuid(UnitPrefabGuid, "Warrior");
        if (turnManager.unitPrefab == null)
        {
            Debug.LogError($"{PuzzleLogTag} puzzle={PuzzleId} verdict=SetupFailed reason=UnitPrefabNotFound");
            return false;
        }

        // 5. Spawn the puzzle city and unit directly, at the same world coords
        //    GridManager.SpawnCity would use for a 7x7 board.
        SpawnCity(gridManager, CityX, CityY, EnemySeatIndex);
        SpawnUnit(turnManager, gridManager, UnitX, UnitY, ActingSeatIndex);

        // 6. Turn state — acting seat is 0, it's the human's turn, no game over.
        turnManager.currentTurnSeatIndex = ActingSeatIndex;
        turnManager.isPlayerTurn = ActingSeatIndex == 0;
        turnManager.gameOver = false;
        turnManager.turnNumber = 1;

        // Mark both source and target tiles visible for the acting seat so
        // the reviewer can see them, matching the test's precondition.
        MarkTileVisible(gridManager, CityX, CityY, ActingSeatIndex);
        MarkTileVisible(gridManager, UnitX, UnitY, ActingSeatIndex);

        // Camera reframing — 7x7 board is centered on the world origin by
        // construction, so the camera's existing transform is correct, but
        // we tighten orthographicSize a bit to avoid the natural-VsAI framing
        // (which would zoom way out for 11x11).
        camera.orthographicSize = (BoardSize / 2f) + 0.75f;
        camera.transform.position = new Vector3(0f, 0f, -10f);

        Debug.Log($"{PuzzleLogTag} puzzle={PuzzleId} type={PuzzleSituationType} verdict=SetupReady " +
                  $"board={BoardSize}x{BoardSize} city=({CityX},{CityY},seat={EnemySeatIndex}) " +
                  $"unit=({UnitX},{UnitY},seat={ActingSeatIndex}) " +
                  $"adjacency=Chebyshev(current)");
        return true;
    }

    private static GameObject LoadPrefabByGuid(string guid, string friendlyName)
    {
        if (string.IsNullOrEmpty(guid))
        {
            return null;
        }
        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning($"{PuzzleLogTag} GUID '{guid}' for '{friendlyName}' did not resolve to an asset path.");
            return null;
        }
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning($"{PuzzleLogTag} Could not load prefab '{friendlyName}' at '{path}'.");
        }
        return prefab;
    }

    private static void DestroyAllCities(GridManager gridManager)
    {
        City[] cities = UnityEngine.Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        for (int i = 0; i < cities.Length; i++)
        {
            if (cities[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(cities[i].gameObject);
            }
        }
    }

    private static void SpawnCity(GridManager gridManager, int x, int y, int ownerSeatIndex)
    {
        Vector3 position = ComputeWorldPosition(gridManager, x, y);
        GameObject cityObject = UnityEngine.Object.Instantiate(
            gridManager.cityPrefab, position, Quaternion.identity, gridManager.transform);
        cityObject.name = $"DevPuzzleReview_City_{x}_{y}_Seat{ownerSeatIndex}";

        City city = cityObject.GetComponent<City>();
        if (city != null)
        {
            city.SetOwnerSeatIndex(ownerSeatIndex);
            city.x = x;
            city.y = y;
        }

        OwnedSprite owned = cityObject.GetComponent<OwnedSprite>();
        if (owned != null)
        {
            owned.SetOwnerSeatIndex(ownerSeatIndex);
        }
    }

    private static void SpawnUnit(TurnManager turnManager, GridManager gridManager, int x, int y, int ownerSeatIndex)
    {
        Vector3 position = ComputeWorldPosition(gridManager, x, y);
        GameObject unitObject = UnityEngine.Object.Instantiate(
            turnManager.unitPrefab, position, Quaternion.identity, gridManager.transform);
        unitObject.name = $"DevPuzzleReview_Unit_{x}_{y}_Seat{ownerSeatIndex}";

        Unit unit = unitObject.GetComponent<Unit>();
        if (unit != null)
        {
            unit.SetOwnerSeatIndex(ownerSeatIndex);
            unit.movesUsedThisTurn = 0;
            unit.ApplyDefinition(UnitRegistry.WarriorTypeId, preserveCurrentHealth: true);
        }
    }

    private static Vector3 ComputeWorldPosition(GridManager gridManager, int x, int y)
    {
        float offsetX = -(gridManager.width - 1) * gridManager.tileSize / 2f;
        float offsetY = -(gridManager.height - 1) * gridManager.tileSize / 2f;
        return new Vector3(
            offsetX + x * gridManager.tileSize,
            offsetY + y * gridManager.tileSize,
            0f);
    }

    private static void MarkTileVisible(GridManager gridManager, int x, int y, int seatIndex)
    {
        if (gridManager == null || gridManager.tileGrid == null)
        {
            return;
        }
        if (x < 0 || y < 0 || x >= gridManager.width || y >= gridManager.height)
        {
            return;
        }
        TileVisibility tile = gridManager.tileGrid[x, y];
        if (tile == null)
        {
            return;
        }
        // TileVisibility exposes SetVisibleForSeat(...) per the existing
        // AdjacentEmptyEnemyCityCaptureTests PlayMode test.
        var method = tile.GetType().GetMethod(
            "SetVisibleForSeat",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(bool), typeof(int) },
            modifiers: null);
        if (method != null)
        {
            method.Invoke(tile, new object[] { true, seatIndex });
        }
    }
}
#endif
