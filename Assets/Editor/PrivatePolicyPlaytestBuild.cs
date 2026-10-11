using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Build an isolated local game with the existing learned-difficulty controls.
// The Python wrapper prepares a credential-free snapshot and pins its model.
public static class PrivatePolicyPlaytestBuild
{
    public const string Marker = ".private-policy-playtest-snapshot";

    public static void ConfigureValidation()
    {
        if (!File.Exists(Marker)) throw new InvalidOperationException("Use the owned private snapshot.");
        var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone).Split(';');
        var selected = new System.Collections.Generic.List<string>();
        foreach (string define in defines)
            if (!string.IsNullOrEmpty(define) && define != "BLOCKNATIONS_ACCOUNT_WEB" &&
                define != "BLOCKNATIONS_LEARNED_AI_WEB" && define != SinglePlayerWebBuild.Define)
                selected.Add(define);
        selected.Add(SinglePlayerWebBuild.Define);
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, string.Join(";", selected));
        PlayerSettings.companyName = "BlockNationsDevelopment";
        PlayerSettings.productName = "BlockNations AI Playtest";
        // Native scene switches retain the menu's pending selection. The flow
        // tests enter Play Mode with the same lifetime for that static state.
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorBuildSettings.scenes = new[] {
            new EditorBuildSettingsScene("Assets/Scenes/MainMenu.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/SampleScene.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Tutorial.unity", true)
        };
        PrepareGameplayScene();
        AssetDatabase.SaveAssets();
    }

    // Only the owned snapshot scene is changed. Its existing camera, input and
    // UITK references are preserved; every added reference is serialized here.
    public static PrivatePolicyPlaytestController PrepareGameplayScene()
    {
        if (!File.Exists(Marker)) throw new InvalidOperationException("Use the owned private snapshot.");
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        TurnManager manager = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (TurnManager candidate in root.GetComponentsInChildren<TurnManager>(true))
            {
                if (manager != null) throw new InvalidOperationException("The private scene requires exactly one TurnManager.");
                manager = candidate;
            }
        if (manager == null || manager.gridManager == null)
            throw new InvalidOperationException("Preserve the authored gameplay manager and grid references.");
        var serialized = new SerializedObject(manager);
        serialized.FindProperty("externallyDrivenMatch").boolValue = true;
        serialized.FindProperty("autoSaveEnabled").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        manager.gridManager.width = manager.gridManager.height = 7;
        EditorUtility.SetDirty(manager.gridManager);
        var driver = manager.GetComponent<PrivatePolicyPlaytestController>();
        if (driver == null) driver = manager.gameObject.AddComponent<PrivatePolicyPlaytestController>();
        var wired = new SerializedObject(driver);
        wired.FindProperty("turnManager").objectReferenceValue = manager;
        wired.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return driver;
    }

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
        PrepareGameplayScene();

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
