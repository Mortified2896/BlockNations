using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class MultiplayerGameRow : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text subtitleText;
    [SerializeField] private Button resumeButton;

    private string gameId;
    private UnityAction resumeClickAction;

    public void Bind(MainMenuController menu, SaveManifestService.ManifestGameSummary summary)
    {
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

        string boundGameId = gameId;
        resumeClickAction = () => menu.ResumePlayByPostGame(boundGameId);
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
        if (TurnIndicatorService.TryGetIsMyTurn(summary.gameId, out bool isMyTurn, out _))
        {
            return isMyTurn ? "Your turn" : "Waiting...";
        }

        return BuildLegacySubtitle(summary);
    }

    private static string BuildLegacySubtitle(SaveManifestService.ManifestGameSummary summary)
    {
        string lastPlayed = string.IsNullOrWhiteSpace(summary.lastPlayedUtc) ? "-" : summary.lastPlayedUtc;
        if (string.IsNullOrWhiteSpace(summary.transportType))
        {
            return $"Last played: {lastPlayed}";
        }

        return $"Last played: {lastPlayed} | Transport: {summary.transportType}";
    }
}
