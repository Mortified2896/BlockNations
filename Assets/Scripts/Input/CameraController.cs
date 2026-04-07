using System.Collections;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    private const string PlayByPostGameIdKey = "pbp_gameId";
    private const int InitialFocusMaxWaitFrames = 180;
    private const float FitMarginTilesPerEdge = 0.5f;
    private const float PanOverscrollTilesPerEdge = 15f;

    private struct PendingCameraRestoreState
    {
        public bool hasPendingState;
        public Vector3 position;
        public float orthographicSize;
        public float fieldOfView;
        public bool isOrthographic;
    }

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
    private static PendingCameraRestoreState pendingRestoreState;

    public static void CaptureCurrentStateForNextSceneLoad()
    {
        Camera cameraToCapture = Camera.main;
        if (cameraToCapture == null)
        {
            return;
        }

        pendingRestoreState = new PendingCameraRestoreState
        {
            hasPendingState = true,
            position = cameraToCapture.transform.position,
            orthographicSize = cameraToCapture.orthographicSize,
            fieldOfView = cameraToCapture.fieldOfView,
            isOrthographic = cameraToCapture.orthographic
        };
    }

    public static void ClearPendingRestoreState()
    {
        pendingRestoreState = default;
    }

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
            cam.orthographicSize = Mathf.Clamp(newSize, minOrthoSize, GetDynamicMaxOrthographicSize());
            ApplyCameraConstraints();
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
        SetCameraWorldPosition(newPos);
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

        if (turnManager.IsAIVsAIDebugModeEnabledForUi())
        {
            ClearPendingRestoreState();
        }
        else if (TryApplyPendingRestoreState())
        {
            initialFocusApplied = true;
            return true;
        }

        Vector3 focusPosition = GetBoardCenter(turnManager.gridManager);
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

    private bool TryApplyPendingRestoreState()
    {
        if (!pendingRestoreState.hasPendingState || cam == null)
        {
            return false;
        }

        if (cam.orthographic == pendingRestoreState.isOrthographic)
        {
            if (cam.orthographic)
            {
                cam.orthographicSize = Mathf.Clamp(
                    pendingRestoreState.orthographicSize,
                    minOrthoSize,
                    GetDynamicMaxOrthographicSize());
            }
            else
            {
                cam.fieldOfView = Mathf.Clamp(
                    pendingRestoreState.fieldOfView,
                    minFieldOfView,
                    maxFieldOfView);
            }
        }

        SetCameraWorldPosition(pendingRestoreState.position);
        pendingRestoreState = default;
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
        Vector3 mapCenter = GetBoardCenter(turnManager.gridManager);
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
        Vector3 mapCenter = GetBoardCenter(turnManager.gridManager);
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

    private static Vector3 GetBoardCenter(GridManager gridManager)
    {
        if (TryGetBoardBounds(gridManager, out Rect bounds))
        {
            return new Vector3(bounds.center.x, bounds.center.y, 0f);
        }

        return Vector3.zero;
    }

    private void SetCameraWorldPosition(Vector3 worldPosition)
    {
        Vector3 newPos = worldPosition;
        newPos.z = cam.transform.position.z;
        cam.transform.position = GetConstrainedCameraPosition(newPos);
    }

    private void ApplyCameraConstraints()
    {
        if (cam == null)
        {
            return;
        }

        cam.transform.position = GetConstrainedCameraPosition(cam.transform.position);
    }

    private Vector3 GetConstrainedCameraPosition(Vector3 desiredPosition)
    {
        float snapStep = 1f / pixelsPerUnit;
        Vector3 constrainedPosition = desiredPosition;
        constrainedPosition.z = cam.transform.position.z;

        if (!cam.orthographic)
        {
            constrainedPosition.x = Mathf.Round(constrainedPosition.x / snapStep) * snapStep;
            constrainedPosition.y = Mathf.Round(constrainedPosition.y / snapStep) * snapStep;
            return constrainedPosition;
        }

        if (!TryGetBoardBounds(GetActiveGridManager(), out Rect boardBounds))
        {
            constrainedPosition.x = Mathf.Round(constrainedPosition.x / snapStep) * snapStep;
            constrainedPosition.y = Mathf.Round(constrainedPosition.y / snapStep) * snapStep;
            return constrainedPosition;
        }

        float halfViewportHeight = cam.orthographicSize;
        float halfViewportWidth = halfViewportHeight * cam.aspect;
        float tileSize = GetGridTileSize(GetActiveGridManager());
        float overscroll = tileSize * PanOverscrollTilesPerEdge;

        float minCenterX = boardBounds.xMin - overscroll + halfViewportWidth;
        float maxCenterX = boardBounds.xMax + overscroll - halfViewportWidth;
        float minCenterY = boardBounds.yMin - overscroll + halfViewportHeight;
        float maxCenterY = boardBounds.yMax + overscroll - halfViewportHeight;

        constrainedPosition.x = ClampAndSnapAxis(constrainedPosition.x, minCenterX, maxCenterX, boardBounds.center.x, snapStep);
        constrainedPosition.y = ClampAndSnapAxis(constrainedPosition.y, minCenterY, maxCenterY, boardBounds.center.y, snapStep);
        return constrainedPosition;
    }

    private float GetDynamicMaxOrthographicSize()
    {
        if (cam == null || !cam.orthographic)
        {
            return maxOrthoSize;
        }

        GridManager gridManager = GetActiveGridManager();
        if (!TryGetBoardBounds(gridManager, out Rect boardBounds))
        {
            return maxOrthoSize;
        }

        float tileSize = GetGridTileSize(gridManager);
        float fitMargin = tileSize * FitMarginTilesPerEdge;
        float fitByHeight = boardBounds.height * 0.5f + fitMargin;
        float fitByWidth = (boardBounds.width * 0.5f + fitMargin) / Mathf.Max(0.0001f, cam.aspect);
        return Mathf.Max(minOrthoSize, Mathf.Max(fitByHeight, fitByWidth));
    }

    private GridManager GetActiveGridManager()
    {
        return TurnManager.Instance != null ? TurnManager.Instance.gridManager : null;
    }

    private static float GetGridTileSize(GridManager gridManager)
    {
        return gridManager != null ? Mathf.Max(0.01f, gridManager.tileSize) : 1f;
    }

    private static bool TryGetBoardBounds(GridManager gridManager, out Rect boardBounds)
    {
        boardBounds = default;
        if (gridManager == null || gridManager.tileGrid == null || gridManager.width <= 0 || gridManager.height <= 0)
        {
            return false;
        }

        float tileSize = GetGridTileSize(gridManager);
        float boardWidth = gridManager.width * tileSize;
        float boardHeight = gridManager.height * tileSize;

        float centerX = 0f;
        float centerY = 0f;

        if (gridManager.TryGetTile(0, 0, out TileVisibility minTile) && minTile != null &&
            gridManager.TryGetTile(gridManager.width - 1, gridManager.height - 1, out TileVisibility maxTile) && maxTile != null)
        {
            centerX = (minTile.transform.position.x + maxTile.transform.position.x) * 0.5f;
            centerY = (minTile.transform.position.y + maxTile.transform.position.y) * 0.5f;
        }

        boardBounds = Rect.MinMaxRect(
            centerX - boardWidth * 0.5f,
            centerY - boardHeight * 0.5f,
            centerX + boardWidth * 0.5f,
            centerY + boardHeight * 0.5f);
        return true;
    }

    private static float ClampAndSnapAxis(float value, float min, float max, float center, float snapStep)
    {
        if (min > max)
        {
            return Mathf.Round(center / snapStep) * snapStep;
        }

        float clampedValue = Mathf.Clamp(value, min, max);
        float snappedValue = Mathf.Round(clampedValue / snapStep) * snapStep;

        if (snappedValue < min)
        {
            float minSnapped = Mathf.Ceil(min / snapStep) * snapStep;
            if (minSnapped <= max)
            {
                snappedValue = minSnapped;
            }
            else
            {
                snappedValue = Mathf.Clamp(center, min, max);
            }
        }
        else if (snappedValue > max)
        {
            float maxSnapped = Mathf.Floor(max / snapStep) * snapStep;
            if (maxSnapped >= min)
            {
                snappedValue = maxSnapped;
            }
            else
            {
                snappedValue = Mathf.Clamp(center, min, max);
            }
        }

        return snappedValue;
    }
}
