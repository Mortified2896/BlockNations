using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

public class TileHoverManager : MonoBehaviour
{
    public static TileHoverManager Instance { get; private set; }

    private TileHighlighter hoveredTile;
    private TileHighlighter selectedTile;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private const float UiRaycastBlockLogCooldownSeconds = 1f;
    private const int UiRaycastBlockMaxHits = 5;
    private float lastUiRaycastBlockLogTime = -999f;
#endif
    private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>(16);
    private PointerEventData cachedPointerEventData;
    private EventSystem cachedPointerEventSystem;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void Update()
    {
        if (!GameplayInputOrchestrator.TryGetSnapshot(out GameplayInputOrchestrator.FrameSnapshot input))
            return;

        bool isPrimaryClickDown = input.TapThisFrame;
        bool pointerOverUi = input.WorldInputBlockedThisFrame;
        int uiRaycastHitCount = 0;
        Vector2 uiPointerPosition = input.PointerPosition;

        // Ignore world interaction when the pointer is over UI (buttons, panels, overlays).
        if (pointerOverUi)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (isPrimaryClickDown && PbpDebugSettingsLoader.EnableInputLogs)
            {
                uiRaycastHitCount = RaycastUiAtPointerPosition(uiPointerPosition, input.PointerId);
                LogUiRaycastBlockIfNeeded(uiPointerPosition, uiRaycastHitCount);

                if (TurnManager.Instance != null)
                {
                    Unit selectedForLog = UnitSelectionManager.Instance != null ? UnitSelectionManager.Instance.SelectedUnit : null;
                    TurnManager.Instance.LogPbpSelectionGateIfNeeded("ignored_over_ui", true, selectedForLog);
                }
            }
#endif
            return;
        }

        if (input.PinchActive || input.DragActive)
        {
            return;
        }

        TurnManager turnManager = TurnManager.Instance;
        if (turnManager != null)
        {
            if (turnManager.gameOver)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (isPrimaryClickDown && PbpDebugSettingsLoader.EnableInputLogs)
                {
                    Unit selectedForLog = UnitSelectionManager.Instance != null ? UnitSelectionManager.Instance.SelectedUnit : null;
                    turnManager.LogPbpSelectionGateIfNeeded("ignored_input_lock", false, selectedForLog, "game_over");
                }
#endif
                return;
            }
        }

        // 1) HOVER / CLICK: find what is under the mouse
        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        Vector2 pointer = input.PointerPosition;
        if (float.IsNaN(pointer.x) || float.IsNaN(pointer.y) ||
            float.IsInfinity(pointer.x) || float.IsInfinity(pointer.y))
        {
            return;
        }

        Vector3 mouseWorld = cam.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, 0f));
        Vector2 mousePos2D = new Vector2(mouseWorld.x, mouseWorld.y);

        RaycastHit2D[] hits = Physics2D.RaycastAll(mousePos2D, Vector2.zero);

        TileHighlighter newHover = null;
        City clickedCity = null;
        Unit clickedUnit = null;

        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;

            // First tile we find becomes the hovered tile
            if (newHover == null)
            {
                newHover = hit.collider.GetComponent<TileHighlighter>();
            }

            // Remember first city and unit under the cursor (support colliders on child objects)
            if (clickedCity == null)
            {
                clickedCity = hit.collider.GetComponentInParent<City>();
            }

            if (clickedUnit == null)
            {
                clickedUnit = hit.collider.GetComponentInParent<Unit>();
            }
        }

        bool suppressHoverHighlight = false;

        // Update hover state if we moved to a different tile
        if (newHover != hoveredTile)
        {
            if (hoveredTile != null)
                hoveredTile.SetHighlighted(false);

            if (newHover != null && !suppressHoverHighlight)
                newHover.SetHighlighted(true);

            hoveredTile = newHover;
        }

        // 2) CLICK: toggle selection
        if (isPrimaryClickDown)
        {
            // Click priority: attack enemy unit (if a friendly is selected and adjacent)
            // > move into enemy city (if a friendly is selected and in range)
            // > movable Unit on player city > player City UI > Unit > Tile

            bool hasCity = clickedCity != null;
            bool hitUnitCollider = clickedUnit != null;
            bool hasUnit = hitUnitCollider && UnitSelectionManager.Instance != null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (PbpDebugSettingsLoader.EnableInputLogs && turnManager != null && hitUnitCollider && UnitSelectionManager.Instance == null)
            {
                turnManager.LogPbpSelectionGateIfNeeded("hit_unit_but_blocked", false, clickedUnit, "selection_manager_missing");
            }
            else if (PbpDebugSettingsLoader.EnableInputLogs && turnManager != null && !hitUnitCollider && !hasCity)
            {
                Unit selectedForLog = UnitSelectionManager.Instance != null ? UnitSelectionManager.Instance.SelectedUnit : null;
                if (selectedForLog == null)
                {
                    turnManager.LogPbpSelectionGateIfNeeded("no_raycast_hit", false, null);
                }
            }
#endif

            // 2a) If a unit is selected and we clicked an enemy unit, try to move/attack onto its tile
            if (hasUnit && UnitSelectionManager.Instance != null)
            {
                Unit selected = UnitSelectionManager.Instance.SelectedUnit;
                if (selected != null &&
                    TurnManager.Instance != null &&
                    TurnManager.Instance.CanControlUnit(selected) &&
                    selected.isPlayerOwned != clickedUnit.isPlayerOwned)
                {
                    UnitSelectionManager.Instance.TryMoveOrAttackAtPosition(clickedUnit.transform.position);
                    return;
                }
            }

            // 2b) Enemy city clicked while a friendly unit is selected: try to move/attack onto that city tile
            if (hasCity && UnitSelectionManager.Instance != null && clickedCity != null && clickedCity.isPlayerOwned != (UnitSelectionManager.Instance.SelectedUnit?.isPlayerOwned ?? clickedCity.isPlayerOwned))
            {
                Unit selected = UnitSelectionManager.Instance.SelectedUnit;
                if (selected != null &&
                    TurnManager.Instance != null &&
                    TurnManager.Instance.CanControlUnit(selected) &&
                    selected.isPlayerOwned != clickedCity.isPlayerOwned)
                {
                    UnitSelectionManager.Instance.TryMoveOrAttackAtPosition(clickedCity.transform.position);
                    return;
                }
            }

            // 2c) Unit clicked: select/deselect unit (takes priority over city UI)
            if (hasUnit)
            {
                if (CityUIManager.Instance != null)
                {
                    CityUIManager.Instance.ClosePanel();
                }

                UnitSelectionManager.Instance.SelectUnit(clickedUnit);
                return;
            }

            // 2d) Player city clicked (and no unit was prioritized): open city UI
            if (hasCity && clickedCity != null &&
                TurnManager.Instance != null &&
                TurnManager.Instance.CanControlCity(clickedCity) &&
                CityUIManager.Instance != null)
            {
                if (UnitSelectionManager.Instance != null)
                {
                    UnitSelectionManager.Instance.ClearSelection();
                }

                CityUIManager.Instance.OnCityClicked(clickedCity);
                return;
            }

            // 2f) No city/unit clicked: treat as tile interaction

            // Clicking an empty tile should close any open city panel.
            if (CityUIManager.Instance != null)
                CityUIManager.Instance.ClosePanel();

            // Inform unit selection logic first (for movement)
            if (hoveredTile != null && UnitSelectionManager.Instance != null)
            {
                UnitSelectionManager.Instance.OnTileClicked(hoveredTile.transform);
            }

            if (hoveredTile == null)
            {
                // Clicked empty space: deselect current tile
                if (selectedTile != null)
                {
                    selectedTile.SetSelected(false);
                    selectedTile = null;
                }
            }
            else if (hoveredTile == selectedTile)
            {
                // Clicked the same green tile again: deselect it
                selectedTile.SetSelected(false);
                selectedTile = null;
            }
            else
            {
                // Clicked a different tile: move selection
                if (selectedTile != null)
                    selectedTile.SetSelected(false);

                selectedTile = hoveredTile;
                selectedTile.SetSelected(true);
            }
        }
    }

    public void ClearSelection()
    {
        if (selectedTile != null)
        {
            selectedTile.SetSelected(false);
            selectedTile = null;
        }

        if (hoveredTile != null)
        {
            hoveredTile.SetHighlighted(false);
            hoveredTile = null;
        }
    }

    private int RaycastUiAtPointerPosition(Vector2 pointerPosition, int pointerId)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            uiRaycastResults.Clear();
            return 0;
        }

        if (cachedPointerEventData == null || cachedPointerEventSystem != eventSystem)
        {
            cachedPointerEventSystem = eventSystem;
            cachedPointerEventData = new PointerEventData(eventSystem);
        }

        cachedPointerEventData.Reset();
        cachedPointerEventData.position = pointerPosition;
        cachedPointerEventData.pointerId = pointerId;

        uiRaycastResults.Clear();
        eventSystem.RaycastAll(cachedPointerEventData, uiRaycastResults);
        return uiRaycastResults.Count;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void LogUiRaycastBlockIfNeeded(Vector2 pointerPosition, int hitCount)
    {
        if (Time.unscaledTime - lastUiRaycastBlockLogTime < UiRaycastBlockLogCooldownSeconds)
            return;

        lastUiRaycastBlockLogTime = Time.unscaledTime;

        StringBuilder builder = new StringBuilder(512);
        builder.Append("[UIRaycastBlock] pointer=")
               .Append(pointerPosition)
               .Append(" hits=")
               .Append(hitCount);

        int limit = Mathf.Min(UiRaycastBlockMaxHits, hitCount);
        for (int i = 0; i < limit; i++)
        {
            RaycastResult hit = uiRaycastResults[i];
            builder.Append(" | ")
                   .Append(i + 1)
                   .Append(": ");

            if (hit.gameObject == null)
            {
                builder.Append("<null>");
                continue;
            }

            builder.Append(hit.gameObject.name)
                   .Append(" path=")
                   .Append(GetTransformPath(hit.gameObject.transform));
        }

        Debug.Log(builder.ToString());
    }

    private static string GetTransformPath(Transform current)
    {
        if (current == null)
            return "<null>";

        Stack<string> pathParts = new Stack<string>();
        while (current != null)
        {
            pathParts.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", pathParts);
    }
#endif
}
