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
        public int actionSchema;
        public int boardSize;
        public int observationSize;
        public int actionCount;
        public string rulesVersion;
        public string modelVersion;
        public string modelSha256;
        public string checkpointSha256;
        public string probabilityOutput;
        public string samplingMode;
    }

    public Manifest Validate(int boardSize, string expectedRulesVersion = null)
    {
        if (metadata == null) throw new InvalidOperationException("The learned AI release has no manifest.");
        Manifest manifest = JsonUtility.FromJson<Manifest>(metadata.text);
        if (manifest == null || !LearnedObservationContract.Supports(manifest.schema) || manifest.boardSize != boardSize ||
            manifest.observationSize != LearnedObservationContract.Size(manifest.schema) ||
            (manifest.schema > 2 && manifest.actionSchema != LearnedActionSchema.Version) || manifest.actionCount != LearnedActionSchema.ActionCount ||
            string.IsNullOrEmpty(manifest.rulesVersion) || string.IsNullOrEmpty(manifest.modelVersion) ||
            (expectedRulesVersion != null && manifest.rulesVersion != expectedRulesVersion) ||
            manifest.probabilityOutput != "policy_probabilities")
            throw new InvalidOperationException("The learned AI release does not match this board, rules or observation schema.");
        try { MatchRuleProfile.Resolve(manifest.rulesVersion); }
        catch (ArgumentException ex) { throw new InvalidOperationException("The learned AI release uses unsupported rules.", ex); }
        return manifest;
    }

    public MatchRuleProfile RulesForBoard(int boardSize) => MatchRuleProfile.Resolve(Validate(boardSize).rulesVersion);

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
