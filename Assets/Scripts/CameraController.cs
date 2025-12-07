using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("General")]
    public float panSpeed = 1f;          // how fast the camera moves
    public float pixelsPerUnit = 16f;    // MUST match your sprite PPU

    [Header("Mouse")]
    public float dragThresholdPixels = 5f;  // how far you must move (on screen) before it starts panning

    private Camera cam;

    // mouse state
    private bool isPanning = false;
    private Vector3 lastPointerWorldPos;
    private Vector3 mouseDownScreenPos;

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
            Touch t = Input.GetTouch(0);
            Vector3 touchWorld = cam.ScreenToWorldPoint(
                new Vector3(t.position.x, t.position.y, 0f)
            );

            if (t.phase == TouchPhase.Began)
            {
                isPanning = true;
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
            }

            return; // if we have touch, skip mouse handling
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