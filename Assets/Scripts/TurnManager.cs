using System.Collections;
using UnityEngine;
using TMPro;   // for TMP_Text

public class TurnManager : MonoBehaviour
{
    public static TurnManager Instance { get; private set; }

    [Header("Turn State")]
    public bool isPlayerTurn = true;
    public int turnNumber = 1;
    public bool gameOver = false;

    [Header("Economy")]
    public int playerGold = 0;
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

    [Header("References")]
    public GridManager gridManager;
    public int visibilityRadius = 1;

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
        ResetRecruitmentForPlayerCities();
        CollectPlayerIncome();
        CollectAIGold();
        UpdateGoldText();
        UpdateTurnText();
        RecalculatePlayerVisibility();
        Debug.Log("Game start. Player Turn " + turnNumber);
    }

    void Update()
    {
        if (gameOver)
            return;

        // Optional: keep Space for PC testing
        if (isPlayerTurn && Input.GetKeyDown(KeyCode.Space))
        {
            OnEndTurnButtonPressed();
        }
    }

    // 🚩 This is what the UI Button will call
    public void OnEndTurnButtonPressed()
    {
        if (!isPlayerTurn || gameOver)
        {
            // Ignore clicks if it's not the player's turn
            return;
        }

        EndPlayerTurn();
    }

    void EndPlayerTurn()
    {
        Debug.Log("Player ends Turn " + turnNumber);
        isPlayerTurn = false;
        UpdateTurnText();

        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.HideAllMoveOutlines();
        }

        // Start AI turn
        StartCoroutine(AITurn());
    }

    IEnumerator AITurn()
    {
        if (gameOver)
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
            UnitSelectionManager.Instance.ResetMovementForPlayerUnits();
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
        Debug.Log("Back to Player. Turn " + turnNumber + " begins.");
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
        foreach (City city in allCities)
        {
            if (!city.isPlayerOwned && city.CanRecruit())
            {
                city.SpawnWarrior();
            }
        }

        // 2) Move AI units toward the nearest player unit or city
        Unit[] allUnits = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);

        // Collect player targets (units + cities)
        System.Collections.Generic.List<Vector3> playerTargets = new System.Collections.Generic.List<Vector3>();
        foreach (Unit unit in allUnits)
        {
            if (unit.isPlayerOwned)
            {
                playerTargets.Add(unit.transform.position);
            }
        }
        foreach (City city in allCities)
        {
            if (city.isPlayerOwned)
            {
                playerTargets.Add(city.transform.position);
            }
        }

        if (playerTargets.Count == 0)
        {
            // Nothing to move toward yet
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

        string message = capturedByPlayer ? "You Win!" : "You Lose!";

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

    void UpdateGoldText()
    {
        if (goldText != null)
        {
            goldText.text = $"Gold: {playerGold}";
        }
    }

    public void RecalculatePlayerVisibility()
    {
        if (gridManager == null)
            return;

        // Reset all tiles to not visible
        foreach (TileVisibility tile in gridManager.GetAllTiles())
        {
            tile.SetVisible(false);
        }

        // Reveal around player-owned cities
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        foreach (City city in cities)
        {
            if (city.isPlayerOwned)
            {
                RevealRadius(city.x, city.y, visibilityRadius);
            }
        }

        // Reveal around player-owned units
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            if (!unit.isPlayerOwned)
                continue;

            if (gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile))
            {
                RevealRadius(tile.gridX, tile.gridY, visibilityRadius);
            }
        }

        // Hide enemy units that are not in visible tiles
        foreach (Unit unit in units)
        {
            bool isVisible = true;
            if (!unit.isPlayerOwned)
            {
                if (gridManager.TryGetTileAtWorldPosition(unit.transform.position, out TileVisibility tile))
                {
                    isVisible = tile.isVisibleNow;
                }
            }
            unit.SetFogVisibility(isVisible || unit.isPlayerOwned);
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
                    tile.SetVisible(true);
                }
            }
        }
    }

    void UpdateTurnText()
    {
        if (turnText == null) return;

        string who = isPlayerTurn ? "Player" : "AI";
        turnText.text = $"Turn {turnNumber} - {who}";
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
        }
    }
}
