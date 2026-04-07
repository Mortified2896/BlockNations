using UnityEngine;

/// <summary>
/// Simple static helper to request a load of the last save when entering the gameplay scene.
/// </summary>
public static class SaveLoadRequest
{
    private static bool pending;
    private static string pendingPath;

    public static bool HasPendingRequest => pending;

    public static void RequestLoad(string path = null)
    {
        pending = true;
        if (string.IsNullOrWhiteSpace(path))
        {
            pendingPath = System.IO.Path.Combine(Application.persistentDataPath, "save.json");
        }
        else
        {
            pendingPath = path;
        }
    }

    public static bool TryConsume(out string path)
    {
        if (!pending)
        {
            path = null;
            return false;
        }

        path = pendingPath;
        pending = false;
        pendingPath = null;
        return true;
    }

    public static void ClearPending()
    {
        pending = false;
        pendingPath = null;
    }
}
