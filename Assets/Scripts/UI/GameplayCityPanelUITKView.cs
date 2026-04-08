using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class GameplayCityPanelUITKView : MonoBehaviour
{
    private const string LayoutResourceName = "GameplayCityPanel_UITK";
    private const string ThemeResourceName = "GameplayTopHud_UITK_Theme";

    [Header("Spike Toggle")]
    [SerializeField] private bool enableCityPanelUITK = true;

    [Header("Optional Source Overrides")]
    [SerializeField] private CityUIManager cityUIManager;
    [SerializeField] private TurnManager turnManager;

    private UIDocument uiDocument;
    private VisualTreeAsset layoutAsset;
    private ThemeStyleSheet themeAsset;
    private readonly UITKResponsiveSizeTierController responsiveSizeTierController = new UITKResponsiveSizeTierController();

    private VisualElement root;
    private VisualElement hudRoot;
    private VisualElement cityPanelContainer;
    private VisualElement cityRecruitRow;
    private readonly List<Button> recruitButtons = new List<Button>();
    private readonly List<string> recruitButtonUnitTypeIds = new List<string>();

    private bool uiReady;
    private bool warnedMissingPanelSettings;
    private bool warnedMissingLayout;
    private bool warnedMissingControls;

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
        ClearUiCache();
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
            return;
        }

        RefreshUiState();
        ApplySafeArea(force: false);
        responsiveSizeTierController.Apply(root);
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

        return uiDocument != null && cityUIManager != null;
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

        root = currentRoot;
        hudRoot = root.Q<VisualElement>("CityPanelHudRoot") ?? root;
        cityPanelContainer = root.Q<VisualElement>("CityPanelContainer");
        cityRecruitRow = root.Q<VisualElement>("CityRecruitRow");

        if (cityPanelContainer == null || cityRecruitRow == null)
        {
            if (!warnedMissingControls)
            {
                warnedMissingControls = true;
                Debug.LogWarning("GameplayCityPanelUITKView: CityPanelContainer/CityRecruitRow not found in UIDocument source asset.", this);
            }

            uiReady = false;
            return false;
        }

        warnedMissingControls = false;
        ConfigurePickingModes();
        RebuildRecruitButtonsIfNeeded(force: true);
        ApplySafeArea(force: true);
        responsiveSizeTierController.Apply(root);
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

        if (cityRecruitRow != null)
        {
            cityRecruitRow.pickingMode = PickingMode.Ignore;
        }

        for (int i = 0; i < recruitButtons.Count; i++)
        {
            if (recruitButtons[i] != null)
            {
                recruitButtons[i].pickingMode = PickingMode.Position;
            }
        }
    }

    private void RebuildRecruitButtonsIfNeeded(bool force)
    {
        if (cityRecruitRow == null)
        {
            return;
        }

        List<UnitDefinition> recruitableUnits = cityUIManager != null
            ? cityUIManager.GetRecruitableUnitDefinitions()
            : (turnManager != null ? turnManager.GetRecruitableOfficialUnitDefinitions() : new List<UnitDefinition>());
        bool needsRebuild = force || recruitButtonUnitTypeIds.Count != recruitableUnits.Count;
        if (!needsRebuild)
        {
            for (int i = 0; i < recruitableUnits.Count; i++)
            {
                UnitDefinition unitDefinition = recruitableUnits[i];
                string expectedTypeId = unitDefinition != null ? unitDefinition.TypeId : string.Empty;
                if (!string.Equals(recruitButtonUnitTypeIds[i], expectedTypeId, System.StringComparison.Ordinal))
                {
                    needsRebuild = true;
                    break;
                }
            }
        }

        if (!needsRebuild)
        {
            return;
        }

        cityRecruitRow.Clear();
        recruitButtons.Clear();
        recruitButtonUnitTypeIds.Clear();

        for (int i = 0; i < recruitableUnits.Count; i++)
        {
            UnitDefinition unitDefinition = recruitableUnits[i];
            if (unitDefinition == null)
            {
                continue;
            }

            string unitTypeId = unitDefinition.TypeId;
            Button recruitButton = new Button(() => HandleRecruitUnitClicked(unitTypeId));
            recruitButton.name = $"Recruit{unitDefinition.TypeId}Button";
            recruitButton.AddToClassList("gameplay-city-recruit-button");
            recruitButton.style.flexBasis = 0f;
            recruitButton.style.minWidth = 0f;
            recruitButton.style.height = 108f;
            recruitButton.style.paddingTop = 0f;
            recruitButton.style.paddingBottom = 0f;
            recruitButton.style.flexShrink = 1f;
            recruitButton.style.overflow = Overflow.Visible;
            recruitButton.style.marginLeft = i > 0 ? 12f : 0f;
            recruitButton.pickingMode = PickingMode.Position;
            cityRecruitRow.Add(recruitButton);

            recruitButtons.Add(recruitButton);
            recruitButtonUnitTypeIds.Add(unitTypeId);
        }
    }

    private void HandleRecruitUnitClicked(string unitTypeId)
    {
        if (cityUIManager != null && !string.IsNullOrWhiteSpace(unitTypeId))
        {
            cityUIManager.OnRecruitUnitButton(unitTypeId);
        }

        RefreshUiState();
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

        RebuildRecruitButtonsIfNeeded(force: false);
        for (int i = 0; i < recruitButtons.Count; i++)
        {
            if (recruitButtons[i] != null)
            {
                recruitButtons[i].text = GetRecruitLabel(recruitButtonUnitTypeIds[i]);
            }
        }
    }

    private string GetRecruitLabel(string unitTypeId)
    {
        if (cityUIManager != null &&
            !string.IsNullOrWhiteSpace(cityUIManager.GetRecruitLabel(unitTypeId)))
        {
            return cityUIManager.GetRecruitLabel(unitTypeId);
        }

        return BuildRecruitLabel(unitTypeId);
    }

    private string BuildRecruitLabel(string unitTypeId)
    {
        UnitDefinition unitDefinition = UnitRegistry.GetDefinitionOrDefault(unitTypeId);
        if (turnManager != null)
        {
            return $"{unitDefinition.DisplayName}\n({turnManager.GetRecruitCost(unitDefinition.TypeId)} Gold)";
        }

        return unitDefinition.DisplayName;
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
        if (uiDocument != null)
        {
            uiDocument.enabled = false;
        }

        ClearUiCache();
    }

    private void ClearUiCache()
    {
        responsiveSizeTierController.Reset(root);
        root = null;
        hudRoot = null;
        cityPanelContainer = null;
        cityRecruitRow = null;
        recruitButtons.Clear();
        recruitButtonUnitTypeIds.Clear();
        uiReady = false;
        lastSafeArea = Rect.zero;
        lastScreenSize = Vector2Int.zero;
    }
}
