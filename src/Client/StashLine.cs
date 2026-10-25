using System;
using System.Collections.Generic;
using UnityEngine;

namespace GearEnhance;

// 1.4.0 stash line under the name (mockup "3단계 · 초월 6"): a copy of the cell's own name caption
// (GridItemView.Caption, TextMeshPro) placed one line below it. Appending "\n…" to the name did not
// work: the game's caption is one line and cut the second line off.
internal static class StashLine
{
    private const string LineName = "GearEnhanceLine";

    internal static void Hide(object view)
    {
        if (!(view is Component component) || component == null) return;
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
        target.anchoredPosition = source.anchoredPosition - new Vector2(0, size + 2);
        var label = line.GetComponent(caption.GetType());
        GameBridge.Set(label, "richText", true);
        GameBridge.Set(label, "fontSize", size);
        GameBridge.Set(label, "text", text);
        GameBridge.Set(label, "raycastTarget", false);
        line.gameObject.SetActive(true);
        line.SetAsLastSibling();
    }

    // "신화 3/5 · 초월 4/5·5/5", or only the part the gear has (user: stages as N/5).
    internal static string Text(int mythic, int second, int third)
    {
        var parts = new List<string>();
        if (mythic >= 0) parts.Add("<color=#FF6AD5>" + Loc.L($"신화 {mythic}/{MythicRules.MaxLevel}", $"Mythic {mythic}/{MythicRules.MaxLevel}") + "</color>");
        if (second + third > 0)
            parts.Add("<color=#40D1D9>" + Loc.L("초월 ", "Trans. ") + $"{second}/{TranscendRules.MaxLevel}·{third}/{TranscendRules.MaxLevel}</color>");
        return string.Join(" · ", parts);
    }
}
