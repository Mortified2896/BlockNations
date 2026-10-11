using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Run only in the isolated, credential-free snapshot prepared by Tools/WebRelease/build.py.
public static class SinglePlayerWebBuild
{
    public const string Define = "BLOCKNATIONS_SINGLE_PLAYER_WEB";

    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Cannot build during Play Mode.");
        string marker = Path.GetFullPath(".single-player-web-snapshot");
        if (!File.Exists(marker)) throw new InvalidOperationException("Use Tools/WebRelease/build.py to prepare the isolated release project.");
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            throw new InvalidOperationException("Install Web Build Support for the version in ProjectVersion.txt.");
        if (File.Exists("Assets/Resources/PbpTransportSettings.asset"))
            throw new InvalidOperationException("Multiplayer credential asset must be absent from the web snapshot.");
        bool multiplayer = Environment.GetEnvironmentVariable("BLOCKNATIONS_WEB_MULTIPLAYER") == "1";
        bool learnedAi = Environment.GetEnvironmentVariable("BLOCKNATIONS_WEB_LEARNED_AI") == "1";
        if (!multiplayer || learnedAi)
        {
            var policy = Resources.Load<LearnedPolicyRelease>(LearnedPolicyRelease.ResourcePath);
            if (policy == null) throw new InvalidOperationException("The frozen learned AI release is missing.");
            var manifest = policy.Validate(7, learnedAi ? BlockNations.Simulation.MatchRuleProfile.CoreThreeVersion : null);
            if (learnedAi && manifest.samplingMode != "Hard")
                throw new InvalidOperationException("The public opponent requires its evaluated Hard sampling release.");
            foreach (BlockNations.AI.LearnedDifficulty difficulty in Enum.GetValues(typeof(BlockNations.AI.LearnedDifficulty)))
                policy.ModelFor(difficulty);
            LearnedPolicyReleaseBuilder.ValidateFrozenSource();
        }

        string output = Environment.GetEnvironmentVariable("BLOCKNATIONS_WEB_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("Missing BLOCKNATIONS_WEB_OUTPUT.");
        Directory.CreateDirectory(output);
        PlayerSettings.WebGL.template = "PROJECT:SinglePlayer";
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        // Unity decodes .unityweb files, without relying on Content-Encoding at the host.
        PlayerSettings.WebGL.decompressionFallback = true;
        // Protected game bytes must be authorized on each visit, including
        // after logout. The notification worker never caches game resources.
        PlayerSettings.WebGL.dataCaching = false;
        PlayerSettings.WebGL.nameFilesAsHashes = true;
        PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
        PlayerSettings.WebGL.showDiagnostics = false;
        PlayerSettings.WebGL.initialMemorySize = 128;
        PlayerSettings.WebGL.maximumMemorySize = 512;
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
        PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.WebGL, Il2CppCompilerConfiguration.Release);
        EditorUserBuildSettings.development = false;
        EditorUserBuildSettings.allowDebugging = false;
#if UNITY_WEBGL
        UnityEditor.WebGL.UserBuildSettings.codeOptimization = UnityEditor.WebGL.WasmCodeOptimization.RuntimeSpeed;
#endif
        PlayerSettings.runInBackground = false;

        var defines = new System.Collections.Generic.List<string> { multiplayer ? "BLOCKNATIONS_ACCOUNT_WEB" : Define };
        if (learnedAi) defines.Add("BLOCKNATIONS_LEARNED_AI_WEB");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/MainMenu.unity", "Assets/Scenes/SampleScene.unity", "Assets/Scenes/Tutorial.unity", "Assets/Scenes/Advanced.unity" },
            target = BuildTarget.WebGL, locationPathName = output,
            options = BuildOptions.None, extraScriptingDefines = defines.ToArray()
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Single-player web build failed: " + report.summary.result);
        Debug.Log("[Single Player Web] Build succeeded: " + output);
    }
}
