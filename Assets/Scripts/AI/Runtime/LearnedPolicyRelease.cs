using System;
using BlockNations.AI;
using BlockNations.Simulation;
using Unity.InferenceEngine;
using UnityEngine;

// A release references a frozen export, independently of the mutable training directory.
// Profiles may reference different evaluated models in later releases.
public sealed class LearnedPolicyRelease : ScriptableObject
{
    public const string ResourcePath = "LearnedAI/Release";
    public ModelAsset easyModel;
    public ModelAsset mediumModel;
    public ModelAsset hardModel;
    public TextAsset metadata;

    [Serializable]
    public sealed class Manifest
    {
        public int schema;
        public int boardSize;
        public int observationSize;
        public int actionCount;
        public string rulesVersion;
        public string modelVersion;
        public string modelSha256;
        public string checkpointSha256;
        public string probabilityOutput;
    }

    public Manifest Validate(int boardSize)
    {
        if (metadata == null) throw new InvalidOperationException("The learned AI release has no manifest.");
        Manifest manifest = JsonUtility.FromJson<Manifest>(metadata.text);
        if (manifest == null || manifest.schema != LearnedActionSchema.Version || manifest.boardSize != boardSize ||
            manifest.observationSize != LearnedActionSchema.ObservationSize || manifest.actionCount != LearnedActionSchema.ActionCount ||
            manifest.rulesVersion != SimulationRules.Version || string.IsNullOrEmpty(manifest.modelVersion) ||
            manifest.probabilityOutput != "policy_probabilities")
            throw new InvalidOperationException("The learned AI release does not match this board, rules or observation schema.");
        return manifest;
    }

    public ModelAsset ModelFor(LearnedDifficulty difficulty)
    {
        ModelAsset asset;
        switch (difficulty)
        {
            case LearnedDifficulty.Easy: asset = easyModel; break;
            case LearnedDifficulty.Medium: asset = mediumModel; break;
            case LearnedDifficulty.Hard: asset = hardModel; break;
            default: throw new ArgumentOutOfRangeException(nameof(difficulty));
        }
        if (asset == null) throw new InvalidOperationException("The selected learned AI model is missing.");
        return asset;
    }
}
