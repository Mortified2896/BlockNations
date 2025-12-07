using UnityEngine;
using TMPro;

/// <summary>
/// Manages the city UI panel and recruitment actions.
/// </summary>
public class CityUIManager : MonoBehaviour
{
    public static CityUIManager Instance { get; private set; }

    [Header("References")]
    public TurnManager turnManager;

    [Header("UI")]
    public GameObject panelRoot;
    public TMP_Text cityNameText;
    public TMP_Text ownerText;

    private City currentCity;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Ensure the panel starts hidden
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    /// <summary>
    /// Called by CityClickHandler when a city is clicked.
    /// Only opens the UI for player-owned cities during the player's turn.
    /// </summary>
    public void OnCityClicked(City city)
    {
        if (city == null) return;

        // Only open UI for player-owned cities
        if (!city.isPlayerOwned)
        {
            Debug.Log("Clicked on an AI city, city UI remains closed.");
            return;
        }

        currentCity = city;
        OpenPanel();
    }

    public void OpenPanel()
    {
        if (panelRoot == null)
        {
            Debug.LogWarning("CityUIManager panelRoot is not assigned.");
            return;
        }

        panelRoot.SetActive(true);

        if (cityNameText != null && currentCity != null)
        {
            cityNameText.text = currentCity.name;
        }

        if (ownerText != null && currentCity != null)
        {
            ownerText.text = currentCity.isPlayerOwned ? "Player City" : "AI City";
        }
    }

    public void ClosePanel()
    {
        currentCity = null;

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    /// <summary>
    /// Hook this up to the "Recruit Warrior" button.
    /// For now it just logs; later we will spawn units.
    /// </summary>
    public void OnRecruitWarriorButton()
    {
        if (currentCity == null)
        {
            Debug.LogWarning("Tried to recruit a Warrior but no city is selected.");
            return;
        }

        if (turnManager != null && !turnManager.isPlayerTurn)
        {
            Debug.Log("Ignored Recruit Warrior click because it is not the player's turn.");
            return;
        }

        // Let the city handle spawning the Warrior
        currentCity.SpawnWarrior();
    }
}
