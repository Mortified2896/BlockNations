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

        // If TileHoverManager is active, it handles all click routing (including cities).
        // Avoid calling CityUIManager twice for a single click.
        if (TileHoverManager.Instance != null)
        {
            return;
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
