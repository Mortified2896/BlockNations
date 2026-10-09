using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// References are authored by the training scene builder, never searched at runtime.
public sealed class TrainingHumanPresentation : MonoBehaviour
{
    [SerializeField] private GameObject[] humanUIRoots;
    [SerializeField] private MonoBehaviour[] humanInput;
    [SerializeField] private UIDocument[] humanDocuments;
    private bool humanMode;
    private Rect viewport = new Rect(0, 0, 1, 1);
    private readonly Dictionary<UIDocument, DocumentLayout> layouts = new Dictionary<UIDocument, DocumentLayout>();

    private sealed class DocumentLayout
    {
        public VisualElement root;
        public StyleLength width, marginLeft;
    }

    public void SetHumanMode(bool human)
    {
        humanMode = human;
        if (!human) RestoreLayouts();
        foreach (GameObject root in humanUIRoots) if (root != null) root.SetActive(human);
        foreach (MonoBehaviour input in humanInput) if (input != null) input.enabled = human;
        if (human) ApplyLayouts(force: true);
    }

    // The authored gameplay documents share the board's horizontal screen region.
    // Percentages keep this independent of UITK panel scaling and display resolution.
    public void SetViewport(Rect normalizedViewport)
    {
        float left = Mathf.Clamp01(normalizedViewport.x);
        var next = new Rect(left, 0, Mathf.Clamp(normalizedViewport.width, 0, 1 - left), 1);
        if (viewport == next) return;
        viewport = next;
        ApplyLayouts(force: true);
    }

    private void LateUpdate() => ApplyLayouts(force: false);

    private void ApplyLayouts(bool force)
    {
        if (!humanMode || humanDocuments == null) return;
        foreach (UIDocument document in humanDocuments)
        {
            if (document == null || !document.isActiveAndEnabled) continue;
            VisualElement root = document.rootVisualElement;
            if (root == null) continue;
            if (!layouts.TryGetValue(document, out DocumentLayout layout) || layout.root != root)
            {
                if (layout != null) Restore(layout);
                layout = new DocumentLayout { root = root, width = root.style.width, marginLeft = root.style.marginLeft };
                layouts[document] = layout;
            }
            else if (!force) continue;
            root.style.marginLeft = Length.Percent(viewport.x * 100);
            root.style.width = Length.Percent(viewport.width * 100);
        }
    }

    private static void Restore(DocumentLayout layout)
    {
        layout.root.style.width = layout.width;
        layout.root.style.marginLeft = layout.marginLeft;
    }

    private void RestoreLayouts()
    {
        foreach (DocumentLayout layout in layouts.Values) Restore(layout);
        layouts.Clear();
    }

    private void OnDisable() => RestoreLayouts();
}
