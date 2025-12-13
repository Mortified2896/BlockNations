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
    public GameObject bottomButtonsRoot; // e.g. the Next/Menu button row
    public TMP_Text cityNameText;
    public TMP_Text ownerText;
    public TMP_Text recruitWarriorButtonText;

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

        if (turnManager == null)
        {
            turnManager = TurnManager.Instance;
        }

        EnsureRecruitWarriorButtonReference();
    }

    /// <summary>
    /// Called by CityClickHandler when a city is clicked.
    /// Only opens the UI for the side whose turn it currently is.
    /// </summary>
    public void OnCityClicked(City city)
    {
        if (city == null) return;

        Debug.Log("CityUIManager.OnCityClicked: " + city.name + " (isPlayerOwned=" + city.isPlayerOwned + ")");

        // Only open UI for cities controlled by the current human side
        if (turnManager != null && !turnManager.CanControlCity(city))
        {
            Debug.Log("Clicked on a city that cannot act this turn, city UI remains closed.");
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayInvalid();
            }
            return;
        }
        else if (turnManager == null && !city.isPlayerOwned)
        {
            Debug.Log("Clicked on a non-player city but TurnManager is not assigned; UI remains closed.");
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayInvalid();
            }
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

        // Try to auto-wire the Recruit Warrior button label if it
        // has not been assigned in the Inspector.
        EnsureRecruitWarriorButtonReference();

        panelRoot.SetActive(true);

        // Hide the default bottom HUD buttons while the city panel is open.
        if (bottomButtonsRoot != null)
        {
            bottomButtonsRoot.SetActive(false);
        }
        Debug.Log("CityUIManager.OpenPanel");

        if (cityNameText != null && currentCity != null)
        {
            cityNameText.text = currentCity.name;
        }

        if (ownerText != null && currentCity != null)
        {
            if (turnManager != null &&
                (turnManager.currentMode == TurnManager.GameMode.Hotseat ||
                 turnManager.currentMode == TurnManager.GameMode.PlayByPost))
            {
                ownerText.text = currentCity.isPlayerOwned ? "Player 1 City" : "Player 2 City";
            }
            else
            {
                ownerText.text = currentCity.isPlayerOwned ? "Player City" : "AI City";
            }
        }

        // Show the cost directly on the Recruit Warrior button label
        if (recruitWarriorButtonText != null && turnManager != null)
        {
            recruitWarriorButtonText.text = $"Recruit Warrior ({turnManager.warriorCost} Gold)";
        }
    }

    private void EnsureRecruitWarriorButtonReference()
    {
        if (recruitWarriorButtonText != null || panelRoot == null)
        {
            return;
        }

        TMP_Text[] texts = panelRoot.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text t in texts)
        {
            if (t == null) continue;
            string lowerName = t.name.ToLower();
            string lowerText = t.text != null ? t.text.ToLower() : string.Empty;
            if (lowerName.Contains("recruit") || lowerText.Contains("recruit"))
            {
                recruitWarriorButtonText = t;
                break;
            }
        }
    }

    public void ClosePanel()
    {
        currentCity = null;

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }

        // Restore the default bottom HUD buttons when the city panel closes.
        if (bottomButtonsRoot != null)
        {
            bottomButtonsRoot.SetActive(true);
        }
    }

    /// <summary>
    /// Hook this up to the "Recruit Warrior" button.
    /// Spawns a warrior for the selected city if allowed.
    /// </summary>
    public void OnRecruitWarriorButton()
    {
        if (currentCity == null)
        {
            Debug.LogWarning("Tried to recruit a Warrior but no city is selected.");
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayInvalid();
            }
            return;
        }

        if (turnManager != null && !turnManager.CanControlCity(currentCity))
        {
            Debug.Log("Ignored Recruit Warrior click because it is not this city's turn.");
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayInvalid();
            }
            return;
        }

        // Let the city handle spawning the Warrior
        currentCity.SpawnWarrior();
    }
}
