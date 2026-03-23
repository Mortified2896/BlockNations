using UnityEngine;

public class CameraController : MonoBehaviour
{
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

    private void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null)
            cam = Camera.main;
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
}
