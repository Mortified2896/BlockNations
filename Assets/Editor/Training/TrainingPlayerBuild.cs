using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Also callable with Unity -executeMethod TrainingPlayerBuild.BuildMac.
// A standalone arena avoids Editor overhead and supports unattended runs.
public static class TrainingPlayerBuild
{
    [MenuItem("Tools/Block Nations/Local ML Training/Build Mac Training Player")]
    public static void BuildMac()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building.");
        if (!File.Exists(TrainingSceneBuilder.ScenePath)) TrainingSceneBuilder.Create();
        string output = Environment.GetEnvironmentVariable("BLOCKNATIONS_TRAINING_APP");
        if (string.IsNullOrWhiteSpace(output)) output = Path.GetFullPath("Build/LocalTrainingV2.app");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        int architecture = PlayerSettings.GetArchitecture(NamedBuildTarget.Standalone);
        bool background = PlayerSettings.runInBackground;
        FullScreenMode fullscreen = PlayerSettings.fullScreenMode;
        try
        {
            PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, 1);
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            BuildReport result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { TrainingSceneBuilder.ScenePath }, locationPathName = output,
                target = BuildTarget.StandaloneOSX, options = BuildOptions.Development });
            if (result.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Training player build failed: " + result.summary.result);
            Debug.Log("[ML Training] Native arm64 training player: " + output);
        }
        finally
        {
            PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, architecture);
            PlayerSettings.runInBackground = background;
            PlayerSettings.fullScreenMode = fullscreen;
        }
    }
}
