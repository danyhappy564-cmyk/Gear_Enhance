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
    internal static bool Loaded => Sans != null && SansBold != null && Num != null && NumBold != null;

    internal static string Load(string folder)
    {
        try
        {
            Sans = Open(folder, "GESans-Regular.ttf", '가');
            SansBold = Open(folder, "GESans-Bold.ttf", '가');
            Num = Open(folder, "GENum-SemiBold.ttf", '5');
            NumBold = Open(folder, "GENum-Bold.ttf", '5');
            if (Loaded) return null;
            Sans = SansBold = Num = NumBold = null;
            return "글꼴 파일이 없어 Windows 글꼴로 표시합니다 / mockup fonts missing, using the Windows font: " + folder;
        }
        catch (Exception ex)
        {
            Sans = SansBold = Num = NumBold = null;
            return "글꼴을 읽지 못해 Windows 글꼴로 표시합니다 / could not load the mockup fonts: " + ex.GetBaseException().Message;
        }
    }

    private static Font Open(string folder, string file, char probe)
    {
        var path = Path.Combine(folder, file);
        if (!File.Exists(path)) return null;
        // Font(path) loads a TrueType file as a dynamic font (Unity 2018.3+).
        var font = new Font(path) { hideFlags = HideFlags.HideAndDontSave };
        return font.dynamic && font.HasCharacter(probe) ? font : null;
    }
}
