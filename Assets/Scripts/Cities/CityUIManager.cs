using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System;

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
    private Button cachedBottomMenuButton;
    private Button cachedBottomEndTurnOrNextButton;
    private GameObject bottomPopupRoot;

    [Header("Tutorial/Debug")]
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

        if (BottomStripController.Instance == null)
        {
            EnsureBottomPopupRootReference();
            UpdateBottomPopupActiveState();
        }

        if (turnManager == null)
        {
            turnManager = TurnManager.Instance;
        }

        EnsureRecruitWarriorButtonReference();
        EnsureBottomButtonsRootReference();
    }

    /// <summary>
    /// Called by CityClickHandler when a city is clicked.
    /// Only opens the UI for the side whose turn it currently is.
    /// </summary>
    public void OnCityClicked(City city)
    {
        if (city == null) return;

        if (TutorialGate.IsActive && TutorialGate.CanClickCity != null && !TutorialGate.CanClickCity(city))
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayInvalid();
            }
            return;
        }

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
        EnsureBottomButtonsRootReference();

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
        if (bottomStrip == null)
        {
            SetBottomPopupActive(true);

            // Fallback when the controller is not present in the scene.
            SetBottomHudButtonsActive(false);
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
        else
        {
            UpdateBottomPopupActiveState();

            // Fallback when the controller is not present in the scene.
            EnsureBottomButtonsRootReference();
            SetBottomHudButtonsActive(true);
        }
    }

    public bool IsPanelOpen => panelRoot != null && panelRoot.activeSelf;

    private BottomStripController GetBottomStripController()
    {
        return BottomStripController.Instance;
    }

    private void EnsureBottomPopupRootReference()
    {
        if (bottomPopupRoot != null || panelRoot == null)
            return;

        Transform parent = panelRoot.transform.parent;
        if (parent != null)
        {
            bottomPopupRoot = parent.gameObject;
        }
    }

    private void SetBottomPopupActive(bool active)
    {
        EnsureBottomPopupRootReference();
        if (bottomPopupRoot == null)
            return;

        if (bottomPopupRoot.activeSelf != active)
        {
            bottomPopupRoot.SetActive(active);
        }
    }

    private void UpdateBottomPopupActiveState()
    {
        bool shouldBeActive = IsPanelOpen || (UnitUIManager.Instance != null && UnitUIManager.Instance.IsPanelOpen);
        SetBottomPopupActive(shouldBeActive);
    }

    private void SetBottomHudButtonsActive(bool active)
    {
        // Safety: never hide the city UI itself.
        if (bottomButtonsRoot != null && panelRoot != null && panelRoot.transform.IsChildOf(bottomButtonsRoot.transform))
        {
            bottomButtonsRoot = null;
        }

        if (bottomButtonsRoot != null)
        {
            bottomButtonsRoot.SetActive(active);
            return;
        }

        // Fallback: toggle the buttons directly if we can't determine a common root.
        if (cachedBottomMenuButton != null)
            cachedBottomMenuButton.gameObject.SetActive(active);
        if (cachedBottomEndTurnOrNextButton != null)
            cachedBottomEndTurnOrNextButton.gameObject.SetActive(active);
    }

    private void EnsureBottomButtonsRootReference()
    {
        if (bottomButtonsRoot != null && cachedBottomMenuButton != null && cachedBottomEndTurnOrNextButton != null)
            return;

        Button[] buttons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Button menuButton = null;
        Button endTurnOrNextButton = null;
        float bestMenuY = float.PositiveInfinity;
        float bestNextY = float.PositiveInfinity;

        foreach (Button b in buttons)
        {
            if (b == null) continue;
            if (!b.gameObject.activeInHierarchy) continue;

            string label = GetButtonLabel(b);
            if (string.IsNullOrWhiteSpace(label))
                continue;

            float centerY = GetButtonScreenCenterY(b);

            if (string.Equals(label, "Menu", StringComparison.OrdinalIgnoreCase))
            {
                if (centerY < bestMenuY)
                {
                    bestMenuY = centerY;
                    menuButton = b;
                }
            }
            else if (string.Equals(label, "End Turn", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(label, "Next", StringComparison.OrdinalIgnoreCase))
            {
                if (centerY < bestNextY)
                {
                    bestNextY = centerY;
                    endTurnOrNextButton = b;
                }
            }
        }

        if (menuButton == null || endTurnOrNextButton == null)
            return;

        cachedBottomMenuButton = menuButton;
        cachedBottomEndTurnOrNextButton = endTurnOrNextButton;

        Transform root = FindLowestCommonAncestor(menuButton.transform, endTurnOrNextButton.transform);
        if (root == null)
            return;

        // Prefer the lowest common parent on the Menu button's path that contains the EndTurn/Next button,
        // but does NOT contain the city panel root (otherwise we'd accidentally hide the city UI too).
        Transform refined = root;
        Transform t = menuButton.transform;
        while (t != null)
        {
            if (endTurnOrNextButton.transform.IsChildOf(t))
            {
                bool containsCityPanel = panelRoot != null && panelRoot.transform != null && panelRoot.transform.IsChildOf(t);
                if (!containsCityPanel)
                {
                    refined = t;
                    break;
                }
            }
            t = t.parent;
        }

        bottomButtonsRoot = refined.gameObject;
    }

    private static string GetButtonLabel(Button b)
    {
        if (b == null)
            return null;

        TMP_Text tmp = b.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text))
            return tmp.text.Trim();

        Text txt = b.GetComponentInChildren<Text>(true);
        if (txt != null && !string.IsNullOrWhiteSpace(txt.text))
            return txt.text.Trim();

        return null;
    }

    private static float GetButtonScreenCenterY(Button b)
    {
        if (b == null)
            return float.PositiveInfinity;

        RectTransform rt = b.GetComponent<RectTransform>();
        if (rt == null)
            return float.PositiveInfinity;

        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < 4; i++)
        {
            Vector2 sp = RectTransformUtility.WorldToScreenPoint(null, corners[i]);
            min = Vector2.Min(min, sp);
            max = Vector2.Max(max, sp);
        }
        return (min.y + max.y) * 0.5f;
    }

    private static Transform FindLowestCommonAncestor(Transform a, Transform b)
    {
        if (a == null || b == null)
            return null;

        HashSet<Transform> ancestors = new HashSet<Transform>();
        Transform t = a;
        while (t != null)
        {
            ancestors.Add(t);
            t = t.parent;
        }

        t = b;
        while (t != null)
        {
            if (ancestors.Contains(t))
                return t;
            t = t.parent;
        }

        return null;
    }

    /// <summary>
    /// Hook this up to the "Recruit Warrior" button.
    /// Spawns a warrior for the selected city if allowed.
    /// </summary>
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

            Debug.Log(
                $"[recruit] click city={cityName} cityNull={(currentCity == null)} mode={mode} isPlayerTurn={(tm != null ? tm.isPlayerTurn : false)} pbpSeat={pbpSeat} cityOwned={(currentCity != null ? currentCity.isPlayerOwned : false)} canControl={canControl} goldP1={goldP1} goldP2={goldP2} hasUnit={(currentCity != null && currentCity.stationedUnit != null)} recruitedThisTurn={(currentCity != null && currentCity.hasRecruitedThisTurn)}"
            );
        }
#endif

        if (TutorialGate.IsActive && TutorialGate.CanRecruitWarrior != null && !TutorialGate.CanRecruitWarrior())
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayInvalid();
            }
            return;
        }

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
        lastRecruitAttemptSucceeded = currentCity.stationedUnit != null;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        {
            TurnManager tm = turnManager != null ? turnManager : TurnManager.Instance;
            int goldP1 = tm != null ? tm.playerGold : -1;
            int goldP2 = tm != null ? tm.aiGold : -1;
            Debug.Log(
                $"[recruit] result city={(currentCity != null ? currentCity.name : "<null>")} succeeded={lastRecruitAttemptSucceeded} hasUnitNow={(currentCity != null && currentCity.stationedUnit != null)} goldP1={goldP1} goldP2={goldP2}"
            );
        }
#endif
    }
}
