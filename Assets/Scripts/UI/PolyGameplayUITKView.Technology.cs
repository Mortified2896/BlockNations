using System.Collections.Generic;
using System.Linq;
using BlockNations.Simulation.Poly;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class PolyGameplayUITKView
{
    private static readonly string[] BranchNames = { "Food", "Forest", "Mines", "Riding" };
    private static readonly string[][] BranchTechnologies = {
        new[] { "organization", "farming", "construction", "shields" },
        new[] { "hunting", "forestry", "mathematics", "archery" },
        new[] { "climbing", "mining", "smithing" },
        new[] { "riding", "roads", "chivalry" }
    };
    private void DrawTechnology()
    {
        overlayTitle.text = "Technology";
        overlaySummary.text = $"Gold {controller.Match.Board.GoldForSeat(0)} · Tap a technology to inspect it";
        for (int i = 0; i < BranchNames.Length; i++) {
            int next = i;
            var tab = Button(overlayTabs, "Branch" + i, BranchNames[i], () => { branch = next; selectedTechnology = BranchTechnologies[next][0]; Refresh(); }, "tab");
            tab.EnableInClassList("selected", branch == i);
        }
        var ids = BranchTechnologies[branch];
        if (!ids.Contains(selectedTechnology)) selectedTechnology = ids[0];
        var graph = new VisualElement { name = "TechnologyTree" }; graph.AddToClassList("technology-tree");
        graph.EnableInClassList("linear", ids.Length == 3);
        overlayChoices.Add(graph);
        var links = new TechnologyLinks(); graph.Add(links);
        var nodes = new Dictionary<string, Button>();
        for (int i = 0; i < ids.Length; i++) {
            string id = ids[i]; var tech = PolyRules.Technology(id);
            bool owned = controller.Match.HasTechnology(0, id);
            bool unlocked = controller.Match.HasTechnology(0, tech.Parent);
            var node = Button(graph, "Tech_" + id, "", () => { selectedTechnology = id; Refresh(); }, "technology-node");
            node.AddToClassList("tree-position-" + i);
            node.EnableInClassList("owned", owned); node.EnableInClassList("locked", !unlocked);
            node.EnableInClassList("selected", selectedTechnology == id);
            var bubble = new VisualElement(); bubble.AddToClassList("technology-bubble");
            var icon = new Image { sprite = TechnologyIcon(id), pickingMode = PickingMode.Ignore }; icon.AddToClassList("technology-icon");
            bubble.Add(icon); node.Add(bubble);
            node.Add(Text(tech.Name, "technology-name"));
            node.Add(Text(owned ? "✓ Unlocked" : controller.Match.ResearchCost(0, id) + " Gold", "technology-cost"));
            nodes.Add(id, node);
        }
        foreach (var id in ids) {
            var tech = PolyRules.Technology(id);
            if (tech.Parent != null && nodes.ContainsKey(tech.Parent))
                links.Edges.Add(new TechnologyLinks.Edge { From = nodes[tech.Parent], To = nodes[id], Unlocked = controller.Match.HasTechnology(0, tech.Parent) });
        }
        graph.RegisterCallback<GeometryChangedEvent>(_ => links.MarkDirtyRepaint());
        var selected = PolyRules.Technology(selectedTechnology);
        var detail = new VisualElement(); detail.AddToClassList("technology-detail"); overlayChoices.Add(detail);
        var copy = new VisualElement(); copy.AddToClassList("technology-copy"); detail.Add(copy);
        copy.Add(Text(selected.Description, "technology-description"));
        bool bought = controller.Match.HasTechnology(0, selected.Id);
        bool parentOwned = controller.Match.HasTechnology(0, selected.Parent);
        var action = Actions.FirstOrDefault(a => a.Kind == PolyActionKind.Research && a.Choice == selected.Id);
        string label = bought ? "Unlocked" : parentOwned ? $"Research · {controller.Match.ResearchCost(0, selected.Id)} Gold" : "Needs " + PolyRules.Technology(selected.Parent).Name;
        Button(detail, "Research_" + selected.Id, label, () => controller.Act(action), "research-button", CanAct && action != null);
        overlayHint.text = "Lines show prerequisites. Gold also pays for units and improvements.";
    }
    private Sprite TechnologyIcon(string id)
    {
        switch (id) {
            case "organization": return world.Icon("Fruit");
            case "farming": return world.Icon("Farm");
            case "construction": return world.Icon("Windmill");
            case "shields": return world.Icon("poly-defender");
            case "hunting": return world.Icon("Animals");
            case "archery": return world.Icon("poly-archer");
            case "forestry": return world.Icon("LumberHut");
            case "mathematics": return world.Icon("poly-catapult");
            case "climbing": return world.Icon("Mountain");
            case "mining": return world.Icon("Mine");
            case "smithing": return world.Icon("poly-swordsman");
            case "riding": return world.Icon("poly-rider");
            case "roads": return world.Icon("Road");
            default: return world.Icon("poly-knight");
        }
    }
    private sealed class TechnologyLinks : VisualElement
    {
        public sealed class Edge { public Button From, To; public bool Unlocked; }
        public readonly List<Edge> Edges = new List<Edge>();
        public TechnologyLinks()
        {
            pickingMode = PickingMode.Ignore; this.StretchToParentSize();
            generateVisualContent += DrawLinks;
        }
        private void DrawLinks(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            foreach (var edge in Edges) {
                var a = this.WorldToLocal(edge.From.Q(className: "technology-bubble").worldBound.center);
                var b = this.WorldToLocal(edge.To.Q(className: "technology-bubble").worldBound.center);
                if ((a - b).sqrMagnitude < 1) continue;
                var direction = (b - a).normalized;
                // Stop at the circumference so the relationship is visible around the image.
                float radius = edge.From.Q(className: "technology-bubble").worldBound.width / 2;
                a += direction * radius; b -= direction * radius;
                painter.lineWidth = 3;
                painter.strokeColor = edge.Unlocked ? new Color(.45f, .78f, .67f) : new Color(.42f, .55f, .65f);
                painter.BeginPath(); painter.MoveTo(a); painter.LineTo(b); painter.Stroke();
                var perpendicular = new Vector2(-direction.y, direction.x) * 4;
                painter.BeginPath(); painter.MoveTo(b - direction * 7 + perpendicular); painter.LineTo(b); painter.LineTo(b - direction * 7 - perpendicular); painter.Stroke();
            }
        }
    }
}
