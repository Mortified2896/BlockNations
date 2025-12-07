using UnityEngine;

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
        // 1) HOVER: find which tile is under the mouse
        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 mousePos2D = new Vector2(mouseWorld.x, mouseWorld.y);

        RaycastHit2D hit = Physics2D.Raycast(mousePos2D, Vector2.zero);

        TileHighlighter newHover = null;

        if (hit.collider != null)
        {
            newHover = hit.collider.GetComponent<TileHighlighter>();
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
