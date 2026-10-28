using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GearEnhance;

// 1.4.0 stage tag under the name ("[신화 V · 초월 X]", StageText.Tag): a copy of the cell's own name
// caption (GridItemView.Caption, TextMeshPro) placed below it. Appending "\n…" to the name did not
// work: the game's caption is one line and cut the second line off. User 2026-10-26: in equipment
// slots the line under the name is the loaded ammo, so the tag moves below any other text of the
// cell it would cover. User 2026-10-28: in equipment slots it still covered the ammo until the cell was
// redrawn (opening the enhancement window): the game fills that line after UpdateInfo, so the tag looks
// again a few times in the next two seconds.
internal static class StashLine
{
    internal const string LineName = "GearEnhanceLine";
    private const float Scale = 0.86f;
    private static MethodInfo forceMesh;
    private static readonly float[] Rechecks = { 0.05f, 0.2f, 0.6f, 2f };
    private static readonly List<Pending> pending = new List<Pending>();
    internal static int Waiting => pending.Count;

    private sealed class Pending
    {
        internal Component Cell, Caption, Label;
        internal RectTransform Target;
        internal float Size, Since;
        internal int Step;
    }

    internal static void Hide(object view)
    {
        if (!(view is Component component) || component == null) return;
        pending.RemoveAll(entry => ReferenceEquals(entry.Cell, component));
        var caption = GameBridge.Get(view, "Caption") as Component;
        caption?.transform.parent?.Find(LineName)?.gameObject.SetActive(false);
    }

    internal static void Show(object view, string text)
    {
        if (!(view is Component component) || component == null || !(GameBridge.Get(view, "Caption") is Component caption) || caption == null) return;
        var parent = caption.transform.parent;
        var line = parent.Find(LineName);
        if (line == null)
        {
            var copy = UnityEngine.Object.Instantiate(caption.gameObject, parent, false);
            copy.name = LineName;
            line = copy.transform;
            // Children of the caption (if any) are not ours to keep.
            for (var index = line.childCount - 1; index >= 0; index--) UnityEngine.Object.Destroy(line.GetChild(index).gameObject);
        }
        var source = (RectTransform)caption.transform;
        var target = (RectTransform)line;
        target.anchorMin = source.anchorMin; target.anchorMax = source.anchorMax; target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta;
        var size = GameBridge.Get(caption, "fontSize") is float points && points > 0 ? points : 12f;
        var label = line.GetComponent(caption.GetType());
        GameBridge.Set(label, "richText", true);
        GameBridge.Set(label, "fontSize", size * Scale);
        GameBridge.Set(label, "text", text);
        GameBridge.Set(label, "raycastTarget", false);
        line.gameObject.SetActive(true);
        line.SetAsLastSibling();
        Place(component.transform, caption, label, target, size);
        pending.RemoveAll(entry => ReferenceEquals(entry.Cell, component));
        // Bounded: drop the oldest when a huge stash opens at once.
        if (pending.Count >= 512) pending.RemoveAt(0);
        pending.Add(new Pending { Cell = component, Caption = caption, Label = label, Target = target, Size = size, Since = Time.unscaledTime });
    }

    internal static void Tick(float now)
    {
        for (var index = pending.Count - 1; index >= 0; index--)
        {
            var entry = pending[index];
            if (entry.Cell == null || entry.Caption == null || entry.Label == null || entry.Target == null) { pending.RemoveAt(index); continue; }
            if (now - entry.Since < Rechecks[entry.Step]) continue;
            if (entry.Label.gameObject.activeInHierarchy) Place(entry.Cell.transform, entry.Caption, entry.Label, entry.Target, entry.Size);
            if (++entry.Step >= Rechecks.Length) pending.RemoveAt(index);
        }
    }

    internal static void Drop() => pending.Clear();

    // Under the name, then down past any text it covers.
    private static void Place(Transform cell, Component caption, Component label, RectTransform target, float size)
    {
        target.anchoredPosition = ((RectTransform)caption.transform).anchoredPosition - new Vector2(0, size + 1);
        Clear(cell, caption, label, target);
    }

    // Moves the tag down until its drawn text covers no other text of the cell (at most 3 steps).
    private static void Clear(Transform cell, Component caption, Component label, RectTransform target)
    {
        var mine = Bounds(label, cell);
        if (mine == null) return;
        var others = cell.GetComponentsInChildren(caption.GetType(), false);
        for (var step = 0; step < 3; step++)
        {
            var lowest = float.PositiveInfinity;
            foreach (var other in others)
            {
                if (other == null || ReferenceEquals(other, label) || ReferenceEquals(other, caption) || !(Bounds(other, cell) is Rect box)) continue;
                if (box.Overlaps(mine.Value)) lowest = Math.Min(lowest, box.yMin);
            }
            if (float.IsPositiveInfinity(lowest)) return;
            var shift = mine.Value.yMax - lowest + 2;
            target.anchoredPosition -= new Vector2(0, shift);
            mine = new Rect(mine.Value.x, mine.Value.y - shift, mine.Value.width, mine.Value.height);
        }
    }

    // The drawn text of a TextMeshPro label in the cell's space (y up), or null when it shows nothing.
    internal static Rect? Bounds(Component label, Transform cell)
    {
        if (!label.gameObject.activeInHierarchy || !(GameBridge.Get(label, "text") is string text) || text.Trim().Length == 0) return null;
        forceMesh ??= label.GetType().GetMethod("ForceMeshUpdate", new[] { typeof(bool), typeof(bool) });
        forceMesh?.Invoke(label, new object[] { false, false });
        if (!(GameBridge.Get(label, "textBounds") is Bounds bounds) || bounds.size.x < 0.5f || bounds.size.y < 0.5f) return null;
        var a = cell.InverseTransformPoint(label.transform.TransformPoint(bounds.min));
        var b = cell.InverseTransformPoint(label.transform.TransformPoint(bounds.max));
        return Rect.MinMaxRect(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Max(a.x, b.x), Math.Max(a.y, b.y));
    }
}
