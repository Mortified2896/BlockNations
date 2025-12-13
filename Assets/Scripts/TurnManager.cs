using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;   // for TMP_Text

public class TurnManager : MonoBehaviour
{
    public enum GameMode
    {
        None,
        VsAI,
        Hotseat,
        PlayByPost
    }

    public enum AIDifficulty
    {
        None,
        Level1,
        Level2,
        Level3,
        Unfair
    }

    public static TurnManager Instance { get; private set; }

    [Header("Mode")]
    public GameMode currentMode = GameMode.None;

    [Header("Turn State")]
    public bool isPlayerTurn = true;
    public int turnNumber = 1;
    public bool gameOver = false;

    [Header("Economy")]
    // Base starting gold; income from cities adds on top at game start.
    public int startingGold = 2;
    public int playerGold = 2;
    public int aiGold = 0;
    public int goldPerCity = 1;
    public int warriorCost = 2;

    [Header("AI Settings")]
    public float aiTurnDelay = 1f; // seconds the AI "thinks" before ending its turn
    public AIDifficulty aiDifficulty = AIDifficulty.Level1;

    [Header("UI")]
    public TMP_Text turnText;      // assign in Inspector
    public TMP_Text goldText;

    [Header("Game Over UI")]
    public GameObject gameOverPanel;
    public TMP_Text gameOverText;

    [Header("Play By Post")]
    [Tooltip("Optional panel shown when a Play-by-Post turn is finished (e.g., with a 'Copy JSON' button).")]
    public GameObject playByPostPopup;

    [Header("References")]
    public GridManager gridManager;
    public int visibilityRadius = 1;
    public bool IsHotseatHandoff => isHotseatHandoff;

    [Header("Prefabs")]
    public GameObject unitPrefab; // used to respawn units on load

    [Header("Audio")]
    public bool playMusicOnStart = true;
    public AudioClip gameplayMusic;

    [Header("Saving")]
    public bool autoSaveEnabled = true;
    public string autoSaveFileName = "save.json";
    public bool playByPostExportPretty = true;

    private bool isHotseatHandoff = false;
    private bool nextHotseatIsPlayer = false;
    private bool hotseatHandoffAdvancesTurn = false;
    private bool isLoadingFromSave = false;
    // When true in PlayByPost, the current side has ended
    // their turn and we are only waiting for the player
    // to copy/export the JSON state. While this is true,
    // no further actions should be taken in the scene.
    private bool isPlayByPostWaitingForExport = false;

    [System.Serializable]
    private class SavedCity
    {
        public int x;
        public int y;
        public bool isPlayerOwned;
        public bool hasRecruitedThisTurn;
    }

    [System.Serializable]
    private class SavedUnit
    {
        public bool isPlayerOwned;
        public float x;
        public float y;
        public float z;
        public int currentHealth;
        public int movesUsedThisTurn;
        public bool hasAttackedThisTurn;
    }

    [System.Serializable]
    private class SavedTile
    {
        public int x;
        public int y;
        public bool playerSeen;
        public bool opponentSeen;
    }

    [System.Serializable]
    private class GameSave
    {
        public string version = "2";
        public string gameId;
        public string mode;
        public string aiDifficulty;
        public bool isPlayerTurn;
        public int turnNumber;
        public int playerGold;
        public int aiGold;
        public bool gameOver;
        public int visibilityRadius;
        public List<SavedCity> cities = new List<SavedCity>();
        public List<SavedUnit> units = new List<SavedUnit>();
        public List<SavedTile> tiles = new List<SavedTile>();
    }

    // Stable id for the current campaign/save chain so exports can be shared
    private string currentGameId;

    public bool IsHumanTurn()
    {
        if (isHotseatHandoff || isPlayByPostWaitingForExport)
            return false;

        if (currentMode == GameMode.None)
            return false;

        if (currentMode == GameMode.VsAI)
            return isPlayerTurn;

        // Hotseat: both sides are human-controlled
        return true;
    }

    public bool IsCurrentSideOwner(bool isPlayerOwned)
    {
        if (currentMode == GameMode.Hotseat || currentMode == GameMode.PlayByPost)
        {
            return isPlayerTurn == isPlayerOwned;
        }

        // Vs AI: only player-owned units/cities are controllable during the player turn
        return isPlayerTurn && isPlayerOwned;
    }

    public bool CanControlUnit(Unit unit)
    {
        if (unit == null || gameOver || isHotseatHandoff)
            return false;

        return IsCurrentSideOwner(unit.isPlayerOwned);
    }

    public bool CanControlCity(City city)
    {
        if (city == null || gameOver || isHotseatHandoff)
            return false;

        return IsCurrentSideOwner(city.isPlayerOwned);
    }

    public string GetCurrentSideName()
    {
        if (currentMode == GameMode.Hotseat || currentMode == GameMode.PlayByPost)
        {
            return isPlayerTurn ? "Player 1" : "Player 2";
        }

        return isPlayerTurn ? "Player" : "AI";
    }

    public void SetGameMode(GameMode mode)
    {
        if (currentMode != GameMode.None || gameOver)
            return;

        currentMode = mode;
        Time.timeScale = 1f;
        UpdateTurnText();
        RecalculatePlayerVisibility();
        Debug.Log("Selected game mode: " + mode);

        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.RefreshMoveOutlinesForCurrentTurn();
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void Start()
    {
        isPlayByPostWaitingForExport = false;
        EnsureTurnAndGoldTexts();
        EnsureEventSystemExists();
        EnsureUIRaycasters();
        TryStartGameplayMusic();
        StartCoroutine(StartupSequence());
    }

    void Update()
    {
        if (gameOver || isHotseatHandoff)
            return;

        // Optional: keep Space for PC testing
        if (IsHumanTurn() && Input.GetKeyDown(KeyCode.Space))
        {
            OnEndTurnButtonPressed();
        }
    }

    // 🚩 This is what the UI Button will call
    public void OnEndTurnButtonPressed()
    {
        Debug.Log($"OnEndTurnButtonPressed clicked (gameOver={gameOver}, isHotseatHandoff={isHotseatHandoff}, isHumanTurn={IsHumanTurn()})");
        if (gameOver || isHotseatHandoff || !IsHumanTurn())
        {
            // Ignore clicks if it's not the current human's turn
            return;
        }

        EndCurrentTurn();
    }

    public void OnPlayAgainButtonPressed()
    {
        // Preserve the mode (VsAI / Hotseat / PlayByPost) for the next game.
        GameModeSelection.SetPendingMode(currentMode);

        Time.timeScale = 1f;
        var currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
    }

    void EnsureEventSystemExists()
    {
        if (EventSystem.current != null)
            return;

        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    void EnsureUIRaycasters()
    {
        var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var c in canvases)
        {
            if (c == null) continue;
            var gr = c.GetComponent<GraphicRaycaster>();
            if (gr == null)
            {
                c.gameObject.AddComponent<GraphicRaycaster>();
                Debug.Log($"Added GraphicRaycaster to canvas '{c.name}' so UI can receive clicks.");
            }
            else if (!gr.enabled)
            {
                gr.enabled = true;
                Debug.Log($"Enabled GraphicRaycaster on canvas '{c.name}' so UI can receive clicks.");
            }
        }
    }

    void EndCurrentTurn()
    {
        Debug.Log(GetCurrentSideName() + " ends Turn " + turnNumber);

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayTurnEnd();
        }

        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.HideAllMoveOutlines();
        }

        if (currentMode == GameMode.VsAI)
        {
            isPlayerTurn = false;
            UpdateTurnText();
            AutoSaveIfEnabled();
            StartCoroutine(AITurn());
            return;
        }

        // Hotseat / Play-by-Post: no AI, just human sides.
        if (currentMode == GameMode.Hotseat)
        {
            // Advance to the next side locally and show the handoff overlay.
            isPlayerTurn = !isPlayerTurn;
            UpdateTurnText();
            ShowHotseatHandoff(isPlayerTurn, true);
            AutoSaveIfEnabled();
        }
        else if (currentMode == GameMode.PlayByPost)
        {
            // In Play-by-Post we do NOT start the next side's turn locally,
            // otherwise the local player would see the opponent's fog-of-war.
            // Instead we freeze interaction, optionally show a popup, and
            // let CopyCurrentStateToClipboard() build a snapshot that
            // represents the *next* side's turn.
            isPlayByPostWaitingForExport = true;
            AutoSaveIfEnabled();

        if (playByPostPopup != null)
        {
            playByPostPopup.SetActive(true);
        }

            Debug.Log("Play-by-Post turn finished. Use the Copy JSON button to export this turn.");
        }
    }

    void TryStartGameplayMusic()
    {
        if (!playMusicOnStart)
            return;

        if (SoundManager.Instance == null)
            return;

        if (gameplayMusic != null)
        {
            SoundManager.Instance.PlayBackgroundMusic(gameplayMusic);
        }
        else
        {
            SoundManager.Instance.PlayBackgroundMusic();
        }
    }

    void ShowHotseatHandoff(bool nextIsPlayer, bool advanceTurnAfterReturn)
    {
        isHotseatHandoff = true;
        nextHotseatIsPlayer = nextIsPlayer;
        hotseatHandoffAdvancesTurn = advanceTurnAfterReturn;
        Time.timeScale = 0f;

        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.ClearSelection();
        }

        if (TileHoverManager.Instance != null)
        {
            TileHoverManager.Instance.ClearSelection();
        }

        if (CityUIManager.Instance != null)
        {
            CityUIManager.Instance.ClosePanel();
        }
    }

    public void ContinueHotseatTurn()
    {
        if (!isHotseatHandoff || currentMode != GameMode.Hotseat)
            return;

        isHotseatHandoff = false;
        Time.timeScale = 1f;

        if (nextHotseatIsPlayer)
        {
            if (hotseatHandoffAdvancesTurn)
            {
                // Completed a full round, advance the turn counter for Player 1.
                turnNumber++;
            }
            BeginPlayerTurn();
        }
        else
        {
            BeginHotseatOpponentTurn();
        }
    }

    IEnumerator AITurn()
    {
        if (gameOver || currentMode != GameMode.VsAI)
            yield break;

        Debug.Log("AI Turn " + turnNumber + " started. AI is thinking...");

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayTurnStart();
        }

        // Simulate thinking time
        yield return new WaitForSeconds(aiTurnDelay);

        // Collect AI income at the start of its turn
        CollectAIGold();

        // AI actions: recruit and move units
        ResetRecruitmentForAICities();
        RunAI();

        Debug.Log("AI finished Turn " + turnNumber);

        if (gameOver)
            yield break;

        AutoSaveIfEnabled();

        // Back to player
        turnNumber++;
        BeginPlayerTurn();
    }

    void BeginPlayerTurn()
    {
        if (gameOver)
            return;

        isPlayerTurn = true;

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayTurnStart();
        }

        // Allow cities and units to act again
        ResetRecruitmentForPlayerCities();
        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.ResetMovementForSide(true, IsCurrentSideOwner(true));
            UnitSelectionManager.Instance.ClearSelection();
        }

        if (TileHoverManager.Instance != null)
        {
            TileHoverManager.Instance.ClearSelection();
        }

        if (CityUIManager.Instance != null)
        {
            CityUIManager.Instance.ClosePanel();
        }

        CollectPlayerIncome();
        RecalculatePlayerVisibility();
        UpdateTurnText();
        Debug.Log(GetCurrentSideName() + " turn " + turnNumber + " begins.");
    }

    void BeginHotseatOpponentTurn()
    {
        if (gameOver)
            return;

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayTurnStart();
        }

        // Allow cities and units to act again
        ResetRecruitmentForAICities();
        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.ResetMovementForSide(false, IsCurrentSideOwner(false));
            UnitSelectionManager.Instance.ClearSelection();
        }

        if (TileHoverManager.Instance != null)
        {
            TileHoverManager.Instance.ClearSelection();
        }

        if (CityUIManager.Instance != null)
        {
            CityUIManager.Instance.ClosePanel();
        }

        CollectAIGold();
        UpdateGoldText();
        RecalculatePlayerVisibility();
        UpdateTurnText();
        Debug.Log(GetCurrentSideName() + " begins their turn.");
    }

    System.Collections.IEnumerator StartupSequence()
    {
        // Ensure the grid is initialized before applying save or starting a new game.
        yield return WaitForGridReady();

        // Attempt to load a pending save request before starting a new game.
        if (SaveLoadRequest.TryConsume(out string loadPath))
        {
            bool loaded = LoadFromFile(loadPath);
            if (loaded)
            {
                Debug.Log("Loaded save from " + loadPath + " on scene start.");
                yield break;
            }

            Debug.LogWarning("Load request failed; starting a new game. Path: " + loadPath);
        }

        InitializeNewGame();
    }

    System.Collections.IEnumerator WaitForGridReady()
    {
        while (gridManager == null || gridManager.tileGrid == null || gridManager.tileGrid.Length == 0)
        {
            yield return null;
        }
    }

    void InitializeNewGame()
    {
        // Reset core state for a fresh game (important when reloading scenes in-editor).
        gameOver = false;
        turnNumber = 1;
        isPlayerTurn = true;
        isPlayByPostWaitingForExport = false;
        playerGold = startingGold;
        aiGold = startingGold;
        aiDifficulty = AIDifficulty.Level1;

        if (string.IsNullOrEmpty(currentGameId))
        {
            currentGameId = System.Guid.NewGuid().ToString();
        }

        if (GameModeSelection.TryConsume(out GameMode pendingMode))
        {
            SetGameMode(pendingMode);
        }
        else if (currentMode == GameMode.None)
        {
            SetGameMode(GameMode.VsAI);
            Debug.Log("No mode preselected. Defaulting to Vs AI.");
        }

        if (AIDifficultySelection.TryConsume(out AIDifficulty pendingDifficulty))
        {
            aiDifficulty = pendingDifficulty;
        }

        ResetRecruitmentForPlayerCities();
        // Give income only to the side whose turn it is.
        // The other side receives income at the start of
        // its first turn (via Begin*Turn / AITurn).
        CollectPlayerIncome();
        UpdateGoldText();
        UpdateTurnText();
        RecalculatePlayerVisibility();
        Debug.Log("Game start. " + GetCurrentSideName() + " Turn " + turnNumber + " (AI difficulty " + aiDifficulty + ")");

        // If we start in Hotseat, show the handoff before the very first turn.
        // PlayByPost should NOT use the hotseat handoff overlay.
        if (currentMode == GameMode.Hotseat)
        {
            ShowHotseatHandoff(isPlayerTurn, false);
        }
    }

    void ResetRecruitmentForPlayerCities()
    {
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        foreach (City city in cities)
        {
            if (city.isPlayerOwned)
            {
                city.hasRecruitedThisTurn = false;
            }
        }
    }

    void ResetRecruitmentForAICities()
    {
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        foreach (City city in cities)
        {
            if (!city.isPlayerOwned)
            {
                city.hasRecruitedThisTurn = false;
            }
        }
    }

    void RunAI()
    {
        // 1) Recruit from each AI city (one unit per city per AI turn, if the city is empty)
        City[] allCities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        City primaryAICity = null;
        foreach (City city in allCities)
        {
            if (!city.isPlayerOwned && city.CanRecruit())
            {
                city.SpawnWarrior();
            }

            if (!city.isPlayerOwned && primaryAICity == null)
            {
                primaryAICity = city;
            }
        }

        // 2) Move AI units toward the nearest player unit or city.
        bool aiHasPerfectInfo = aiDifficulty == AIDifficulty.Unfair;
        Unit[] allUnits = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);

        HashSet<TileVisibility> aiVisibleTiles = aiHasPerfectInfo ? null : ComputeVisibilityForSide(false);

        // Collect player targets: visible units + known player cities.
        List<Vector3> playerTargets = new List<Vector3>();
        List<Vector3> playerCityPositions = new List<Vector3>();
        List<TileVisibility> playerUnitTiles = new List<TileVisibility>();
        bool anyVisiblePlayerUnit = false;
        bool enemyNearAICity = false;

        foreach (Unit unit in allUnits)
        {
            if (!unit.isPlayerOwned)
                continue;

            // Unfair AI: always knows all player unit positions. Otherwise, respect visibility.
            bool unitIsVisible = aiHasPerfectInfo;

            // If we could not determine visibility, fall back to the old behavior.
            if (!unitIsVisible && (aiVisibleTiles == null || aiVisibleTiles.Count == 0 || gridManager == null))
            {
                playerTargets.Add(unit.transform.position);
                anyVisiblePlayerUnit = true;
                continue;
            }

            if (gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile))
            {
                if (aiHasPerfectInfo || aiVisibleTiles.Contains(tile))
                {
                    playerTargets.Add(unit.transform.position);
                    playerUnitTiles.Add(tile);
                    anyVisiblePlayerUnit = true;

                    // Track whether any enemy unit is near the AI city.
                    if (primaryAICity != null)
                    {
                        int dxCity = Mathf.Abs(tile.gridX - primaryAICity.x);
                        int dyCity = Mathf.Abs(tile.gridY - primaryAICity.y);
                        int distToAICityTilesForEnemy = Mathf.Max(dxCity, dyCity);
                        if (distToAICityTilesForEnemy <= 2)
                        {
                            enemyNearAICity = true;
                        }
                    }
                }
            }
        }

        foreach (City city in allCities)
        {
            if (!city.isPlayerOwned)
                continue;

            // Cities are at fixed positions in this map, so
            // allow the AI to always know their locations.
            Vector3 cityPos = city.transform.position;
            playerTargets.Add(cityPos);
            playerCityPositions.Add(cityPos);
        }

        if (playerTargets.Count == 0)
        {
            // Nothing visible to move toward this turn (AI does not cheat).
            return;
        }

        // Determine grid step size from the UnitSelectionManager (fallback to 1)
        float stepSize = 1f;
        if (UnitSelectionManager.Instance != null)
        {
            stepSize = UnitSelectionManager.Instance.tileSize;
        }

        // For threat checks: a player can move one tile then attack adjacent, so staying
        // beyond Chebyshev distance 2 avoids being attacked next turn by a fresh unit.
        const int playerThreatRadiusTiles = 2;

        // Level 2 behavior: if no enemy units are currently visible, units that
        // are closest to the enemy city have a small chance to hold position
        // instead of always advancing, to make behavior less predictable.
        bool applyLevel2HoldBehavior = (aiDifficulty == AIDifficulty.Level2) &&
                                       !anyVisiblePlayerUnit &&
                                       playerCityPositions.Count > 0;

        // Unfair skips the Level 3 defender anchor logic since it has perfect info and prefers full offense.
        bool applyLevel3DefenderBehavior = (aiDifficulty == AIDifficulty.Level3) &&
                                           (primaryAICity != null);

        // For Level 2 we compute distances in grid coordinates rather than world
        // space so the logic is stable regardless of tile size.
        Dictionary<Unit, int> distToEnemyCityTiles = null;
        int nearestEnemyCityDistTiles = int.MaxValue;

        Dictionary<Unit, int> distToAICityTiles = null;
        int nearestAICityDistTiles = int.MaxValue;
        Unit defenderCandidate = null;

        int GetThreatDistanceTiles(Vector3 pos)
        {
            if (gridManager == null || playerUnitTiles.Count == 0)
                return int.MaxValue;

            if (!gridManager.TryGetTileAtWorldPosition(pos, out TileVisibility posTile))
                return int.MaxValue;

            int minDist = int.MaxValue;
            foreach (var enemyTile in playerUnitTiles)
            {
                int dx = Mathf.Abs(enemyTile.gridX - posTile.gridX);
                int dy = Mathf.Abs(enemyTile.gridY - posTile.gridY);
                int dist = Mathf.Max(dx, dy);
                if (dist < minDist)
                {
                    minDist = dist;
                }
            }
            return minDist;
        }

        Vector3 PredictOneStep(Vector3 from, Vector3 target)
        {
            Vector3 delta = target - from;
            delta.z = 0f;

            float stepX = 0f;
            float stepY = 0f;
            if (Mathf.Abs(delta.x) > 0.1f)
            {
                stepX = Mathf.Sign(delta.x) * stepSize;
            }
            if (Mathf.Abs(delta.y) > 0.1f)
            {
                stepY = Mathf.Sign(delta.y) * stepSize;
            }

            return new Vector3(from.x + stepX, from.y + stepY, from.z);
        }

        if (applyLevel2HoldBehavior || applyLevel3DefenderBehavior)
        {
            distToEnemyCityTiles = applyLevel2HoldBehavior ? new Dictionary<Unit, int>() : null;
            distToAICityTiles = applyLevel3DefenderBehavior ? new Dictionary<Unit, int>() : null;

            foreach (Unit unit in allUnits)
            {
                if (unit == null || unit.isPlayerOwned)
                    continue;

                if (!gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility originTile))
                    continue;

                if (applyLevel2HoldBehavior)
                {
                    int bestEnemyCityDistTiles = int.MaxValue;

                    foreach (City city in allCities)
                    {
                        if (!city.isPlayerOwned)
                            continue;

                        int dx = Mathf.Abs(originTile.gridX - city.x);
                        int dy = Mathf.Abs(originTile.gridY - city.y);
                        int distTiles = Mathf.Max(dx, dy); // Chebyshev distance (diagonal moves allowed)

                        if (distTiles < bestEnemyCityDistTiles)
                        {
                            bestEnemyCityDistTiles = distTiles;
                        }
                    }

                    if (bestEnemyCityDistTiles < int.MaxValue)
                    {
                        distToEnemyCityTiles[unit] = bestEnemyCityDistTiles;
                        if (bestEnemyCityDistTiles < nearestEnemyCityDistTiles)
                        {
                            nearestEnemyCityDistTiles = bestEnemyCityDistTiles;
                        }
                    }
                }

                if (applyLevel3DefenderBehavior && primaryAICity != null)
                {
                    int dxAi = Mathf.Abs(originTile.gridX - primaryAICity.x);
                    int dyAi = Mathf.Abs(originTile.gridY - primaryAICity.y);
                    int distToAiTiles = Mathf.Max(dxAi, dyAi);

                    distToAICityTiles[unit] = distToAiTiles;
                    if (distToAiTiles < nearestAICityDistTiles)
                    {
                        nearestAICityDistTiles = distToAiTiles;
                        defenderCandidate = unit;
                    }
                    else if (distToAiTiles == nearestAICityDistTiles && defenderCandidate == null)
                    {
                        defenderCandidate = unit;
                    }
                }
            }
        }

        // === Level 3: recruit vs recall defender when city is under visible threat ===
        if (applyLevel3DefenderBehavior && enemyNearAICity && primaryAICity != null)
        {
            // Enemy distance to AI city in tiles (Chebyshev)
            int enemyTurnsToCity = int.MaxValue;
            foreach (Unit unit in allUnits)
            {
                if (unit == null || !unit.isPlayerOwned)
                    continue;

                if (!gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility enemyTile))
                    continue;

                int dxE = Mathf.Abs(enemyTile.gridX - primaryAICity.x);
                int dyE = Mathf.Abs(enemyTile.gridY - primaryAICity.y);
                int distEnemy = Mathf.Max(dxE, dyE);
                if (distEnemy < enemyTurnsToCity)
                {
                    enemyTurnsToCity = distEnemy;
                }
            }

            // Closest AI unit distance to own city (from distToAICityTiles)
            int aiTurnsFromFront = nearestAICityDistTiles;

            // Estimate income per turn for AI from owned cities
            int aiIncomePerTurn = 0;
            foreach (City city in allCities)
            {
                if (!city.isPlayerOwned)
                {
                    aiIncomePerTurn += goldPerCity;
                }
            }

            int aiGoldNow = aiGold;
            int turnsUntilCanRecruit;
            if (aiGoldNow >= warriorCost)
            {
                turnsUntilCanRecruit = 0;
            }
            else if (aiIncomePerTurn > 0)
            {
                turnsUntilCanRecruit = Mathf.CeilToInt((warriorCost - aiGoldNow) / (float)aiIncomePerTurn);
            }
            else
            {
                turnsUntilCanRecruit = int.MaxValue;
            }

            bool shouldRecruitDefender =
                enemyTurnsToCity < int.MaxValue &&
                turnsUntilCanRecruit <= enemyTurnsToCity;

            if (shouldRecruitDefender)
            {
                // Spawn defender at the AI city if the tile is free.
                Vector3 spawnPosition = primaryAICity.transform.position;
                if (!GridUtils.IsTileOccupied(spawnPosition, null) && unitPrefab != null)
                {
                    // TrySpendGold(false, ...) will also update AI gold and UI when appropriate.
                    if (TrySpendGold(false, warriorCost))
                    {
                        GameObject defender = Instantiate(unitPrefab, spawnPosition, Quaternion.identity);
                        Unit defenderUnit = defender.GetComponent<Unit>();
                        if (defenderUnit != null)
                        {
                            defenderUnit.isPlayerOwned = false;
                            defenderUnit.currentCity = primaryAICity;
                            defenderUnit.ResetMovementForTurn();
                        }

                        primaryAICity.stationedUnit = defender;

                        OwnedSprite owned = defender.GetComponent<OwnedSprite>();
                        if (owned != null)
                        {
                            owned.SetOwner(false);
                        }
                    }
                }
            }
        }

        foreach (Unit unit in allUnits)
        {
            if (unit.isPlayerOwned)
                continue;

            // Reset AI unit movement for this AI turn
            unit.ResetMovementForTurn();

            // Level 3: never idle on our own city tile; step off toward the defensive anchor
            // (or any free/attackable adjacent tile) before making other decisions.
            if (applyLevel3DefenderBehavior && primaryAICity != null && gridManager != null)
            {
                if (gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility unitTile) &&
                    unitTile.gridX == primaryAICity.x && unitTile.gridY == primaryAICity.y)
                {
                    Vector2Int[] offsets = new Vector2Int[]
                    {
                        new Vector2Int(1, 1),  // preferred anchor
                        new Vector2Int(1, 0),
                        new Vector2Int(0, 1),
                        new Vector2Int(-1, 0),
                        new Vector2Int(0, -1),
                        new Vector2Int(-1, -1),
                        new Vector2Int(1, -1),
                        new Vector2Int(-1, 1),
                    };

                    bool steppedOffCity = false;
                    foreach (var off in offsets)
                    {
                        int tx = primaryAICity.x + off.x;
                        int ty = primaryAICity.y + off.y;
                        if (!gridManager.TryGetTile(tx, ty, out TileVisibility anchorTile))
                            continue;

                        Vector3 targetPos = anchorTile.transform.position;
                        Unit occupant = GridUtils.GetUnitAtPosition(targetPos, unit);
                        // Skip if a friendly unit already occupies the tile; allow moving onto enemies.
                        if (occupant != null && occupant.isPlayerOwned == unit.isPlayerOwned)
                            continue;

                        MoveAIUnitOneStep(unit, targetPos, stepSize);
                        steppedOffCity = true;
                        break;
                    }

                    if (steppedOffCity)
                    {
                        // This unit already used its action to reposition off the city tile.
                        continue;
                    }
                }
            }

            // Optional Level 2 randomness: closest units to the enemy city
            // sometimes hold position when no enemies are currently visible.
            if (applyLevel2HoldBehavior &&
                distToEnemyCityTiles != null &&
                distToEnemyCityTiles.TryGetValue(unit, out int unitCityDistTiles))
            {
                // Treat as "closest group" if it has the minimum distance in tiles.
                if (unitCityDistTiles == nearestEnemyCityDistTiles)
                {
                    // Do not skip if this unit could capture the enemy city
                    // in a single tile move (axial or diagonal).
                    // Chebyshev distance 1 => within one move, 0 => already on the city.
                    if (unitCityDistTiles > 1 && Random.value < 0.5f)
                    {
                        // Skip moving this turn to keep behavior varied.
                        continue;
                    }
                }
            }

            // Optional Level 3 behavior: always try to keep the closest AI unit
            // near its own city as a defender when the city is not under visible
            // threat.
            if (applyLevel3DefenderBehavior &&
                !enemyNearAICity &&
                primaryAICity != null &&
                unit == defenderCandidate &&
                distToAICityTiles != null &&
                distToAICityTiles.TryGetValue(unit, out int unitDistToAiTiles))
            {
                // The closest AI unit to its own city acts as the defender.
                if (unitDistToAiTiles == nearestAICityDistTiles)
                {
                    // Prefer to hold one tile "behind" the city toward the corner,
                    // e.g., at (city.x + 1, city.y + 1) for the default top-right AI city.
                    if (gridManager != null)
                    {
                        int anchorX = primaryAICity.x + 1;
                        int anchorY = primaryAICity.y + 1;

                        if (gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility unitTile) &&
                            gridManager.TryGetTile(anchorX, anchorY, out TileVisibility anchorTile))
                        {
                            int distToAnchor = Mathf.Max(Mathf.Abs(unitTile.gridX - anchorX), Mathf.Abs(unitTile.gridY - anchorY));

                            // If already on the anchor tile, hold position.
                            if (distToAnchor == 0)
                            {
                                continue;
                            }

                            // Move toward the defensive anchor tile rather than the city center.
                            MoveAIUnitOneStep(unit, anchorTile.transform.position, stepSize);
                            continue;
                        }
                    }

                    // Fallback: if we cannot compute an anchor, keep the previous behavior
                    // of staying within 1 tile of the city.
                    if (unitDistToAiTiles <= 1)
                    {
                        continue;
                    }

                    MoveAIUnitOneStep(unit, primaryAICity.transform.position, stepSize);
                    continue;
                }
            }

            // Find nearest target
            Vector3 from = unit.transform.position;
            Vector3? bestTarget = null;
            float bestDistSq = float.MaxValue;

            foreach (Vector3 targetPos in playerTargets)
            {
                float dSq = (targetPos - from).sqrMagnitude;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestTarget = targetPos;
                }
            }

            if (bestTarget.HasValue)
            {
                Vector3 chosenTarget = bestTarget.Value;

                // Unfair AI: if the preferred step would end inside the player's move+attack radius,
                // try to choose a safer adjacent step that still heads toward the goal.
                // If no safe step exists, only retreat if we are not about to kill an adjacent player unit
                // by moving onto it.
                if (aiHasPerfectInfo && playerUnitTiles.Count > 0)
                {
                    Vector3 predictedPos = PredictOneStep(from, chosenTarget);
                    int threatDist = GetThreatDistanceTiles(predictedPos);

                    bool predictedKillsEnemy = false;
                    if (GridUtils.GetUnitAtPosition(predictedPos, unit) is Unit enemyAtPos &&
                        enemyAtPos.isPlayerOwned)
                    {
                        // If we step onto an enemy tile, treat as a kill attempt; allow it even in danger.
                        predictedKillsEnemy = true;
                    }

                    // Also allow entering danger if we will be adjacent and can attack after moving.
                    bool predictedCanAttackAdjacent = threatDist <= 1;

                    if (!predictedKillsEnemy && !predictedCanAttackAdjacent && threatDist <= playerThreatRadiusTiles)
                    {
                        Vector3 bestSafeTarget = chosenTarget;
                        float bestSafeDistToGoal = float.MaxValue;
                        bool foundSafe = false;

                        for (int ox = -1; ox <= 1; ox++)
                        {
                            for (int oy = -1; oy <= 1; oy++)
                            {
                                if (ox == 0 && oy == 0)
                                    continue;

                                Vector3 altTarget = from + new Vector3(ox * stepSize, oy * stepSize, 0f);

                                // Stay on the board.
                                if (gridManager != null && !gridManager.TryGetTileAtWorldPosition(altTarget, out _))
                                    continue;

                                int altThreat = GetThreatDistanceTiles(altTarget);
                                if (altThreat <= playerThreatRadiusTiles)
                                    continue;

                                float distToGoal = (chosenTarget - altTarget).sqrMagnitude;
                                if (distToGoal < bestSafeDistToGoal)
                                {
                                    bestSafeDistToGoal = distToGoal;
                                    bestSafeTarget = altTarget;
                                    foundSafe = true;
                                }
                            }
                        }

                        if (foundSafe)
                        {
                            chosenTarget = bestSafeTarget;
                        }
                        // else: no safe tile and not an immediate kill -> fall back to original move (risk it)
                    }
                }

                MoveAIUnitOneStep(unit, chosenTarget, stepSize);
            }
        }
    }

    void MoveAIUnitOneStep(Unit unit, Vector3 targetPosition, float tileSize)
    {
        if (!unit.CanMoveThisTurn())
            return;

        Vector3 from = unit.transform.position;
        Vector3 delta = targetPosition - from;
        delta.z = 0f;

        // If already very close to the target, no need to move
        if (delta.sqrMagnitude < 0.01f)
            return;

        // Decide a step of at most one tile in each axis (diagonal allowed)
        float stepX = 0f;
        float stepY = 0f;

        if (Mathf.Abs(delta.x) > 0.1f)
        {
            stepX = Mathf.Sign(delta.x) * tileSize;
        }
        if (Mathf.Abs(delta.y) > 0.1f)
        {
            stepY = Mathf.Sign(delta.y) * tileSize;
        }

        Vector3 move = new Vector3(stepX, stepY, 0f);
        if (move.sqrMagnitude < 0.01f)
            return;

        // If the unit was stationed in a city, clear that link when it moves away
        if (unit.currentCity != null)
        {
            unit.currentCity.stationedUnit = null;
            unit.currentCity = null;
        }

        Vector3 newPos = from + move;
        newPos.z = from.z;

        Unit targetUnit = GridUtils.GetUnitAtPosition(newPos, unit);
        if (targetUnit != null)
        {
            // Same owner: do not move onto this tile
            if (targetUnit.isPlayerOwned == unit.isPlayerOwned)
            {
                return;
            }

            // Enemy: attack
            unit.hasAttackedThisTurn = true;
            unit.RegisterMove();
            bool killed = unit.Attack(targetUnit);
            Debug.Log("AI unit " + unit.name + " attacked " + targetUnit.name);

            if (killed)
            {
                unit.transform.position = newPos;

                if (SoundManager.Instance != null)
                {
                    SoundManager.Instance.PlayMove();
                }

                Debug.Log("AI unit moved into defeated enemy tile at " + newPos);
            }
        }
        else
        {
            // Empty tile: move normally
            unit.transform.position = newPos;
            unit.RegisterMove();

            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayMove();
            }

            Debug.Log("AI moved unit " + unit.name + " to " + newPos);
        }

        // After moving, if the AI unit has not attacked yet,
        // look for an adjacent enemy to attack (move-then-attack).
        if (!unit.hasAttackedThisTurn)
        {
            float maxDist = 1.5f * tileSize;
            float minDist = 0.1f * tileSize;
            Unit[] allUnits = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
            Unit bestEnemy = null;
            float bestDistSq = float.MaxValue;

            foreach (Unit other in allUnits)
            {
                if (other == null || other.isPlayerOwned == unit.isPlayerOwned)
                    continue;

                float dSq = (other.transform.position - unit.transform.position).sqrMagnitude;
                if (dSq < bestDistSq && dSq >= minDist * minDist && dSq <= maxDist * maxDist)
                {
                    bestDistSq = dSq;
                    bestEnemy = other;
                }
            }

            if (bestEnemy != null)
            {
                unit.hasAttackedThisTurn = true;
                bool killed = unit.Attack(bestEnemy);
                Debug.Log("AI unit " + unit.name + " performed a follow-up attack on " + bestEnemy.name);

                if (killed)
                {
                    unit.transform.position = bestEnemy.transform.position;
                    Debug.Log("AI unit moved into defeated enemy tile at " + bestEnemy.transform.position);
                }
            }
        }

        // Check for city capture after moving or killing
        City city = GridUtils.GetCityAtPosition(unit.transform.position);
        if (city != null && city.isPlayerOwned && !unit.isPlayerOwned)
        {
            OnCityCaptured(false);
        }
    }

    public void OnCityCaptured(bool capturedByPlayer)
    {
        if (gameOver)
            return;

        gameOver = true;

        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.HideAllMoveOutlines();
            UnitSelectionManager.Instance.ClearSelection();
        }

        if (TileHoverManager.Instance != null)
        {
            TileHoverManager.Instance.ClearSelection();
        }

        if (CityUIManager.Instance != null)
        {
            CityUIManager.Instance.ClosePanel();
        }

        string message;
        if (currentMode == GameMode.Hotseat)
        {
            message = capturedByPlayer ? "Player 1 wins!" : "Player 2 wins!";
        }
        else
        {
            message = capturedByPlayer ? "You Win!" : "You Lose!";
        }

        if (SoundManager.Instance != null)
        {
            // In local Hotseat (and Play-by-Post), both sides are humans, so always treat game-over as a "win" cue.
            bool playWinCue = (currentMode == GameMode.VsAI) ? capturedByPlayer : true;
            SoundManager.Instance.PlayGameOver(playWinCue);
        }

        if (gameOverText != null)
        {
            gameOverText.text = message;
            gameOverText.gameObject.SetActive(true);
        }

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }

        Debug.Log("Game Over: " + message);
    }

    void CollectPlayerIncome()
    {
        if (gameOver) return;

        int income = 0;
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        foreach (City city in cities)
        {
            if (city.isPlayerOwned)
            {
                income += goldPerCity;
            }
        }

        if (income > 0)
        {
            AddGold(true, income);
        }
    }

    void CollectAIGold()
    {
        if (gameOver) return;

        int income = 0;
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        foreach (City city in cities)
        {
            if (!city.isPlayerOwned)
            {
                income += goldPerCity;
            }
        }

        if (income > 0)
        {
            AddGold(false, income);
        }
    }

    void EnsureTurnAndGoldTexts()
    {
        if (turnText == null || goldText == null)
        {
            var texts = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var t in texts)
            {
                if (t == null) continue;
                string name = t.name.ToLower();

                if (turnText == null && name.Contains("turn"))
                {
                    turnText = t;
                    t.gameObject.SetActive(true);
                }

                if (goldText == null && name.Contains("gold"))
                {
                    goldText = t;
                    t.gameObject.SetActive(true);
                }
            }
        }

        if (turnText == null)
        {
            Debug.LogWarning("TurnManager: No turnText assigned and none found in scene (name containing 'turn').");
        }
    }

    void UpdateGoldText()
    {
        EnsureTurnAndGoldTexts();

        if (goldText == null)
            return;

        int displayGold = playerGold;

        // In Hotseat and Play-by-Post, the second side's gold
        // is stored in aiGold, so show that when it's their turn.
        if ((currentMode == GameMode.Hotseat || currentMode == GameMode.PlayByPost) && !isPlayerTurn)
        {
            displayGold = aiGold;
        }

        // Force single-line display.
        goldText.enableAutoSizing = false;
        goldText.textWrappingMode = TextWrappingModes.NoWrap;
        goldText.overflowMode = TextOverflowModes.Overflow;
        goldText.richText = false;
        goldText.text = $"Gold {displayGold}";
    }

    /// <summary>
    /// Computes which tiles are currently visible for a given side
    /// based on cities and units that side owns, using the same
    /// radius rules as the fog-of-war visuals.
    /// This does not mutate any TileVisibility state.
    /// </summary>
    HashSet<TileVisibility> ComputeVisibilityForSide(bool sideIsPlayerOwned)
    {
        HashSet<TileVisibility> visibleTiles = new HashSet<TileVisibility>();

        if (gridManager == null)
            return visibleTiles;

        // Reveal around cities owned by this side
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        foreach (City city in cities)
        {
            if (city.isPlayerOwned != sideIsPlayerOwned)
                continue;

            for (int dx = -visibilityRadius; dx <= visibilityRadius; dx++)
            {
                for (int dy = -visibilityRadius; dy <= visibilityRadius; dy++)
                {
                    int tx = city.x + dx;
                    int ty = city.y + dy;
                    if (gridManager.TryGetTile(tx, ty, out TileVisibility tile))
                    {
                        visibleTiles.Add(tile);
                    }
                }
            }
        }

        // Reveal around units owned by this side
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            if (unit.isPlayerOwned != sideIsPlayerOwned)
                continue;

            if (!gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility originTile))
                continue;

            for (int dx = -visibilityRadius; dx <= visibilityRadius; dx++)
            {
                for (int dy = -visibilityRadius; dy <= visibilityRadius; dy++)
                {
                    int tx = originTile.gridX + dx;
                    int ty = originTile.gridY + dy;
                    if (gridManager.TryGetTile(tx, ty, out TileVisibility tile))
                    {
                        visibleTiles.Add(tile);
                    }
                }
            }
        }

        return visibleTiles;
    }

    public void RecalculatePlayerVisibility()
    {
        if (gridManager == null)
            return;

        bool currentSideIsPlayerOwned = true;
        if (currentMode == GameMode.Hotseat || currentMode == GameMode.PlayByPost)
        {
            // Player 1 uses isPlayerOwned=true, Player 2 uses isPlayerOwned=false
            currentSideIsPlayerOwned = isPlayerTurn;
        }

        // Reset current visibility for this side (keep per-side explored memory)
        foreach (TileVisibility tile in gridManager.GetAllTiles())
        {
            tile.SetVisibleForSide(false, currentSideIsPlayerOwned);
        }

        // Compute which tiles should be visible for this side
        HashSet<TileVisibility> visibleTiles = ComputeVisibilityForSide(currentSideIsPlayerOwned);
        foreach (TileVisibility tile in visibleTiles)
        {
            tile.SetVisibleForSide(true, currentSideIsPlayerOwned);
        }

        // Hide enemy units that are not in visible tiles
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            bool isCurrentSideUnit = unit.isPlayerOwned == currentSideIsPlayerOwned;
            bool isVisible = isCurrentSideUnit;
            if (!isVisible)
            {
                if (gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile))
                {
                    isVisible = tile.isVisibleNow;
                }
            }
            unit.SetFogVisibility(isVisible, isCurrentSideUnit);
        }
    }

    void UpdateTurnText()
    {
        EnsureTurnAndGoldTexts();

        if (turnText == null)
            return;

        string who = GetCurrentSideName();

        // Make sure the turn label is always visible and single-line.
        turnText.enableAutoSizing = false;
        turnText.textWrappingMode = TextWrappingModes.NoWrap;
        turnText.overflowMode = TextOverflowModes.Overflow;
        turnText.richText = false;
        turnText.color = Color.white;
        turnText.text = $"Turn {turnNumber} - {who}";
    }

    string GetDefaultSavePath()
    {
        return Path.Combine(Application.persistentDataPath, autoSaveFileName);
    }

    public void AutoSaveIfEnabled()
    {
        if (!autoSaveEnabled || isLoadingFromSave)
            return;

        SaveToFile();
    }

    public void SaveToFile(string path = null)
    {
        string targetPath = path;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            targetPath = GetDefaultSavePath();
        }

        if (gridManager == null)
        {
            Debug.LogWarning("Cannot save: gridManager is null.");
            return;
        }

        GameSave save = BuildCurrentSave();
        if (save == null)
            return;

        string json = JsonUtility.ToJson(save, true);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            File.WriteAllText(targetPath, json);
            Debug.Log("Game saved to " + targetPath);
        }
        catch (IOException ex)
        {
            Debug.LogError("Failed to save game: " + ex.Message);
        }
    }

    GameSave BuildCurrentSave()
    {
        if (gridManager == null)
        {
            Debug.LogWarning("Cannot build save: gridManager is null.");
            return null;
        }

        GameSave save = new GameSave
        {
            gameId = string.IsNullOrEmpty(currentGameId) ? (currentGameId = System.Guid.NewGuid().ToString()) : currentGameId,
            mode = currentMode.ToString(),
            aiDifficulty = aiDifficulty.ToString(),
            isPlayerTurn = isPlayerTurn,
            turnNumber = turnNumber,
            playerGold = playerGold,
            aiGold = aiGold,
            gameOver = gameOver,
            visibilityRadius = visibilityRadius
        };

        // Cities
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        foreach (City city in cities)
        {
            save.cities.Add(new SavedCity
            {
                x = city.x,
                y = city.y,
                isPlayerOwned = city.isPlayerOwned,
                hasRecruitedThisTurn = city.hasRecruitedThisTurn
            });
        }

        // Units
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            Vector3 pos = unit.transform.position;
            save.units.Add(new SavedUnit
            {
                isPlayerOwned = unit.isPlayerOwned,
                x = pos.x,
                y = pos.y,
                z = pos.z,
                currentHealth = unit.currentHealth,
                movesUsedThisTurn = unit.movesUsedThisTurn,
                hasAttackedThisTurn = unit.hasAttackedThisTurn
            });
        }

        // Tiles (seen state per side)
        foreach (TileVisibility tile in gridManager.GetAllTiles())
        {
            tile.GetSeenState(out bool playerSeen, out bool opponentSeen);
            save.tiles.Add(new SavedTile
            {
                x = tile.gridX,
                y = tile.gridY,
                playerSeen = playerSeen,
                opponentSeen = opponentSeen
            });
        }

        return save;
    }

    /// <summary>
    /// Copy the current game state as JSON into the clipboard (for Play-by-Post).
    /// </summary>
    public void CopyCurrentStateToClipboard()
    {
        GameSave current = BuildCurrentSave();
        if (current == null)
        {
            return;
        }

        GameSave saveForExport = current;

        // For Play-by-Post we build a snapshot that already
        // represents the *next* side's turn so the receiving
        // player can simply load and start playing.
        if (currentMode == GameMode.PlayByPost)
        {
            // Deep-copy via JSON so we don't mutate live state.
            string tmp = JsonUtility.ToJson(current);
            saveForExport = JsonUtility.FromJson<GameSave>(tmp);

            PreparePlayByPostNextTurnSnapshot(saveForExport);
        }

        string json = JsonUtility.ToJson(saveForExport, playByPostExportPretty);
        GUIUtility.systemCopyBuffer = json;
        Debug.Log($"Play-by-Post JSON copied to clipboard ({json.Length} chars).");
    }

    public bool LoadFromFile(string path = null)
    {
        string targetPath = path;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            targetPath = GetDefaultSavePath();
        }

        if (!File.Exists(targetPath))
        {
            Debug.LogWarning("No save file found at " + targetPath);
            return false;
        }

        if (gridManager == null)
        {
            Debug.LogWarning("Cannot load: gridManager is null.");
            return false;
        }

        string json = File.ReadAllText(targetPath);
        GameSave save;
        isLoadingFromSave = true;
        try
        {
            save = JsonUtility.FromJson<GameSave>(json);
        }
        catch (System.Exception ex)
        {
            isLoadingFromSave = false;
            Debug.LogError("Failed to parse save: " + ex.Message);
            return false;
        }

        // Basic grid validation: ensure saved tiles fit current grid.
        int maxTileX = -1;
        int maxTileY = -1;
        foreach (SavedTile t in save.tiles)
        {
            if (t.x > maxTileX) maxTileX = t.x;
            if (t.y > maxTileY) maxTileY = t.y;
        }
        if (maxTileX >= gridManager.width || maxTileY >= gridManager.height)
        {
            isLoadingFromSave = false;
            Debug.LogError($"Save grid ({maxTileX + 1}x{maxTileY + 1}) does not fit current grid ({gridManager.width}x{gridManager.height}). Aborting load.");
            return false;
        }

        // Apply basic state
        if (System.Enum.TryParse(save.mode, out GameMode loadedMode))
        {
            currentMode = loadedMode;
        }

        // AI difficulty (optional for older saves)
        aiDifficulty = AIDifficulty.Level1;
        if (!string.IsNullOrEmpty(save.aiDifficulty) &&
            System.Enum.TryParse(save.aiDifficulty, out AIDifficulty loadedDifficulty))
        {
            aiDifficulty = loadedDifficulty;
        }
        currentGameId = string.IsNullOrEmpty(save.gameId) ? System.Guid.NewGuid().ToString() : save.gameId;
        isPlayerTurn = save.isPlayerTurn;
        turnNumber = save.turnNumber;
        playerGold = save.playerGold;
        aiGold = save.aiGold;
        gameOver = save.gameOver;
        visibilityRadius = save.visibilityRadius;
        isHotseatHandoff = false;
        isPlayByPostWaitingForExport = false;
        Time.timeScale = 1f;

        // Clear units
        Unit[] existingUnits = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit u in existingUnits)
        {
            if (u != null)
            {
                Destroy(u.gameObject);
            }
        }

        // Restore cities
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        foreach (City city in cities)
        {
            city.stationedUnit = null;
        }

        foreach (SavedCity c in save.cities)
        {
            foreach (City city in cities)
            {
                if (city.x == c.x && city.y == c.y)
                {
                    city.isPlayerOwned = c.isPlayerOwned;
                    city.hasRecruitedThisTurn = c.hasRecruitedThisTurn;
                }
            }
        }

        // Restore units
        GameObject prefab = unitPrefab;
        if (prefab == null)
        {
            // fallback: try grab from any city
            foreach (City city in cities)
            {
                if (city.warriorPrefab != null)
                {
                    prefab = city.warriorPrefab;
                    break;
                }
            }
        }

        if (prefab == null)
        {
            isLoadingFromSave = false;
            Debug.LogError("No unit prefab configured (TurnManager.unitPrefab or any City.warriorPrefab). Cannot restore units; load aborted.");
            return false;
        }

        foreach (SavedUnit u in save.units)
        {
            Vector3 pos = new Vector3(u.x, u.y, u.z);
            GameObject go = Instantiate(prefab, pos, Quaternion.identity);
            Unit unit = go.GetComponent<Unit>();
            if (unit != null)
            {
                unit.isPlayerOwned = u.isPlayerOwned;
                unit.currentHealth = Mathf.Clamp(u.currentHealth, 1, unit.maxHealth);
                unit.movesUsedThisTurn = Mathf.Clamp(u.movesUsedThisTurn, 0, unit.maxMovesPerTurn);
                unit.hasAttackedThisTurn = u.hasAttackedThisTurn;
                bool isCurrentSideUnit = currentMode != GameMode.Hotseat || (unit.isPlayerOwned == isPlayerTurn);
                unit.SetFogVisibility(true, isCurrentSideUnit); // will be updated after visibility recalculation
            }

            OwnedSprite owned = go.GetComponent<OwnedSprite>();
            if (owned != null)
            {
                owned.SetOwner(u.isPlayerOwned);
            }

            // Link to city if occupying one
            foreach (City city in cities)
            {
                if (Vector3.SqrMagnitude(city.transform.position - pos) < 0.001f)
                {
                    city.stationedUnit = go;
                    if (unit != null)
                    {
                        unit.currentCity = city;
                    }
                    break;
                }
            }
        }

        // Update move outlines for the active side based on loaded move state
        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.ClearSelection();
            UnitSelectionManager.Instance.RefreshMoveOutlinesForCurrentTurn();
        }

        // Restore tile seen state.
        // For Play-by-Post we keep things simpler and recompute fog
        // purely from current cities/units, ignoring remembered
        // exploration from the save to avoid asymmetric artefacts.
        foreach (TileVisibility tile in gridManager.GetAllTiles())
        {
            tile.ResetVisibilityState();
        }

        if (currentMode != GameMode.PlayByPost)
        {
            foreach (SavedTile t in save.tiles)
            {
                if (gridManager.TryGetTile(t.x, t.y, out TileVisibility tile))
                {
                    // Use current side to drive visuals; for symmetric modes
                    // (Hotseat), respect whose turn it is.
                    bool activeSideIsPlayer = true;
                    if (currentMode == GameMode.Hotseat)
                    {
                        activeSideIsPlayer = isPlayerTurn;
                    }

                    tile.SetSeenState(t.playerSeen, t.opponentSeen, activeSideIsPlayer);
                }
            }
        }

        // After loading, ensure selection is cleared and move outlines
        // reflect the loaded movement state.
        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.ClearSelection();
            UnitSelectionManager.Instance.RefreshMoveOutlinesForCurrentTurn();
        }

        UpdateGoldText();
        RecalculatePlayerVisibility();
        UpdateTurnText();
        Debug.Log("Game loaded from " + targetPath);
        isLoadingFromSave = false;
        return true;
    }

    public bool TrySpendGold(bool forPlayer, int amount)
    {
        if (amount <= 0)
            return true;

        bool shouldPlayInvalid = false;
        if (SoundManager.Instance != null && !gameOver)
        {
            if (currentMode == GameMode.VsAI)
            {
                // Only play invalid for the human player's gold in Vs AI.
                shouldPlayInvalid = forPlayer && isPlayerTurn;
            }
            else if (currentMode == GameMode.Hotseat || currentMode == GameMode.PlayByPost)
            {
                // In Hotseat/Play-by-Post, both banks can be human-controlled depending on whose turn it is.
                bool spendingSideIsActive = isPlayerTurn == forPlayer;
                shouldPlayInvalid = spendingSideIsActive && IsHumanTurn();
            }
            else
            {
                // Fallback: treat "forPlayer" as human.
                shouldPlayInvalid = forPlayer;
            }
        }

        if (forPlayer)
        {
            if (playerGold < amount)
            {
                if (shouldPlayInvalid)
                {
                    SoundManager.Instance.PlayInvalid();
                }
                return false;
            }

            playerGold -= amount;
            UpdateGoldText();
            return true;
        }
        else
        {
            if (aiGold < amount)
            {
                if (shouldPlayInvalid)
                {
                    SoundManager.Instance.PlayInvalid();
                }
                return false;
            }

            aiGold -= amount;
            if ((currentMode == GameMode.Hotseat || currentMode == GameMode.PlayByPost) && !isPlayerTurn)
            {
                UpdateGoldText();
            }
            return true;
        }
    }

    public void AddGold(bool forPlayer, int amount)
    {
        if (amount <= 0)
            return;

        if (forPlayer)
        {
            playerGold += amount;
            UpdateGoldText();
        }
        else
        {
            aiGold += amount;
            if ((currentMode == GameMode.Hotseat || currentMode == GameMode.PlayByPost) && !isPlayerTurn)
            {
                UpdateGoldText();
            }
        }
    }

    /// <summary>
    /// Mutates the given save so that it represents the start
    /// of the next side's turn for Play-by-Post exports.
    /// </summary>
    private void PreparePlayByPostNextTurnSnapshot(GameSave save)
    {
        // Determine which side will act next.
        bool nextIsPlayer = !isPlayerTurn; // Player 1 -> Player 2, Player 2 -> Player 1
        save.isPlayerTurn = nextIsPlayer;

        // Advance the turn counter only when we wrap back to Player 1.
        save.turnNumber = turnNumber;
        if (!isPlayerTurn && nextIsPlayer)
        {
            // We just finished Player 2 locally; next save is Player 1, new round.
            save.turnNumber = turnNumber + 1;
        }

        // Reset recruitment flags for the side whose turn is starting
        // and compute their income from owned cities.
        int income = 0;
        foreach (SavedCity city in save.cities)
        {
            if (city.isPlayerOwned == nextIsPlayer)
            {
                city.hasRecruitedThisTurn = false;
                income += goldPerCity;
            }
        }

        if (income > 0)
        {
            if (nextIsPlayer)
            {
                save.playerGold += income;
            }
            else
            {
                save.aiGold += income;
            }
        }

        // Reset movement for units belonging to the side
        // that will act next so they have fresh moves.
        foreach (SavedUnit unit in save.units)
        {
            if (unit.isPlayerOwned == nextIsPlayer)
            {
                unit.movesUsedThisTurn = 0;
            }
        }
    }
}
