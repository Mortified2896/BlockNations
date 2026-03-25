using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public class MultiplayerScrollViewTests
{
    private const string LayoutResourceName = "MainMenu_UITK";
    private const string StyleResourceName = "MainMenu_UITK";

    private TestWindow window;
    private ScrollView activeGamesList;

    [SetUp]
    public void SetUp()
    {
        VisualTreeAsset layout = Resources.Load<VisualTreeAsset>(LayoutResourceName);
        StyleSheet style = Resources.Load<StyleSheet>(StyleResourceName);

        Assert.NotNull(layout, "Expected MainMenu_UITK UXML resource.");
        Assert.NotNull(style, "Expected MainMenu_UITK USS resource.");

        window = ScriptableObject.CreateInstance<TestWindow>();
        window.ShowUtility();
        window.position = new Rect(100f, 100f, 480f, 820f);

        VisualElement root = window.rootVisualElement;
        root.Clear();
        root.styleSheets.Add(style);
        root.style.flexGrow = 1f;

        layout.CloneTree(root);

        VisualElement mainPanel = root.Q<VisualElement>("MainPanel");
        if (mainPanel != null)
        {
            mainPanel.style.display = DisplayStyle.None;
        }

        VisualElement multiplayerPanel = root.Q<VisualElement>("MultiplayerPanel");
        Assert.NotNull(multiplayerPanel, "Expected MultiplayerPanel in cloned layout.");
        multiplayerPanel.style.display = DisplayStyle.Flex;

        activeGamesList = root.Q<ScrollView>("ActiveGamesList");
        Assert.NotNull(activeGamesList, "Expected ActiveGamesList ScrollView in cloned layout.");

        activeGamesList.mode = ScrollViewMode.Vertical;
        activeGamesList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        activeGamesList.verticalScrollerVisibility = ScrollerVisibility.Auto;
        activeGamesList.mouseWheelScrollSize = 120f;
        activeGamesList.scrollOffset = Vector2.zero;
    }

    [TearDown]
    public void TearDown()
    {
        if (window != null)
        {
            window.Close();
            Object.DestroyImmediate(window);
            window = null;
        }
    }

    [UnityTest]
    public IEnumerator SingleGame_DoesNotCreateScrollableOverflow()
    {
        PopulateCards(count: 1);
        yield return null;
        yield return null;

        VisualElement viewport = GetViewport();
        Assert.NotNull(viewport, "Expected ScrollView viewport.");
        Assert.LessOrEqual(
            activeGamesList.contentContainer.layout.height,
            viewport.layout.height + 1f,
            "A single bounded card should not overflow the viewport.");
    }

    [UnityTest]
    public IEnumerator MultipleGames_CreateScrollableOverflow_AndScrollOffsetChanges()
    {
        PopulateCards(count: 8);
        yield return null;
        yield return null;

        VisualElement viewport = GetViewport();
        Assert.NotNull(viewport, "Expected ScrollView viewport.");
        Assert.Greater(
            activeGamesList.contentContainer.layout.height,
            viewport.layout.height + 1f,
            "Expected multiple cards to overflow the viewport.");

        float before = activeGamesList.scrollOffset.y;
        activeGamesList.scrollOffset = new Vector2(0f, 160f);
        yield return null;

        Assert.Greater(
            activeGamesList.scrollOffset.y,
            before,
            "Expected scrollOffset to move after assigning a vertical scroll value.");
    }

    [TestCase(0f, 0f)]
    [TestCase(20f, 7f)]
    [TestCase(400f, 140f)]
    [TestCase(-400f, -140f)]
    [TestCase(1200f, 352f)]
    [TestCase(-1200f, -352f)]
    public void NonOverflowElasticOffset_IsDampedAndClamped(float dragDistance, float expectedOffset)
    {
        MethodInfo method = typeof(MainMenuUITKView).GetMethod("ComputeElasticListOffset", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method, "Expected private ComputeElasticListOffset helper.");

        float actual = (float)method.Invoke(null, new object[] { dragDistance });
        Assert.AreEqual(expectedOffset, actual, 0.001f);
    }

    private void PopulateCards(int count)
    {
        activeGamesList.Clear();
        activeGamesList.EnableInClassList("multiplayer-games-list--single", count == 1);
        activeGamesList.EnableInClassList("multiplayer-games-list--multi", count > 1);

        for (int i = 0; i < count; i++)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("multiplayer-game-card");
            card.EnableInClassList("multiplayer-game-card--single", count == 1);
            card.EnableInClassList("multiplayer-game-card--last", i == count - 1);

            Label title = new Label($"Game test{i}");
            title.AddToClassList("multiplayer-game-card-title");
            card.Add(title);

            Label status = new Label("Waiting for opponent");
            status.AddToClassList("multiplayer-game-card-status");
            card.Add(status);

            activeGamesList.Add(card);
        }
    }

    private VisualElement GetViewport()
    {
        VisualElement viewport = activeGamesList.Q(className: "unity-scroll-view__content-viewport");
        if (viewport != null)
        {
            return viewport;
        }

        return activeGamesList.Q(className: "unity-scroll-view__viewport");
    }

    private sealed class TestWindow : EditorWindow
    {
    }
}
