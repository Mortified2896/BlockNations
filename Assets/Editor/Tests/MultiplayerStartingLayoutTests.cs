using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class MultiplayerStartingLayoutTests
{
    [TestCase(2, "1,1;5,5")]
    [TestCase(3, "3,1;5,5;1,5")]
    [TestCase(4, "1,1;5,5;1,5;5,1")]
    public void StandardBoardUsesDistinctInsetStarts(int count, string expected)
    {
        var root = new GameObject("Starting layout test"); root.SetActive(false);
        try {
            var grid = root.AddComponent<GridManager>(); grid.width = grid.height = 7;
            var anchors = (List<Vector2Int>)typeof(GridManager).GetMethod("BuildDesiredStartingAnchors", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(grid, new object[] {count});
            Assert.That(string.Join(";", anchors.Select(point => point.x + "," + point.y)), Is.EqualTo(expected));
            Assert.That(anchors.Distinct().Count(), Is.EqualTo(count));
        } finally { Object.DestroyImmediate(root); }
    }

    [TestCase(3)] [TestCase(4)]
    public void SelectingMultiplayerSeedsAllSeatsBeforeVisibilityRuns(int count)
    {
        var root = new GameObject("Preexisting multiplayer board"); root.SetActive(false);
        var cities = new List<GameObject>();
        try {
            var grid = root.AddComponent<GridManager>(); grid.width = grid.height = 7;
            var manager = root.AddComponent<TurnManager>(); manager.gridManager = grid;
            manager.currentMode = TurnManager.GameMode.None;
            for (int seat = 0; seat < count; seat++) {
                var cityObject = new GameObject("Existing seat " + seat); cities.Add(cityObject);
                var city = cityObject.AddComponent<City>(); city.SetOwnerSeatIndex(seat); city.x = seat; city.y = 1;
            }
            // A generated board may exist before TurnManager receives its mode.
            // Use real tile components so SceneSimulationAdapter imports every city.
            grid.tileGrid = new TileVisibility[7,7];
            for (int x = 0; x < 7; x++) for (int y = 0; y < 7; y++) {
                var tileObject = new GameObject("Tile"); tileObject.transform.SetParent(root.transform);
                var tile = tileObject.AddComponent<TileVisibility>(); tile.Initialize(x,y); grid.tileGrid[x,y] = tile;
            }
            PlayByPostSeatCountSelection.SetPending(count);
            Assert.DoesNotThrow(() => manager.SetGameMode(TurnManager.GameMode.PlayByPost));
            Assert.That(new SceneSimulationAdapter(manager).State.SeatCount, Is.EqualTo(count));
        } finally {
            PlayByPostSeatCountSelection.TryConsume(out _);
            foreach (var city in cities) Object.DestroyImmediate(city);
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ThreePlayerAssignmentCoversEveryOrientationAndSeatPermutationReproducibly()
    {
        var anchors = new List<Vector2Int> { new Vector2Int(3,1), new Vector2Int(5,5), new Vector2Int(1,5) };
        var layouts = new HashSet<string>();
        for (uint seed = 0; seed < 24; seed++) {
            string id = seed.ToString("x8") + new string('0',24);
            var points = MultiplayerStartingLayout.AssignThreePlayerStarts(anchors,7,id);
            Assert.That(points, Is.EqualTo(MultiplayerStartingLayout.AssignThreePlayerStarts(anchors,7,id)));
            Assert.That(points.Distinct().Count(), Is.EqualTo(3));
            Assert.That(points.All(point => point.x >= 1 && point.x <= 5 && point.y >= 1 && point.y <= 5), Is.True);
            layouts.Add(string.Join(";", points.Select(point => point.x + "," + point.y)));
        }
        Assert.That(layouts.Count, Is.EqualTo(24));
        // Cross-language fixture: opening.mjs must return the same coordinates.
        Assert.That(MultiplayerStartingLayout.AssignThreePlayerStarts(anchors,7,"ffffffff000000000000000000000000"),
            Is.EqualTo(new[] {new Vector2Int(5,1),new Vector2Int(5,5),new Vector2Int(1,3)}));
    }

    [TestCase(0,"Blue")][TestCase(1,"Red")][TestCase(2,"Green")][TestCase(3,"Yellow")]
    public void EverySeatHasAStableColourInTheOpeningLabel(int seat, string colour)
    {
        Assert.That(MatchOpeningLabels.ForSeat(seat,0,4,true), Does.Contain("(" + colour + ")"));
        var root = new GameObject("Seat colour test");
        try {
            var renderer = root.AddComponent<SpriteRenderer>(); var sprite = root.AddComponent<OwnedSprite>();
            sprite.SetOwnerSeatIndex(seat);
            Color expected = seat == 0 ? sprite.playerColor : seat == 1 ? sprite.aiColor : seat == 2 ? sprite.player3Color : sprite.player4Color;
            Assert.That(renderer.color, Is.EqualTo(expected));
        } finally { Object.DestroyImmediate(root); }
    }
}
