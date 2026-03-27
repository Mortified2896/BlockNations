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
    public TMP_Text recruitWarriorButtonText;

    private City currentCity;
    private static bool hasLoggedMissingBottomStripController;

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
        if (panelRoot == null)
        {
            Debug.LogWarning("CityUIManager panelRoot is not assigned.");
            return;
        }

        // Try to auto-wire the Recruit Warrior button label if it
        // has not been assigned in the Inspector.
        EnsureRecruitWarriorButtonReference();

        BottomStripController bottomStrip = GetBottomStripController();
        if (bottomStrip != null)
        {
            // Claim the bottom strip mode first so handoffs do not flash DefaultHud.
            bottomStrip.SetMode(BottomStripController.BottomStripMode.CityUi);
        }

        if (UnitUIManager.Instance != null)
        {
            UnitUIManager.Instance.ClosePanel();
        }

        panelRoot.SetActive(true);

        if (cityNameText != null && currentCity != null)
        {
            cityNameText.text = currentCity.name;
        }

        if (ownerText != null && currentCity != null)
        {
            if (turnManager != null &&
                turnManager.currentMode == TurnManager.GameMode.PlayByPost)
            {
                ownerText.text = currentCity.isPlayerOwned ? "Player 1 City" : "Player 2 City";
            }
            else
            {
                ownerText.text = currentCity.isPlayerOwned ? "Player City" : "AI City";
            }
        }

        // Show the cost directly on the button label (cost on a separate line).
        if (recruitWarriorButtonText != null && turnManager != null)
        {
            recruitWarriorButtonText.text = $"Warrior\n({turnManager.warriorCost} Gold)";
            recruitWarriorButtonText.alignment = TextAlignmentOptions.Center;
            recruitWarriorButtonText.textWrappingMode = TextWrappingModes.NoWrap;
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
            if (lowerName.Contains("recruit") || lowerText.Contains("recruit") ||
                lowerName.Contains("warrior") || lowerText.Contains("warrior"))
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

        BottomStripController bottomStrip = GetBottomStripController();
        if (bottomStrip != null)
        {
            bottomStrip.ReleaseMode(BottomStripController.BottomStripMode.CityUi);
        }
    }

    public bool IsPanelOpen => panelRoot != null && panelRoot.activeSelf;

    private static BottomStripController GetBottomStripController()
    {
        BottomStripController bottomStrip = BottomStripController.Instance;
        if (bottomStrip == null)
        {
            bottomStrip = UnityEngine.Object.FindFirstObjectByType<BottomStripController>();
        }

        if (bottomStrip == null)
        {
            BottomStripController[] controllers = UnityEngine.Object.FindObjectsByType<BottomStripController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (controllers != null && controllers.Length > 0)
            {
                bottomStrip = controllers[0];
            }
        }

        if (bottomStrip == null && !hasLoggedMissingBottomStripController)
        {
            hasLoggedMissingBottomStripController = true;
            Debug.LogError("CityUIManager requires BottomStripController in the gameplay scene.");
        }

        return bottomStrip;
    }
    public void OnRecruitWarriorButton()
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

            if (PbpDebugSettingsLoader.EnableInputLogs)
            {
                Debug.Log(
                    $"[recruit] click city={cityName} cityNull={(currentCity == null)} mode={mode} isPlayerTurn={(tm != null ? tm.isPlayerTurn : false)} pbpSeat={pbpSeat} cityOwned={(currentCity != null ? currentCity.isPlayerOwned : false)} canControl={canControl} goldP1={goldP1} goldP2={goldP2} hasUnit={(currentCity != null && currentCity.stationedUnit != null)} recruitedThisTurn={(currentCity != null && currentCity.hasRecruitedThisTurn)}"
                );
            }
        }
#endif

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
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayInvalid();
            }
            return;
        }

        // Let the city handle spawning the Warrior
        currentCity.SpawnWarrior();
        lastRecruitAttemptSucceeded = currentCity.stationedUnit != null;

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
