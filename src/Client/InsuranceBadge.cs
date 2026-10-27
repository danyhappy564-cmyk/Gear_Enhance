using System;
using UnityEngine;

namespace GearEnhance;

// 1.4.1 F12 choices for the stash insurance mark (user feedback "the icon is in the way", mockup
// docs/mockup/insurance-badge.html). New values go at the end: the cfg stores the name.
public enum InsuranceMark { Corner, Letter, Dot, NameTag, Off }
public enum InsuranceMarkPlace { BottomLeft, TopLeft, BesideCount }

// F12 settings and the name tag, free of Unity types (the name hook and the tests use them).
internal static class InsuranceMarks
{
    internal static InsuranceMark Shape = InsuranceMark.Corner;
    internal static InsuranceMarkPlace Place = InsuranceMarkPlace.BottomLeft;
    internal static bool ShowUninsured;

    // Whether this tier gets any mark under the current settings.
    internal static bool Marked(InsuranceTier tier) => Shape != InsuranceMark.Off && (tier != InsuranceTier.None || ShowUninsured);

    // " [P]" after the stars for InsuranceMark.NameTag, else "". Same colors as InsuranceBadge.Tint.
    internal static string NameTag(InsuranceTier tier) => Shape != InsuranceMark.NameTag || !Marked(tier) ? ""
        : " <size=85%><color=#" + (tier == InsuranceTier.Premium ? "FAC75C" : tier == InsuranceTier.Advanced ? "73B3FA"
            : tier == InsuranceTier.Basic ? "B3DB9E" : "9EA6AB") + ">[" + Loc.TierLetter(tier) + "]</color></size>";
}

// Enhancement insurance on stash and slot cells (Codex mockup A, approved 2026-10-24): a small mark on
// the cell, apart from the name line (clipped in equipment slots). 1.4.1: a corner triangle (default),
// the 1.4.0 letter square, a dot, a [P] tag after the stars (ItemMarkers.Name) or nothing; uninsured
// gear shows its grey X only when asked. Built once per item view from the game's uGUI types and font,
// then only recolored and moved; a pooled view showing other gear hides it. Runs from UpdateInfo,
// never per frame.
internal static class InsuranceBadge
{
    private const string BadgeName = "GearEnhanceInsurance";
    private static Type image, text;
    private static object center, bold;
    private static Sprite dot, cornerTopLeft, cornerBottomLeft, cornerBottomRight;
    private static readonly Color Edge = new Color(0.043f, 0.055f, 0.067f);

    // Grade colors of the insurance window; grey for none.
    internal static Color Tint(InsuranceTier tier) => tier == InsuranceTier.Premium ? new Color(0.98f, 0.78f, 0.36f)
        : tier == InsuranceTier.Advanced ? new Color(0.45f, 0.70f, 0.98f)
        : tier == InsuranceTier.Basic ? new Color(0.70f, 0.86f, 0.62f) : new Color(0.62f, 0.65f, 0.67f);

    internal static void Hide(object view)
    {
        if (view is Component component && component != null) component.transform.Find(BadgeName)?.gameObject.SetActive(false);
    }

    internal static void Show(object view, InsuranceTier tier)
    {
        if (!(view is Component component) || component == null) return;
        var shape = InsuranceMarks.Shape;
        if (!InsuranceMarks.Marked(tier) || shape == InsuranceMark.NameTag) { Hide(view); return; }
        var badge = (RectTransform)(component.transform.Find(BadgeName) ?? Create(view, component.transform));
        var fill = (RectTransform)badge.GetChild(0);
        var letter = fill.GetChild(0);
        var root = badge.GetComponent(image);
        var inner = fill.GetComponent(image);
        var tint = Tint(tier);
        switch (shape)
        {
            case InsuranceMark.Letter:
                // 18 px dark edge with a 16 px grade square and its letter (the 1.4.0 badge).
                badge.sizeDelta = new Vector2(18, 18);
                Paint(root, null, Edge); Paint(inner, null, tint);
                fill.gameObject.SetActive(true); letter.gameObject.SetActive(true);
                GameBridge.Set(letter.GetComponent(text), "text", Loc.TierLetter(tier));
                break;
            case InsuranceMark.Dot:
                // 7 px dot inside a 9 px dark ring so it reads on the gold and purple cells.
                badge.sizeDelta = new Vector2(9, 9);
                Paint(root, dot, Edge); Paint(inner, dot, tint);
                fill.gameObject.SetActive(true); letter.gameObject.SetActive(false);
                break;
            default:
                // 11 px triangle folded into the corner, color only.
                badge.sizeDelta = new Vector2(11, 11);
                var place = InsuranceMarks.Place;
                Paint(root, place == InsuranceMarkPlace.TopLeft ? cornerTopLeft : place == InsuranceMarkPlace.BesideCount ? cornerBottomRight : cornerBottomLeft, tint);
                fill.gameObject.SetActive(false);
                break;
        }
        Position(component, badge);
        badge.gameObject.SetActive(true);
        badge.SetAsLastSibling();
    }

    // Item cells are 63 px. Top left keeps the 1.4.0 spot (the mockup's 22 px badge at (4, 32) on its
    // 86 px cell, below the slot number); bottom left is a corner the game leaves empty; beside the count
    // sits left of the cell's lower-right text (ammo count, durability), the corner itself for a triangle.
    private static void Position(Component cell, RectTransform badge)
    {
        var corner = InsuranceMarks.Shape == InsuranceMark.Corner;
        var dotMark = InsuranceMarks.Shape == InsuranceMark.Dot;
        var gap = corner ? 0f : dotMark ? 5f : 3f;
        if (InsuranceMarks.Place == InsuranceMarkPlace.TopLeft)
        {
            badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(0, 1);
            badge.anchoredPosition = corner ? Vector2.zero : dotMark ? new Vector2(5, -6) : new Vector2(3, -23);
            return;
        }
        var right = InsuranceMarks.Place == InsuranceMarkPlace.BesideCount;
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(right ? 1 : 0, 0);
        if (!right || corner)
        {
            badge.anchoredPosition = new Vector2(right ? -gap : gap, gap);
            return;
        }
        // Without drawn count text (inactive view during Init, or none) it waits in the corner.
        var rect = ((RectTransform)cell.transform).rect;
        badge.anchoredPosition = CountLeft(cell, badge) is float left ? new Vector2(left - rect.xMax - 3, gap) : new Vector2(-gap, gap);
    }

    // Left edge (cell space) of the drawn text in the cell's lower-right quarter, if any. The badge's own
    // letter sits there too and is skipped, or every refresh would push it further left (Codex review).
    private static float? CountLeft(Component cell, Transform badge)
    {
        if (!(GameBridge.Get(cell, "Caption") is Component caption) || caption == null) return null;
        var rect = ((RectTransform)cell.transform).rect;
        float? left = null;
        foreach (var label in cell.GetComponentsInChildren(caption.GetType(), false))
        {
            if (label == null || ReferenceEquals(label, caption) || label.gameObject.name == StashLine.LineName || label.transform.IsChildOf(badge)) continue;
            if (!(StashLine.Bounds(label, cell.transform) is Rect box) || box.center.x < rect.center.x || box.center.y > rect.center.y) continue;
            left = left is float known ? Math.Min(known, box.xMin) : box.xMin;
        }
        return left;
    }

    private static Transform Create(object view, Transform cell)
    {
        if (image == null)
        {
            image = GameBridge.RuntimeType("UnityEngine.UI.Image") ?? throw new TypeLoadException("UnityEngine.UI.Image");
            text = GameBridge.RuntimeType("TMPro.TextMeshProUGUI") ?? throw new TypeLoadException("TMPro.TextMeshProUGUI");
            center = Enum.Parse(GameBridge.RuntimeType("TMPro.TextAlignmentOptions") ?? throw new TypeLoadException("TextAlignmentOptions"), "Center");
            bold = Enum.Parse(GameBridge.RuntimeType("TMPro.FontStyles") ?? throw new TypeLoadException("FontStyles"), "Bold");
            dot = Mask(16, (x, y) => (x - 7.5f) * (x - 7.5f) + (y - 7.5f) * (y - 7.5f) <= 7.6f * 7.6f);
            cornerTopLeft = Mask(16, (x, y) => x + (15 - y) <= 15);
            cornerBottomLeft = Mask(16, (x, y) => x + y <= 15);
            cornerBottomRight = Mask(16, (x, y) => (15 - x) + y <= 15);
        }
        var badge = Rect(BadgeName, cell);
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(0, 1);
        badge.sizeDelta = new Vector2(18, 18);
        Paint(badge, Edge);
        var fill = Rect("Fill", badge);
        fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
        fill.offsetMin = Vector2.one; fill.offsetMax = -Vector2.one;
        Paint(fill, Color.white);
        var letter = Rect("Letter", fill);
        letter.anchorMin = Vector2.zero; letter.anchorMax = Vector2.one;
        letter.offsetMin = letter.offsetMax = Vector2.zero;
        var label = letter.gameObject.AddComponent(text);
        // The cell caption's font has the Korean glyphs the game already shows in item names.
        var font = GameBridge.Get(GameBridge.Get(view, "Caption"), "font");
        if (font != null) GameBridge.Set(label, "font", font);
        GameBridge.Set(label, "fontSize", 12f);
        GameBridge.Set(label, "fontStyle", bold);
        GameBridge.Set(label, "alignment", center);
        GameBridge.Set(label, "color", Edge);
        GameBridge.Set(label, "enableWordWrapping", false);
        GameBridge.Set(label, "raycastTarget", false);
        return badge;
    }

    // A white mask made once (P2: no per-frame textures), tinted by the Image color.
    private static Sprite Mask(int size, Func<int, int, bool> inside)
    {
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(inside(x, y) ? 255 : 0));
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        texture.SetPixels32(pixels); texture.Apply(false, true);
        var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
    }

    // Never takes the cell's clicks or drags.
    private static void Paint(RectTransform rect, Color color)
    {
        var graphic = rect.gameObject.AddComponent(image);
        GameBridge.Set(graphic, "color", color);
        GameBridge.Set(graphic, "raycastTarget", false);
    }

    private static void Paint(Component graphic, Sprite sprite, Color color)
    {
        GameBridge.Set(graphic, "sprite", sprite);
        GameBridge.Set(graphic, "color", color);
    }
}
