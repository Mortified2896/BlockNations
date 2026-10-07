using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class LocalAIPlaytestSetupTests
{
    private sealed class TestWindow : EditorWindow { }
    private static AIPlaytestModelChoice Luna() => new AIPlaytestModelChoice {
        Id = "gpt-6-luna", Name = "Luna test label", ReasoningEfforts = new[] { "low", "medium", "high", "xhigh", "max" } };

    [SetUp, TearDown]
    public void RestoreDefaults() => LocalAIPlaytestSettings.Select(Luna(), "max");

    [UnityTest]
    public IEnumerator ModelAndReasoningSelectionsUseTypedCatalogAndSupportedEfforts()
    {
        var model = new DropdownField();
        var effort = new DropdownField();
        TestWindow window = ScriptableObject.CreateInstance<TestWindow>();
        window.ShowUtility();
        window.rootVisualElement.Add(model);
        window.rootVisualElement.Add(effort);
        try
        {
            yield return null;
            using (var setup = new LocalAIPlaytestSetupController(new VisualElement(), model, effort, new Label(), null))
            {
            setup.ApplyCatalog(new AIPlaytestModelCatalog { CallsRemaining = 64, Models = new[] {
                new AIPlaytestModelChoice { Id = "other-model", Name = "Different display text", ReasoningEfforts = new[] { "medium" } }, Luna() } });
            Assert.That(setup.Ready, Is.True);
            Assert.That(model.index, Is.EqualTo(1));
            Assert.That(effort.value, Is.EqualTo("Maximum"));
            Assert.That(effort.choices, Does.Not.Contain("Ultra"));
            effort.index = 0;
            Assert.That(LocalAIPlaytestSettings.ReasoningEffort, Is.EqualTo("low"));
            model.index = 0;
            Assert.That(LocalAIPlaytestSettings.Model, Is.EqualTo("other-model"));
            Assert.That(LocalAIPlaytestSettings.ReasoningEffort, Is.EqualTo("medium"));
            Assert.That(effort.choices.Count, Is.EqualTo(1));
            }
        }
        finally { window.Close(); UnityEngine.Object.DestroyImmediate(window); }
    }

    [Test]
    public void ExhaustedBridgeDisablesStartingAndUnsupportedEffortCannotBeSelected()
    {
        var model = new DropdownField();
        var effort = new DropdownField();
        using (var setup = new LocalAIPlaytestSetupController(new VisualElement(), model, effort, new Label(), null))
        {
            setup.ApplyCatalog(new AIPlaytestModelCatalog { CallsRemaining = 0, Models = new[] { Luna() } });
            Assert.That(setup.Ready, Is.False);
            Assert.That(model.enabledSelf, Is.False);
            Assert.That(effort.enabledSelf, Is.False);
            Assert.Throws<ArgumentException>(() => LocalAIPlaytestSettings.Select(Luna(), "ultra"));
        }
    }

    [Test]
    public void ModelModeHasASeparateMainMenuEntryAndSeparateSetupSection()
    {
        VisualElement root = Resources.Load<VisualTreeAsset>("MainMenu_UITK").CloneTree();
        Assert.That(root.Q<Button>("PlayVsModelButton"), Is.Not.Null);
        Assert.That(root.Q<Button>("PlayVsAIButton"), Is.Not.Null);
        VisualElement regular = root.Q<VisualElement>("GeneralSettingsAiStyleSection");
        Assert.That(regular.Q<DropdownField>(), Is.Null);
        Assert.That(regular.Q<Button>("GeneralSettingsAiStyleLunaButton"), Is.Null);
        VisualElement external = root.Q<VisualElement>("GeneralSettingsLlmSection");
        Assert.That(external.parent, Is.Not.SameAs(regular));
        Assert.That(external.Q<DropdownField>("GeneralSettingsLlmModelField"), Is.Not.Null);
        Assert.That(external.Q<DropdownField>("GeneralSettingsLlmEffortField"), Is.Not.Null);
    }
}
