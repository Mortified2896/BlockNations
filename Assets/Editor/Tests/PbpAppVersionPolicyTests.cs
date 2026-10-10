using NUnit.Framework;
using System;
using System.Reflection;
using UnityEngine;

public sealed class PbpAppVersionPolicyTests
{
    [TestCase("1.0.4", "1.0.4", true)]
    [TestCase("1.0.4", "1.0.3", true)]
    [TestCase("1.0.3", "1.0.4", false)]
    [TestCase("1.0.4", "1.0.2", false)]
    [TestCase("1.0.4", "1.0.5", false)]
    [TestCase("1.0.5", "1.0.3", false)]
    [TestCase("1.0.4", null, false)]
    [TestCase(" 1.0.4 ", " 1.0.3 ", true)]
    public void ExplicitPreviousBuildImportNeverAllowsAnOlderClientToReadNewRules(string current, string incoming, bool accepted)
    {
        Assert.That(PbpAppVersionPolicy.Supports(current, incoming), Is.EqualTo(accepted));
    }

    [Test]
    public void CurrentBuildKeepsBothSupportedProtocolPathsAndOnlyTheApprovedAppImport()
    {
        Assert.That(TurnManager.CurrentAppVersion, Is.EqualTo(PbpAppVersionPolicy.BalanceRelease));
        Assert.That(TurnManager.IsSupportedPbpAppVersion("1.0.3"), Is.True);
        Assert.That(TurnManager.IsSupportedPbpAppVersion("1.0.2"), Is.False);
        Assert.That(TurnManager.IsSupportedPbpLoadProtocolVersion(4), Is.True);
        Assert.That(TurnManager.IsSupportedPbpLoadProtocolVersion(5), Is.True);
        Assert.That(TurnManager.IsSupportedPbpLoadProtocolVersion(3), Is.False);
    }

    [TestCase(4)] [TestCase(5)]
    public void LegacyImportGatesKeepHealthUnitsAndNextExportUsesTheNewBuild(int protocol)
    {
        var root = new GameObject("Inactive compatibility fixture"); root.SetActive(false);
        try
        {
            var manager = root.AddComponent<TurnManager>();
            manager.gridManager = root.AddComponent<GridManager>();
            manager.currentMode = TurnManager.GameMode.PlayByPost;
            typeof(TurnManager).GetField("currentGameId", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(manager, "app-import-fixture");
            Type saveType = typeof(TurnManager).GetNestedType("GameSave", BindingFlags.NonPublic);
            object incoming = JsonUtility.FromJson("{\"protocolVersion\":" + protocol + ",\"appVersion\":\"1.0.3\"}", saveType);
            object[] protocolArgs = { incoming, 0, 0, null };
            Assert.That(typeof(TurnManager).GetMethod("TryValidatePbpLoadProtocol", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(manager, protocolArgs), Is.True);
            object[] appArgs = { incoming, null, null };
            Assert.That(typeof(TurnManager).GetMethod("TryValidatePbpLoadAppVersion", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, appArgs), Is.True);
            Assert.That(appArgs[1], Is.EqualTo("1.0.3"));
            Type unitType = typeof(TurnManager).GetNestedType("SavedUnit", BindingFlags.NonPublic);
            object unit = JsonUtility.FromJson("{\"currentHealthUnits\":5}", unitType);
            Assert.That(typeof(TurnManager).GetMethod("ResolveLoadedCurrentHealthUnits", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new[] { unit }), Is.EqualTo(5));
            object exported = typeof(TurnManager).GetMethod("BuildCurrentSave", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(manager, null);
            Assert.That(saveType.GetField("appVersion").GetValue(exported), Is.EqualTo("1.0.4"));
            Assert.That(saveType.GetField("protocolVersion").GetValue(exported), Is.EqualTo(5));
            Assert.That(saveType.GetField("appVersion").GetValue(incoming), Is.EqualTo("1.0.3"), "Import must not mutate the preserved snapshot.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
