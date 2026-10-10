using System;
using System.IO;
using UnityEngine;

// A human inference session lives immediately inside its owning run. Return
// requests only affect the optional viewer, never the learner or worker controls.
public static class TrainingViewerReturn
{
    [Serializable] private sealed class Manifest { public string owner; public int schema, boardSize; }

    public static bool Request(string humanSessionDirectory, int boardSize)
    {
        if (string.IsNullOrEmpty(humanSessionDirectory)) return false;
        DirectoryInfo run = Directory.GetParent(Path.GetFullPath(humanSessionDirectory));
        if (run == null) return false;
        string manifestPath = Path.Combine(run.FullName, "run.json");
        if (!File.Exists(manifestPath)) return false;
        Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
        if (manifest == null || manifest.owner != "BlockNations.LocalTraining.v1" ||
            manifest.schema != 2 || manifest.boardSize != boardSize) return false;
        File.WriteAllText(Path.Combine(run.FullName, "viewer-live.request"), "Show the current training matches.\n");
        File.WriteAllText(Path.Combine(run.FullName, "viewer.request"), "Return from human playtest.\n");
        return true;
    }
}
