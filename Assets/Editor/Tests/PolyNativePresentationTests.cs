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
        Assert.That(original.TryApply(new PolyAction(PolyActionKind.Develop,0,1,11,"farm"),out _),Is.True);
        Assert.That(original.TryApply(new PolyAction(PolyActionKind.Reward,0,1,choice:"workshop"),out _),Is.True);
        string checkpoint=JsonUtility.ToJson(original.CreateSave());
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
        Assert.That(JsonUtility.ToJson(controller.Match.CreateSave()),Is.EqualTo(checkpoint),"Loading the replacement presentation must preserve the complete existing checkpoint.");
        Assert.That(new SceneSimulationAdapter(manager).State,Is.SameAs(controller.Match.Board));
        Assert.That(UnitSelectionManager.Instance.HasLegalAttackTargetNow(null),Is.False);
        var live=controller.SceneUnits[1]; Assert.That(live.UnitTypeId,Is.EqualTo("poly-warrior"));
        var prefab=manager.GetUnitPrefabForType("warrior");
        Assert.That(live.GetComponentsInChildren<SpriteRenderer>().First(r=>r!=live.moveOutline).sprite,
            Is.SameAs(prefab.GetComponentsInChildren<SpriteRenderer>().First(r=>r!=prefab.GetComponent<Unit>().moveOutline).sprite));
        var view=Object.FindAnyObjectByType<PolyGameplayUITKView>();
        Assert.That(view.Document.rootVisualElement.Query<ScrollView>().ToList(),Is.Empty);
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
        Assert.That(controller.Act(new PolyAction(PolyActionKind.Recruit,0,1,choice:"poly-warrior")),Is.True);
        Assert.That(controller.SceneUnits.Count(u=>u.Value.ownerSeatIndex==0),Is.EqualTo(2));
        Assert.That(controller.TryEndTurn(0),Is.True);
        for(int frame=0;frame<800 && controller.IsOpponentRunning;frame++) yield return null;
        Assert.That(controller.IsOpponentRunning,Is.False); Assert.That(controller.Match.currentTurnSeatIndex,Is.EqualTo(0));
        Assert.That(manager.turnNumber,Is.EqualTo(2));
        Assert.That(controller.Act(new PolyAction(PolyActionKind.Move,0,3,11)),Is.True);
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
}
