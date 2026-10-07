using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

[InitializeOnLoad]
public static class LocalLunaBridgeMenu
{
    private const string OwnedPidKey = "BlockNations.Luna.BridgePid";
    private const string OwnedStartKey = "BlockNations.Luna.BridgeStart";
    private static Process ownedBridge;
    static LocalLunaBridgeMenu()
    {
        int pid = SessionState.GetInt(OwnedPidKey, 0);
        if (pid > 0)
        {
            try
            {
                Process candidate = Process.GetProcessById(pid);
                if (!candidate.HasExited && candidate.StartTime.ToUniversalTime().Ticks.ToString() == SessionState.GetString(OwnedStartKey, ""))
                    ownedBridge = candidate;
                else candidate.Dispose();
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
        EditorApplication.quitting += StopOwnedBridge;
    }

    [MenuItem("Tools/Block Nations/Luna Bridge/Start (64 calls)")]
    public static void StartBridge()
    {
        if (ownedBridge != null && !ownedBridge.HasExited)
        { Debug.Log("[Luna playtest] This Editor's bridge is already running."); return; }
        string script = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/AI/luna_playtest_bridge.py"));
        string python = File.Exists("/usr/local/bin/python3.10") ? "/usr/local/bin/python3.10" : "python3";
        var info = new ProcessStartInfo(python) {
            Arguments = "\"" + script + "\" --max-calls 64",
            UseShellExecute = false, CreateNoWindow = true };
        ownedBridge?.Dispose();
        ownedBridge = Process.Start(info);
        if (ownedBridge == null) throw new InvalidOperationException("Could not start the local Luna bridge.");
        SessionState.SetInt(OwnedPidKey, ownedBridge.Id);
        SessionState.SetString(OwnedStartKey, ownedBridge.StartTime.ToUniversalTime().Ticks.ToString());
        Debug.Log("[Luna playtest] Started a local bridge with a 64-call limit at 127.0.0.1:8769. Uses the existing Codex sign-in. No match was started.");
    }

    [MenuItem("Tools/Block Nations/Luna Bridge/Stop")]
    public static void StopOwnedBridge()
    {
        if (ownedBridge == null) return;
        try
        {
            if (!ownedBridge.HasExited)
            {
                if (File.Exists("/bin/kill"))
                {
                    using (Process signal = Process.Start(new ProcessStartInfo("/bin/kill", "-TERM " + ownedBridge.Id) { UseShellExecute = false, CreateNoWindow = true }))
                        signal?.WaitForExit(1000);
                    if (!ownedBridge.WaitForExit(1500)) ownedBridge.Kill();
                }
                else ownedBridge.Kill();
            }
        }
        catch (InvalidOperationException) { }
        finally
        {
            ownedBridge.Dispose(); ownedBridge = null;
            SessionState.EraseInt(OwnedPidKey);
            SessionState.EraseString(OwnedStartKey);
        }
    }
}
