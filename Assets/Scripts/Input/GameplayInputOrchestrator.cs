using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public sealed class GameplayInputOrchestrator : MonoBehaviour
{
    public struct FrameSnapshot
    {
        public Vector2 PointerPosition;
        public int PointerId;
        public bool TapThisFrame;
        public bool DragActive;
        public Vector2 DragDelta;
        public bool PinchActive;
        public float PinchDelta;
        public float ScrollDelta;
        public bool AnyHumanInputThisFrame;
        public bool WorldInputBlockedThisFrame;
    }

    private struct ActiveTouch
    {
        public int touchId;
        public Vector2 position;
        public bool downThisFrame;
        public bool upThisFrame;
        public bool isPressed;
    }

    public static GameplayInputOrchestrator Instance { get; private set; }

    [Header("Gesture Rules")]
    [SerializeField] private float dragStartThresholdPixels = 10f;
    [SerializeField] private float tapMaxDurationSeconds = 0.30f;

    public FrameSnapshot Snapshot => currentSnapshot;

    private FrameSnapshot currentSnapshot;

    private bool primaryGestureStarted;
    private bool primaryGestureBlockedByUi;
    private bool dragActive;
    private Vector2 primaryStartPosition;
    private Vector2 lastPrimaryPosition;
    private float primaryStartTime;

    private bool pinchActive;
    private bool pinchBlockedByUi;
    private float lastPinchDistancePixels;
    private bool suppressPrimaryUntilReleaseAfterPinch;
    private int primaryPointerId = -1;

    private Vector2 lastKnownPointerPosition;
    private bool releasedTouchThisFrame;
    private int releasedTouchId;
    private Vector2 releasedTouchPosition;

    private readonly Dictionary<int, bool> touchStartedOverUi = new Dictionary<int, bool>(8);
    private readonly List<ActiveTouch> activeTouches = new List<ActiveTouch>(8);
    private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>(16);
    private PointerEventData cachedPointerEventData;
    private EventSystem cachedPointerEventSystem;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
            return;

        GameObject go = new GameObject("GameplayInputOrchestrator");
        go.hideFlags = HideFlags.None;
        DontDestroyOnLoad(go);
        go.AddComponent<GameplayInputOrchestrator>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        currentSnapshot = BuildSnapshot();
    }

    public static bool TryGetSnapshot(out FrameSnapshot snapshot)
    {
        if (Instance == null)
        {
            snapshot = default;
            return false;
        }

        snapshot = Instance.currentSnapshot;
        return true;
    }

    private FrameSnapshot BuildSnapshot()
    {
        FrameSnapshot snapshot = default;

        CollectActiveTouches();

        bool hasTouch = activeTouches.Count > 0;
        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;

        int pointerId = -1;
        Vector2 pointerPosition = lastKnownPointerPosition;
        bool primaryDown = false;
        bool primaryUp = false;
        bool primaryHeld = false;

        if (hasTouch)
        {
            ActiveTouch touch = activeTouches[0];
            pointerId = touch.touchId;
            pointerPosition = touch.position;
            primaryDown = touch.downThisFrame;
            primaryUp = touch.upThisFrame;
            primaryHeld = touch.isPressed;
        }
        else if (mouse != null)
        {
            pointerId = -1;
            pointerPosition = mouse.position.ReadValue();
            primaryDown = mouse.leftButton.wasPressedThisFrame;
            primaryUp = mouse.leftButton.wasReleasedThisFrame;
            primaryHeld = mouse.leftButton.isPressed;
        }
        else if (releasedTouchThisFrame)
        {
            pointerId = releasedTouchId;
            pointerPosition = releasedTouchPosition;
        }

        lastKnownPointerPosition = pointerPosition;

        if (!hasTouch && primaryGestureStarted && releasedTouchThisFrame && releasedTouchId == primaryPointerId)
        {
            primaryUp = true;
            pointerId = releasedTouchId;
            pointerPosition = releasedTouchPosition;
        }

        bool pointerOverUiNow = IsPointerOverUi(pointerId, pointerPosition);

        bool hasPinchInput = activeTouches.Count >= 2;
        if (hasPinchInput)
        {
            ActiveTouch first = activeTouches[0];
            ActiveTouch second = activeTouches[1];
            float distance = Vector2.Distance(first.position, second.position);

            if (!pinchActive)
            {
                pinchActive = true;
                lastPinchDistancePixels = distance;
                pinchBlockedByUi = DidTouchStartOverUi(first.touchId) && DidTouchStartOverUi(second.touchId);
            }
            else
            {
                if (!pinchBlockedByUi)
                {
                    snapshot.PinchDelta = distance - lastPinchDistancePixels;
                }
                lastPinchDistancePixels = distance;
            }

            snapshot.PinchActive = true;
            ClearPrimaryGestureState();
        }
        else if (pinchActive)
        {
            pinchActive = false;
            pinchBlockedByUi = false;
            lastPinchDistancePixels = 0f;
            ClearPrimaryGestureState();

            if (activeTouches.Count == 1)
            {
                // Clarification rule: when pinch hands off from 2 -> 1 touches, require a fresh drag gesture.
                suppressPrimaryUntilReleaseAfterPinch = true;
            }
        }

        if (suppressPrimaryUntilReleaseAfterPinch && !primaryHeld)
        {
            suppressPrimaryUntilReleaseAfterPinch = false;
        }

        bool primaryHeldRaw = primaryHeld;
        if (suppressPrimaryUntilReleaseAfterPinch)
        {
            primaryDown = false;
            primaryUp = false;
            primaryHeld = false;
        }

        if (!snapshot.PinchActive)
        {
            if (primaryDown)
            {
                primaryGestureStarted = true;
                primaryGestureBlockedByUi = pointerOverUiNow;
                dragActive = false;
                primaryPointerId = pointerId;
                primaryStartPosition = pointerPosition;
                lastPrimaryPosition = pointerPosition;
                primaryStartTime = Time.unscaledTime;
            }

            if (primaryGestureStarted && primaryHeld)
            {
                float movement = (pointerPosition - primaryStartPosition).magnitude;
                if (!dragActive && !primaryGestureBlockedByUi && movement >= dragStartThresholdPixels)
                {
                    dragActive = true;
                }

                if (dragActive)
                {
                    snapshot.DragDelta = pointerPosition - lastPrimaryPosition;
                }

                lastPrimaryPosition = pointerPosition;
            }

            if (primaryUp)
            {
                if (primaryGestureStarted)
                {
                    float movement = (pointerPosition - primaryStartPosition).magnitude;
                    float duration = Time.unscaledTime - primaryStartTime;
                    bool tapAllowed = !dragActive && !primaryGestureBlockedByUi && !pointerOverUiNow;
                    if (tapAllowed && movement < dragStartThresholdPixels && duration <= tapMaxDurationSeconds)
                    {
                        snapshot.TapThisFrame = true;
                    }
                }

                ClearPrimaryGestureState();
            }

            snapshot.DragActive = dragActive;
        }

        float scrollDelta = 0f;
        if (mouse != null)
        {
            scrollDelta = mouse.scroll.ReadValue().y;
        }

        bool anyHumanInput = false;

        if (keyboard != null && keyboard.anyKey.isPressed)
        {
            anyHumanInput = true;
        }

        if (mouse != null)
        {
            if (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed ||
                mouse.leftButton.wasPressedThisFrame || mouse.leftButton.wasReleasedThisFrame ||
                mouse.rightButton.wasPressedThisFrame || mouse.rightButton.wasReleasedThisFrame)
            {
                anyHumanInput = true;
            }
        }

        if (activeTouches.Count > 0)
        {
            anyHumanInput = true;
        }
        else if (releasedTouchThisFrame)
        {
            anyHumanInput = true;
        }

        if (Mathf.Abs(scrollDelta) > 0.0001f)
        {
            anyHumanInput = true;
        }

        if (snapshot.TapThisFrame || snapshot.DragActive || snapshot.PinchActive || primaryHeldRaw)
        {
            anyHumanInput = true;
        }

        bool blockedByPrimaryGesture = primaryGestureStarted && primaryGestureBlockedByUi;
        bool blockedByPinchGesture = snapshot.PinchActive && pinchBlockedByUi;
        bool blockedByUiHover = pointerOverUiNow && !snapshot.DragActive && !snapshot.PinchActive;

        snapshot.PointerPosition = pointerPosition;
        snapshot.PointerId = pointerId;
        snapshot.ScrollDelta = scrollDelta;
        snapshot.AnyHumanInputThisFrame = anyHumanInput;
        snapshot.WorldInputBlockedThisFrame = blockedByPrimaryGesture || blockedByPinchGesture || blockedByUiHover;

        return snapshot;
    }

    private void CollectActiveTouches()
    {
        activeTouches.Clear();
        releasedTouchThisFrame = false;
        releasedTouchId = -1;
        releasedTouchPosition = lastKnownPointerPosition;

        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen == null)
        {
            touchStartedOverUi.Clear();
            return;
        }

        foreach (TouchControl touch in touchscreen.touches)
        {
            int touchId = touch.touchId.ReadValue();
            Vector2 position = touch.position.ReadValue();
            bool down = touch.press.wasPressedThisFrame;
            bool up = touch.press.wasReleasedThisFrame;
            bool pressed = touch.press.isPressed;

            if (down)
            {
                touchStartedOverUi[touchId] = IsPointerOverUi(touchId, position);
            }

            if (up && !pressed)
            {
                touchStartedOverUi.Remove(touchId);
            }

            if (up)
            {
                releasedTouchThisFrame = true;
                releasedTouchId = touchId;
                releasedTouchPosition = position;
            }

            if (!pressed)
                continue;

            activeTouches.Add(new ActiveTouch
            {
                touchId = touchId,
                position = position,
                downThisFrame = down,
                upThisFrame = up,
                isPressed = true
            });
        }

        if (activeTouches.Count == 0)
        {
            touchStartedOverUi.Clear();
            return;
        }

        activeTouches.Sort((a, b) => a.touchId.CompareTo(b.touchId));
    }

    private bool IsPointerOverUi(int pointerId, Vector2 pointerPosition)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return false;

        if (eventSystem.IsPointerOverGameObject(pointerId))
            return true;

        if (cachedPointerEventData == null || cachedPointerEventSystem != eventSystem)
        {
            cachedPointerEventSystem = eventSystem;
            cachedPointerEventData = new PointerEventData(eventSystem);
        }

        cachedPointerEventData.Reset();
        cachedPointerEventData.position = pointerPosition;
        cachedPointerEventData.pointerId = pointerId;

        uiRaycastResults.Clear();
        eventSystem.RaycastAll(cachedPointerEventData, uiRaycastResults);
        return uiRaycastResults.Count > 0;
    }

    private bool DidTouchStartOverUi(int touchId)
    {
        if (touchStartedOverUi.TryGetValue(touchId, out bool startedOverUi))
            return startedOverUi;

        return IsPointerOverUi(touchId, lastKnownPointerPosition);
    }

    private void ClearPrimaryGestureState()
    {
        primaryGestureStarted = false;
        primaryGestureBlockedByUi = false;
        dragActive = false;
        primaryPointerId = -1;
    }
}
