using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System;

/// <summary>
/// Shows basic information about the currently selected unit.
/// </summary>
public class UnitUIManager : MonoBehaviour
{
    public static UnitUIManager Instance { get; private set; }

    [Header("UI")]
    public GameObject panelRoot;
    public GameObject bottomButtonsRoot; // e.g. the Next/Menu button row
    public TMP_Text unitNameText;
    public TMP_Text healthText;
    public TMP_Text attackText;
    public TMP_Text defenseText;

    private Unit currentUnit;
    private Button cachedBottomMenuButton;
    private Button cachedBottomEndTurnOrNextButton;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }

        EnsureBottomButtonsRootReference();
    }

    public void ShowUnit(Unit unit)
    {
        currentUnit = unit;

        if (CityUIManager.Instance != null)
        {
            CityUIManager.Instance.ClosePanel();
        }

        if (panelRoot == null)
        {
            Debug.LogWarning("UnitUIManager panelRoot is not assigned.");
            return;
        }

        EnsureBottomButtonsRootReference();
        panelRoot.SetActive(true);

        // Hide the default bottom HUD buttons while the unit panel is open.
        SetBottomHudButtonsActive(false);

        if (unitNameText != null && unit != null)
        {
            // Strip Unity's "(Clone)" suffix so the UI shows a clean unit name.
            string rawName = unit.name;
            const string cloneSuffix = "(Clone)";
            if (rawName.EndsWith(cloneSuffix))
            {
                rawName = rawName.Substring(0, rawName.Length - cloneSuffix.Length).TrimEnd();
            }
            unitNameText.text = rawName;
        }

        if (healthText != null && unit != null)
        {
            healthText.text = $"HP: {unit.currentHealth}/{unit.maxHealth}";
        }

        if (attackText != null && unit != null)
        {
            attackText.text = $"ATK: {unit.attack}";
        }

        if (defenseText != null && unit != null)
        {
            defenseText.text = $"DEF: {unit.defense}";
        }
    }

    public void ClosePanel()
    {
        currentUnit = null;

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }

        // Restore the default bottom HUD buttons when the unit panel closes.
        EnsureBottomButtonsRootReference();
        SetBottomHudButtonsActive(true);
    }

    private void SetBottomHudButtonsActive(bool active)
    {
        // Safety: never hide the unit UI itself.
        if (bottomButtonsRoot != null && panelRoot != null && panelRoot.transform.IsChildOf(bottomButtonsRoot.transform))
        {
            bottomButtonsRoot = null;
        }

        if (bottomButtonsRoot != null)
        {
            bottomButtonsRoot.SetActive(active);
            return;
        }

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
        // but does NOT contain the unit panel root (otherwise we'd accidentally hide the unit UI too).
        Transform refined = root;
        Transform t = menuButton.transform;
        while (t != null)
        {
            if (endTurnOrNextButton.transform.IsChildOf(t))
            {
                bool containsUnitPanel = panelRoot != null && panelRoot.transform != null && panelRoot.transform.IsChildOf(t);
                if (!containsUnitPanel)
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
}
