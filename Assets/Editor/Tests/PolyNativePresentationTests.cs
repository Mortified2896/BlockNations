using System;
using System.Collections;
using System.IO;
using System.Linq;
using BlockNations.Simulation.Poly;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public sealed class PolyNativePresentationTests
{
    private string root, previousRoot;
    private bool previousOptionsEnabled, prepared;
    private EnterPlayModeOptions previousOptions;
    [UnityTearDown] public IEnumerator Cleanup()
    {
        if (!prepared) yield break;
        if (Application.isPlaying) yield return new ExitPlayMode();
        Environment.SetEnvironmentVariable("BLOCKNATIONS_POLY_TEST_SAVE_ROOT",previousRoot);
        EditorSettings.enterPlayModeOptionsEnabled=previousOptionsEnabled; EditorSettings.enterPlayModeOptions=previousOptions;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        if(Directory.Exists(root)) Directory.Delete(root,true);
    }
    [UnityTest] public IEnumerator ExistingPolySaveUsesNativeBoardLegalityFogAndPagedPanels()
    {
        root=Path.Combine(Path.GetTempPath(),"poly-native-"+Guid.NewGuid()); Directory.CreateDirectory(root);
        previousRoot=Environment.GetEnvironmentVariable("BLOCKNATIONS_POLY_TEST_SAVE_ROOT");
        Environment.SetEnvironmentVariable("BLOCKNATIONS_POLY_TEST_SAVE_ROOT",root);
        previousOptionsEnabled=EditorSettings.enterPlayModeOptionsEnabled; previousOptions=EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled=true; EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload; prepared=true;
        var original=new PolyMatch(31); original.Board.SetGold(0,100);
        Assert.That(original.TryApply(new PolyAction(PolyActionKind.Research,0,choice:"organization"),out _),Is.True);
        Assert.That(original.TryApply(new PolyAction(PolyActionKind.Research,0,choice:"farming"),out _),Is.True);
        original.Tiles[11].improvement=PolyImprovement.Farm; original.Tiles[11].improvementOwnerSeatIndex=0;
        var legacy = original.CreateSave(); legacy.rulesVersion=PolyRules.LegacyVersion;
        legacy.cities[0].level=2; legacy.cities[0].workshops=1;
        legacy.tiles[11].resource=PolyResource.None; legacy.seats[0].memory[11].resource=PolyResource.None;
        string checkpoint=JsonUtility.ToJson(legacy);
        string migratedCheckpoint=JsonUtility.ToJson(PolyMatch.Restore(legacy).CreateSave());
        string path=PolySaveStore.PathFor(root,WebAccountClient.PlayerId);
        File.WriteAllText(path,checkpoint);
        string standard=Path.Combine(root,"save_sp.json"); File.WriteAllText(standard,"synthetic-standard-save");
        EditorSceneManager.OpenScene("Assets/Scenes/Poly.unity",OpenSceneMode.Single);
        yield return new EnterPlayMode(false);
        PolyGameController controller=null;
        for(int frame=0;frame<180;frame++) { controller=Object.FindAnyObjectByType<PolyGameController>(); if(controller!=null && controller.IsReady) break; yield return null; }
        Assert.That(controller!=null && controller.IsReady,Is.True);
        yield return null; yield return null;
        var manager=controller.Manager;
        Assert.That(manager.IsPolyMatch,Is.True); Assert.That(manager.IsAdvancedMatch,Is.False);
        Assert.That(manager.gridManager.GetAllTiles().Count(),Is.EqualTo(121));
        Assert.That(controller.SceneCities.Count,Is.EqualTo(7));
        Assert.That(controller.Match.City(1).level,Is.EqualTo(2)); Assert.That(controller.Match.Income(0),Is.EqualTo(4));
        Assert.That(JsonUtility.ToJson(controller.Match.CreateSave()),Is.EqualTo(migratedCheckpoint));
        Assert.That(File.ReadAllText(path),Is.EqualTo(checkpoint),"Migration preserves the original checkpoint until an action saves it.");
        Assert.That(new SceneSimulationAdapter(manager).State,Is.SameAs(controller.Match.Board));
        Assert.That(UnitSelectionManager.Instance.HasLegalAttackTargetNow(null),Is.False);
        var live=controller.SceneUnits[1]; Assert.That(live.UnitTypeId,Is.EqualTo("poly-warrior"));
        var prefab=manager.GetUnitPrefabForType("warrior");
        Assert.That(live.GetComponentsInChildren<SpriteRenderer>().First(r=>r!=live.moveOutline).sprite,
            Is.SameAs(prefab.GetComponentsInChildren<SpriteRenderer>().First(r=>r!=prefab.GetComponent<Unit>().moveOutline).sprite));
        var view=Object.FindAnyObjectByType<PolyGameplayUITKView>();
        Assert.That(view.Document.rootVisualElement.Query<ScrollView>().ToList(),Is.Empty);
        manager.gridManager.TryGetTile(1,1,out var capitalTile);
        for(int tap=0;tap<3;tap++) {
            controller.HandleBoardTap(capitalTile.GetComponent<TileHighlighter>(),live,controller.SceneCities[1]); yield return null;
            Assert.That(controller.Inspection,Is.EqualTo(tap==1 ? PolyGameController.InspectKind.City : PolyGameController.InspectKind.Unit));
            if(tap==1) {
                Assert.That(view.Document.rootVisualElement.Q<Button>("Recruit_poly-warrior"),Is.Null,"Occupied cities keep the unit accessible instead of filling the sheet with unavailable recruits.");
                Assert.That(view.Document.rootVisualElement.Q<Button>("InspectUnit"),Is.Not.Null);
            }
        }
        Assert.That(view.Document.rootVisualElement.Q<Button>("OpenCity"),Is.Null);
        Submit(view.Document.rootVisualElement,"Tech");
        yield return null;
        var graph=view.Document.rootVisualElement.Q("TechnologyTree");
        Assert.That(graph,Is.Not.Null); Assert.That(graph.Query<Image>().ToList().Count,Is.EqualTo(4));
        Assert.That(graph.Q<Button>("Tech_farming"),Is.Not.Null);
        Submit(view.Document.rootVisualElement,"CloseOverlay");
        yield return null;
        controller.CloseInspection();
        var selection=UnitSelectionManager.Instance; selection.SelectUnit(live);
        var legal=LegalActionService.GetLegalActionsForUnit(manager,live,0);
        Assert.That(legal.Where(a=>a.ActionType==LegalActionType.UnitMove).Select(a=>a.TargetTile.gridY*11+a.TargetTile.gridX).OrderBy(p=>p),
            Is.EqualTo(controller.Match.Paths(controller.Match.Board.GetUnit(1)).Keys.OrderBy(p=>p)));
        var move=legal.First(a=>a.ActionType==LegalActionType.UnitMove && a.TargetTile.gridX==2 && a.TargetTile.gridY==1);
        selection.TryMoveOrAttackAtPosition(move.TargetTile.transform.position);
        Assert.That(controller.Match.Board.GetUnit(1).Position,Is.EqualTo(13));
        Assert.That(live.transform.position.x,Is.EqualTo(move.TargetTile.transform.position.x));
        Assert.That(controller.Match.UnitState(1).moved,Is.True);
        controller.OpenCity(1); yield return null;
        Assert.That(view.Document.rootVisualElement.Q<Button>("Recruit_poly-rider"),Is.Not.Null);
        Assert.That(controller.Act(new PolyAction(PolyActionKind.Recruit,0,1,choice:"poly-builder")),Is.True);
        Assert.That(controller.SceneUnits.Count(u=>u.Value.ownerSeatIndex==0),Is.EqualTo(2));
        Assert.That(controller.TryEndTurn(0),Is.True);
        for(int frame=0;frame<800 && controller.IsOpponentRunning;frame++) yield return null;
        Assert.That(controller.IsOpponentRunning,Is.False); Assert.That(controller.Match.currentTurnSeatIndex,Is.EqualTo(0));
        Assert.That(manager.turnNumber,Is.EqualTo(2));
        int builderId=controller.Match.Board.Units.Single(u=>u.Seat==0 && u.Definition.TypeId=="poly-builder").Id;
        Assert.That(controller.Act(new PolyAction(PolyActionKind.Move,0,builderId,23)),Is.True);
        controller.CloseInspection(); manager.gridManager.TryGetTile(1,2,out var forestTile);
        controller.HandleBoardTap(forestTile.GetComponent<TileHighlighter>(),controller.SceneUnits[builderId],null); yield return null;
        Assert.That(controller.Inspection,Is.EqualTo(PolyGameController.InspectKind.Unit));
        Submit(view.Document.rootVisualElement,"BuildHere"); yield return null;
        Submit(view.Document.rootVisualElement,"TileResearch_hunting"); yield return null;
        Assert.That(controller.Inspection,Is.EqualTo(PolyGameController.InspectKind.Tile));
        Assert.That(controller.Match.HasTechnology(0,"hunting"),Is.True);
        Submit(view.Document.rootVisualElement,"Develop_hunt"); yield return null;
        Assert.That(controller.Match.Tiles[23].resource,Is.EqualTo(PolyResource.Animals));
        var connection=forestTile.transform.Find("Poly city connection").GetComponent<SpriteRenderer>();
        Assert.That(connection.enabled,Is.True);
        Assert.That(connection.sortingOrder,Is.GreaterThan(forestTile.transform.Find("Poly improvement").GetComponent<SpriteRenderer>().sortingOrder));
        Assert.That(connection.sortingOrder,Is.LessThan(forestTile.exploredRenderer.sortingOrder),"Connections stay under ordinary fog.");
        Assert.That(controller.Match.UnitState(builderId).rested,Is.True);
        Assert.That(controller.SceneUnits[builderId].GetComponentsInChildren<SpriteRenderer>().First(r=>r!=controller.SceneUnits[builderId].moveOutline).sprite.name,Is.EqualTo("Poly poly-builder"));
        Assert.That(forestTile.transform.Find("Poly food yield").GetComponent<SpriteRenderer>().sprite.name,Is.EqualTo("Poly FoodYield2"));

        Assert.That(controller.Act(new PolyAction(PolyActionKind.Research,0,choice:"shields")),Is.True);
        Assert.That(controller.Act(new PolyAction(PolyActionKind.Recruit,0,1,choice:"poly-defender")),Is.True);
        var defender=controller.SceneUnits.Values.Single(u=>u.UnitTypeId=="poly-defender");
        var renderer=defender.GetComponentsInChildren<SpriteRenderer>().First(r=>r!=defender.moveOutline);
        Assert.That(renderer.sprite.name,Is.EqualTo("Poly poly-defender"));
        Assert.That(renderer.bounds.size.y,Is.GreaterThan(.5f),"New Poly sprites must recompute the native prefab's size clamp.");
        manager.SaveToFile(standard); Assert.That(File.ReadAllText(standard),Is.EqualTo("synthetic-standard-save"));
        Assert.That(manager.LoadFromFile(standard),Is.False);
        Assert.That(PolySaveStore.Load(path).Board.Round,Is.EqualTo(2));
    }
    private static void Submit(VisualElement root,string name)
    {
        var button=root.Q<Button>(name); Assert.That(button,Is.Not.Null,name); Assert.That(button.enabledInHierarchy,Is.True,name);
        using var submit=NavigationSubmitEvent.GetPooled(); submit.target=button; button.SendEvent(submit);
    }
}
