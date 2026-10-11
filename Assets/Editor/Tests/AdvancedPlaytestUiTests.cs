using System.Linq;
using System.Reflection;
using BlockNations.AdvancedMode;
using BlockNations.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class AdvancedPlaytestUiTests
{
    [Test]
    public void LegalPurchasesAreInteractiveAndTurnLimitsDisableThem()
    {
        var host = new GameObject("Advanced UI test");
        host.SetActive(false);
        var panel = ScriptableObject.CreateInstance<PanelSettings>();
        panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("AdvancedPlaytest/AdvancedTheme");
        try
        {
            var document = host.AddComponent<UIDocument>(); document.panelSettings = panel;
            var view = host.AddComponent<AdvancedGamePlaytestView>();
            view.Configure(document, Resources.Load<VisualTreeAsset>("AdvancedPlaytest/Advanced"),
                Resources.Load<StyleSheet>("AdvancedPlaytest/Advanced"), null, null, null);
            host.SetActive(true);
            Invoke(view, "OnEnable");
            var root = document.rootVisualElement;
            var recruit = root.Q("RecruitActions"); var upgrades = root.Q("CityActions");
            Assert.That(recruit.Children().Count(), Is.EqualTo(3));
            Assert.That(recruit.Children().All(b => b.enabledInHierarchy), Is.True, "Legal recruits must be clickable.");
            Assert.That(upgrades.Children().All(b => b.enabledInHierarchy), Is.True, "Affordable upgrades must be clickable.");
            var match = (AdvancedMatch)typeof(AdvancedGamePlaytestView).GetField("match", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            Assert.That(match.TryRecruit(0, 1, UnitRegistry.WarriorTypeId, PopulationSex.Female, out _), Is.True);
            Assert.That(match.TryUpgrade(0, 1, CityUpgrade.Food, out _), Is.True);
            Invoke(view, "Refresh");
            Assert.That(recruit.Children().All(b => !b.enabledInHierarchy), Is.True, "One recruit per city turn.");
            Assert.That(upgrades.Children().All(b => !b.enabledInHierarchy), Is.True, "One upgrade per city turn.");
            Assert.That(match.Apply(new MatchCommand(MatchActionKind.EndTurn, 0)).Applied, Is.True);
            Invoke(view, "Refresh");
            Assert.That(root.Q<Button>("EndTurn").enabledInHierarchy, Is.False, "Human commands are disabled on the opponent's turn.");
        }
        finally { Object.DestroyImmediate(host); Object.DestroyImmediate(panel); }
    }
    private static void Invoke(AdvancedGamePlaytestView view, string method) =>
        typeof(AdvancedGamePlaytestView).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
}
