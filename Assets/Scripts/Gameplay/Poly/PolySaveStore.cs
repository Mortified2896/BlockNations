using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BlockNations.Simulation.Poly;
using UnityEngine;

public static class PolySaveStore
{
    public static string PathFor(string root, string playerId)
    {
        using (var sha = SHA256.Create()) {
            string scope = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.IsNullOrEmpty(playerId) ? "editor" : playerId))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            return Path.Combine(root, "save_poly_v1_" + scope + ".json");
        }
    }
    public static PolyMatch Load(string path)
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 1000000) throw new ArgumentException("Invalid Poly save size.");
        return PolyMatch.Restore(JsonUtility.FromJson<PolySave>(File.ReadAllText(path)));
    }
    public static void Write(string path, PolyMatch match)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string json = JsonUtility.ToJson(match.CreateSave());
        // Validate the actual JSON codec before committing a new checkpoint.
        PolyMatch.Restore(JsonUtility.FromJson<PolySave>(json));
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        if (File.Exists(path)) {
            try { File.Replace(temporary, path, path + ".previous"); }
            catch (PlatformNotSupportedException) {
                File.Copy(path, path + ".previous", true);
                File.Copy(temporary, path, true); File.Delete(temporary);
            }
        } else File.Move(temporary, path);
    }
}
