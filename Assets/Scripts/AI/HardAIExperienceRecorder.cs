using System;
using System.IO;
using System.Text;
using BlockNations.AI;
using UnityEngine;

// Explicit, bounded development capture. Records contain fair game observations only.
// The runtime invokes this exclusively from the VsAI adapter; no PBp state/identity is read.
public static class HardAIExperienceRecorder
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private const int RecordLimit = 512;
    private const long ByteLimit = 16 * 1024 * 1024;
    public static bool Enabled { get; private set; }
    public static string CurrentPath { get; private set; }
    public static int RecordsWritten { get; private set; }
    private static long bytesWritten;

    public static void Start()
    {
        string directory = Path.Combine(Application.persistentDataPath, "DevMatchResults", "AIExperience");
        CurrentPath = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N") + ".jsonl");
        RecordsWritten = 0;
        bytesWritten = 0;
        Enabled = true;
    }
    public static void Stop() => Enabled = false;
    public static void Append(AIExperienceRecord record)
    {
        if (!Enabled || record == null) return;
        try
        {
            string line = JsonUtility.ToJson(record) + "\n";
            int bytes = Encoding.UTF8.GetByteCount(line);
            if (RecordsWritten >= RecordLimit || bytesWritten + bytes > ByteLimit)
            {
                Enabled = false;
                Debug.Log("[Hard AI records] Capture stopped at its development storage limit.");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(CurrentPath));
            File.AppendAllText(CurrentPath, line, new UTF8Encoding(false));
            bytesWritten += bytes;
            RecordsWritten++;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
        {
            Enabled = false;
            Debug.LogWarning($"[Hard AI records] Capture stopped after {e.GetType().Name}.");
        }
    }
#else
    public static bool Enabled => false;
    public static void Append(AIExperienceRecord record) { }
#endif
}
