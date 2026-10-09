using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class TrainingSimulationWorkerBuild
{
    public static string WorkerPath => Path.GetFullPath("Build/TrainingWorker/BlockNations.TrainingWorker");
    [MenuItem("Tools/Block Nations/Local ML Training/Build C# Training Worker")]
    public static void Build()
    {
        string dotnet = Environment.GetEnvironmentVariable("BLOCKNATIONS_DOTNET");
        if (string.IsNullOrEmpty(dotnet))
        {
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                if (File.Exists(Path.Combine(directory, "dotnet"))) { dotnet = Path.Combine(directory, "dotnet"); break; }
        }
        if (string.IsNullOrEmpty(dotnet) && File.Exists("/opt/homebrew/bin/dotnet")) dotnet = "/opt/homebrew/bin/dotnet";
        if (string.IsNullOrEmpty(dotnet) || !File.Exists(dotnet)) throw new FileNotFoundException("Install the .NET 8 SDK to build the shared C# training worker.");
        string project = Path.GetFullPath("Tools/Training/SimulationWorker/BlockNations.TrainingWorker.csproj");
        string output = Path.GetDirectoryName(WorkerPath);
        var messages = new StringBuilder();
        using (var process = new Process { StartInfo = new ProcessStartInfo(dotnet,
            "publish " + Quote(project) + " -c Release -r osx-arm64 --self-contained true -o " + Quote(output) + " --nologo")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } })
        {
            DataReceivedEventHandler read = (_, data) => { if (data.Data != null) lock (messages) { if (messages.Length < 16000) messages.AppendLine(data.Data); } };
            process.OutputDataReceived += read; process.ErrorDataReceived += read;
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            if (!process.WaitForExit(180000)) { process.Kill(); throw new TimeoutException("C# worker build timed out."); }
            process.WaitForExit();
            if (process.ExitCode != 0 || !File.Exists(WorkerPath)) throw new InvalidOperationException("C# worker build failed: " + messages);
        }
        UnityEngine.Debug.Log("[ML Training] Self-contained C# worker: " + WorkerPath);
    }
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
