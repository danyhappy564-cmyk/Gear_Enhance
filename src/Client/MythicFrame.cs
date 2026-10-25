using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GearEnhance;

// 1.4.0 stash shimmer (design 8, 12.5): a 2 px pink frame on mythic gear cells, built once per
// item view from the game's uGUI Image like the insurance badge. Only frames on screen are
// recolored each frame (pink ↔ gold, 2.2 s); with the F12 shimmer off they stay pink.
internal static class MythicFrame
{
    private const string FrameName = "GearEnhanceMythic";
    private static readonly Color Pink = new Color(1f, 0.416f, 0.835f), Gold = new Color(1f, 0.847f, 0.471f);
    private static Type image;
    private static PropertyInfo color;
    private static readonly List<(Component View, Component[] Lines)> shown = new List<(Component, Component[])>();
    internal static int Count => shown.Count;

    internal static void Hide(object view)
    {
        if (!(view is Component component) || component == null) return;
        component.transform.Find(FrameName)?.gameObject.SetActive(false);
        shown.RemoveAll(entry => ReferenceEquals(entry.View, component));
    }

    internal static void Show(object view)
    {
        if (!(view is Component component) || component == null) return;
        var frame = component.transform.Find(FrameName) ?? Create(component.transform);
        frame.gameObject.SetActive(true);
        frame.SetAsLastSibling();
        if (shown.Exists(entry => ReferenceEquals(entry.View, component))) return;
        var lines = new Component[frame.childCount];
        for (var index = 0; index < lines.Length; index++) lines[index] = frame.GetChild(index).GetComponent(image);
        // Bounded: a stash never shows thousands of mythic cells; drop the oldest past 256.
        if (shown.Count >= 256) shown.RemoveAt(0);
        shown.Add((component, lines));
        Paint(lines, Pink);
    }

    internal static void Tick(double time, bool shimmer)
    {
        if (shown.Count == 0) return;
        var tint = shimmer ? Color.Lerp(Pink, Gold, (float)(0.5 + 0.5 * Math.Sin(time * Math.PI * 2 / 2.2))) : Pink;
        for (var index = shown.Count - 1; index >= 0; index--)
        {
            var (view, lines) = shown[index];
            if (view == null || !view.gameObject.activeInHierarchy) { if (view == null) shown.RemoveAt(index); continue; }
            Paint(lines, tint);
        }
    }

    internal static void Clear() => shown.Clear();

    private static void Paint(Component[] lines, Color tint)
    {
        foreach (var line in lines) if (line != null) color.SetValue(line, tint);
    }

    // Top, bottom, left and right lines inside the cell; they never take its clicks or drags.
    private static Transform Create(Transform cell)
    {
        if (image == null)
        {
            image = GameBridge.RuntimeType("UnityEngine.UI.Image") ?? throw new TypeLoadException("UnityEngine.UI.Image");
            color = image.GetProperty("color") ?? throw new MissingMemberException("Image.color");
        }
        var frame = Rect(FrameName, cell);
        frame.anchorMin = Vector2.zero; frame.anchorMax = Vector2.one;
        frame.offsetMin = frame.offsetMax = Vector2.zero;
        foreach (var (min, max, size) in new[]
        {
            (new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 2)), (new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 2)),
            (new Vector2(0, 0), new Vector2(0, 1), new Vector2(2, 0)), (new Vector2(1, 0), new Vector2(1, 1), new Vector2(2, 0))
        })
        {
            var line = Rect("Line", frame);
            line.anchorMin = min; line.anchorMax = max; line.pivot = new Vector2(min.x, min.y);
            line.sizeDelta = size; line.anchoredPosition = Vector2.zero;
            var graphic = line.gameObject.AddComponent(image);
            GameBridge.Set(graphic, "raycastTarget", false);
        }
        return frame;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
    }
}
