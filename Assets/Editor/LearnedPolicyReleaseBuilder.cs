using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using Unity.InferenceEngine;
using UnityEngine;

// Explicitly freeze/import release references. Never reads the live training folder.
public static class LearnedPolicyReleaseBuilder
{
    private const string DirectoryPath = "Assets/Resources/LearnedAI/";

    [MenuItem("Tools/AI/Refresh Learned AI Release")]
    public static void Build()
    {
        ValidateFrozenSource();
        AssetDatabase.ImportAsset(DirectoryPath + "Compact7.onnx", ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(DirectoryPath + "manifest.json", ImportAssetOptions.ForceSynchronousImport);
        var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(DirectoryPath + "Compact7.onnx");
        var metadata = AssetDatabase.LoadAssetAtPath<TextAsset>(DirectoryPath + "manifest.json");
        if (model == null || metadata == null) throw new InvalidOperationException("Export the frozen policy before building its release.");
        var release = AssetDatabase.LoadAssetAtPath<LearnedPolicyRelease>(DirectoryPath + "Release.asset");
        if (release == null)
        {
            release = ScriptableObject.CreateInstance<LearnedPolicyRelease>();
            AssetDatabase.CreateAsset(release, DirectoryPath + "Release.asset");
        }
        release.easyModel = model;
        release.mediumModel = model;
        release.hardModel = model;
        release.metadata = metadata;
        release.Validate(7);
        EditorUtility.SetDirty(release);
        AssetDatabase.SaveAssets();
        ValidateCpu(model);
        Debug.Log("[Learned AI Release] Frozen policy references and CPU inference verified.");
    }

    public static void ValidateFrozenSource()
    {
        var metadata = JsonUtility.FromJson<LearnedPolicyRelease.Manifest>(File.ReadAllText(DirectoryPath + "manifest.json"));
        using var hash = SHA256.Create();
        string actual = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(DirectoryPath + "Compact7.onnx")))
            .Replace("-", "").ToLowerInvariant();
        if (metadata == null || !string.Equals(actual, metadata.modelSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("The frozen model does not match its release manifest.");
    }

    [Serializable] private sealed class Dataset { public Example[] examples; }
    [Serializable] private sealed class Example { public float[] observation, mask, probabilities; }

    private static void ValidateCpu(ModelAsset asset)
    {
        string fixturePath = Environment.GetEnvironmentVariable("BLOCKNATIONS_POLICY_VALIDATION_DATA");
        if (string.IsNullOrEmpty(fixturePath)) return;
        Dataset dataset = JsonUtility.FromJson<Dataset>(File.ReadAllText(fixturePath));
        if (dataset?.examples == null || dataset.examples.Length == 0) throw new InvalidOperationException("No policy validation fixtures.");
        using var worker = new Worker(ModelLoader.Load(asset), BackendType.CPU);
        float largestError = 0;
        foreach (Example example in dataset.examples)
        {
            using var input = new Tensor<float>(new TensorShape(1, 3120), example.observation);
            using var mask = new Tensor<float>(new TensorShape(1, 259), example.mask);
            worker.SetInput("obs_0", input); worker.SetInput("action_masks", mask);
            worker.Schedule();
            var output = worker.PeekOutput("policy_probabilities") as Tensor<float>;
            float[] actual = output.DownloadToArray();
            if (actual.Length != 259) throw new InvalidOperationException("The policy output dimensions differ.");
            for (int i = 0; i < actual.Length; i++)
            {
                largestError = Mathf.Max(largestError, Mathf.Abs(actual[i] - example.probabilities[i]));
                if (float.IsNaN(actual[i]) || float.IsInfinity(actual[i]) || largestError > 0.0001f)
                    throw new InvalidOperationException("Unity CPU inference differs from the exported actor probabilities.");
            }
        }
        Debug.Log($"[Learned AI Release] {dataset.examples.Length} CPU parity fixtures; maximum probability error {largestError}.");
    }
}
