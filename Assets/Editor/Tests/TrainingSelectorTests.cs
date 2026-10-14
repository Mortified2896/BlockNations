using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BlockNations.AI;
using BlockNations.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class TrainingSelectorTests
{
    [Test]
    public void FullVisionCannotBeActivatedInAnOrdinaryMatch()
    {
        var root = new GameObject("Inactive ordinary game"); root.SetActive(false);
        try
        {
            var manager = root.AddComponent<TurnManager>();
            Assert.Throws<InvalidOperationException>(() => manager.ConfigureDiagnosticHumanVision(true));
            Assert.That(manager.DiagnosticFullVision, Is.False);
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void HumanAndAIShareFullVisionWithoutChangingRangeOrOwnership()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        foreach (var existing in Object.FindObjectsByType<TurnManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(existing.gameObject);
        var board = new GameObject("Diagnostic board"); var grid = board.AddComponent<GridManager>(); grid.enabled = false;
        grid.width = grid.height = 7;
        grid.tilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BasicTile.prefab");
        grid.cityPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/City.prefab");
        TrainingSceneBuilder.Set(grid, "externallyDrivenBoard", true);
        var root = new GameObject("Inactive human driver"); root.SetActive(false);
        var manager = root.AddComponent<TurnManager>();
        Assert.That(manager, Is.Not.Null, "The fixture must create its isolated TurnManager.");
        manager.gridManager = grid;
        manager.autoSaveEnabled = false;manager.autoEndTurnWhenNoActions = false;
        TrainingSceneBuilder.Set(manager, "externallyDrivenMatch", true);
        manager.ConfigureExternalHumanSeat(0);
        manager.ConfigureExperimentalRules(MatchRuleProfile.CoreThree);
        manager.ConfigureDiagnosticHumanVision(true);
        foreach (var definition in UnitRegistry.AllDefinitions)
            manager.officialUnitRegistrations.Add(new TurnManager.OfficialUnitRegistration { unitTypeId = definition.TypeId,
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + definition.DisplayName + ".prefab"), recruitable = true });
        typeof(TurnManager).GetProperty(nameof(TurnManager.ExternalMatchReady)).SetValue(manager, true);
        manager.ResetExternalMatch(3,0);
        var source = new SeatAIObservationSource();
        for (int seat=0;seat<2;seat++)
        {
            var observed = source.Observe(manager,seat).Observation;
            Assert.That(observed.Visible.All(v=>v), Is.True, "Both players must see every tile.");
            Assert.That(observed.Cities.Length, Is.EqualTo(2));
            Assert.That(observed.LegalActions.Any(a=>a.Kind==AIActionKind.Attack), Is.False, "Visibility does not create ranged attacks.");
        }
        Assert.That(new SceneSimulationAdapter(manager).VisibilityForSeat(0).Count, Is.EqualTo(49));
        Assert.That(new SceneSimulationAdapter(manager).VisibilityForSeat(1).Count, Is.EqualTo(49));
        var recruit = LegalActionService.GetLegalActionsForSeat(manager,0).First(a=>a.ActionType==LegalActionType.CityRecruit);
        Assert.That(new SceneSimulationAdapter(manager).TryApply(recruit), Is.True);
        Assert.That(source.Observe(manager,1).Observation.Units.Length, Is.EqualTo(1), "The opponent sees distant recruitment.");
        Assert.Throws<InvalidOperationException>(()=>manager.ConfigureDiagnosticHumanVision(false), "Visibility is frozen for this match.");
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(board);
    }

    [TestCase(2,3120)]
    [TestCase(3,3259)]
    [TestCase(4,4712)]
    public void HumanAgentConfiguresTheSelectedObservationContract(int schema,int size)
    {
        var root = new GameObject("Inactive contract fixture");root.SetActive(false);
        try
        {
            var agent=root.AddComponent<TrainingSeatAgent>();agent.ConfigureObservationVersion(schema);
            var parameters=agent.GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
            Assert.That(parameters.BehaviorName,Is.EqualTo(LearnedObservationContract.Behavior(schema)));
            Assert.That(parameters.BrainParameters.VectorObservationSize,Is.EqualTo(size));
        }
        finally{Object.DestroyImmediate(root);}
    }
}
