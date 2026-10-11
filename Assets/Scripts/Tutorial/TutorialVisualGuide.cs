using System;
using UnityEngine;
using UnityEngine.UIElements;

// Board signs are presentation only. The controller observes real commands.
public sealed class TutorialVisualGuide
{
    private readonly VisualElement originRing, targetRing, arrow, arrowHead;
    private readonly Label originCue, targetCue;
    private Rect origin, target, bounds;
    private float scale;
    private bool hasOrigin, hasTarget;
    private bool showOriginCue;
    private string originText, targetText;

    public TutorialVisualGuide(VisualElement root)
    {
        originRing = Required<VisualElement>(root, "TutorialOriginRing");
        targetRing = Required<VisualElement>(root, "TutorialTargetRing");
        originCue = Required<Label>(root, "TutorialOriginCue");
        targetCue = Required<Label>(root, "TutorialTargetCue");
        arrow = Required<VisualElement>(root, "TutorialArrow");
        arrowHead = Required<VisualElement>(root, "TutorialArrowHead");
    }

    private static T Required<T>(VisualElement root, string name) where T : VisualElement => root.Q<T>(name) ??
        throw new InvalidOperationException("Missing tutorial guide reference: " + name);

    public void Begin(Rect bounds, float scale)
    {
        this.bounds = bounds; this.scale = scale;
        hasOrigin = hasTarget = false;
        foreach (var element in new[] { originRing, targetRing, originCue, targetCue, arrow, arrowHead })
            element.style.display = DisplayStyle.None;
    }

    public void Origin(Rect position, string text, bool selected)
    {
        origin = position; hasOrigin = true;
        originText = text; showOriginCue = !selected;
        PlaceRing(originRing, position);
        if (!selected) PlaceCue(originCue, position, text, above: true);
    }

    public void Target(Rect position, string text, bool focused)
    {
        target = position; hasTarget = true;
        targetText = text;
        PlaceRing(targetRing, position);
        // A gentle pulse directs attention without obscuring the unit or tile.
        targetRing.style.opacity = focused ? 0.78f + 0.22f * Mathf.Sin(Time.unscaledTime * 4f) : 1f;
        PlaceCue(targetCue, position, text, above: false);
    }

    public void Connect()
    {
        if (!hasOrigin || !hasTarget) return;
        // Put both signs outside the marked pair. A diagonal's "below
        // target" sign would otherwise cover the origin unit and its health.
        float top = Mathf.Min(origin.yMin, target.yMin);
        float bottom = Mathf.Max(origin.yMax, target.yMax);
        if (showOriginCue) PlaceCue(originCue,
            Rect.MinMaxRect(origin.xMin, top, origin.xMax, bottom), originText, above: true, gap: 22 * scale);
        PlaceCue(targetCue, Rect.MinMaxRect(target.xMin, top, target.xMax, bottom),
            targetText, above: false, gap: 22 * scale);
        Vector2 delta = target.center - origin.center;
        float distance = delta.magnitude;
        if (distance < 1) return;
        Vector2 direction = delta / distance;
        float inset = Mathf.Min(origin.width, origin.height) * 0.38f;
        Vector2 start = origin.center + direction * inset;
        Vector2 end = target.center - direction * inset;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        arrow.style.left = start.x - bounds.xMin;
        arrow.style.top = start.y - bounds.yMin - 2 * scale;
        arrow.style.width = Mathf.Max(1, Vector2.Distance(start, end));
        arrow.style.height = 4 * scale;
        arrow.style.rotate = new Rotate(Angle.Degrees(angle));
        arrow.style.display = DisplayStyle.Flex;
        arrowHead.style.left = end.x - bounds.xMin - 8 * scale;
        arrowHead.style.top = end.y - bounds.yMin - 8 * scale;
        arrowHead.style.width = arrowHead.style.height = 16 * scale;
        arrowHead.style.rotate = new Rotate(Angle.Degrees(angle + 45));
        arrowHead.style.display = DisplayStyle.Flex;
    }

    private void PlaceRing(VisualElement ring, Rect position)
    {
        ring.style.left = position.xMin - bounds.xMin - 2;
        ring.style.top = position.yMin - bounds.yMin - 2;
        ring.style.width = position.width + 4;
        ring.style.height = position.height + 4;
        ring.style.display = DisplayStyle.Flex;
    }

    private void PlaceCue(Label cue, Rect position, string text, bool above, float gap = -1)
    {
        float width = Mathf.Min(bounds.width - 24 * scale, (text.Length * 13 + 28) * scale);
        float height = 44 * scale;
        cue.text = text;
        cue.style.fontSize = 25 * scale;
        cue.style.width = width;
        cue.style.height = height;
        cue.style.left = Mathf.Clamp(position.center.x - bounds.xMin - width / 2,
            12 * scale, bounds.width - width - 12 * scale);
        if (gap < 0) gap = 8 * scale;
        float top = above ? position.yMin - height - gap : position.yMax + gap;
        if (top + height > bounds.yMax - 8 * scale) top = position.yMin - height - gap;
        cue.style.top = Mathf.Clamp(top - bounds.yMin, 8 * scale, bounds.height - height - 8 * scale);
        cue.style.display = DisplayStyle.Flex;
    }
}
