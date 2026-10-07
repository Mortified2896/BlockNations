using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

// Explicit named UITK references are supplied by MainMenuUITKView. Model/effort
// indices map to typed catalog data; rendered strings are never gameplay inputs.
public sealed class LocalAIPlaytestSetupController : IDisposable
{
    private readonly VisualElement section;
    private readonly DropdownField modelField, effortField;
    private readonly Label status;
    private readonly Action changed;
    private AIPlaytestModelChoice[] choices = Array.Empty<AIPlaytestModelChoice>();
    private UnityWebRequest request;
    private bool visible, disposed, updating;
    public bool CatalogRequested { get; private set; }
    public bool Ready { get; private set; }

    public LocalAIPlaytestSetupController(VisualElement section, DropdownField modelField,
        DropdownField effortField, Label status, Action changed)
    {
        this.section = section;
        this.modelField = modelField;
        this.effortField = effortField;
        this.status = status;
        this.changed = changed;
        modelField?.RegisterValueChangedCallback(ModelChanged);
        effortField?.RegisterValueChangedCallback(EffortChanged);
    }

    public void SetVisible(bool value)
    {
        if (disposed) return;
        if (value && !visible) CatalogRequested = false;
        visible = value;
        if (section != null) section.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public IEnumerator LoadCatalog()
    {
        if (disposed || request != null || !visible) yield break;
        CatalogRequested = true;
        Ready = false;
        modelField?.SetEnabled(false);
        effortField?.SetEnabled(false);
        SetStatus("Connecting to your local model bridge…");
        request = UnityWebRequest.Get(LocalLunaPlaytestPolicy.Endpoint + "/models");
        request.timeout = 3;
        UnityWebRequest current = request;
        yield return current.SendWebRequest();
        if (disposed) yield break;
        try
        {
            if (current.result != UnityWebRequest.Result.Success) throw new InvalidOperationException();
            AIPlaytestModelCatalog catalog = JsonUtility.FromJson<AIPlaytestModelCatalog>(current.downloadHandler.text);
            if (catalog?.Models == null || catalog.Models.Length == 0) throw new InvalidOperationException();
            ApplyCatalog(catalog);
        }
        catch (Exception)
        {
            SetStatus("Local bridge is offline. In Unity, choose Tools → Block Nations → Luna Bridge → Start, then reopen Play vs Model AI.");
        }
        finally { current.Dispose(); request = null; changed?.Invoke(); }
    }

    public void ApplyCatalog(AIPlaytestModelCatalog catalog)
    {
        if (disposed || modelField == null || effortField == null) return;
        List<AIPlaytestModelChoice> supported = new List<AIPlaytestModelChoice>();
        foreach (AIPlaytestModelChoice model in catalog.Models)
            if (model != null && !string.IsNullOrEmpty(model.Id) && model.ReasoningEfforts?.Length > 0) supported.Add(model);
        if (supported.Count == 0) throw new ArgumentException("Model catalog is empty.");
        choices = supported.ToArray();
        updating = true;
        modelField.choices = supported.ConvertAll(model => model.Name);
        int selected = Array.FindIndex(choices, model => model.Id == LocalAIPlaytestSettings.Model);
        if (selected < 0) selected = Array.FindIndex(choices, model => model.Id == LocalAIPlaytestSettings.DefaultModel);
        modelField.index = Math.Max(0, selected);
        SelectModel(modelField.index);
        updating = false;
        Ready = catalog.CallsRemaining > 0;
        modelField.SetEnabled(Ready);
        effortField.SetEnabled(Ready);
        SetStatus(Ready
            ? $"Uses your Codex allowance · {catalog.CallsRemaining} calls left in this session. One request per action; stronger reasoning can take longer."
            : "This session's call limit is reached. Restart the local bridge for another playtest.");
    }

    private void ModelChanged(ChangeEvent<string> evt) { if (!updating) SelectModel(modelField.index); }
    private void EffortChanged(ChangeEvent<string> evt)
    {
        if (updating) return;
        if (modelField.index < 0 || modelField.index >= choices.Length) return;
        AIPlaytestModelChoice model = choices[modelField.index];
        if (effortField.index < 0 || effortField.index >= model.ReasoningEfforts.Length) return;
        LocalAIPlaytestSettings.Select(model, model.ReasoningEfforts[effortField.index]);
    }
    private void SelectModel(int index)
    {
        if (index < 0 || index >= choices.Length) return;
        bool previousUpdating = updating;
        updating = true;
        AIPlaytestModelChoice model = choices[index];
        effortField.choices = new List<string>(model.ReasoningEfforts).ConvertAll(EffortLabel);
        int effort = Array.IndexOf(model.ReasoningEfforts, LocalAIPlaytestSettings.ReasoningEffort);
        if (effort < 0) effort = Array.IndexOf(model.ReasoningEfforts, LocalAIPlaytestSettings.DefaultEffort);
        effortField.index = Math.Max(0, effort);
        LocalAIPlaytestSettings.Select(model, model.ReasoningEfforts[effortField.index]);
        updating = previousUpdating;
    }
    private static string EffortLabel(string effort)
    {
        switch (effort)
        {
            case "xhigh": return "Extra High";
            case "max": return "Maximum";
            default: return char.ToUpperInvariant(effort[0]) + effort.Substring(1);
        }
    }
    private void SetStatus(string text) { if (status != null) status.text = text; }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        modelField?.UnregisterValueChangedCallback(ModelChanged);
        effortField?.UnregisterValueChangedCallback(EffortChanged);
        request?.Abort();
        request?.Dispose();
        request = null;
    }
}
