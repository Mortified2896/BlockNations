using UnityEngine;
using UnityEngine.EventSystems;

public class CameraController : MonoBehaviour
{
    [Header("General")]
    public float panSpeed = 1f;          // how fast the camera moves
    public float pixelsPerUnit = 16f;    // MUST match your sprite PPU

    [Header("Mouse")]
    public float dragThresholdPixels = 5f;  // how far you must move (on screen) before it starts panning

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
    public float pinchStartThresholdPixels = 2f;

    private Camera cam;

    // mouse state
    private bool isPanning = false;
    private Vector3 lastPointerWorldPos;
    private Vector3 mouseDownScreenPos;

    // touch zoom state
    private bool isPinching = false;
    private float lastPinchDistancePixels = 0f;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null)
            cam = Camera.main;
    }

    void Update()
    {
        // ---------- TOUCH (phone) ----------
        if (Input.touchCount > 0)
        {
            // Avoid world camera controls when interacting with UI.
            if (EventSystem.current != null)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch uiTouch = Input.GetTouch(i);
                    if (EventSystem.current.IsPointerOverGameObject(uiTouch.fingerId))
                    {
                        isPanning = false;
                        isPinching = false;
                        return;
                    }
                }
            }

            // Pinch zoom uses two touches.
            if (enablePinchZoom && Input.touchCount >= 2 && cam != null)
            {
                Touch a = Input.GetTouch(0);
                Touch b = Input.GetTouch(1);

                float distance = Vector2.Distance(a.position, b.position);

                if (!isPinching || a.phase == TouchPhase.Began || b.phase == TouchPhase.Began)
                {
                    isPinching = true;
                    isPanning = false;
                    lastPinchDistancePixels = distance;
                    return;
                }

                float deltaPixels = distance - lastPinchDistancePixels;
                if (Mathf.Abs(deltaPixels) >= pinchStartThresholdPixels)
                {
                    // Spread fingers => zoom in (smaller ortho size / smaller FOV).
                    if (cam.orthographic)
                    {
                        float newSize = cam.orthographicSize - (deltaPixels * pinchZoomSpeed);
                        cam.orthographicSize = Mathf.Clamp(newSize, minOrthoSize, maxOrthoSize);
                    }
                    else
                    {
                        float newFov = cam.fieldOfView - (deltaPixels * pinchZoomSpeed);
                        cam.fieldOfView = Mathf.Clamp(newFov, minFieldOfView, maxFieldOfView);
                    }
                }

                lastPinchDistancePixels = distance;
                return;
            }

            // Single-touch pan.
            Touch t = Input.GetTouch(0);
            Vector3 touchWorld = cam.ScreenToWorldPoint(new Vector3(t.position.x, t.position.y, 0f));

            if (t.phase == TouchPhase.Began)
            {
                isPanning = true;
                isPinching = false;
                lastPointerWorldPos = touchWorld;
            }
            else if (t.phase == TouchPhase.Moved && isPanning)
            {
                Vector3 delta = lastPointerWorldPos - touchWorld;
                MoveCamera(delta);
                lastPointerWorldPos = touchWorld;
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                isPanning = false;
                isPinching = false;
            }

            return; // if we have touch, skip mouse handling
        }

        // ---------- MOUSE WHEEL ZOOM (PC) ----------
        if (enableMouseWheelZoom && cam != null)
        {
            // Avoid zooming when scrolling UI.
            if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
            {
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0.0001f)
                {
                    if (cam.orthographic)
                    {
                        float newSize = cam.orthographicSize - (scroll * zoomSpeed);
                        cam.orthographicSize = Mathf.Clamp(newSize, minOrthoSize, maxOrthoSize);
                    }
                    else
                    {
                        float newFov = cam.fieldOfView - (scroll * zoomSpeed);
                        cam.fieldOfView = Mathf.Clamp(newFov, minFieldOfView, maxFieldOfView);
                    }
                }
            }
        }

        // ---------- MOUSE (PC) ----------
        if (Input.GetMouseButtonDown(0))
        {
            mouseDownScreenPos = Input.mousePosition;
            isPanning = false; // we don't know yet if this will be a drag or a click
        }
        else if (Input.GetMouseButton(0))
        {
            // check if we've moved enough on screen to start panning
            float dist = (Input.mousePosition - mouseDownScreenPos).magnitude;
            if (!isPanning && dist > dragThresholdPixels)
            {
                // start panning: set reference world position
                lastPointerWorldPos = cam.ScreenToWorldPoint(Input.mousePosition);
                isPanning = true;
            }

            if (isPanning)
            {
                Vector3 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);
                Vector3 delta = lastPointerWorldPos - mouseWorld;
                MoveCamera(delta);
                lastPointerWorldPos = mouseWorld;
            }
        }
        else if (Input.GetMouseButtonUp(0))
        {
            isPanning = false;
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
