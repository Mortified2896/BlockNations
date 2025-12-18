using UnityEngine;

/// <summary>
/// Handles mouse clicks on a city and notifies the CityUIManager.
/// Requires a Collider2D on the same GameObject to receive OnMouseDown.
/// </summary>
public class CityClickHandler : MonoBehaviour
{
    private City city;

    void Awake()
    {
        city = GetComponent<City>();
        if (city == null)
        {
            Debug.LogWarning("CityClickHandler requires a City component on the same GameObject.", this);
        }
    }

    void OnMouseDown()
    {
        if (city == null) return;

        Debug.Log("CityClickHandler.OnMouseDown on " + city.name);

        // If there's also a unit under the mouse, let TileHoverManager prioritize the unit.
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);
            Vector2 mousePos2D = new Vector2(mouseWorld.x, mouseWorld.y);
            RaycastHit2D[] hits = Physics2D.RaycastAll(mousePos2D, Vector2.zero);
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                if (hit.collider.GetComponentInParent<Unit>() != null)
                {
                    Debug.Log("CityClickHandler: unit under cursor; skipping city UI to allow unit selection.");
                    return;
                }
            }
        }

        if (CityUIManager.Instance != null)
        {
            CityUIManager.Instance.OnCityClicked(city);
        }
        else
        {
            Debug.LogWarning("CityClickHandler: CityUIManager.Instance is null, cannot open city UI.");
        }
    }
}
