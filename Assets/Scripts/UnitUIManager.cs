using UnityEngine;
using TMPro;

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
    }

    public void ShowUnit(Unit unit)
    {
        currentUnit = unit;

        if (panelRoot == null)
        {
            Debug.LogWarning("UnitUIManager panelRoot is not assigned.");
            return;
        }

        panelRoot.SetActive(true);

        // Hide the default bottom HUD buttons while the unit panel is open.
        if (bottomButtonsRoot != null)
        {
            bottomButtonsRoot.SetActive(false);
        }

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
        if (bottomButtonsRoot != null)
        {
            bottomButtonsRoot.SetActive(true);
        }
    }
}
