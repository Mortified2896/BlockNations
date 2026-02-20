using TMPro;
using UnityEngine;

/// <summary>
/// Inspector wiring:
/// - Attach to a stable parent (for example, the Multiplayer button root).
/// - Assign `badgeText` to the number label.
/// - Assign `badgeRoot` to the red dot GameObject that should be shown/hidden.
/// - Optionally assign `mainMenu` (defaults: auto-find MainMenuController).
/// </summary>
public class PbpBadgeView : MonoBehaviour
{
    [SerializeField] private MainMenuController mainMenu;
    [SerializeField] private GameObject badgeRoot;
    [SerializeField] private TMP_Text badgeText;

    private bool triedResolveMainMenu;
    private bool subscribed;

    private void Awake()
    {
        TryResolveMainMenu();
    }

    private void OnEnable()
    {
        TryResolveMainMenu();
        SubscribeIfPossible();
        Refresh();
    }

    private void OnDisable()
    {
        UnsubscribeIfNeeded();
    }

    public void Refresh()
    {
        int count = mainMenu != null ? mainMenu.PbpBadgeCountMyTurn : 0;
        bool visible = count > 0;

        if (badgeRoot != null)
        {
            badgeRoot.SetActive(visible);
        }

        if (badgeText != null)
        {
            badgeText.text = count >= 100 ? "99+" : count.ToString();
        }
    }

    private void TryResolveMainMenu()
    {
        if (mainMenu != null || triedResolveMainMenu)
        {
            return;
        }

        triedResolveMainMenu = true;
        mainMenu = FindObjectOfType<MainMenuController>();
    }

    private void SubscribeIfPossible()
    {
        if (subscribed || mainMenu == null)
        {
            return;
        }

        mainMenu.PbpBadgeChanged += Refresh;
        subscribed = true;
    }

    private void UnsubscribeIfNeeded()
    {
        if (!subscribed || mainMenu == null)
        {
            subscribed = false;
            return;
        }

        mainMenu.PbpBadgeChanged -= Refresh;
        subscribed = false;
    }
}
