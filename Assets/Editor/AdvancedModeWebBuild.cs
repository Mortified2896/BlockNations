using System;
using System.IO;
using BlockNations.AdvancedMode;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

public static class AdvancedModeWebBuild
{
    public static void Build()
    {
        if (!File.Exists(".advanced-mode-web-snapshot")) throw new InvalidOperationException("Build only in the dedicated advanced snapshot.");
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL)) throw new InvalidOperationException("WebGL support is required.");
        string output = Environment.GetEnvironmentVariable("BLOCKNATIONS_ADVANCED_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("Missing advanced output directory.");
        Directory.CreateDirectory("Assets/GeneratedAdvanced"); AssetDatabase.Refresh();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var panel = ScriptableObject.CreateInstance<PanelSettings>();
        panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/Resources/AdvancedPlaytest/AdvancedTheme.tss");
        panel.scaleMode = PanelScaleMode.ConstantPixelSize;
        panel.scale = 1;
        panel.clearColor = true; panel.colorClearValue = new Color(23f / 255, 37f / 255, 48f / 255);
        AssetDatabase.CreateAsset(panel, "Assets/GeneratedAdvanced/AdvancedPanel.asset");
        var ui = new GameObject("AdvancedPlaytest");
        var document = ui.AddComponent<UIDocument>(); document.panelSettings = panel;
        var view = ui.AddComponent<AdvancedGamePlaytestView>();
        view.Configure(document, AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Resources/AdvancedPlaytest/Advanced.uxml"),
            AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Resources/AdvancedPlaytest/Advanced.uss"),
            AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Warrior.png"),
            AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Rider.png"),
            AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Archer.png"));
        var events = new GameObject("UIInput"); events.AddComponent<EventSystem>();
        events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        EditorSceneManager.SaveScene(scene, "Assets/GeneratedAdvanced/AdvancedPlaytest.unity");
        PlayerSettings.WebGL.template = "PROJECT:Advanced";
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = false;
        PlayerSettings.WebGL.nameFilesAsHashes = true;
        PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
        PlayerSettings.WebGL.initialMemorySize = 32;
        PlayerSettings.WebGL.maximumMemorySize = 256;
        PlayerSettings.defaultWebScreenWidth = 980; PlayerSettings.defaultWebScreenHeight = 900;
        PlayerSettings.runInBackground = false;
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
        PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.WebGL, Il2CppCompilerConfiguration.Release);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/GeneratedAdvanced/AdvancedPlaytest.unity" }, target = BuildTarget.WebGL,
            locationPathName = output, options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Advanced WebGL build failed: " + report.summary.result);
        Debug.Log("[Advanced Playtest] WebGL build succeeded.");
    }
}
