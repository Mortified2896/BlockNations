using System.Collections;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    private const string PlayByPostGameIdKey = "pbp_gameId";
    private const int InitialFocusMaxWaitFrames = 180;

    [Header("General")]
    public float panSpeed = 1f;          // how fast the camera moves
    public float pixelsPerUnit = 16f;    // MUST match your sprite PPU

    [Header("Mouse")]
    public float dragThresholdPixels = 5f;  // Legacy inspector value; drag threshold is now owned by GameplayInputOrchestrator.

    [Header("Zoom (PC)")]
    public bool enableMouseWheelZoom = true;
    public float zoomSpeed = 1.5f;
    public float minOrthoSize = 2.5f;
    public float maxOrthoSize = 12f;
    public float minFieldOfView = 25f;
    public float maxFieldOfView = 80f;

    [Header("Zoom (Touch)")]
    public bool enablePinchZoom = true;
    public float pinchZoomSpeed = 0.02f; // scale factor for pixel-distance deltas
    public float pinchStartThresholdPixels = 2f; // Legacy inspector value; pinch threshold is now owned by GameplayInputOrchestrator.

    private Camera cam;
    private bool initialFocusApplied;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null)
            cam = Camera.main;
    }

    private void Start()
    {
        StartCoroutine(ApplyInitialFocusWhenReady());
    }

    private void Update()
    {
        if (!GameplayInputOrchestrator.TryGetSnapshot(out GameplayInputOrchestrator.FrameSnapshot input))
            return;

        if (cam == null)
            cam = Camera.main;

        if (cam == null)
            return;

        if (enablePinchZoom && input.PinchActive)
        {
            ApplyZoomDelta(input.PinchDelta * pinchZoomSpeed);
            return;
        }

        if (input.DragActive && !input.WorldInputBlockedThisFrame)
        {
            Vector2 currentScreen = input.PointerPosition;
            Vector2 previousScreen = currentScreen - input.DragDelta;

            Vector3 currentWorld = cam.ScreenToWorldPoint(new Vector3(currentScreen.x, currentScreen.y, 0f));
            Vector3 previousWorld = cam.ScreenToWorldPoint(new Vector3(previousScreen.x, previousScreen.y, 0f));
            Vector3 worldDelta = previousWorld - currentWorld;
            MoveCamera(worldDelta);
            return;
        }

        if (enableMouseWheelZoom && !input.WorldInputBlockedThisFrame)
        {
            if (Mathf.Abs(input.ScrollDelta) > 0.0001f)
            {
                ApplyZoomDelta(input.ScrollDelta * zoomSpeed);
            }
        }
    }

    private void ApplyZoomDelta(float zoomDelta)
    {
        if (Mathf.Abs(zoomDelta) <= 0.00001f)
            return;

        if (cam.orthographic)
        {
            float newSize = cam.orthographicSize - zoomDelta;
            cam.orthographicSize = Mathf.Clamp(newSize, minOrthoSize, maxOrthoSize);
        }
        else
        {
            float newFov = cam.fieldOfView - zoomDelta;
            cam.fieldOfView = Mathf.Clamp(newFov, minFieldOfView, maxFieldOfView);
        }
    }

    private void MoveCamera(Vector3 delta)
    {
        // move in world space
        Vector3 newPos = cam.transform.position +
                         new Vector3(delta.x, delta.y, 0f) * panSpeed;

        // snap X/Y to pixel grid
        float snapStep = 1f / pixelsPerUnit;   // 1/16 = 0.0625 units
        newPos.x = Mathf.Round(newPos.x / snapStep) * snapStep;
        newPos.y = Mathf.Round(newPos.y / snapStep) * snapStep;
        newPos.z = cam.transform.position.z;   // keep Z

        cam.transform.position = newPos;
    }

    private IEnumerator ApplyInitialFocusWhenReady()
    {
        int waitedFrames = 0;

        while (!initialFocusApplied && waitedFrames < InitialFocusMaxWaitFrames)
        {
            if (TryApplyInitialFocus())
            {
                yield break;
            }

            waitedFrames++;
            yield return null;
        }

        TryApplyInitialFocus(allowModeNoneFallback: true);
    }

    private bool TryApplyInitialFocus(bool allowModeNoneFallback = false)
    {
        if (initialFocusApplied)
        {
            return true;
        }

        if (cam == null)
        {
            cam = GetComponent<Camera>();
            if (cam == null)
            {
                cam = Camera.main;
            }
        }

        if (cam == null)
        {
            return false;
        }

        TurnManager turnManager = TurnManager.Instance;
        if (turnManager == null || turnManager.gridManager == null || turnManager.gridManager.tileGrid == null || turnManager.gridManager.tileGrid.Length == 0)
        {
            return false;
        }

        if (!allowModeNoneFallback && turnManager.currentMode == TurnManager.GameMode.None)
        {
            return false;
        }

        Vector3 focusPosition = GetMapCenter(turnManager.gridManager);
        if (TryGetOwnedCityFocus(turnManager, out Vector3 cityFocus))
        {
            focusPosition = cityFocus;
        }
        else if (TryGetOwnedUnitFocus(turnManager, out Vector3 unitFocus))
        {
            focusPosition = unitFocus;
        }

        SetCameraWorldPosition(focusPosition);
        initialFocusApplied = true;
        return true;
    }

    private bool TryGetOwnedCityFocus(TurnManager turnManager, out Vector3 focusPosition)
    {
        focusPosition = Vector3.zero;
        City[] cities = Object.FindObjectsByType<City>(FindObjectsSortMode.None);
        if (cities == null || cities.Length == 0)
        {
            return false;
        }

        bool viewerIsPlayerOwned = GetViewerIsPlayerOwned(turnManager);
        Vector3 mapCenter = GetMapCenter(turnManager.gridManager);
        City bestCity = null;
        float bestDistance = float.MaxValue;

        foreach (City city in cities)
        {
            if (city == null || city.isPlayerOwned != viewerIsPlayerOwned)
            {
                continue;
            }

            float distance = (city.transform.position - mapCenter).sqrMagnitude;
            if (bestCity == null || distance < bestDistance)
            {
                bestCity = city;
                bestDistance = distance;
            }
        }

        if (bestCity == null)
        {
            return false;
        }

        focusPosition = bestCity.transform.position;
        return true;
    }

    private bool TryGetOwnedUnitFocus(TurnManager turnManager, out Vector3 focusPosition)
    {
        focusPosition = Vector3.zero;
        Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
        if (units == null || units.Length == 0)
        {
            return false;
        }

        bool viewerIsPlayerOwned = GetViewerIsPlayerOwned(turnManager);
        Vector3 mapCenter = GetMapCenter(turnManager.gridManager);
        Unit bestUnit = null;
        float bestDistance = float.MaxValue;

        foreach (Unit unit in units)
        {
            if (unit == null || !unit.gameObject.activeInHierarchy || unit.isPlayerOwned != viewerIsPlayerOwned)
            {
                continue;
            }

            float distance = (unit.transform.position - mapCenter).sqrMagnitude;
            if (bestUnit == null || distance < bestDistance)
            {
                bestUnit = unit;
                bestDistance = distance;
            }
        }

        if (bestUnit == null)
        {
            return false;
        }

        focusPosition = bestUnit.transform.position;
        return true;
    }

    private bool GetViewerIsPlayerOwned(TurnManager turnManager)
    {
        if (turnManager == null || turnManager.currentMode != TurnManager.GameMode.PlayByPost)
        {
            return true;
        }

        string gameId = PlayerPrefs.GetString(PlayByPostGameIdKey, string.Empty);
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return true;
        }

        if (!LocalPlayerSeatStore.TryGetSeat(gameId, out int seat))
        {
            return true;
        }

        return seat == 0;
    }

    private static Vector3 GetMapCenter(GridManager gridManager)
    {
        if (gridManager != null &&
            gridManager.TryGetTile(gridManager.width / 2, gridManager.height / 2, out TileVisibility centerTile) &&
            centerTile != null)
        {
            return centerTile.transform.position;
        }

        return Vector3.zero;
    }

    private void SetCameraWorldPosition(Vector3 worldPosition)
    {
        float snapStep = 1f / pixelsPerUnit;
        Vector3 newPos = worldPosition;
        newPos.x = Mathf.Round(newPos.x / snapStep) * snapStep;
        newPos.y = Mathf.Round(newPos.y / snapStep) * snapStep;
        newPos.z = cam.transform.position.z;
        cam.transform.position = newPos;
    }
}
