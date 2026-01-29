using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class FileTurnTransport : MonoBehaviour, ITurnTransport
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private bool initialized;
    private bool isAvailable;
    private string rootPath;

    public string TransportName => "File";
    public bool IsAvailable => isAvailable;

    public void Initialize()
    {
        if (initialized)
            return;

        initialized = true;

#if UNITY_WEBGL && !UNITY_EDITOR
        isAvailable = false;
        rootPath = null;
        return;
#else
        rootPath = Path.Combine(Application.persistentDataPath, "PlayByPost", "Turns");

        try
        {
            Directory.CreateDirectory(rootPath);
        }
        catch
        {
            isAvailable = false;
            return;
        }

        TryDeleteStaleTempFiles(rootPath);
        isAvailable = ProbeWriteDelete(rootPath);
#endif
    }

    public IEnumerator SubmitTurn(string gameId, int turnNumber, string json, Action<bool, string> done)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            done?.Invoke(false, "INVALID_GAME_ID");
            yield break;
        }

        if (turnNumber <= 0)
        {
            done?.Invoke(false, "INVALID_TURN");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            done?.Invoke(false, "EMPTY_JSON");
            yield break;
        }

        if (!IsAvailable)
        {
            done?.Invoke(false, "UNAVAILABLE");
            yield break;
        }

        // Predictable timing: for non-validation paths, always yield at least once before invoking callback.
        yield return null;

        string tmpPath = null;
        try
        {
            string gameFolder = GetGameFolderPath(gameId);
            Directory.CreateDirectory(gameFolder);

            string finalPath = Path.Combine(gameFolder, GetTurnFileName(turnNumber));
            tmpPath = finalPath + ".tmp";

            if (File.Exists(finalPath))
            {
                byte[] existingBytes = File.ReadAllBytes(finalPath);
                byte[] newBytes = Utf8NoBom.GetBytes(json);
                if (BytesEqual(existingBytes, newBytes))
                {
                    done?.Invoke(true, null);
                }
                else
                {
                    done?.Invoke(false, "CONFLICT");
                }
                yield break;
            }

            // Best-effort cleanup of a stale temp file.
            TryDeleteFile(tmpPath);

            byte[] bytes = Utf8NoBom.GetBytes(json);
            using (var fs = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush();
            }

            File.Move(tmpPath, finalPath);
            tmpPath = null;

            done?.Invoke(true, null);
        }
        catch
        {
            if (tmpPath != null)
            {
                TryDeleteFile(tmpPath);
            }
            done?.Invoke(false, "IO_ERROR");
        }
    }

    public IEnumerator TryFetchNextTurn(string gameId, int afterTurnNumber, Action<bool, string, int, string> done)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            done?.Invoke(false, "INVALID_GAME_ID", 0, null);
            yield break;
        }

        if (!IsAvailable)
        {
            done?.Invoke(false, "UNAVAILABLE", 0, null);
            yield break;
        }

        // Predictable timing: for non-validation paths, always yield at least once before invoking callback.
        yield return null;

        try
        {
            string gameFolder = GetGameFolderPath(gameId);
            if (!Directory.Exists(gameFolder))
            {
                done?.Invoke(false, "NO_TURN", 0, null);
                yield break;
            }

            int bestTurn = 0;
            string bestPath = null;

            foreach (string path in Directory.EnumerateFiles(gameFolder, "turn_*.json", SearchOption.TopDirectoryOnly))
            {
                if (!TryParseTurnNumberFromPath(path, out int turn))
                    continue;

                if (turn <= afterTurnNumber)
                    continue;

                if (bestTurn == 0 || turn < bestTurn)
                {
                    bestTurn = turn;
                    bestPath = path;
                }
            }

            if (bestTurn == 0 || string.IsNullOrEmpty(bestPath))
            {
                done?.Invoke(false, "NO_TURN", 0, null);
                yield break;
            }

            string json = File.ReadAllText(bestPath, Utf8NoBom);
            if (string.IsNullOrWhiteSpace(json))
            {
                done?.Invoke(false, "IO_ERROR", 0, null);
                yield break;
            }

            done?.Invoke(true, null, bestTurn, json);
        }
        catch
        {
            done?.Invoke(false, "IO_ERROR", 0, null);
        }
    }

    private string GetGameFolderPath(string gameId)
    {
        string safeGameId = ComputeHash128String(gameId);
        return Path.Combine(rootPath, safeGameId);
    }

    private static string GetTurnFileName(int turnNumber)
    {
        return $"turn_{turnNumber:D6}.json";
    }

    private static bool TryParseTurnNumberFromPath(string path, out int turnNumber)
    {
        turnNumber = 0;
        string name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name))
            return false;

        const string prefix = "turn_";
        const string suffix = ".json";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        string middle = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
        return int.TryParse(middle, out turnNumber) && turnNumber > 0;
    }

    private static string ComputeHash128String(string input)
    {
        if (input == null)
            input = string.Empty;

        return Hash128.Compute(input).ToString();
    }

    private static bool ProbeWriteDelete(string folder)
    {
        string probePath = null;
        try
        {
            probePath = Path.Combine(folder, "probe_" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var fs = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.WriteByte(0);
                fs.Flush();
            }
            File.Delete(probePath);
            return true;
        }
        catch
        {
            if (probePath != null)
            {
                TryDeleteFile(probePath);
            }
            return false;
        }
    }

    private static void TryDeleteStaleTempFiles(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return;

            foreach (string tmpPath in Directory.EnumerateFiles(folder, "turn_*.json.tmp", SearchOption.AllDirectories))
            {
                TryDeleteFile(tmpPath);
            }
        }
        catch
        {
            // Best-effort only.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort only.
        }
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a == null || b == null)
            return false;
        if (a.Length != b.Length)
            return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }
}
