using UnityEngine;
using UnityEngine.EventSystems;

public class TileHoverManager : MonoBehaviour
{
    public static TileHoverManager Instance { get; private set; }

    [SerializeField] private bool useNewInputSystemPointerOverUi = false;

    private TileHighlighter hoveredTile;
    private TileHighlighter selectedTile;

    private static bool IsPointerOverUi(bool useNewInputSystemPointerOverUi)
    {
        // On mobile, EventSystem.current.IsPointerOverGameObject() without a pointer id checks the
        // "mouse" pointer and can return false for touches, causing world clicks to leak through UI.
        if (EventSystem.current == null)
            return false;

#if ENABLE_INPUT_SYSTEM
        if (useNewInputSystemPointerOverUi)
        {
            var touches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                if (EventSystem.current.IsPointerOverGameObject(touch.touchId))
                    return true;
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.touchCount > 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                if (EventSystem.current.IsPointerOverGameObject(t.fingerId))
                    return true;
            }
        }
#endif

        return EventSystem.current.IsPointerOverGameObject();
    }

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
        // Ignore world interaction when the pointer is over UI (buttons, panels, overlays).
        if (IsPointerOverUi(useNewInputSystemPointerOverUi))
        {
            return;
        }

        if (TurnManager.Instance != null)
        {
            if (TurnManager.Instance.gameOver || TurnManager.Instance.IsHotseatHandoff)
            {
                return;
            }
        }

        // 1) HOVER / CLICK: find what is under the mouse
        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        Vector3 mouse = Input.mousePosition;
        if (float.IsNaN(mouse.x) || float.IsNaN(mouse.y) ||
            float.IsInfinity(mouse.x) || float.IsInfinity(mouse.y))
        {
            return;
        }

        Vector3 mouseWorld = cam.ScreenToWorldPoint(mouse);
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

        bool suppressHoverHighlight = TutorialGate.IsActive;

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
        if (Input.GetMouseButtonDown(0))
        {
            // Click priority: attack enemy unit (if a friendly is selected and adjacent)
            // > move into enemy city (if a friendly is selected and in range)
            // > movable Unit on player city > player City UI > Unit > Tile

            bool hasCity = clickedCity != null;
            bool hasUnit = clickedUnit != null && UnitSelectionManager.Instance != null;

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
                if (!TutorialGate.IsActive && CityUIManager.Instance != null)
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
                Debug.Log($"TileHoverManager: opening city UI for {clickedCity.name}");
                if (UnitSelectionManager.Instance != null)
                {
                    UnitSelectionManager.Instance.ClearSelection();
                }

                CityUIManager.Instance.OnCityClicked(clickedCity);
                return;
            }

            // 2f) No city/unit clicked: treat as tile interaction

            // During the tutorial, keep tile clicks deterministic and avoid extra visual noise
            // (no green tile selection, and don't auto-close city panels on misclicks).
            if (TutorialGate.IsActive)
            {
                if (hoveredTile != null && UnitSelectionManager.Instance != null)
                {
                    UnitSelectionManager.Instance.OnTileClicked(hoveredTile.transform);
                }
                return;
            }

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
}
