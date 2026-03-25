using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class GameplayCityPanelUITKView : MonoBehaviour
{
    private const string LayoutResourceName = "GameplayCityPanel_UITK";
    private const string ThemeResourceName = "GameplayTopHud_UITK_Theme";
    private const string LegacyCityPanelName = "CityPanel";

    [Header("Spike Toggle")]
    [SerializeField] private bool enableCityPanelUITK = true;

    [Header("Optional Source Overrides")]
    [SerializeField] private CityUIManager cityUIManager;
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private RectTransform legacyCityPanelRoot;

    private UIDocument uiDocument;
    private VisualTreeAsset layoutAsset;
    private ThemeStyleSheet themeAsset;

    private VisualElement root;
    private VisualElement hudRoot;
    private VisualElement cityPanelContainer;
    private Label cityNameLabel;
    private Label ownerLabel;
    private Button recruitWarriorButton;
    private Button closeButton;

    private bool uiReady;
    private bool callbacksBound;
    private bool warnedMissingPanelSettings;
    private bool warnedMissingLayout;
    private bool warnedMissingControls;

    private CanvasGroup legacyCityPanelCanvasGroup;
    private bool cachedLegacyState;
    private float cachedLegacyAlpha = 1f;
    private bool cachedLegacyInteractable = true;
    private bool cachedLegacyBlocksRaycasts = true;
    private bool legacyPanelHidden;

    private Rect lastSafeArea = Rect.zero;
    private Vector2Int lastScreenSize = Vector2Int.zero;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
        if (layoutAsset == null)
        {
            layoutAsset = Resources.Load<VisualTreeAsset>(LayoutResourceName);
        }

        if (themeAsset == null)
        {
            themeAsset = Resources.Load<ThemeStyleSheet>(ThemeResourceName);
        }
    }

    private void OnEnable()
    {
        ResolveSceneReferences(force: true);
        CacheUiElements(force: true);
    }

    private void OnDisable()
    {
        RestoreLegacyCityPanel();
        ClearUiCache();
    }

    private void OnDestroy()
    {
        RestoreLegacyCityPanel();
    }

    private void Update()
    {
        if (!enableCityPanelUITK)
        {
            DisableOverlay();
            return;
        }

        if (!ResolveSceneReferences(force: false))
        {
            DisableOverlay();
            return;
        }

        if (!EnsureUiReady())
        {
            RestoreLegacyCityPanel();
            return;
        }

        RefreshUiState();
        ApplySafeArea(force: false);
        HideLegacyCityPanel();
    }

    private bool ResolveSceneReferences(bool force)
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        if (cityUIManager == null || force)
        {
            cityUIManager = CityUIManager.Instance;
            if (cityUIManager == null)
            {
                cityUIManager = UnityEngine.Object.FindFirstObjectByType<CityUIManager>();
            }
        }

        if (turnManager == null || force)
        {
            if (cityUIManager != null)
            {
                turnManager = cityUIManager.turnManager;
            }

            if (turnManager == null)
            {
                turnManager = TurnManager.Instance;
            }

            if (turnManager == null)
            {
                turnManager = UnityEngine.Object.FindFirstObjectByType<TurnManager>();
            }
        }

        if (legacyCityPanelRoot == null || force)
        {
            legacyCityPanelRoot = ResolveLegacyCityPanelRoot(gameObject.scene);
        }

        return uiDocument != null && cityUIManager != null;
    }

    private RectTransform ResolveLegacyCityPanelRoot(Scene scene)
    {
        if (cityUIManager != null && cityUIManager.panelRoot != null)
        {
            RectTransform cityPanelRect = cityUIManager.panelRoot.transform as RectTransform;
            if (cityPanelRect != null)
            {
                return cityPanelRect;
            }
        }

        RectTransform[] rects = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];
            if (rect == null || rect.gameObject.scene != scene)
            {
                continue;
            }

            if (string.Equals(rect.name, LegacyCityPanelName, StringComparison.Ordinal))
            {
                return rect;
            }
        }

        return null;
    }

    private bool EnsureUiReady()
    {
        if (uiDocument == null)
        {
            return false;
        }

        if (!uiDocument.enabled)
        {
            uiDocument.enabled = true;
            uiReady = false;
        }

        if (uiDocument.panelSettings == null)
        {
            if (!warnedMissingPanelSettings)
            {
                warnedMissingPanelSettings = true;
                Debug.LogWarning("GameplayCityPanelUITKView: UIDocument requires a PanelSettings asset assigned in scene.", this);
            }

            return false;
        }

        warnedMissingPanelSettings = false;

        if (uiDocument.panelSettings.themeStyleSheet == null && themeAsset != null)
        {
            uiDocument.panelSettings.themeStyleSheet = themeAsset;
        }

        if (uiDocument.visualTreeAsset == null)
        {
            if (layoutAsset == null)
            {
                layoutAsset = Resources.Load<VisualTreeAsset>(LayoutResourceName);
            }

            if (layoutAsset == null)
            {
                if (!warnedMissingLayout)
                {
                    warnedMissingLayout = true;
                    Debug.LogWarning("GameplayCityPanelUITKView: GameplayCityPanel_UITK layout not found in Resources.", this);
                }

                return false;
            }

            uiDocument.visualTreeAsset = layoutAsset;
        }

        warnedMissingLayout = false;

        return CacheUiElements(force: false);
    }

    private bool CacheUiElements(bool force)
    {
        if (uiDocument == null)
        {
            return false;
        }

        VisualElement currentRoot = uiDocument.rootVisualElement;
        if (currentRoot == null)
        {
            return false;
        }

        if (!force && uiReady && root == currentRoot)
        {
            return true;
        }

        UnbindButtons();

        root = currentRoot;
        hudRoot = root.Q<VisualElement>("CityPanelHudRoot") ?? root;
        cityPanelContainer = root.Q<VisualElement>("CityPanelContainer");
        cityNameLabel = root.Q<Label>("CityNameLabel");
        ownerLabel = root.Q<Label>("CityOwnerLabel");
        recruitWarriorButton = root.Q<Button>("RecruitWarriorButton");
        closeButton = root.Q<Button>("CityCloseButton");

        if (cityPanelContainer == null || cityNameLabel == null || recruitWarriorButton == null)
        {
            if (!warnedMissingControls)
            {
                warnedMissingControls = true;
                Debug.LogWarning("GameplayCityPanelUITKView: CityPanelContainer/CityNameLabel/RecruitWarriorButton not found in UIDocument source asset.", this);
            }

            uiReady = false;
            return false;
        }

        warnedMissingControls = false;
        ConfigurePickingModes();
        BindButtons();
        ApplySafeArea(force: true);
        cityPanelContainer.style.display = DisplayStyle.None;
        uiReady = true;
        return true;
    }

    private void ConfigurePickingModes()
    {
        if (root != null)
        {
            root.pickingMode = PickingMode.Ignore;
        }

        if (hudRoot != null)
        {
            hudRoot.pickingMode = PickingMode.Ignore;
        }

        if (cityPanelContainer != null)
        {
            cityPanelContainer.pickingMode = PickingMode.Ignore;
        }

        if (recruitWarriorButton != null)
        {
            recruitWarriorButton.pickingMode = PickingMode.Position;
        }

        if (closeButton != null)
        {
            closeButton.pickingMode = PickingMode.Position;
        }
    }

    private void BindButtons()
    {
        if (callbacksBound)
        {
            return;
        }

        if (recruitWarriorButton != null)
        {
            recruitWarriorButton.clicked += HandleRecruitWarriorClicked;
        }

        if (closeButton != null)
        {
            closeButton.clicked += HandleCloseClicked;
        }

        callbacksBound = true;
    }

    private void UnbindButtons()
    {
        if (!callbacksBound)
        {
            return;
        }

        if (recruitWarriorButton != null)
        {
            recruitWarriorButton.clicked -= HandleRecruitWarriorClicked;
        }

        if (closeButton != null)
        {
            closeButton.clicked -= HandleCloseClicked;
        }

        callbacksBound = false;
    }

    private void HandleRecruitWarriorClicked()
    {
        if (cityUIManager != null)
        {
            cityUIManager.OnRecruitWarriorButton();
        }

        RefreshUiState();
    }

    private void HandleCloseClicked()
    {
        if (cityUIManager != null)
        {
            cityUIManager.ClosePanel();
        }
    }

    private void RefreshUiState()
    {
        if (!uiReady || cityPanelContainer == null)
        {
            return;
        }

        bool panelOpen = cityUIManager != null && cityUIManager.IsPanelOpen;
        cityPanelContainer.style.display = panelOpen ? DisplayStyle.Flex : DisplayStyle.None;

        if (!panelOpen)
        {
            return;
        }

        if (cityNameLabel != null)
        {
            cityNameLabel.text = GetCityName();
        }

        if (ownerLabel != null)
        {
            string ownerText = cityUIManager != null && cityUIManager.ownerText != null
                ? cityUIManager.ownerText.text
                : string.Empty;
            ownerLabel.text = ownerText;
            ownerLabel.style.display = string.IsNullOrWhiteSpace(ownerText) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        if (recruitWarriorButton != null)
        {
            recruitWarriorButton.text = GetRecruitWarriorLabel();
        }
    }

    private string GetCityName()
    {
        if (cityUIManager != null &&
            cityUIManager.cityNameText != null &&
            !string.IsNullOrWhiteSpace(cityUIManager.cityNameText.text))
        {
            return cityUIManager.cityNameText.text;
        }

        return "City";
    }

    private string GetRecruitWarriorLabel()
    {
        if (cityUIManager != null &&
            cityUIManager.recruitWarriorButtonText != null &&
            !string.IsNullOrWhiteSpace(cityUIManager.recruitWarriorButtonText.text))
        {
            return cityUIManager.recruitWarriorButtonText.text;
        }

        if (turnManager != null)
        {
            return $"Warrior\n({turnManager.warriorCost} Gold)";
        }

        return "Warrior";
    }

    private void HideLegacyCityPanel()
    {
        if (legacyPanelHidden || legacyCityPanelRoot == null)
        {
            return;
        }

        legacyCityPanelCanvasGroup = legacyCityPanelRoot.GetComponent<CanvasGroup>();
        if (legacyCityPanelCanvasGroup == null)
        {
            legacyCityPanelCanvasGroup = legacyCityPanelRoot.gameObject.AddComponent<CanvasGroup>();
        }

        cachedLegacyAlpha = legacyCityPanelCanvasGroup.alpha;
        cachedLegacyInteractable = legacyCityPanelCanvasGroup.interactable;
        cachedLegacyBlocksRaycasts = legacyCityPanelCanvasGroup.blocksRaycasts;
        cachedLegacyState = true;

        legacyCityPanelCanvasGroup.alpha = 0f;
        legacyCityPanelCanvasGroup.interactable = false;
        legacyCityPanelCanvasGroup.blocksRaycasts = false;
        legacyPanelHidden = true;
    }

    private void RestoreLegacyCityPanel()
    {
        if (!legacyPanelHidden || legacyCityPanelCanvasGroup == null)
        {
            legacyPanelHidden = false;
            return;
        }

        if (cachedLegacyState)
        {
            legacyCityPanelCanvasGroup.alpha = cachedLegacyAlpha;
            legacyCityPanelCanvasGroup.interactable = cachedLegacyInteractable;
            legacyCityPanelCanvasGroup.blocksRaycasts = cachedLegacyBlocksRaycasts;
        }

        legacyPanelHidden = false;
    }

    private void ApplySafeArea(bool force)
    {
        if (!uiReady || root == null)
        {
            return;
        }

        Rect safeArea = Screen.safeArea;
        Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
        if (screenSize.x <= 0 || screenSize.y <= 0)
        {
            return;
        }

        if (!force && safeArea == lastSafeArea && screenSize == lastScreenSize)
        {
            return;
        }

        lastSafeArea = safeArea;
        lastScreenSize = screenSize;

        float leftInset = safeArea.xMin;
        float rightInset = screenSize.x - safeArea.xMax;
        float bottomInset = safeArea.yMin;

        VisualElement safeAreaTarget = hudRoot ?? root;
        safeAreaTarget.style.paddingLeft = leftInset;
        safeAreaTarget.style.paddingRight = rightInset;
        safeAreaTarget.style.paddingTop = 0f;
        safeAreaTarget.style.paddingBottom = bottomInset;
    }

    private void DisableOverlay()
    {
        RestoreLegacyCityPanel();

        if (uiDocument != null)
        {
            uiDocument.enabled = false;
        }

        ClearUiCache();
    }

    private void ClearUiCache()
    {
        UnbindButtons();
        root = null;
        hudRoot = null;
        cityPanelContainer = null;
        cityNameLabel = null;
        ownerLabel = null;
        recruitWarriorButton = null;
        closeButton = null;
        uiReady = false;
        lastSafeArea = Rect.zero;
        lastScreenSize = Vector2Int.zero;
    }
}
