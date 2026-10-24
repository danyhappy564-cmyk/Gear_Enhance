using System;
using UnityEngine;

namespace GearEnhance;

// Enhancement insurance on stash and slot cells (Codex mockup A, approved 2026-10-24): a small
// letter square on the cell's left edge, apart from the name line (clipped in equipment slots).
// Built once per item view from the game's uGUI types and font, then only recolored; a pooled
// view showing other gear hides it. Runs from UpdateInfo, never per frame.
internal static class InsuranceBadge
{
    private const string BadgeName = "GearEnhanceInsurance";
    private static Type image, text;
    private static object center, bold;
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
        var badge = component.transform.Find(BadgeName) ?? Create(view, component.transform);
        var fill = badge.GetChild(0);
        GameBridge.Set(fill.GetComponent(image), "color", Tint(tier));
        GameBridge.Set(fill.GetChild(0).GetComponent(text), "text", Loc.TierLetter(tier));
        badge.gameObject.SetActive(true);
        badge.SetAsLastSibling();
    }

    // 18 px dark edge with a 16 px grade square and its letter. Item cells are 63 px: this is the
    // mockup's 22 px badge at (4, 32) on its 86 px cell, below the slot number and above the stars.
    private static Transform Create(object view, Transform cell)
    {
        if (image == null)
        {
            image = GameBridge.RuntimeType("UnityEngine.UI.Image") ?? throw new TypeLoadException("UnityEngine.UI.Image");
            text = GameBridge.RuntimeType("TMPro.TextMeshProUGUI") ?? throw new TypeLoadException("TMPro.TextMeshProUGUI");
            center = Enum.Parse(GameBridge.RuntimeType("TMPro.TextAlignmentOptions") ?? throw new TypeLoadException("TextAlignmentOptions"), "Center");
            bold = Enum.Parse(GameBridge.RuntimeType("TMPro.FontStyles") ?? throw new TypeLoadException("FontStyles"), "Bold");
        }
        var badge = Rect(BadgeName, cell);
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(0, 1);
        badge.anchoredPosition = new Vector2(3, -23);
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
}
