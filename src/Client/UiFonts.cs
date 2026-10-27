using System;
using System.IO;
using UnityEngine;

namespace GearEnhance;

// 1.4.0: the mockup's fonts in the game. GE Sans = Noto Sans KR (400/700, Hangul + Latin subset),
// GE Num = Rajdhani (600/700) with ₽ → ★ ✦ ◈ added (tools/make_fonts.py, both SIL OFL). They ship
// in BepInEx/plugins/GearEnhance/fonts and load from the file at runtime; without them the window
// keeps the Windows font (Malgun Gothic) it used before.
internal static class UiFonts
{
    internal static Font Sans, SansBold, Num, NumBold;
    // 1.4.1: Chinese (or a CJK custom file) draws text with the Windows CJK font; Unity bolds it.
    internal static bool Cjk;
    // The header's "中文" button in every language (GE Sans has no Chinese glyphs).
    internal static Font CjkLabel;
    // While Cjk: GE Sans kept for the header's "한국어" button (the CJK font may lack Hangul).
    internal static Font HangulLabel;
    internal static readonly string[] CjkFonts = { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "SimSun", "Arial Unicode MS" };
    internal static bool Loaded => Sans != null && SansBold != null && Num != null && NumBold != null;

    internal static string Load(string folder)
    {
        try
        {
            var why = new System.Collections.Generic.List<string>();
            Sans = Open(folder, "GESans-Regular.ttf", '가', why);
            SansBold = Open(folder, "GESans-Bold.ttf", '가', why);
            Num = Open(folder, "GENum-SemiBold.ttf", '5', why);
            NumBold = Open(folder, "GENum-Bold.ttf", '5', why);
            CjkLabel = Font.CreateDynamicFontFromOSFont(CjkFonts, 14);
            if (CjkLabel != null) CjkLabel.hideFlags = HideFlags.HideAndDontSave;
            if (Cjk && Sans != null)
            {
                var cjk = Font.CreateDynamicFontFromOSFont(CjkFonts, 16);
                if (cjk != null)
                {
                    cjk.hideFlags = HideFlags.HideAndDontSave;
                    HangulLabel = Sans; Destroy(SansBold);
                    Sans = SansBold = cjk;
                }
            }
            if (Loaded) return null;
            Sans = SansBold = Num = NumBold = null;
            return "글꼴을 쓰지 못해 Windows 글꼴로 표시합니다 / mockup fonts not usable, using the Windows font: " + folder + " (" + string.Join(", ", why) + ")";
        }
        catch (Exception ex)
        {
            Sans = SansBold = Num = NumBold = null;
            return "글꼴을 읽지 못해 Windows 글꼴로 표시합니다 / could not load the mockup fonts: " + ex.GetBaseException().Message;
        }
    }

    internal static void Unload()
    {
        Destroy(Sans); Destroy(SansBold); Destroy(Num); Destroy(NumBold); Destroy(CjkLabel); Destroy(HangulLabel);
        Sans = SansBold = Num = NumBold = CjkLabel = HangulLabel = null;
    }

    private static void Destroy(Font font)
    {
        if (font != null) UnityEngine.Object.Destroy(font);
    }

    private static Font Open(string folder, string file, char probe, System.Collections.Generic.List<string> why)
    {
        var path = Path.Combine(folder, file);
        if (!File.Exists(path)) { why.Add(file + ": 없음/missing"); return null; }
        // Font(path) loads a TrueType file as a dynamic font (Unity 2018.3+). A fresh dynamic font
        // knows a glyph only once it is requested (HasCharacter alone said no in game, 2026-10-26).
        var font = new Font(path) { hideFlags = HideFlags.HideAndDontSave };
        if (!font.dynamic) { why.Add(file + ": dynamic=false"); return null; }
        font.RequestCharactersInTexture(probe.ToString(), 16);
        if (font.GetCharacterInfo(probe, out _, 16) || font.HasCharacter(probe)) return font;
        why.Add(file + ": '" + probe + "' 없음/no glyph");
        return null;
    }
}
