using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class MultiplayerGamesListView : MonoBehaviour
{
    [SerializeField] private MainMenuController mainMenu;
    [SerializeField] private RectTransform contentRoot;
    [SerializeField] private MultiplayerGameRow rowPrefab;
    [SerializeField] private TMP_Text emptyText;

    private void OnEnable()
    {
        if (mainMenu != null)
        {
            mainMenu.ActivePbpGamesChanged += HandleActivePbpGamesChanged;
        }

        RefreshNow();
    }

    private void OnDisable()
    {
        if (mainMenu != null)
        {
            mainMenu.ActivePbpGamesChanged -= HandleActivePbpGamesChanged;
        }
    }

    public void ManualRefresh()
    {
        RefreshNow();
    }

    public void RefreshNow()
    {
        if (contentRoot == null)
        {
            return;
        }

        for (int i = contentRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = contentRoot.GetChild(i);
            if (child != null)
            {
                Destroy(child.gameObject);
            }
        }

        if (mainMenu == null || rowPrefab == null)
        {
            SetEmptyStateVisible(true);
            return;
        }

        IReadOnlyList<SaveManifestService.ManifestGameSummary> games = mainMenu.ActivePbpGames;
        bool hasGames = games != null && games.Count > 0;
        SetEmptyStateVisible(!hasGames);
        if (!hasGames)
        {
            return;
        }

        for (int i = 0; i < games.Count; i++)
        {
            MultiplayerGameRow row = Instantiate(rowPrefab, contentRoot);
            row.Bind(mainMenu, games[i]);
        }
    }

    private void HandleActivePbpGamesChanged()
    {
        RefreshNow();
    }

    private void SetEmptyStateVisible(bool visible)
    {
        if (emptyText != null)
        {
            emptyText.gameObject.SetActive(visible);
        }
    }
}
