using System;

[Serializable]
public sealed class AIPlaytestModelChoice
{
    public string Id, Name;
    public string[] ReasoningEfforts;
}

[Serializable]
public sealed class AIPlaytestModelCatalog
{
    public AIPlaytestModelChoice[] Models;
    public int CallsRemaining;
}

// Transient local experiment settings. No credentials, PlayerPrefs, save, or PBp fields.
public static class LocalAIPlaytestSettings
{
    public const string DefaultModel = "gpt-6-luna";
    public const string DefaultEffort = "max";
    public static string Model { get; private set; } = DefaultModel;
    public static string ReasoningEffort { get; private set; } = DefaultEffort;
    public static void Select(AIPlaytestModelChoice model, string effort)
    {
        if (model == null || string.IsNullOrEmpty(model.Id) || model.ReasoningEfforts == null ||
            Array.IndexOf(model.ReasoningEfforts, effort) < 0)
            throw new ArgumentException("Select a supported model and reasoning combination.");
        Model = model.Id;
        ReasoningEffort = effort;
    }
}
