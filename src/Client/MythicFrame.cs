using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GearEnhance;

// 1.4.0 stash shimmer (design 8, 12.5; mockup @keyframes mythBorder): a 2 px frame on mythic gear
// cells with a soft glow outside (3 bands) and inside (3 bands), built once per item view from the
// game's uGUI Image like the insurance badge. Frames on screen are recolored each frame: pink ↔ gold
// over 2.2 s, the glow brighter at gold. With the F12 shimmer off they stay pink and still.
internal static class MythicFrame
{
    private const string FrameName = "GearEnhanceMythic";
    private static readonly Color Pink = new Color(1f, 0.416f, 0.835f), Gold = new Color(1f, 0.847f, 0.471f);
    private static Type image;
    private static PropertyInfo color;
    private static readonly List<(Component View, Component[] Lines)> shown = new List<(Component, Component[])>();
    // Band 0 = the 2 px border; bands 1-3 outside at 2, 4, 6 px; bands 4-6 inside at 2, 4, 6 px.
    // Declared before Weights: static fields initialize in text order and Bands() reads Layout.
    private static readonly (float Offset, float Weight)[] Layout =
        { (0, 1), (2, 0.55f), (4, 0.3f), (6, 0.12f), (-2, 0.28f), (-4, 0.15f), (-6, 0.07f) };
    // Per line of a frame (same order as Create): base opacity. 4 lines per band.
    private static readonly float[] Weights = Bands();
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
        Paint(lines, Pink, 0);
    }

    internal static void Tick(double time, bool shimmer)
    {
        if (shown.Count == 0) return;
        var pulse = shimmer ? (float)(0.5 + 0.5 * Math.Sin(time * Math.PI * 2 / 2.2)) : 0;
        var tint = Color.Lerp(Pink, Gold, pulse);
        for (var index = shown.Count - 1; index >= 0; index--)
        {
            var (view, lines) = shown[index];
            if (view == null || !view.gameObject.activeInHierarchy) { if (view == null) shown.RemoveAt(index); continue; }
            Paint(lines, tint, pulse);
        }
    }

    internal static void Clear() => shown.Clear();

    // Border lines stay solid; glow bands fade outward and grow with the pulse (12 px → 22 px glow).
    private static void Paint(Component[] lines, Color tint, float pulse)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index] == null) continue;
            var weight = index < Weights.Length ? Weights[index] : 1;
            var alpha = weight >= 1 ? 1 : Mathf.Clamp01(weight * (0.75f + 0.6f * pulse));
            color.SetValue(lines[index], new Color(tint.r, tint.g, tint.b, alpha));
        }
    }

    private static float[] Bands()
    {
        var weights = new List<float>();
        foreach (var (_, weight) in Layout) for (var side = 0; side < 4; side++) weights.Add(weight);
        return weights.ToArray();
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
        // Each band is a 2 px ring. Border: 0..2 px inside the edge. Outside band o: (o-2)..o px out.
        // Inside band o: o..o+2 px in. Top/bottom lines own the corners; left/right fit between them.
        foreach (var (offset, _) in Layout)
        {
            var o = Mathf.Abs(offset);
            float shift, across, along;
            if (offset > 0) { shift = o - 1; across = 2 * o; along = 2 * (o - 2); }
            else if (offset < 0) { shift = -(o + 1); across = -2 * o; along = -2 * (o + 2); }
            else { shift = -1; across = 0; along = -4; }
            foreach (var (min, max, horizontal, sign) in new[]
            {
                (new Vector2(0, 1), new Vector2(1, 1), true, 1f), (new Vector2(0, 0), new Vector2(1, 0), true, -1f),
                (new Vector2(0, 0), new Vector2(0, 1), false, -1f), (new Vector2(1, 0), new Vector2(1, 1), false, 1f)
            })
            {
                var line = Rect("Line", frame);
                line.anchorMin = min; line.anchorMax = max; line.pivot = new Vector2(0.5f, 0.5f);
                line.sizeDelta = horizontal ? new Vector2(across, 2) : new Vector2(2, along);
                line.anchoredPosition = horizontal ? new Vector2(0, sign * shift) : new Vector2(sign * shift, 0);
                var graphic = line.gameObject.AddComponent(image);
                GameBridge.Set(graphic, "raycastTarget", false);
            }
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
