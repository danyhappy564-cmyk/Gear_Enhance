using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;

namespace GearEnhance;

// 1.4.0 stash shimmer (design 8, 12.5; mockup stash.html .mythcell): a 1 px border with a 1 px outline,
// a soft glow outside (box-shadow 12 → 22 px) and inside (inset 14 → 18 px), and a light sweep across the
// cell (::after, 3.2 s). Built once per item view from the game's uGUI Image like the insurance badge;
// frames on screen are recolored each frame. User 2026-10-26: thinner border, slower pulse (2.2 → 4 s).
// With the F12 shimmer off they stay pink and still, without the sweep.
internal static class MythicFrame
{
    private const string FrameName = "GearEnhanceMythic";
    private const float Period = 4f, SweepPeriod = 3.2f;
    // Glow sprites: Edge texels of falloff on each side of a 9-sliced texture (1 texel = 1 UI px at 100 ppu).
    private const int Edge = 16;
    private static readonly Color Pink = new Color(1f, 0.416f, 0.835f), Gold = new Color(1f, 0.847f, 0.471f);
    private static Type image;
    private static Action<Component, Color> paint;
    private static Sprite outer, inner, sweep;
    private static readonly List<Shown> shown = new List<Shown>();
    internal static int Count => shown.Count;

    // Children of a frame, in Create order.
    private sealed class Shown
    {
        internal Component View;
        internal RectTransform Frame, Band;
        internal Component Glow, Inset, Shine;
        internal readonly Component[] Border = new Component[4], Outline = new Component[4];
    }

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
        var entry = new Shown { View = component, Frame = (RectTransform)frame };
        entry.Glow = frame.GetChild(0).GetComponent(image);
        entry.Inset = frame.GetChild(1).GetComponent(image);
        var mask = frame.GetChild(2);
        entry.Band = (RectTransform)mask.GetChild(0);
        entry.Shine = entry.Band.GetComponent(image);
        for (var side = 0; side < 4; side++)
        {
            entry.Border[side] = frame.GetChild(3 + side).GetComponent(image);
            entry.Outline[side] = frame.GetChild(7 + side).GetComponent(image);
        }
        // Bounded: a stash never shows thousands of mythic cells; drop the oldest past 256.
        if (shown.Count >= 256) shown.RemoveAt(0);
        shown.Add(entry);
        Paint(entry, Pink, 0, -1);
    }

    internal static void Tick(double time, bool shimmer)
    {
        if (shown.Count == 0) return;
        var pulse = shimmer ? (float)(0.5 - 0.5 * Math.Cos(time * Math.PI * 2 / Period)) : 0;
        var tint = Color.Lerp(Pink, Gold, pulse);
        var sweepAt = shimmer ? (float)(time % SweepPeriod / SweepPeriod) : -1;
        for (var index = shown.Count - 1; index >= 0; index--)
        {
            var entry = shown[index];
            if (entry.View == null || !entry.View.gameObject.activeInHierarchy) { if (entry.View == null) shown.RemoveAt(index); continue; }
            Paint(entry, tint, pulse, sweepAt);
        }
    }

    internal static void Clear() => shown.Clear();

    // Mockup keyframes: outline .6 → .8, outer glow .45 → .55, inset .18 → .22 (pink → gold).
    private static void Paint(Shown entry, Color tint, float pulse, float sweepAt)
    {
        for (var side = 0; side < 4; side++)
        {
            if (entry.Border[side] != null) paint(entry.Border[side], new Color(tint.r, tint.g, tint.b, 1));
            if (entry.Outline[side] != null) paint(entry.Outline[side], new Color(tint.r, tint.g, tint.b, 0.6f + 0.2f * pulse));
        }
        if (entry.Glow != null) paint(entry.Glow, new Color(tint.r, tint.g, tint.b, 0.45f + 0.1f * pulse));
        if (entry.Inset != null) paint(entry.Inset, new Color(tint.r, tint.g, tint.b, 0.18f + 0.04f * pulse));
        if (entry.Band == null) return;
        var visible = sweepAt >= 0;
        if (entry.Band.gameObject.activeSelf != visible) entry.Band.gameObject.SetActive(visible);
        if (!visible) return;
        // CSS background-position 160% → -60% on a 260% wide gradient: the band centre runs from
        // -1.26 to 2.26 cell widths, left to right, so it is on the cell about 40% of the time.
        var size = entry.Frame.rect.size;
        entry.Band.sizeDelta = new Vector2(0.42f * size.x, (size.x + size.y) * 1.2f);
        entry.Band.anchoredPosition = new Vector2((-1.26f + 3.52f * sweepAt) * size.x - size.x * 0.5f, 0);
    }

    // Glow outside, glow inside, masked sweep, then border and outline lines (top, bottom, left, right).
    private static Transform Create(Transform cell)
    {
        if (image == null) Prepare();
        var frame = Rect(FrameName, cell);
        Stretch(frame, 0);
        Sliced(Stretch(Rect("Glow", frame), -Edge), outer);
        Sliced(Stretch(Rect("Inset", frame), 0), inner);
        var mask = Stretch(Rect("Sweep", frame), 0);
        mask.gameObject.AddComponent(GameBridge.RuntimeType("UnityEngine.UI.RectMask2D") ?? throw new TypeLoadException("UnityEngine.UI.RectMask2D"));
        var band = Rect("Band", mask);
        band.anchorMin = band.anchorMax = new Vector2(0.5f, 0.5f);
        band.pivot = new Vector2(0.5f, 0.5f);
        band.localRotation = Quaternion.Euler(0, 0, -25);
        var shine = Graphic(band);
        GameBridge.Set(shine, "sprite", sweep);
        paint(shine, new Color(1, 1, 1, 1));
        band.gameObject.SetActive(false);
        // Border: 0..1 px inside the edge. Outline: 0..1 px outside it (box-shadow 0 0 0 1px).
        foreach (var shift in new[] { -0.5f, 0.5f }) Lines(frame, shift);
        return frame;
    }

    private static void Lines(RectTransform frame, float shift)
    {
        var grow = shift > 0 ? 2f : 0f;
        foreach (var (min, max, horizontal, sign) in new[]
        {
            (new Vector2(0, 1), new Vector2(1, 1), true, 1f), (new Vector2(0, 0), new Vector2(1, 0), true, -1f),
            (new Vector2(0, 0), new Vector2(0, 1), false, -1f), (new Vector2(1, 0), new Vector2(1, 1), false, 1f)
        })
        {
            var line = Rect("Line", frame);
            line.anchorMin = min; line.anchorMax = max; line.pivot = new Vector2(0.5f, 0.5f);
            // Top/bottom own the corners; left/right fit between them.
            line.sizeDelta = horizontal ? new Vector2(grow, 1) : new Vector2(1, shift > 0 ? 0 : -2);
            line.anchoredPosition = horizontal ? new Vector2(0, sign * shift) : new Vector2(sign * shift, 0);
            Graphic(line);
        }
    }

    private static void Prepare()
    {
        image = GameBridge.RuntimeType("UnityEngine.UI.Image") ?? throw new TypeLoadException("UnityEngine.UI.Image");
        var setter = image.GetProperty("color")?.GetSetMethod() ?? throw new MissingMemberException("Image.color");
        // Typed setter: reflection SetValue would box a Color per line per frame.
        var method = new DynamicMethod("GearEnhancePaint", null, new[] { typeof(Component), typeof(Color) }, typeof(MythicFrame).Module, true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, setter.DeclaringType);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, setter); il.Emit(OpCodes.Ret);
        paint = (Action<Component, Color>)method.CreateDelegate(typeof(Action<Component, Color>));
        outer = Glow(true);
        inner = Glow(false);
        sweep = Sweep();
        // Unreferenced runtime sprites would go in Resources.UnloadUnusedAssets between raids, and an
        // Image without its sprite fills the whole cell.
        outer.hideFlags = inner.hideFlags = sweep.hideFlags = HideFlags.HideAndDontSave;
    }

    // White falloff, tinted by Image.color. Outer: peak at the cell edge (texel Edge), fading outward,
    // round at the corners, nothing inside the cell. Inner: peak at the edge, fading inward.
    private static Sprite Glow(bool outside)
    {
        const int size = Edge * 2 + 2;
        var texture = Texture(size, size);
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            float alpha;
            if (outside)
            {
                var dx = Math.Max(0, Math.Max(Edge - (x + 0.5f), x + 0.5f - (size - Edge)));
                var dy = Math.Max(0, Math.Max(Edge - (y + 0.5f), y + 0.5f - (size - Edge)));
                var r = Mathf.Sqrt(dx * dx + dy * dy);
                alpha = dx == 0 && dy == 0 ? 0 : Mathf.Exp(-0.5f * (r / 5.5f) * (r / 5.5f));
            }
            else
            {
                var d = Math.Min(Math.Min(x, y), Math.Min(size - 1 - x, size - 1 - y)) + 0.5f;
                alpha = d >= Edge ? 0 : Mathf.Exp(-0.5f * (d / 4.5f) * (d / 4.5f));
            }
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(alpha)));
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect,
            new Vector4(Edge, Edge, Edge, Edge));
    }

    // Mockup sweep: transparent 42% → white .2 at 50% → transparent 58%, across the band.
    private static Sprite Sweep()
    {
        const int width = 32;
        var texture = Texture(width, 1);
        var pixels = new Color32[width];
        for (var x = 0; x < width; x++)
            pixels[x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255 * 0.2f * (1 - Math.Abs((x + 0.5f) / width * 2 - 1))));
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, width, 1), new Vector2(0.5f, 0.5f), 100);
    }

    private static Texture2D Texture(int width, int height) => new Texture2D(width, height, TextureFormat.RGBA32, false)
    {
        wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
    };

    private static void Sliced(RectTransform rect, Sprite sprite)
    {
        var graphic = Graphic(rect);
        GameBridge.Set(graphic, "sprite", sprite);
        GameBridge.Set(graphic, "type", Enum.Parse(image.GetNestedType("Type") ?? throw new TypeLoadException("Image.Type"), "Sliced"));
        GameBridge.Set(graphic, "fillCenter", false);
    }

    // They never take the cell's clicks or drags.
    private static Component Graphic(RectTransform rect)
    {
        var graphic = rect.gameObject.AddComponent(image);
        GameBridge.Set(graphic, "raycastTarget", false);
        return graphic;
    }

    private static RectTransform Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset); rect.offsetMax = new Vector2(-inset, -inset);
        return rect;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
    }
}
