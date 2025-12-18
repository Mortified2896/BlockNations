using UnityEngine;

public class UnitClickHandler : MonoBehaviour
{
    void OnMouseDown()
    {
        // Selection is handled centrally by TileHoverManager via raycasts.
        // This component is kept only so existing prefabs do not lose references.
    }
}
