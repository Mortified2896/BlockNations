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

        if (CityUIManager.Instance != null)
        {
            CityUIManager.Instance.OnCityClicked(city);
        }
    }
}

