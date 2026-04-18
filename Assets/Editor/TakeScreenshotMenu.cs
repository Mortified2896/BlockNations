using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class TakeScreenshotMenu
{
    private const string MenuPath = "Tools/Block Nations/Take Screenshot";
    private const double VerificationTimeoutSeconds = 5.0d;

    private static string pendingScreenshotPath;
    private static double pendingScreenshotStartTime;

    [MenuItem(MenuPath)]
    private static void TakeScreenshot()
    {
        string screenshotPath = BuildScreenshotPath();
        string screenshotDirectory = Path.GetDirectoryName(screenshotPath);
        if (!string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            Directory.CreateDirectory(screenshotDirectory);
        }

        pendingScreenshotPath = screenshotPath;
        pendingScreenshotStartTime = EditorApplication.timeSinceStartup;
        EditorApplication.update -= VerifyPendingScreenshot;
        EditorApplication.update += VerifyPendingScreenshot;

        ScreenCapture.CaptureScreenshot(screenshotPath);
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
        DirectoryInfo projectDirectory = Directory.GetParent(Application.dataPath);
        if (projectDirectory != null)
        {
            return Path.Combine(projectDirectory.FullName, "Screenshots");
        }

        return Path.Combine(Application.dataPath, "..", "Screenshots");
    }

    private static void VerifyPendingScreenshot()
    {
        if (string.IsNullOrWhiteSpace(pendingScreenshotPath))
        {
            EditorApplication.update -= VerifyPendingScreenshot;
            return;
        }

        if (File.Exists(pendingScreenshotPath))
        {
            Debug.Log($"[EditorScreenshot] Screenshot saved to: {pendingScreenshotPath}");
            ClearPendingVerification();
            return;
        }

        double elapsedSeconds = EditorApplication.timeSinceStartup - pendingScreenshotStartTime;
        if (elapsedSeconds < VerificationTimeoutSeconds)
        {
            return;
        }

        Debug.LogError(
            $"[EditorScreenshot] Screenshot did not appear within {VerificationTimeoutSeconds:0.##} seconds. " +
            $"Path: {pendingScreenshotPath}. Play Mode Active: {EditorApplication.isPlaying}.");
        ClearPendingVerification();
    }

    private static void ClearPendingVerification()
    {
        EditorApplication.update -= VerifyPendingScreenshot;
        pendingScreenshotPath = null;
        pendingScreenshotStartTime = 0d;
    }
}
