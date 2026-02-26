using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Simple button hooks for saving/loading and returning to the main menu.
/// Drop this on a UI GameObject and wire the public methods to Buttons.
/// </summary>
public class GameMenuActions : MonoBehaviour
{
    [Header("Scenes")]
    public string mainMenuSceneName = "MainMenu";

    private const string PlayByPostGameIdKey = "pbp_gameId";
    private const string ReturnToMultiplayerPaneKey = "ui_returnToMultiplayerPane";
    private static GameObject tutorialLeaveConfirmRoot;

    public void SaveGame()
    {
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.SaveToFile();
        }
    }

    public void LoadGame()
    {
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.LoadFromFile();
        }
    }

    public void QuitToMainMenu()
    {
        Debug.Log("QuitToMainMenu clicked");

        if (TurnManager.Instance != null && TurnManager.Instance.IsPbpEndgameMenuExitBlocked)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("QuitToMainMenu blocked: PBp endgame submit flow is active; use the endgame button.");
#endif
            return;
        }

        if (TutorialGate.IsActive)
        {
            ShowTutorialLeaveConfirm();
            return;
        }

        DoQuitToMainMenu();
    }

    private void DoQuitToMainMenu()
    {
        TurnManager tm = TurnManager.Instance;
        bool shouldReturnToMultiplayerPane = tm != null
            ? tm.currentMode == TurnManager.GameMode.PlayByPost
            : !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty));

        if (shouldReturnToMultiplayerPane)
        {
            PlayerPrefs.SetInt(ReturnToMultiplayerPaneKey, 1);
            PlayerPrefs.Save();
        }

        if (!string.IsNullOrEmpty(mainMenuSceneName))
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(mainMenuSceneName);
        }
        else
        {
            Debug.LogWarning("Main menu scene name is not set on GameMenuActions.");
        }
    }

    private void ShowTutorialLeaveConfirm()
    {
        EnsureEventSystem();

        if (tutorialLeaveConfirmRoot == null)
        {
            tutorialLeaveConfirmRoot = BuildTutorialLeaveConfirmUI();
        }

        tutorialLeaveConfirmRoot.SetActive(true);
    }

    private static void EnsureEventSystem()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null)
            return;

        Debug.LogWarning("GameMenuActions: No EventSystem detected. Please add one to the gameplay scene.");
    }

    private GameObject BuildTutorialLeaveConfirmUI()
    {
        GameObject go = new GameObject("TutorialLeaveConfirm", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform root = new GameObject("Root", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(go.transform, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        Image bg = new GameObject("Dim", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.SetParent(root, false);
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        bg.raycastTarget = true;

        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        RectTransform panelRt = panel.GetComponent<RectTransform>();
        panelRt.SetParent(root, false);
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(760f, 0f);

        Image panelImg = panel.GetComponent<Image>();
        panelImg.color = new Color(0.08f, 0.10f, 0.14f, 0.96f);
        panelImg.raycastTarget = true;

        VerticalLayoutGroup v = panel.GetComponent<VerticalLayoutGroup>();
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.spacing = 16f;
        v.padding = new RectOffset(28, 28, 24, 20);

        ContentSizeFitter fitter = panel.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI title = CreateTMP(panel.transform, "Title", 48, FontStyles.Bold);
        title.text = "Leave Tutorial?";

        TextMeshProUGUI body = CreateTMP(panel.transform, "Body", 32, FontStyles.Normal);
        body.text = "Return to the main menu?\n\nYour tutorial progress will be reset.";

        GameObject row = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        RectTransform rowRt = row.GetComponent<RectTransform>();
        rowRt.SetParent(panel.transform, false);
        HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = false;
        h.spacing = 16f;
        h.childAlignment = TextAnchor.MiddleCenter;

        Button cancel = CreateButton(row.transform, "Cancel", out TextMeshProUGUI cancelLabel, minWidth: 0f, flexibleWidth: 1f, minHeight: 76f, fontSize: 34);
        cancelLabel.text = "Cancel";
        cancel.onClick.AddListener(() =>
        {
            if (tutorialLeaveConfirmRoot != null)
                tutorialLeaveConfirmRoot.SetActive(false);
        });

        Button leave = CreateButton(row.transform, "Leave", out TextMeshProUGUI leaveLabel, minWidth: 0f, flexibleWidth: 1f, minHeight: 76f, fontSize: 34);
        leaveLabel.text = "Leave";
        leave.onClick.AddListener(() =>
        {
            TutorialGate.SetActive(false);
            TutorialGate.ClearAll();
            DoQuitToMainMenu();
        });

        go.SetActive(false);
        return go;
    }

    private static TextMeshProUGUI CreateTMP(Transform parent, string name, int fontSize, FontStyles style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        LayoutElement le = go.GetComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.flexibleHeight = 0f;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.raycastTarget = false;
        tmp.enableAutoSizing = false;
        tmp.textWrappingMode = TextWrappingModes.Normal;

        return tmp;
    }

    private static Button CreateButton(Transform parent, string name, out TextMeshProUGUI label, float minWidth, float flexibleWidth, float minHeight, int fontSize)
    {
        GameObject go = new GameObject(name + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = new Color(0.18f, 0.52f, 0.82f, 0.95f);

        LayoutElement le = go.GetComponent<LayoutElement>();
        le.minHeight = minHeight;
        le.minWidth = minWidth;
        le.flexibleWidth = flexibleWidth;

        Button btn = go.GetComponent<Button>();

        GameObject txt = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        txt.transform.SetParent(go.transform, false);
        RectTransform rt = txt.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(10f, 6f);
        rt.offsetMax = new Vector2(-10f, -6f);

        label = txt.GetComponent<TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.enableAutoSizing = false;

        return btn;
    }
}
