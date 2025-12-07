using System.Collections;
using UnityEngine;
using TMPro;   // for TMP_Text

public class TurnManager : MonoBehaviour
{
    [Header("Turn State")]
    public bool isPlayerTurn = true;
    public int turnNumber = 1;

    [Header("AI Settings")]
    public float aiTurnDelay = 1f; // seconds the AI "thinks" before ending its turn

    [Header("UI")]
    public TMP_Text turnText;      // assign in Inspector

    void Start()
    {
        ResetRecruitmentForPlayerCities();
        UpdateTurnText();
        Debug.Log("Game start. Player Turn " + turnNumber);
    }

    void Update()
    {
        // Optional: keep Space for PC testing
        if (isPlayerTurn && Input.GetKeyDown(KeyCode.Space))
        {
            OnEndTurnButtonPressed();
        }
    }

    // 🚩 This is what the UI Button will call
    public void OnEndTurnButtonPressed()
    {
        if (!isPlayerTurn)
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

        // Start AI turn
        StartCoroutine(AITurn());
    }

    IEnumerator AITurn()
    {
        Debug.Log("AI Turn " + turnNumber + " started. AI is thinking...");

        // Simulate thinking time
        yield return new WaitForSeconds(aiTurnDelay);

        // TODO: later: AI moves units, builds, etc.
        Debug.Log("AI finished Turn " + turnNumber);

        // Back to player
        turnNumber++;
        BeginPlayerTurn();
    }

    void BeginPlayerTurn()
    {
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

        UpdateTurnText();
        Debug.Log("Back to Player. Turn " + turnNumber + " begins.");
    }

    void ResetRecruitmentForPlayerCities()
    {
        City[] cities = FindObjectsOfType<City>();
        foreach (City city in cities)
        {
            if (city.isPlayerOwned)
            {
                city.hasRecruitedThisTurn = false;
            }
        }
    }

    void UpdateTurnText()
    {
        if (turnText == null) return;

        string who = isPlayerTurn ? "Player" : "AI";
        turnText.text = $"Turn {turnNumber} - {who}";
    }
}
