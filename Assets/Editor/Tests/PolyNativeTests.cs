using System;
using System.IO;
using System.Linq;
using BlockNations.Simulation.Poly;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class PolyNativeTests
{
    [Test] public void UnityJsonRoundTripPreservesUnexploredTilesAndTurnBudget()
    {
        var match = new PolyMatch(31);
        Assert.That(match.TryApply(new PolyAction(PolyActionKind.Move, 0, 1, 13), out _), Is.True);
        string json = JsonUtility.ToJson(match.CreateSave()); var restored = PolyMatch.Restore(JsonUtility.FromJson<PolySave>(json));
        Assert.That(restored.UnitState(1).moved, Is.True); Assert.That(restored.Board.GetUnit(1).Position, Is.EqualTo(13));
        Assert.That(Enumerable.Range(0,121).Select(p=>restored.KnownTile(0,p)!=null), Is.EqualTo(Enumerable.Range(0,121).Select(p=>match.KnownTile(0,p)!=null)));
        Assert.That(JsonUtility.ToJson(restored.CreateSave()), Is.EqualTo(json));
    }
    [Test] public void PolySaveSlotIsAccountScopedAndPreservesStandardSave()
    {
        string root = Path.Combine(Path.GetTempPath(), "blocknations-poly-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try {
            string standard = Path.Combine(root,"save_sp.json"); File.WriteAllText(standard,"synthetic-standard-save");
            string path = PolySaveStore.PathFor(root,"synthetic-owner"); var match = new PolyMatch(2);
            PolySaveStore.Write(path,match); Assert.That(PolySaveStore.Load(path).Seed, Is.EqualTo(2));
            PolySaveStore.Write(path,new PolyMatch(3)); Assert.That(PolySaveStore.Load(path).Seed, Is.EqualTo(3));
            Assert.That(PolySaveStore.Load(path+".previous").Seed, Is.EqualTo(2));
            Assert.That(File.ReadAllText(standard), Is.EqualTo("synthetic-standard-save"));
            Assert.That(PolySaveStore.PathFor(root,"synthetic-other"), Is.Not.EqualTo(path));
            File.WriteAllText(path,"{\"mode\":\"VsAI\"}"); Assert.Throws<ArgumentException>(()=>PolySaveStore.Load(path));
        } finally { Directory.Delete(root,true); }
    }
    [Test] public void PolySceneHasExplicitDocumentAndInputWiringAndRegisteredBuildEntry()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Poly.unity",OpenSceneMode.Additive);
        try {
            var controller = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PolyGameController>()).Single();
            var view = controller.GetComponent<PolyGameplayUITKView>();
            var serialized = new SerializedObject(view); var doc = serialized.FindProperty("document").objectReferenceValue as UIDocument;
            Assert.That(doc,Is.Not.Null); Assert.That(doc.panelSettings,Is.Not.Null); Assert.That(doc.visualTreeAsset,Is.Not.Null);
            var tree = doc.visualTreeAsset.CloneTree(); Assert.That(tree.Q<Button>("EndTurn"),Is.Not.Null); Assert.That(tree.Query<ScrollView>().ToList(), Is.Empty);
            var manager = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<TurnManager>()).Single();
            Assert.That(manager.PolyController, Is.SameAs(controller));
            Assert.That(manager.gridManager.width, Is.EqualTo(11));
            Assert.That(scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CameraController>()).Single().enabled, Is.True);
            Assert.That(EditorBuildSettings.scenes.Any(s=>s.enabled && s.path=="Assets/Scenes/Poly.unity"),Is.True);
            Assert.That(scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<UnityEngine.InputSystem.UI.InputSystemUIInputModule>()).Any(),Is.True);
        } finally { EditorSceneManager.CloseScene(scene,true); }
    }
}
