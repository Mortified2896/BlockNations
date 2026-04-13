using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class TakeScreenshotMenu
{
    private const string MenuPath = "Tools/Block Nations/Take Screenshot";

    [MenuItem(MenuPath)]
    private static void TakeScreenshot()
    {
        string screenshotPath = BuildScreenshotPath();
        string screenshotDirectory = Path.GetDirectoryName(screenshotPath);
        if (!string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            Directory.CreateDirectory(screenshotDirectory);
        }

        ScreenCapture.CaptureScreenshot(screenshotPath);
        Debug.Log($"[EditorScreenshot] Screenshot saved to: {screenshotPath}");
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateTakeScreenshot()
    {
        return EditorApplication.isPlaying;
    }

    private static string BuildScreenshotPath()
    {
        string screenshotsDirectory = GetScreenshotsDirectory();
        string fileName = $"blocknations_{System.DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
        return Path.Combine(screenshotsDirectory, fileName);
    }

    private static string GetScreenshotsDirectory()
    {
        string userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfilePath))
        {
            return Path.Combine(userProfilePath, "Downloads");
        }

        DirectoryInfo projectDirectory = Directory.GetParent(Application.dataPath);
        if (projectDirectory != null)
        {
            return Path.Combine(projectDirectory.FullName, "Screenshots");
        }

        return Path.Combine(Application.dataPath, "..", "Screenshots");
    }
}
