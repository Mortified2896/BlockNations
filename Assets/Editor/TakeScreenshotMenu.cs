using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class TakeScreenshotMenu
{
    private const string MenuPath = "Tools/Block Nations/Take Screenshot";
    private const double VerificationTimeoutSeconds = 30.0d;
    private const double DefaultCaptureDelaySeconds = 0.3d;
    private const string FilenamePrefix = "blocknations";

    private static string pendingScreenshotPath;
    private static double pendingScreenshotRequestedTime;
    private static double pendingScreenshotCaptureReadyTime;
    private static bool pendingScreenshotCaptureStarted;

    [MenuItem(MenuPath)]
    private static void TakeScreenshot()
    {
        RequestScreenshot();
    }

    public static string RequestScreenshot(string label = null, double captureDelaySeconds = DefaultCaptureDelaySeconds)
    {
        string screenshotPath = BuildScreenshotPath(label);
        string screenshotDirectory = Path.GetDirectoryName(screenshotPath);
        if (!string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            Directory.CreateDirectory(screenshotDirectory);
        }

        pendingScreenshotPath = screenshotPath;
        pendingScreenshotRequestedTime = EditorApplication.timeSinceStartup;
        pendingScreenshotCaptureReadyTime = pendingScreenshotRequestedTime + Math.Max(0d, captureDelaySeconds);
        pendingScreenshotCaptureStarted = false;
        EditorApplication.update -= VerifyPendingScreenshot;
        EditorApplication.update += VerifyPendingScreenshot;

        Debug.Log(
            $"[EditorScreenshot] Requested screenshot. " +
            $"Path: {pendingScreenshotPath}. " +
            $"Delay: {captureDelaySeconds:0.##} seconds. " +
            $"Timeout: {VerificationTimeoutSeconds:0.##} seconds. " +
            $"Play Mode Active: {EditorApplication.isPlaying}.");
        return screenshotPath;
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateTakeScreenshot()
    {
        return EditorApplication.isPlaying;
    }

    private static string BuildScreenshotPath(string label)
    {
        string screenshotsDirectory = GetScreenshotsDirectory();
        string sanitizedLabel = SanitizeLabel(label);
        string labelSegment = string.IsNullOrWhiteSpace(sanitizedLabel) ? string.Empty : $"_{sanitizedLabel}";
        string fileName = $"{FilenamePrefix}{labelSegment}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
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
            ClearPendingVerification();
            return;
        }

        if (!pendingScreenshotCaptureStarted)
        {
            if (EditorApplication.timeSinceStartup < pendingScreenshotCaptureReadyTime)
            {
                return;
            }

            pendingScreenshotCaptureStarted = true;
            ScreenCapture.CaptureScreenshot(pendingScreenshotPath);
        }

        if (File.Exists(pendingScreenshotPath))
        {
            double successElapsedSeconds = EditorApplication.timeSinceStartup - pendingScreenshotRequestedTime;
            Debug.Log(
                $"[EditorScreenshot] Screenshot saved successfully. " +
                $"Requested Path: {pendingScreenshotPath}. " +
                $"Elapsed: {successElapsedSeconds:0.##} seconds. " +
                $"Timeout: {VerificationTimeoutSeconds:0.##} seconds. " +
                $"Play Mode Active: {EditorApplication.isPlaying}.");
            ClearPendingVerification();
            return;
        }

        double elapsedSeconds = EditorApplication.timeSinceStartup - pendingScreenshotRequestedTime;
        if (elapsedSeconds < VerificationTimeoutSeconds)
        {
            return;
        }

        Debug.LogError(
            $"[EditorScreenshot] Screenshot failed to appear. " +
            $"Requested Path: {pendingScreenshotPath}. " +
            $"Elapsed: {elapsedSeconds:0.##} seconds. " +
            $"Timeout: {VerificationTimeoutSeconds:0.##} seconds. " +
            $"Play Mode Active: {EditorApplication.isPlaying}.");
        ClearPendingVerification();
    }

    private static void ClearPendingVerification()
    {
        EditorApplication.update -= VerifyPendingScreenshot;
        pendingScreenshotPath = null;
        pendingScreenshotRequestedTime = 0d;
        pendingScreenshotCaptureReadyTime = 0d;
        pendingScreenshotCaptureStarted = false;
    }

    private static string SanitizeLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        string trimmed = label.Trim().ToLowerInvariant();
        char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
        char[] buffer = new char[trimmed.Length];
        int count = 0;
        bool previousWasSeparator = false;

        for (int i = 0; i < trimmed.Length; i++)
        {
            char character = trimmed[i];
            bool isInvalid = Array.IndexOf(invalidFileNameChars, character) >= 0;
            bool isSeparator = isInvalid || char.IsWhiteSpace(character) || character == '-' || character == '_';

            if (char.IsLetterOrDigit(character))
            {
                buffer[count++] = character;
                previousWasSeparator = false;
                continue;
            }

            if (isSeparator)
            {
                if (previousWasSeparator || count <= 0)
                {
                    continue;
                }

                buffer[count++] = '_';
                previousWasSeparator = true;
            }
        }

        while (count > 0 && buffer[count - 1] == '_')
        {
            count--;
        }

        return count > 0 ? new string(buffer, 0, count) : string.Empty;
    }
}
