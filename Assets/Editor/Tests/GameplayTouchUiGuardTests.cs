using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.EnhancedTouch;

public sealed class GameplayTouchUiGuardTests
{
    private bool restoreOptions, previousOptionsEnabled;
    private EnterPlayModeOptions previousOptions;

    [UnityTearDown]
    public IEnumerator RestoreAfterTouchTest()
    {
        if (!restoreOptions) yield break;
        if (Application.isPlaying) yield return new ExitPlayMode();
        EditorSettings.enterPlayModeOptions = previousOptions;
        EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
        restoreOptions = false;
    }

    [UnityTest]
    public IEnumerator TouchReleaseUsesItsOwnPositionWithAPresentMouse()
    {
        previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        previousOptions = EditorSettings.enterPlayModeOptions;
        restoreOptions = true;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode(false);
        // Native Input System button frame counters must run in a player.
        CheckTouchRelease(true, 500, 500, true);
        CheckTouchRelease(false, 500, 500, true);
        CheckTouchRelease(true, 99, 101, false);
        CheckTouchRelease(true, 500, 530, false);
        CheckTouchRelease(true, 500, 500, false, 0.5);
        CheckTouchRelease(true, 500, 500, false, 0.05, true);
    }

    private static void CheckTouchRelease(bool betweenUpdates, int start, int end, bool expectedTap, double duration = 0.05, bool cancelled = false)
    {
        EventSystem previous = EventSystem.current;
        Touchscreen previousTouch = Touchscreen.current;
        Mouse previousMouse = Mouse.current;
        var inputRoot = new GameObject("Inactive touch release fixture");
        inputRoot.SetActive(false);
        var uiRoot = new GameObject("Touch release UI fixture");
        EventSystem events = null;
        TutorialPositionRaycaster raycaster = null;
        Touchscreen touch = null;
        Mouse mouse = null;
        try
        {
            EnhancedTouchSupport.Enable();
            var input = inputRoot.AddComponent<GameplayInputOrchestrator>();
            events = uiRoot.AddComponent<EventSystem>();
            var module = uiRoot.AddComponent<TutorialCachedMouseHover>();
            raycaster = uiRoot.AddComponent<TutorialPositionRaycaster>();
            typeof(EventSystem).GetField("m_CurrentInputModule", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(events, module);
            EventSystem.current = events;
            mouse = InputSystem.AddDevice<Mouse>();
            touch = InputSystem.AddDevice<Touchscreen>();
            touch.MakeCurrent();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(50, 50) });
            InputSystem.Update();
            var build = typeof(GameplayInputOrchestrator).GetMethod("BuildSnapshot", BindingFlags.NonPublic | BindingFlags.Instance);
            build.Invoke(input, null); // Observe controls before events, as the game does each update.
            InputSystem.QueueStateEvent(touch, new TouchState { touchId = 1, phase = UnityEngine.InputSystem.TouchPhase.Began, position = new Vector2(start, start) }, time: 1.0);
            if (!betweenUpdates)
            {
                InputSystem.Update();
                build.Invoke(input, null);
            }
            InputSystem.QueueStateEvent(touch, new TouchState { touchId = 1, phase = cancelled ? UnityEngine.InputSystem.TouchPhase.Canceled : UnityEngine.InputSystem.TouchPhase.Ended, position = new Vector2(end, end) }, time: 1.0 + duration);
            InputSystem.Update();
            var frame = (GameplayInputOrchestrator.FrameSnapshot)build.Invoke(input, null);
            // Coalesced events surface as Began first, then Ended on the next update.
            if (betweenUpdates)
            {
                InputSystem.Update();
                frame = (GameplayInputOrchestrator.FrameSnapshot)build.Invoke(input, null);
            }
            Assert.That(frame.PointerId, Is.EqualTo(1));
            Assert.That(frame.PointerPosition, Is.EqualTo(new Vector2(end, end)));
            Assert.That(frame.TapThisFrame, Is.EqualTo(expectedTap));
        }
        finally
        {
            EnhancedTouchSupport.Disable();
            if (touch != null) InputSystem.RemoveDevice(touch);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (previousTouch != null) previousTouch.MakeCurrent();
            if (previousMouse != null) previousMouse.MakeCurrent();
            if (raycaster != null) typeof(BaseRaycaster).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(raycaster, null);
            if (events != null) typeof(EventSystem).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(events, null);
            Object.DestroyImmediate(uiRoot);
            Object.DestroyImmediate(inputRoot);
            if (previous != null) EventSystem.current = previous;
        }
    }

    [TestCase(-1, 500, 500, true)]
    [TestCase(1, 500, 500, false)]
    [TestCase(1, 50, 50, true)]
    public void TouchUsesItsCurrentPositionWhileMouseKeepsItsOwnHover(int pointerId, int x, int y, bool blocked)
    {
        EventSystem previous = EventSystem.current;
        var inputRoot = new GameObject("Inactive touch guard fixture");
        inputRoot.SetActive(false);
        var uiRoot = new GameObject("Touch guard UI fixture");
        EventSystem events = null;
        TutorialPositionRaycaster raycaster = null;
        try
        {
            var input = inputRoot.AddComponent<GameplayInputOrchestrator>();
            events = uiRoot.AddComponent<EventSystem>();
            var module = uiRoot.AddComponent<TutorialCachedMouseHover>();
            raycaster = uiRoot.AddComponent<TutorialPositionRaycaster>();
            // UIBehaviour lifecycle hooks do not run in an EditMode fixture.
            typeof(EventSystem).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(events, null);
            typeof(BaseRaycaster).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(raycaster, null);
            typeof(EventSystem).GetField("m_CurrentInputModule", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(events, module);
            EventSystem.current = events;
            // The mouse is still hovering the button that opened the lesson.
            Assert.That(events.IsPointerOverGameObject(-1), Is.True);
            var check = typeof(GameplayInputOrchestrator).GetMethod("IsPointerOverUi", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(check.Invoke(input, new object[] { pointerId, new Vector2(x, y) }), Is.EqualTo(blocked));
        }
        finally
        {
            if (raycaster != null) typeof(BaseRaycaster).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(raycaster, null);
            if (events != null) typeof(EventSystem).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(events, null);
            Object.DestroyImmediate(uiRoot);
            Object.DestroyImmediate(inputRoot);
            if (previous != null) EventSystem.current = previous;
        }
    }
}

public sealed class TutorialCachedMouseHover : BaseInputModule
{
    public override void Process() { }
    public override bool IsPointerOverGameObject(int pointerId) => true;
}

public sealed class TutorialPositionRaycaster : BaseRaycaster
{
    public override Camera eventCamera => null;
    public override void Raycast(PointerEventData eventData, List<RaycastResult> results)
    {
        if (!new Rect(0, 0, 100, 100).Contains(eventData.position)) return;
        results.Add(new RaycastResult { gameObject = gameObject, module = this, screenPosition = eventData.position });
    }
}
