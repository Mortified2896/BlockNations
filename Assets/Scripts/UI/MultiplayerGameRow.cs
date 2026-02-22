using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class MultiplayerGameRow : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text subtitleText;
    // Inspector wiring: assign the clickable row button (can be the existing resume/open button).
    [SerializeField] private Button resumeButton;

    private string gameId;
    private SaveManifestService.ManifestGameSummary boundSummary;
    private UnityAction resumeClickAction;

    public void Bind(MainMenuController menu, SaveManifestService.ManifestGameSummary summary)
    {
        boundSummary = summary;
        gameId = summary.gameId;

        if (titleText != null)
        {
            titleText.text = BuildTitle(gameId);
        }

        if (subtitleText != null)
        {
            subtitleText.text = BuildSubtitle(summary);
        }

        if (resumeButton == null)
        {
            return;
        }

        if (resumeClickAction != null)
        {
            resumeButton.onClick.RemoveListener(resumeClickAction);
            resumeClickAction = null;
        }

        if (menu == null || string.IsNullOrWhiteSpace(gameId))
        {
            resumeButton.interactable = false;
            return;
        }

        resumeClickAction = () => menu.OpenSelectedGameDetails(boundSummary);
        resumeButton.onClick.AddListener(resumeClickAction);
        resumeButton.interactable = true;
    }

    private void OnDisable()
    {
        if (resumeButton != null && resumeClickAction != null)
        {
            resumeButton.onClick.RemoveListener(resumeClickAction);
            resumeClickAction = null;
        }
    }

    private static string BuildTitle(string rawGameId)
    {
        if (string.IsNullOrWhiteSpace(rawGameId))
        {
            return "Game Unknown";
        }

        string shortId = rawGameId.Length <= 8 ? rawGameId : rawGameId.Substring(0, 8);
        return $"Game {shortId}";
    }

    private static string BuildSubtitle(SaveManifestService.ManifestGameSummary summary)
    {
        return MainMenuController.BuildPlayByPostTurnSubtitle(summary);
    }
}
