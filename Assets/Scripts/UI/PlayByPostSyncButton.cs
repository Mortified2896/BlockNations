using UnityEngine;
using System.Collections.Generic;

public class PlayByPostSyncButton : MonoBehaviour
{
    private const int CheckIntervalFrames = 10;

    private static readonly List<PlayByPostSyncButton> Instances = new List<PlayByPostSyncButton>(8);
    private static bool globalHooked;
    private static int globalNextCheckFrame;

    private bool registered;
    private int nextCheckFrame;

    private void Awake()
    {
        EnsureGlobalHook();
        RegisterIfNeeded();
    }

    private void OnEnable()
    {
        nextCheckFrame = 0;
        RefreshVisibility();
    }

    private void Update()
    {
        int frame = Time.frameCount;
        if (frame < nextCheckFrame)
            return;

        nextCheckFrame = frame + CheckIntervalFrames;
        RefreshVisibility();
    }

    private void OnDestroy()
    {
        if (!registered)
            return;

        registered = false;

        for (int i = Instances.Count - 1; i >= 0; i--)
        {
            if (Instances[i] == this)
            {
                Instances.RemoveAt(i);
                break;
            }
        }

        if (Instances.Count == 0)
            RemoveGlobalHook();
    }

    private void RegisterIfNeeded()
    {
        if (registered)
            return;

        registered = true;
        Instances.Add(this);
    }

    private static void EnsureGlobalHook()
    {
        if (globalHooked)
            return;

        globalHooked = true;
        globalNextCheckFrame = 0;
        Application.onBeforeRender += GlobalTick;
    }

    private static void RemoveGlobalHook()
    {
        if (!globalHooked)
            return;

        globalHooked = false;
        Application.onBeforeRender -= GlobalTick;
    }

    private static void GlobalTick()
    {
        int frame = Time.frameCount;
        if (frame < globalNextCheckFrame)
            return;

        globalNextCheckFrame = frame + CheckIntervalFrames;

        for (int i = Instances.Count - 1; i >= 0; i--)
        {
            PlayByPostSyncButton instance = Instances[i];
            if (instance == null)
            {
                Instances.RemoveAt(i);
                continue;
            }

            if (instance.isActiveAndEnabled)
                continue;

            instance.RefreshVisibility();
        }

        if (Instances.Count == 0)
            RemoveGlobalHook();
    }

    public void OnClick()
    {
        Debug.Log("PBp Sync button clicked");
        TurnManager turnManager = TurnManager.Instance;
        if (turnManager == null)
            return;

        turnManager.PlayByPostSyncNow();
    }

    private void RefreshVisibility()
    {
        bool shouldShow = false;

        TurnManager turnManager = TurnManager.Instance;
        if (turnManager != null)
        {
            shouldShow =
                turnManager.currentMode == TurnManager.GameMode.PlayByPost &&
                !turnManager.gameOver;
        }

        if (gameObject.activeSelf != shouldShow)
            gameObject.SetActive(shouldShow);
    }
}
