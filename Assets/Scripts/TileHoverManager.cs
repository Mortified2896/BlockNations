using UnityEngine;
using UnityEngine.EventSystems;

public class TileHoverManager : MonoBehaviour
{
    public static TileHoverManager Instance { get; private set; }

    private TileHighlighter hoveredTile;
    private TileHighlighter selectedTile;

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
        // Ignore world interaction when clicking over UI (buttons, panels, etc.).
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
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

        // Update hover state if we moved to a different tile
        if (newHover != hoveredTile)
        {
            if (hoveredTile != null)
                hoveredTile.SetHighlighted(false);

            if (newHover != null)
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

            // 2c) If both a player city and a movable player unit are under the cursor,
            //     prefer selecting the unit so it can move out of the city,
            //     and hide the city panel.
            if (hasCity && clickedCity != null &&
                TurnManager.Instance != null &&
                TurnManager.Instance.CanControlCity(clickedCity) &&
                hasUnit &&
                TurnManager.Instance.CanControlUnit(clickedUnit) &&
                clickedUnit.CanMoveThisTurn())
            {
                if (CityUIManager.Instance != null)
                {
                    CityUIManager.Instance.ClosePanel();
                }

                UnitSelectionManager.Instance.SelectUnit(clickedUnit);
                return;
            }

            // 2d) Player city clicked (and no movable player unit to prioritize):
            //     deselect unit and open city UI
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

            // 2e) Unit clicked (not on a city, or city handled above): select/deselect unit
            if (hasUnit)
            {
                if (CityUIManager.Instance != null)
                {
                    CityUIManager.Instance.ClosePanel();
                }

                UnitSelectionManager.Instance.SelectUnit(clickedUnit);
                return;
            }

            // 2f) No city/unit clicked: treat as tile interaction

            // Clicking an empty tile should close any open city panel.
            if (CityUIManager.Instance != null)
            {
                CityUIManager.Instance.ClosePanel();
            }

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
