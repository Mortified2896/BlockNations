using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class TrainingReplayOutlineTests
{
    private GameObject objects, owners;
    private TurnManager manager;
    private Sprite sprite;
    private Texture2D texture;

    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        objects = new GameObject("Replay regression pieces");
        owners = new GameObject("Inactive match owners"); owners.SetActive(false);
        var grid = owners.AddComponent<GridManager>();
        grid.width = grid.height = 5;
        grid.tileGrid = new TileVisibility[5, 5];
        for (int x = 0; x < 5; x++) for (int y = 0; y < 5; y++)
        {
            var tile = new GameObject("Tile"); tile.transform.SetParent(objects.transform);
            tile.transform.position = new Vector3(x, y);
            tile.AddComponent<SpriteRenderer>();
            var visibility = tile.AddComponent<TileVisibility>(); visibility.Initialize(x, y);
            grid.tileGrid[x, y] = visibility;
        }
        manager = owners.AddComponent<TurnManager>(); manager.gridManager = grid;
        manager.currentMode = TurnManager.GameMode.VsAI;
        texture = new Texture2D(2, 2);
        sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f, 2);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(objects); Object.DestroyImmediate(owners);
        Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
    }

    private Unit CreateUnit(int seat, int x, int y)
    {
        var root = new GameObject("Recorded unit"); root.transform.SetParent(objects.transform);
        root.transform.position = new Vector3(x, y);
        var outline = new GameObject("Authored outline"); outline.transform.SetParent(root.transform, false);
        outline.transform.localScale = Vector3.one * 1.4f;
        var renderer = outline.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
        var unit = root.AddComponent<Unit>(); unit.SetOwnerSeatIndex(seat); unit.moveOutline = renderer;
        unit.UpdateMoveOutline(false); // Cache authored geometry before deliberately stale renderer state.
        renderer.enabled = seat == 0; renderer.color = Color.magenta;
        return unit;
    }

    private TrainingReplayHistory.Piece Recorded(TrainingReplayHistory.Frame frame, int seat) =>
        frame.pieces.Single(piece => !piece.city && piece.seat == seat);

    [TestCase(0)] [TestCase(1)]
    public void BothSeatsUseRemainingMovesInsteadOfStaleRendererFlags(int seat)
    {
        Unit own = CreateUnit(seat, 1, 1), other = CreateUnit(1 - seat, 4, 4);
        manager.currentTurnSeatIndex = seat;
        manager.isPlayerTurn = seat != 0; // Deliberately wrong legacy bridge must not affect markers.
        var before = TrainingReplayRecorder.Capture(manager, "before move");
        Assert.That(Recorded(before, seat).outlineSprite, Is.SameAs(sprite));
        Assert.That(Recorded(before, 1 - seat).outlineSprite, Is.Null);
        Assert.That(Recorded(before, seat).outlineColor, Is.Not.EqualTo(Color.magenta));
        own.RegisterMove(); own.transform.position = new Vector3(2, 1);
        var after = TrainingReplayRecorder.Capture(manager, "after move");
        Assert.That(Recorded(after, seat).outlineSprite, Is.Null);
        Assert.That(Recorded(before, seat).outlineSprite, Is.SameAs(sprite), "Previous replay state stays immutable.");
        Assert.That(own.moveOutline.enabled, Is.EqualTo(seat == 0), "Recording must not refresh live renderers.");
        Assert.That(other.movesUsedThisTurn, Is.Zero);
    }

    [Test]
    public void TurnChangeAndResetMoveMarkersToRedThenBackToBlue()
    {
        Unit blue = CreateUnit(0, 1, 1); CreateUnit(1, 4, 4);
        manager.currentTurnSeatIndex = 0;
        var history = new TrainingReplayHistory();
        history.Begin(1, 5, 0, TrainingReplayRecorder.Capture(manager, "Blue turn"));
        blue.RegisterMove();
        history.Record(TrainingReplayRecorder.Capture(manager, "Blue moves"));
        manager.currentTurnSeatIndex = 1;
        history.Record(TrainingReplayRecorder.Capture(manager, "Red turn"));
        manager.currentTurnSeatIndex = 0; blue.ResetMovementForTurn();
        history.Complete(TrainingReplayRecorder.Capture(manager, "Blue next turn"));
        history.Inspect(0); history.Playing = false;
        Assert.That(Recorded(history.CurrentFrame, 0).outlineSprite, Is.SameAs(sprite));
        history.Step(0); Assert.That(Recorded(history.CurrentFrame, 0).outlineSprite, Is.Null);
        history.Step(0);
        Assert.That(Recorded(history.CurrentFrame, 0).outlineSprite, Is.Null);
        Assert.That(Recorded(history.CurrentFrame, 1).outlineSprite, Is.SameAs(sprite));
        history.Step(0); Assert.That(Recorded(history.CurrentFrame, 0).outlineSprite, Is.SameAs(sprite));
    }

    [Test]
    public void MultiStepMovementKeepsYellowUntilItsMoveBudgetIsExhausted()
    {
        Unit scout = CreateUnit(1, 1, 1); scout.maxMovesPerTurn = 2;
        manager.currentTurnSeatIndex = 1;
        scout.RegisterMove();
        Assert.That(Recorded(TrainingReplayRecorder.Capture(manager, "one step"), 1).outlineSprite, Is.SameAs(sprite));
        scout.RegisterMove();
        Assert.That(Recorded(TrainingReplayRecorder.Capture(manager, "two steps"), 1).outlineSprite, Is.Null);
    }

    [Test]
    public void CommittedMovementConsumesRiderMarkerAfterOneMove()
    {
        Unit rider = CreateUnit(1, 1, 1); rider.maxMovesPerTurn = 2;
        var serialized = new SerializedObject(rider);
        serialized.FindProperty("unitTypeId").stringValue = UnitRegistry.RiderTypeId;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        manager.currentTurnSeatIndex = 1; rider.RegisterMove();
        Assert.That(Recorded(TrainingReplayRecorder.Capture(manager, "Rider moves"), 1).outlineSprite, Is.Null);
    }

    [TestCase(0)] [TestCase(1)]
    public void ExhaustedMovementShowsOnlyLegalAttackMarkerThenClearsAfterAttack(int seat)
    {
        Unit own = CreateUnit(seat, 1, 1); CreateUnit(1 - seat, 2, 1);
        manager.currentTurnSeatIndex = seat;
        var move = Recorded(TrainingReplayRecorder.Capture(manager, "movement remains"), seat);
        own.RegisterMove();
        var attack = Recorded(TrainingReplayRecorder.Capture(manager, "attack remains"), seat);
        Assert.That(attack.outlineSprite, Is.SameAs(sprite));
        Assert.That(attack.outlineColor, Is.Not.EqualTo(move.outlineColor));
        Assert.That(attack.outlineBounds.width, Is.EqualTo(move.outlineBounds.width * .85f).Within(.001f));
        own.RegisterAttack();
        Assert.That(Recorded(TrainingReplayRecorder.Capture(manager, "attack used"), seat).outlineSprite, Is.Null);
        own.ResetMovementForTurn();
        Assert.That(Recorded(TrainingReplayRecorder.Capture(manager, "reset"), seat).outlineBounds,
            Is.EqualTo(move.outlineBounds), "Authored size must not shrink across recorded states.");
    }

    [Test]
    public void AttackMarkerDoesNotRevealAnEnemyOutsideThatSeatsVision()
    {
        Unit own = CreateUnit(1, 1, 1); CreateUnit(0, 3, 1);
        typeof(Unit).GetField("resolvedDefinition", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(own,
            new UnitDefinition("test", "test", 1, 1, "test", 10, 10, 3, true, 0, 1, 1));
        manager.currentTurnSeatIndex = 1; own.RegisterMove();
        var frame = TrainingReplayRecorder.Capture(manager, "hidden enemy in attack range");
        Assert.That(frame.Visible(3, 1, TrainingVision.Red), Is.False);
        Assert.That(Recorded(frame, 1).outlineSprite, Is.Null);
    }

    [Test]
    public void TerminalFramesHaveNoActionMarkers()
    {
        CreateUnit(0, 1, 1); CreateUnit(1, 4, 4);
        manager.gameOver = true;
        Assert.That(TrainingReplayRecorder.Capture(manager, "capture").pieces.All(piece => piece.outlineSprite == null), Is.True);
    }
}
