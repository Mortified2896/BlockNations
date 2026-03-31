using UnityEngine;

/// <summary>
/// Manages the city UI panel and recruitment actions.
/// </summary>
public class CityUIManager : MonoBehaviour
{
    public static CityUIManager Instance { get; private set; }

    [Header("References")]
    public TurnManager turnManager;

    private City currentCity;
    private bool isPanelOpen;

    [Header("Debug")]
    public int lastRecruitAttemptFrame = -1;
    public bool lastRecruitAttemptSucceeded = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (turnManager == null)
        {
            turnManager = TurnManager.Instance;
        }
    }

    /// <summary>
    /// Called by CityClickHandler when a city is clicked.
    /// Only opens the UI for the side whose turn it currently is.
    /// </summary>
    public void OnCityClicked(City city)
    {
        if (city == null) return;

        // Only open UI for cities controlled by the current human side
        if (turnManager != null && !turnManager.CanControlCity(city))
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayInvalid();
            }
            return;
        }
        else if (turnManager == null && !city.isPlayerOwned)
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayInvalid();
            }
            return;
        }

        // Re-tap toggle: tapping the same already-open city closes the panel.
        if (IsPanelOpen && currentCity == city)
        {
            ClosePanel();
            return;
        }

        currentCity = city;
        OpenPanel();
    }

    public void OpenPanel()
    {
        if (currentCity == null)
        {
            Debug.LogWarning("CityUIManager.OpenPanel called with no selected city.");
            return;
        }

        if (UnitUIManager.Instance != null)
        {
            UnitUIManager.Instance.ClosePanel();
        }

        isPanelOpen = true;
    }

    public void ClosePanel()
    {
        currentCity = null;
        isPanelOpen = false;
    }

    public bool IsPanelOpen => isPanelOpen;
    public City CurrentCity => currentCity;
    public string CurrentCityName => currentCity != null ? currentCity.name : string.Empty;
    public string CurrentOwnerText
    {
        get
        {
            if (currentCity == null)
            {
                return string.Empty;
            }

            TurnManager tm = ResolveTurnManager();
            if (tm != null && tm.currentMode == TurnManager.GameMode.PlayByPost)
            {
                return currentCity.isPlayerOwned ? "Player 1 City" : "Player 2 City";
            }

            return currentCity.isPlayerOwned ? "Player City" : "AI City";
        }
    }

    public string RecruitWarriorLabel
    {
        get
        {
            return BuildRecruitLabel(UnitRegistry.WarriorTypeId);
        }
    }

    public string RecruitScoutLabel
    {
        get
        {
            return BuildRecruitLabel(UnitRegistry.ScoutTypeId);
        }
    }

    private TurnManager ResolveTurnManager()
    {
        if (turnManager == null)
        {
            turnManager = TurnManager.Instance;
        }

        return turnManager;
    }

    public void OnRecruitWarriorButton()
    {
        OnRecruitUnitButton(UnitRegistry.WarriorTypeId);
    }

    public void OnRecruitScoutButton()
    {
        OnRecruitUnitButton(UnitRegistry.ScoutTypeId);
    }

    private string BuildRecruitLabel(string unitTypeId)
    {
        UnitDefinition unitDefinition = UnitRegistry.GetDefinitionOrDefault(unitTypeId);
        TurnManager tm = ResolveTurnManager();
        if (tm != null)
        {
            return $"{unitDefinition.DisplayName}\n({tm.GetRecruitCost(unitDefinition.TypeId)} Gold)";
        }

        return unitDefinition.DisplayName;
    }

    private void OnRecruitUnitButton(string unitTypeId)
    {
        lastRecruitAttemptFrame = Time.frameCount;
        lastRecruitAttemptSucceeded = false;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        {
            TurnManager tm = turnManager != null ? turnManager : TurnManager.Instance;
            string pbpGameId = PlayerPrefs.GetString("pbp_gameId", string.Empty);
            int pbpSeat = LocalPlayerSeatStore.TryGetSeat(pbpGameId, out int resolvedSeat) ? resolvedSeat : -1;
            string cityName = currentCity != null ? currentCity.name : "<null>";
            string mode = tm != null ? tm.currentMode.ToString() : "<null>";
            bool canControl = tm != null && currentCity != null && tm.CanControlCity(currentCity);
            int goldP1 = tm != null ? tm.playerGold : -1;
            int goldP2 = tm != null ? tm.aiGold : -1;
            string unitLabel = UnitRegistry.GetDefinitionOrDefault(unitTypeId).DisplayName;

            if (PbpDebugSettingsLoader.EnableInputLogs)
            {
                Debug.Log(
                    $"[recruit] click unit={unitLabel} city={cityName} cityNull={(currentCity == null)} mode={mode} isPlayerTurn={(tm != null ? tm.isPlayerTurn : false)} pbpSeat={pbpSeat} cityOwned={(currentCity != null ? currentCity.isPlayerOwned : false)} canControl={canControl} goldP1={goldP1} goldP2={goldP2} hasUnit={(currentCity != null && currentCity.stationedUnit != null)} recruitedThisTurn={(currentCity != null && currentCity.hasRecruitedThisTurn)}"
                );
            }
        }
#endif

        if (currentCity == null)
        {
            Debug.LogWarning($"Tried to recruit a {UnitRegistry.GetDefinitionOrDefault(unitTypeId).DisplayName} but no city is selected.");
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayInvalid();
            }
            return;
        }

        if (turnManager != null && !turnManager.CanControlCity(currentCity))
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayInvalid();
            }
            return;
        }

        lastRecruitAttemptSucceeded = currentCity.TrySpawnUnit(unitTypeId);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        {
            TurnManager tm = turnManager != null ? turnManager : TurnManager.Instance;
            int goldP1 = tm != null ? tm.playerGold : -1;
            int goldP2 = tm != null ? tm.aiGold : -1;
            if (PbpDebugSettingsLoader.EnableInputLogs)
            {
                Debug.Log(
                    $"[recruit] result city={(currentCity != null ? currentCity.name : "<null>")} succeeded={lastRecruitAttemptSucceeded} hasUnitNow={(currentCity != null && currentCity.stationedUnit != null)} goldP1={goldP1} goldP2={goldP2}"
                );
            }
        }
#endif
    }
}
