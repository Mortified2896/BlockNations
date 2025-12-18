using UnityEngine;

/// <summary>
/// Small cross-scene flag to request showing the tutorial after loading gameplay.
/// Also stores a "completed" preference so the tutorial can auto-show on first run.
/// </summary>
public static class TutorialLaunch
{
    private const string TutorialCompletedKey = "tutorial_completed_v1";
    private const string TutorialForceShowKey = "tutorial_force_show_v1";

    private static bool pendingShow;

    public static void RequestShow(bool resetCompleted = true)
    {
        pendingShow = true;
        PlayerPrefs.SetInt(TutorialForceShowKey, 1);
        if (resetCompleted)
        {
            PlayerPrefs.SetInt(TutorialCompletedKey, 0);
            PlayerPrefs.Save();
        }
    }

    public static bool TryConsumeShowRequest()
    {
        bool shouldShow = pendingShow || PlayerPrefs.GetInt(TutorialForceShowKey, 0) == 1;
        if (!shouldShow)
            return false;

        pendingShow = false;
        PlayerPrefs.SetInt(TutorialForceShowKey, 0);
        PlayerPrefs.Save();
        return true;
    }

    public static bool IsShowRequested()
    {
        return pendingShow || PlayerPrefs.GetInt(TutorialForceShowKey, 0) == 1;
    }

    public static bool ShouldAutoShow()
    {
        return PlayerPrefs.GetInt(TutorialCompletedKey, 0) == 0;
    }

    public static void MarkCompleted()
    {
        PlayerPrefs.SetInt(TutorialCompletedKey, 1);
        PlayerPrefs.Save();
    }
}
