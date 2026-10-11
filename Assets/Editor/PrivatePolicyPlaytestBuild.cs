using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Build an isolated local game with the existing learned-difficulty controls.
// The Python wrapper prepares a credential-free snapshot and pins its model.
public static class PrivatePolicyPlaytestBuild
{
    public const string Marker = ".private-policy-playtest-snapshot";

    public static void BuildMac()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Cannot build during Play Mode.");
        if (!File.Exists(Marker)) throw new InvalidOperationException("Use Tools/NativePlaytest/build.py.");
        if (File.Exists("Assets/Resources/PbpTransportSettings.asset") || Directory.Exists("Assets/Scripts/Training"))
            throw new InvalidOperationException("The private player requires the isolated game snapshot.");
        string output = Environment.GetEnvironmentVariable("BLOCKNATIONS_PRIVATE_PLAYTEST_OUTPUT");
        if (string.IsNullOrEmpty(output) || !output.EndsWith(".app", StringComparison.Ordinal) || Directory.Exists(output))
            throw new InvalidOperationException("Choose a new Mac app destination.");
        LearnedPolicyReleaseBuilder.ValidateFrozenSource();
        LearnedPolicyRelease release = Resources.Load<LearnedPolicyRelease>(LearnedPolicyRelease.ResourcePath);
        if (release == null) throw new InvalidOperationException("The frozen release is missing.");
        release.Validate(7, BlockNations.Simulation.MatchRuleProfile.CoreThreeVersion);
        foreach (BlockNations.AI.LearnedDifficulty difficulty in Enum.GetValues(typeof(BlockNations.AI.LearnedDifficulty)))
            release.ModelFor(difficulty);
        // Optional reference vectors execute through the same CPU backend as play.
        LearnedPolicyReleaseBuilder.Build();

        PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, 1);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.blocknations.privatepolicyplaytest");
        PlayerSettings.companyName = "BlockNationsDevelopment";
        PlayerSettings.productName = "BlockNations AI Playtest";
        PlayerSettings.defaultScreenWidth = 1400;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.runInBackground = false;
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/MainMenu.unity", "Assets/Scenes/SampleScene.unity", "Assets/Scenes/Tutorial.unity" },
            target = BuildTarget.StandaloneOSX, locationPathName = output, options = BuildOptions.None,
            // Reuse the single-player capability: Google/PBp and dev simulations
            // are hidden. Omit the public fixed-opponent define so presets remain.
            extraScriptingDefines = new[] { SinglePlayerWebBuild.Define }
        });
        if (result.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Private native build failed: " + result.summary.result);
        Debug.Log("[Private Policy Playtest] Native arm64 game built: " + output);
    }
}
