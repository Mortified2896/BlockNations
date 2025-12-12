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
        public string version = "1";
        public string gameId;
        public string mode;
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

        if (string.IsNullOrEmpty(currentGameId))
        {
            currentGameId = System.Guid.NewGuid().ToString();
        }

        ResetRecruitmentForPlayerCities();
        // Give income only to the side whose turn it is.
        // The other side receives income at the start of
        // its first turn (via Begin*Turn / AITurn).
        CollectPlayerIncome();
        UpdateGoldText();
        UpdateTurnText();
        RecalculatePlayerVisibility();
        Debug.Log("Game start. " + GetCurrentSideName() + " Turn " + turnNumber);

        if (GameModeSelection.TryConsume(out GameMode pendingMode))
        {
            SetGameMode(pendingMode);
        }
        else if (currentMode == GameMode.None)
        {
            SetGameMode(GameMode.VsAI);
            Debug.Log("No mode preselected. Defaulting to Vs AI.");
        }

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

    /// <summary>
    /// Collects tiles that would be visible around the given grid center
    /// using the same radius rule as the fog-of-war system, without
    /// mutating any TileVisibility state.
    /// </summary>
    void CollectVisibleTilesAround(int centerX, int centerY, int radius, HashSet<TileVisibility> buffer)
    {
        if (gridManager == null || buffer == null)
            return;

        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int tx = centerX + dx;
                int ty = centerY + dy;
                if (gridManager.TryGetTile(tx, ty, out TileVisibility tile))
                {
                    buffer.Add(tile);
                }
            }
        }
    }

    void RunAI()
    {
        // 1) Recruit from each AI city (one unit per city per AI turn, if the city is empty)
        City[] allCities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        foreach (City city in allCities)
        {
            if (!city.isPlayerOwned && city.CanRecruit())
            {
                city.SpawnWarrior();
            }
        }

        // 2) Move AI units toward the nearest player unit or city,
        // but only using information it could have under fog-of-war.
        Unit[] allUnits = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);

        HashSet<TileVisibility> aiVisibleTiles = null;
        if (gridManager != null)
        {
            aiVisibleTiles = new HashSet<TileVisibility>();

            // Vision from AI-owned cities
            foreach (City city in allCities)
            {
                if (!city.isPlayerOwned)
                {
                    CollectVisibleTilesAround(city.x, city.y, visibilityRadius, aiVisibleTiles);
                }
            }

            // Vision from AI-owned units
            foreach (Unit unit in allUnits)
            {
                if (unit == null || unit.isPlayerOwned)
                    continue;

                if (gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile))
                {
                    CollectVisibleTilesAround(tile.gridX, tile.gridY, visibilityRadius, aiVisibleTiles);
                }
            }
        }

        // Collect player targets (units + cities) that are in AI-visible tiles.
        List<Vector3> playerTargets = new List<Vector3>();

        foreach (Unit unit in allUnits)
        {
            if (!unit.isPlayerOwned)
                continue;

            // If we could not determine visibility, fall back to the old behavior.
            if (aiVisibleTiles == null || aiVisibleTiles.Count == 0 || gridManager == null)
            {
                playerTargets.Add(unit.transform.position);
                continue;
            }

            if (gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile) &&
                aiVisibleTiles.Contains(tile))
            {
                playerTargets.Add(unit.transform.position);
            }
        }

        foreach (City city in allCities)
        {
            if (!city.isPlayerOwned)
                continue;

            if (aiVisibleTiles == null || aiVisibleTiles.Count == 0 || gridManager == null)
            {
                playerTargets.Add(city.transform.position);
                continue;
            }

            if (gridManager.TryGetTile(city.x, city.y, out TileVisibility tile) &&
                aiVisibleTiles.Contains(tile))
            {
                playerTargets.Add(city.transform.position);
            }
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

        foreach (Unit unit in allUnits)
        {
            if (unit.isPlayerOwned)
                continue;

            // Reset AI unit movement for this AI turn
            unit.ResetMovementForTurn();

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
                MoveAIUnitOneStep(unit, bestTarget.Value, stepSize);
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
                Debug.Log("AI unit moved into defeated enemy tile at " + newPos);
            }
        }
        else
        {
            // Empty tile: move normally
            unit.transform.position = newPos;
            unit.RegisterMove();

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

        // Reveal around cities owned by the current side
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        foreach (City city in cities)
        {
            if (city.isPlayerOwned == currentSideIsPlayerOwned)
            {
                RevealRadius(city.x, city.y, visibilityRadius);
            }
        }

        // Reveal around units owned by the current side
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            if (unit.isPlayerOwned != currentSideIsPlayerOwned)
                continue;

            if (gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile))
            {
                RevealRadius(tile.gridX, tile.gridY, visibilityRadius);
            }
        }

        // Hide enemy units that are not in visible tiles
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

    private void RevealRadius(int centerX, int centerY, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int tx = centerX + dx;
                int ty = centerY + dy;
                if (gridManager.TryGetTile(tx, ty, out TileVisibility tile))
                {
                    bool sideIsPlayer = true;
                    if (currentMode == GameMode.Hotseat || currentMode == GameMode.PlayByPost)
                    {
                        sideIsPlayer = isPlayerTurn;
                    }
                    tile.SetVisibleForSide(true, sideIsPlayer);
                }
            }
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

        if (forPlayer)
        {
            if (playerGold < amount)
                return false;

            playerGold -= amount;
            UpdateGoldText();
            return true;
        }
        else
        {
            if (aiGold < amount)
                return false;

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
