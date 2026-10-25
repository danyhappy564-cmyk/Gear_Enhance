using System;
using System.Collections.Generic;
using UnityEngine;

namespace GearEnhance;

// 1.4.0 tab window (design 1, approved mockup): tabs 148 + window 760 = 908 × 980, the status
// panel 268 wide beside it. All positions are window coordinates; Unity scales the whole window.
public sealed partial class Plugin
{
    private bool UiButton(Rect rect, string text, GUIStyle style, UiInteraction cue = UiInteraction.Button)
    {
        if (InputLocked) { Locked(() => GUI.Button(rect, text, style)); return false; }
        if (!GUI.Button(rect, text, style)) return false;
        PlayInteraction(cue); return true;
    }
    private bool UiButton(Rect rect, GUIContent content, GUIStyle style)
    {
        if (InputLocked) { Locked(() => GUI.Button(rect, content, style)); return false; }
        if (!GUI.Button(rect, content, style)) return false;
        PlayInteraction(UiInteraction.Button); return true;
    }
    // Mockup checkbox (accent-color gold): a 16 px box, gold with a dark tick when checked.
    // `hit` widens the clickable area (the label next to the box clicks too, like <label for>).
    private bool UiToggle(Rect rect, bool current, string text, Rect? hit = null)
    {
        if (Event.current.type == EventType.Repaint)
        {
            var box = new Rect(rect.x + 1, rect.y + (rect.height - 16) / 2, 16, 16);
            var enabled = GUI.enabled && !InputLocked;
            var gold = enabled ? Gold : Hex(0x5f5236);
            if (current)
            {
                Fill(box, gold);
                var old = GUI.color; GUI.color = Hex(0x1a1408);
                GUI.DrawTexture(box, checkTexture);
                GUI.color = old;
            }
            else Box(box, enabled ? Hex(0x1b2025) : Hex(0x15191d), enabled ? Hex(0x767e86) : Hex(0x3a4148));
        }
        var area = hit ?? rect;
        if (InputLocked || !GUI.enabled) return current;
        if (!GUI.Button(area, GUIContent.none, card)) return current;
        PlayInteraction(UiInteraction.CheckBox);
        return !current;
    }
    // Drawn but inert under a popup.
    private static void Locked(Func<bool> draw)
    {
        var enabled = GUI.enabled;
        GUI.enabled = false;
        draw();
        GUI.enabled = enabled;
    }

    // Frame of the main window and the status panel beside it (design 1).
    private const float FrameWidth = 908, WindowHeight = 980, TabWidth = 148, HeaderHeight = 56;
    private const float StatusX = 924, StatusWidth = 268, WindowWidth = StatusX + StatusWidth;
    private const float BodyX = TabWidth + 20, BodyWidth = 720, ButtonY = 916, ButtonHeight = 48;

    // Mockup palette.
    private static readonly Color Gold = Hex(0xd9b36c), LightGold = Hex(0xf3e3bf), Text = Hex(0xe6e2d8), Muted = Hex(0x9aa1a8);
    private static readonly Color Soft = Hex(0xb9b3a6), Heading = Hex(0xd6d0c3), Purple = Hex(0xb49cff), Danger = Hex(0xff6b5e);
    private static readonly Color Cyan = Hex(0x4fd1c5), Good = Hex(0x8fd694), Blue = Hex(0x5fa8ff), Caution = Hex(0xffb26b);
    private static readonly Color Loss = Hex(0xff8a7e), Myth = Hex(0xff6ad5), MythGold = Hex(0xffd878), Locked0 = Hex(0x5d646b);
    private static readonly Color Line = Hex(0x2a3037), Edge = Hex(0x353c44), Panel = Hex(0x111417), TabColumn = Hex(0x0d1013);
    private static readonly Color HeaderFill = Hex(0x15191d), Section = Hex(0x13171b), Deep = Hex(0x0e1114), Inner = Hex(0x101316);
    private static readonly Color BarBack = Hex(0x22282e), BarNow = Hex(0x6b737c), BarBase = Hex(0xc4c0b6);
    private static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    private static Color StarTint(int stars) => stars >= 3 ? Hex(0xff6b6b) : stars == 2 ? Purple : Gold;
    private static Color TierColor(InsuranceTier tier) => tier == InsuranceTier.Basic ? Good
        : tier == InsuranceTier.Advanced ? Hex(0x7fb2ff) : tier == InsuranceTier.Premium ? Hex(0xffc46b) : Muted;
    // Rich text only wraps our own fixed strings and numbers, never server or item text.
    private static string Paint(string text, Color color) =>
        "<color=#" + ((int)(Mathf.Clamp01(color.r) * 255)).ToString("X2") + ((int)(Mathf.Clamp01(color.g) * 255)).ToString("X2")
        + ((int)(Mathf.Clamp01(color.b) * 255)).ToString("X2") + ">" + text + "</color>";
    private double StarMultiplier(int stars) => 1 + (stars >= 3 ? thirdStarBonus.Value : completionBonus.Value) / 100;

    private readonly List<Texture2D> skinTextures = new List<Texture2D>();
    private Texture2D previewGrid, ringTexture, checkTexture;
    private readonly Dictionary<EnhancePath, Texture2D> glyphs = new Dictionary<EnhancePath, Texture2D>();
    private static readonly EnhancePath[] WeaponTracks = { EnhancePath.Recoil, EnhancePath.Ergonomics, EnhancePath.Precision };
    private static readonly EnhancePath[] ArmorTracks = { EnhancePath.ArmorDurability, EnhancePath.Mobility };
    private GUI.WindowFunction drawWindow;
    private GUIStyle title, version, itemTitle, kindStyle, stars, starsExtra, mythMark, mythExtra, sub, body, bodyBold, bodyBoldCenter, small, smallRight, smallCenter, tiny, tinyRight;
    private GUIStyle number, numberBig, numberHuge, numberRight, numberCenter, costTotal, richSmall, richBody, richCenter, richSmallRight;
    private GUIStyle numberInline, numberSmallRight, costLine, costLineRight, optionLabel, materialName, stepCount, stepSign, bonusNumber, materialCost;
    private GUIStyle tabMark, tabLabel, tabSub, cardName, cardLevel, cardSub, popKicker, popTitle, popLine, wrap, wrapCenter;
    private GUIStyle card, button, buttonDark, buttonSmall, frame;
    private float UiScale => Mathf.Max(0.1f, Mathf.Min(1.1f,
        Mathf.Min((Screen.height - 64f) / WindowHeight, (Screen.width - 48f) / WindowWidth)));

    private void ResetWindow()
    {
        var scale = UiScale;
        window = new Rect(Math.Max(0, (Screen.width / scale - WindowWidth) / 2),
            Math.Max(0, (Screen.height / scale - WindowHeight) / 2), WindowWidth, WindowHeight);
    }

    private void EnsureSkin()
    {
        if (windowSkin != null) return;
        windowSkin = Instantiate(GUI.skin);
        drawWindow = DrawWindow;
        windowSkin.hideFlags = HideFlags.HideAndDontSave;
        // Mockup fonts (UiFonts): Noto Sans KR for text, Rajdhani for numbers. Without the files the
        // Windows fonts stay; Segoe UI Symbol covers ✦ ◆ ◈ there (dynamic font fallback).
        var fontNote = UiFonts.Load(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Info.Location) ?? "", "fonts"));
        if (fontNote != null) Logger.LogWarning(fontNote);
        else LogDetailed("강화 창 글꼴: GE Sans(Noto Sans KR) · GE Num(Rajdhani) 불러옴");
        uiFont = UiFonts.Sans ?? Font.CreateDynamicFontFromOSFont(
            new[] { "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "Segoe UI Symbol", "Arial Unicode MS" }, 16);
        if (uiFont != null) { uiFont.hideFlags = HideFlags.HideAndDontSave; windowSkin.font = uiFont; }
        // Sizes and weights are the mockup CSS ones (px = window units).
        title = TextStyle(26, LightGold, FontStyle.Bold, numeric: true);
        version = TextStyle(14, Muted, numeric: true);
        itemTitle = TextStyle(24, Text, FontStyle.Bold);
        kindStyle = TextStyle(13, Muted);
        stars = TextStyle(28, Gold); stars.richText = true;
        starsExtra = TextStyle(18, Cyan); starsExtra.richText = true;
        // User 2026-10-26: the regular-weight "✦ 신화" looked rough; mythic gear uses bold.
        mythMark = TextStyle(28, Myth, FontStyle.Bold);
        mythExtra = TextStyle(20, Myth, FontStyle.Bold, numeric: true);
        sub = TextStyle(13, Soft); sub.richText = true;
        body = TextStyle(14, Text); body.richText = true;
        bodyBold = TextStyle(13, Heading, FontStyle.Bold); bodyBold.richText = true;
        bodyBoldCenter = new GUIStyle(bodyBold) { alignment = TextAnchor.MiddleCenter };
        small = TextStyle(12, Muted); small.richText = true;
        smallRight = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
        smallCenter = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
        tiny = TextStyle(11, Muted); tiny.richText = true;
        tinyRight = new GUIStyle(tiny) { alignment = TextAnchor.MiddleRight };
        number = TextStyle(18, BarBase, FontStyle.Bold, TextAnchor.MiddleRight, true); number.richText = true;
        numberRight = TextStyle(15, Text, FontStyle.Bold, TextAnchor.MiddleRight, true);
        numberBig = TextStyle(34, LightGold, FontStyle.Bold, numeric: true);
        numberHuge = TextStyle(96, Myth, FontStyle.Bold, TextAnchor.MiddleCenter, true);
        numberCenter = TextStyle(40, Blue, FontStyle.Bold, TextAnchor.MiddleCenter, true);
        costTotal = TextStyle(26, LightGold, FontStyle.Bold, TextAnchor.MiddleRight, true);
        numberInline = TextStyle(16, Text, numeric: true);
        numberSmallRight = TextStyle(13, Hex(0xc9b07a), alignment: TextAnchor.MiddleRight, numeric: true);
        costLine = TextStyle(12, Muted); costLine.richText = true;
        costLineRight = new GUIStyle(costLine) { alignment = TextAnchor.MiddleRight };
        optionLabel = TextStyle(13, Text);
        materialName = TextStyle(14, Text, FontStyle.Bold);
        stepCount = TextStyle(18, Text, FontStyle.Bold, TextAnchor.MiddleCenter, true);
        stepSign = TextStyle(18, Text, alignment: TextAnchor.MiddleCenter);
        bonusNumber = TextStyle(18, Blue, FontStyle.Bold, numeric: true);
        materialCost = TextStyle(15, Text, alignment: TextAnchor.MiddleRight, numeric: true);
        richSmall = new GUIStyle(small);
        richSmallRight = new GUIStyle(smallRight);
        richBody = new GUIStyle(body);
        richCenter = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter };
        tabMark = TextStyle(13, Gold, FontStyle.Bold, TextAnchor.MiddleCenter);
        tabLabel = TextStyle(14, Hex(0xc9c4b8), FontStyle.Bold);
        tabSub = TextStyle(11, Muted);
        cardName = TextStyle(14, Text, FontStyle.Bold, TextAnchor.MiddleCenter);
        cardLevel = TextStyle(20, Gold, FontStyle.Bold, TextAnchor.MiddleCenter, true);
        cardSub = TextStyle(11, Muted, alignment: TextAnchor.MiddleCenter);
        popKicker = TextStyle(12, Muted, alignment: TextAnchor.MiddleCenter);
        popTitle = TextStyle(34, Text, FontStyle.Bold, TextAnchor.MiddleCenter);
        popLine = TextStyle(17, Text, alignment: TextAnchor.MiddleCenter); popLine.wordWrap = true; popLine.richText = true;
        wrap = TextStyle(14, Hex(0xc9c4b8)); wrap.wordWrap = true; wrap.alignment = TextAnchor.UpperLeft; wrap.richText = true;
        wrapCenter = TextStyle(13, Muted, alignment: TextAnchor.MiddleCenter); wrapCenter.wordWrap = true; wrapCenter.richText = true;
        card = new GUIStyle(windowSkin.button) { padding = new RectOffset(), margin = new RectOffset() };
        foreach (var state in new[] { card.normal, card.hover, card.active, card.focused,
            card.onNormal, card.onHover, card.onActive, card.onFocused }) state.background = null;
        button = new GUIStyle(card) { fontSize = 15, fontStyle = UiFonts.Loaded ? FontStyle.Normal : FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true };
        if (UiFonts.Loaded) button.font = UiFonts.SansBold;
        button.normal.textColor = button.hover.textColor = button.active.textColor = Hex(0x1a1408);
        buttonDark = new GUIStyle(button);
        buttonDark.normal.textColor = buttonDark.hover.textColor = buttonDark.active.textColor = Text;
        buttonSmall = new GUIStyle(buttonDark) { fontSize = 12, fontStyle = FontStyle.Normal };
        if (UiFonts.Loaded) buttonSmall.font = UiFonts.Sans;
        frame = new GUIStyle(windowSkin.window) { padding = new RectOffset(), border = new RectOffset() };
        foreach (var state in new[] { frame.normal, frame.hover, frame.active, frame.focused,
            frame.onNormal, frame.onHover, frame.onActive, frame.onFocused }) state.background = null;
        windowSkin.toggle.fontSize = 13;
        windowSkin.toggle.wordWrap = true;
        var gridPixels = new Color32[24 * 24];
        for (var pixel = 0; pixel < gridPixels.Length; pixel++)
            gridPixels[pixel] = new Color32(87, 100, 107, (byte)(pixel % 24 == 0 || pixel / 24 == 0 ? 14 : 0));
        previewGrid = new Texture2D(24, 24, TextureFormat.RGBA32, false)
            { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Repeat };
        previewGrid.SetPixels32(gridPixels); previewGrid.Apply(false, true); skinTextures.Add(previewGrid);
        // One ring for the mythic effect, made once (P2: no per-frame textures or rotations).
        const int ring = 128;
        var ringPixels = new Color32[ring * ring];
        for (var pixel = 0; pixel < ringPixels.Length; pixel++)
        {
            var dx = pixel % ring - (ring - 1) / 2f; var dy = pixel / ring - (ring - 1) / 2f;
            var distance = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - (ring / 2f - 6));
            ringPixels[pixel] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(1 - distance / 4f) * 255));
        }
        ringTexture = new Texture2D(ring, ring, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        ringTexture.SetPixels32(ringPixels); ringTexture.Apply(false, true); skinTextures.Add(ringTexture);
        checkTexture = MakeCheck(); skinTextures.Add(checkTexture);
        MakeIcons();
        foreach (EnhancePath path in Enum.GetValues(typeof(EnhancePath)))
        {
            var mask = UiGlyphs.Rasterize(path);
            var pixels = new Color32[mask.Length];
            for (var pixel = 0; pixel < pixels.Length; pixel++)
                pixels[(UiGlyphs.Size - 1 - pixel / UiGlyphs.Size) * UiGlyphs.Size + pixel % UiGlyphs.Size]
                    = new Color32(255, 255, 255, mask[pixel]);
            var texture = new Texture2D(UiGlyphs.Size, UiGlyphs.Size, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply(false, true);
            glyphs[path] = texture; skinTextures.Add(texture);
        }
    }

    // Mockup header/status icons (the SVG paths, 16 units) as 64 px stroke textures, made once.
    private Texture2D soundOnIcon, soundOffIcon, refreshIcon, closeIcon;
    private static Vector2[] Arc(Vector2 center, float radius, float from, float to, int steps = 16)
    {
        var points = new Vector2[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var angle = (from + (to - from) * i / steps) * Mathf.Deg2Rad;
            points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        return points;
    }
    // Polylines in SVG coordinates (y down) of a `units` box, stroked `width` units wide.
    private static Texture2D StrokeIcon(float units, float width, params Vector2[][] lines)
    {
        const int size = 64;
        var scale = size / units;
        var pixels = new Color32[size * size];
        for (var pixel = 0; pixel < pixels.Length; pixel++)
        {
            // Texture rows go up; SVG y goes down.
            var p = new Vector2(pixel % size + 0.5f, size - 1 - pixel / size + 0.5f) / scale;
            var distance = float.MaxValue;
            foreach (var line in lines)
                for (var i = 0; i + 1 < line.Length; i++) distance = Mathf.Min(distance, Segment(p, line[i], line[i + 1]));
            pixels[pixel] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01((width / 2 - distance) * scale + 0.5f) * 255));
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(pixels); texture.Apply(false, true);
        return texture;
    }
    private void MakeIcons()
    {
        Vector2 V(float x, float y) => new Vector2(x, y);
        var speaker = new[] { V(2, 6), V(5, 6), V(9, 3), V(9, 13), V(5, 10), V(2, 10), V(2, 6) };
        soundOnIcon = StrokeIcon(16, 1.6f, speaker, Arc(V(8.55f, 8), 3.5f, -45.6f, 45.6f), Arc(V(8.83f, 8), 6, -48.6f, 48.6f));
        soundOffIcon = StrokeIcon(16, 1.6f, speaker, new[] { V(11, 6), V(15, 10) }, new[] { V(15, 6), V(11, 10) });
        refreshIcon = StrokeIcon(16, 1.8f, Arc(V(8, 8), 5.5f, 0, 315, 24), new[] { V(12.5f, 1.5f), V(12.5f, 4.7f), V(9.3f, 4.7f) });
        closeIcon = StrokeIcon(14, 2, new[] { V(2, 2), V(12, 12) }, new[] { V(12, 2), V(2, 12) });
        skinTextures.Add(soundOnIcon); skinTextures.Add(soundOffIcon); skinTextures.Add(refreshIcon); skinTextures.Add(closeIcon);
    }
    private void IconButton(Rect rect, Texture2D icon, float iconSize, Action action, UiInteraction cue = UiInteraction.Button)
    {
        MiniButton(rect, "", action, cue);
        if (Event.current.type != EventType.Repaint) return;
        var old = GUI.color; GUI.color = GUI.enabled ? Text : Hex(0x4b5259);
        GUI.DrawTexture(new Rect(rect.center.x - iconSize / 2, rect.center.y - iconSize / 2, iconSize, iconSize), icon);
        GUI.color = old;
    }

    // Tick of the checkbox, 32 px (drawn at 16): two strokes with soft edges, made once.
    private static Texture2D MakeCheck()
    {
        const int size = 32;
        var points = new[] { new Vector2(7, 16.5f), new Vector2(13.5f, 23), new Vector2(25.5f, 9) };
        var pixels = new Color32[size * size];
        for (var pixel = 0; pixel < pixels.Length; pixel++)
        {
            var p = new Vector2(pixel % size + 0.5f, size - 1 - pixel / size + 0.5f);
            var distance = Mathf.Min(Segment(p, points[0], points[1]), Segment(p, points[1], points[2]));
            pixels[pixel] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(2.8f - distance) * 255));
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(pixels); texture.Apply(false, true);
        return texture;
    }
    private static float Segment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    // `numeric`: the mockup's .num class (Rajdhani). With the font files, weight picks the file
    // (no synthetic bold); without them the Windows font is bolded by Unity as before.
    private GUIStyle TextStyle(int size, Color color, FontStyle weight = FontStyle.Normal,
        TextAnchor alignment = TextAnchor.MiddleLeft, bool numeric = false)
    {
        var bold = weight == FontStyle.Bold;
        var style = new GUIStyle(windowSkin.label)
        {
            font = !UiFonts.Loaded ? null : numeric ? bold ? UiFonts.NumBold : UiFonts.Num : bold ? UiFonts.SansBold : UiFonts.Sans,
            fontSize = size, fontStyle = UiFonts.Loaded ? FontStyle.Normal : weight, alignment = alignment,
            padding = new RectOffset(), margin = new RectOffset(),
            wordWrap = false, richText = false, clipping = TextClipping.Clip
        };
        style.normal.textColor = color;
        return style;
    }
    private static void Fill(Rect rect, Color color)
    {
        if (Event.current.type != EventType.Repaint || rect.width <= 0 || rect.height <= 0) return;
        var old = GUI.color; GUI.color = new Color(color.r, color.g, color.b, color.a * old.a);
        GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old;
    }
    private static void Box(Rect rect, Color fill, Color border)
    {
        Fill(rect, border); Fill(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), fill);
    }
    private static void Outline(Rect rect, Color color, float width = 1)
    {
        Fill(new Rect(rect.x, rect.y, rect.width, width), color); Fill(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
        Fill(new Rect(rect.x, rect.y, width, rect.height), color); Fill(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
    }
    private void Label(float x, float y, float width, float height, string text, GUIStyle style)
    {
        // Labels retain their control IDs on every event; Unity draws them only on Repaint.
        GUI.Label(new Rect(x, y, width, height), text, style);
    }
    // GUI.color would multiply the style's own color; swap the style's color for this one label.
    private void Label(float x, float y, float width, float height, string text, GUIStyle style, Color color)
    {
        var old = style.normal.textColor;
        style.normal.textColor = color;
        GUI.Label(new Rect(x, y, width, height), text, style);
        style.normal.textColor = old;
    }
    // Two-layer bar (mockup .bar): the grey "now" part over the blue "after" part, both filling in.
    // `key` names the bar so a changed value slides there (Tween) instead of jumping.
    // `fillNext` < 0 fills both parts together; the effect rows fill the blue part later (mockup fillB).
    private void Bar(Rect rect, double now, double next, double cap, Color nowColor, Color nextColor, float fill = 1, string key = null, float fillNext = -1)
    {
        Fill(rect, BarBack);
        if (cap <= 0) return;
        float Share(double amount) => (float)Math.Max(0, Math.Min(1, amount / cap));
        var nextShare = key == null ? Share(next) : Tween(key + ".next", Share(next));
        var nowShare = key == null ? Share(now) : Tween(key + ".now", Share(now));
        Fill(new Rect(rect.x, rect.y, rect.width * nextShare * (fillNext < 0 ? fill : fillNext), rect.height), nextColor);
        Fill(new Rect(rect.x, rect.y, rect.width * nowShare * fill, rect.height), nowColor);
    }

    // Values that ease to a new target over 0.35 s (mockup `.bar > div { transition: width .35s }`).
    // ReplayMotion clears them, so a new tab or item fills from the left again instead.
    private readonly Dictionary<string, (float From, float To, double Start)> tweens = new Dictionary<string, (float, float, double)>();
    private float Tween(string key, float target)
    {
        if (Reduced) return target;
        if (!tweens.TryGetValue(key, out var tween)) { tweens[key] = (target, target, Clock); return target; }
        if (Math.Abs(tween.To - target) > 1e-4f) tweens[key] = tween = (TweenValue(tween), target, Clock);
        return TweenValue(tween);
    }
    private static float TweenValue((float From, float To, double Start) tween)
    {
        var t = (float)UiMotion.Clamp((Clock - tween.Start) / 0.35);
        return tween.From + (tween.To - tween.From) * (1 - (1 - t) * (1 - t) * (1 - t));
    }

    private void OnGUI()
    {
        if (!visible) return;
        var started = Event.current.type == EventType.Repaint ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        EnsureSkin();
        var matrix = GUI.matrix; var skin = GUI.skin; var depth = GUI.depth;
        var active = GUI.enabled; var color = GUI.color;
        try
        {
            GUI.depth = -1000; GUI.skin = windowSkin; GUI.color = Color.white;
            GUI.matrix = Matrix4x4.identity;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.42f));
            var scale = UiScale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            window.width = WindowWidth; window.height = WindowHeight;
            window.x = Mathf.Clamp(window.x, 0, Math.Max(0, Screen.width / scale - WindowWidth));
            window.y = Mathf.Clamp(window.y, 0, Math.Max(0, Screen.height / scale - WindowHeight));
            window = GUI.Window(GetInstanceID(), window, drawWindow, "", frame);
        }
        finally
        {
            GUI.matrix = matrix; GUI.skin = skin; GUI.depth = depth; GUI.enabled = active; GUI.color = color;
            if (started != 0)
            {
                var elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - started;
                repaintCount++; repaintTicks += elapsed;
                maximumRepaintTicks = Math.Max(maximumRepaintTicks, elapsed);
            }
        }
    }

    private void DrawWindow(int id)
    {
        Box(new Rect(0, 0, FrameWidth, WindowHeight), Panel, Edge);
        DrawTabs();
        DrawHeader();
        DrawItemCard();
        switch (tab)
        {
            case UiTab.Insurance: DrawInsuranceTab(); break;
            case UiTab.Help: DrawHelpTab(); break;
            case UiTab.Mythic when MythicLevel < 0: DrawMysteryTab(); break;
            default: DrawUpgradeTab(); break;
        }
        DrawStatusPanel();
        if (popup != PopupKind.None) DrawPopup();
        if (popup == PopupKind.None) GUI.DragWindow(new Rect(TabWidth, 0, 420, HeaderHeight));
    }

    private void DrawTabs()
    {
        Fill(new Rect(1, 1, TabWidth - 1, WindowHeight - 2), TabColumn);
        Fill(new Rect(TabWidth, 1, 1, WindowHeight - 2), Line);
        Fill(new Rect(1, HeaderHeight, TabWidth - 1, 1), Line);
        Label(0, 0, TabWidth, HeaderHeight, Loc.L("목록", "MENU"), smallCenter, Muted);
        var y = HeaderHeight + 1;
        foreach (var entry in TabEntries())
        {
            var rect = new Rect(1, y, TabWidth - 1, 58);
            var selected = entry.Tab == tab;
            var open = entry.Open && itemId != null;
            GUI.enabled = open && !busy && popup == PopupKind.None && !uncertain;
            if (selected) Fill(rect, Hex(0x2a2416));
            else if (GUI.enabled && rect.Contains(Event.current.mousePosition)) Fill(rect, Hex(0x171b20));
            if (entry.Ring.a > 0) Outline(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), entry.Ring, 2);
            Fill(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Hex(0x22282e));
            if (UiButton(rect, GUIContent.none, card)) { var picked = entry.Tab; main.Enqueue(() => SelectTab(picked)); }
            GUI.enabled = true;
            Label(rect.x + 8, rect.y, 34, rect.height, entry.Mark, tabMark, open ? entry.Color : Locked0);
            Label(rect.x + 46, rect.y + 11, rect.width - 50, 18, entry.Label, tabLabel, !open ? Locked0 : selected ? LightGold : Hex(0xc9c4b8));
            Label(rect.x + 46, rect.y + 31, rect.width - 50, 16, entry.Sub, tabSub, !open ? Locked0 : selected ? Hex(0xcdb88c) : Muted);
            y += 58;
        }
    }

    private void DrawHeader()
    {
        var x = TabWidth + 1;
        Fill(new Rect(x, 1, FrameWidth - x - 1, HeaderHeight - 1), HeaderFill);
        Fill(new Rect(x, HeaderHeight, FrameWidth - x - 1, 1), Line);
        Label(BodyX, 8, 190, 40, "GEAR Enhance", title);
        Label(BodyX + 182, 14, 80, 30, "v" + EnhanceProtocol.ModVersion, version);
        GUI.enabled = popup == PopupKind.None && !busy;
        // Language: the window switches at once (design 1); Auto stays until a choice is made.
        var english = Loc.English;
        var korean = new Rect(588, 12, 56, 32);
        var en = new Rect(644, 12, 56, 32);
        Box(new Rect(587, 11, 114, 34), Hex(0x15191d), Hex(0x333a42));
        if (!english) Fill(korean, Hex(0x2a2416)); else Fill(en, Hex(0x2a2416));
        if (UiButton(korean, "", card)) main.Enqueue(() => language.Value = UiLanguage.Korean);
        if (UiButton(en, "", card)) main.Enqueue(() => language.Value = UiLanguage.English);
        Label(korean.x, korean.y, korean.width, korean.height, "한국어", smallCenter, english ? Muted : LightGold);
        Label(en.x, en.y, en.width, en.height, "EN", smallCenter, english ? LightGold : Muted);
        // Mockup: a speaker (with waves when on, crossed when off), 44 px wide.
        IconButton(new Rect(724, 12, 40, 32), soundsEnabled.Value ? soundOnIcon : soundOffIcon, 16, () => soundsEnabled.Value = !soundsEnabled.Value);
        if (!uncertain && itemId != null) MiniButton(new Rect(772, 12, 84, 32), Loc.L("새로고침", "Refresh"), () => { ReplayMotion(); RefreshTab(); });
        GUI.enabled = popup == PopupKind.None && !attemptTimeline.Active;
        IconButton(new Rect(864, 12, 36, 32), closeIcon, 14, Close, UiInteraction.Close);
        GUI.enabled = true;
    }

    private void MiniButton(Rect rect, string text, Action action, UiInteraction cue = UiInteraction.Button)
    {
        var hovered = GUI.enabled && rect.Contains(Event.current.mousePosition);
        Box(rect, !GUI.enabled ? HeaderFill : hovered ? Hex(0x252b32) : Hex(0x1b2025), Hex(0x333a42));
        if (UiButton(rect, "", card, cue)) main.Enqueue(action);
        Label(rect.x, rect.y, rect.width, rect.height, text, smallCenter, GUI.enabled ? Text : Hex(0x4b5259));
    }

    // Big button of the bottom row (mockup .btn): sec (dark), act (gold), fast (blue), danger (pink).
    private enum ButtonKind { Dark, Gold, Fast, Danger }
    private bool BigButton(Rect rect, string text, ButtonKind kind, UiInteraction cue = UiInteraction.Button)
    {
        var hovered = GUI.enabled && rect.Contains(Event.current.mousePosition);
        Color fill, border, ink;
        if (!GUI.enabled) { fill = Hex(0x1e2226); border = Line; ink = Hex(0x7b838b); }
        else switch (kind)
        {
            case ButtonKind.Gold: fill = hovered ? Hex(0xe6c47f) : Gold; border = fill; ink = Hex(0x1a1408); break;
            case ButtonKind.Fast: fill = hovered ? Hex(0x233447) : Hex(0x1d2a38); border = Hex(0x3b5f8a); ink = Hex(0xcfe4ff); break;
            case ButtonKind.Danger: fill = hovered ? Hex(0xbd3b6b) : Hex(0xa8325e); border = fill; ink = Color.white; break;
            default: fill = hovered ? Hex(0x222830) : Hex(0x1b2025); border = Hex(0x333a42); ink = Text; break;
        }
        Box(rect, fill, border);
        var pressed = UiButton(rect, "", card, cue);
        Label(rect.x + 6, rect.y, rect.width - 12, rect.height, text, button, ink);
        return pressed;
    }

    // Gear card (design 1-2): picture │ name, stars or ✦ Mythic, levels.
    private void DrawItemCard()
    {
        var area = new Rect(BodyX, 70, BodyWidth, 100);
        Box(area, Deep, Line);
        if (Event.current.type == EventType.Repaint)
            GUI.DrawTextureWithTexCoords(new Rect(area.x + 1, area.y + 1, 299, 98), previewGrid, new Rect(0, 0, 299 / 24f, 98 / 24f));
        var picture = new Rect(area.x + 20, area.y + 8, 260, 84);
        var sprite = GameBridge.Get(itemIcon, "Sprite") as Sprite;
        var shattering = popup == PopupKind.Result && resultCue == FeedbackCue.Destroy;
        if (!shattering && sprite != null && sprite.texture != null) DrawSprite(sprite, picture, 1);
        if (!shattering && (sprite == null || sprite.texture == null))
            Label(picture.x, picture.y, picture.width, picture.height, itemId == null ? Loc.L("장비가 파괴되었습니다", "Gear destroyed")
                : Loc.L("장비 이미지를 불러오는 중…", "Loading gear image…"), smallCenter);
        Fill(new Rect(area.x + 300, area.y + 16, 1, 68), Edge);
        var x = area.x + 321;
        var w = area.width - 330;
        var gearName = itemName ?? Loc.L("장비", "Gear");
        Label(x, area.y + 8, w, 32, gearName, itemTitle);
        var width = itemTitle.CalcSize(new GUIContent(gearName)).x;
        Label(x + Math.Min(width, w - 80) + 10, area.y + 14, 200, 24, KindLine(), kindStyle);
        var id = itemId ?? "";
        var level = StatPatches.Registry.StarLevel(id);
        var mythic = MythicLevel;
        string mark, extra = "";
        Color markColor, extraColor = Cyan;
        if (itemId == null) { mark = Loc.L("파괴됨", "Destroyed"); markColor = Danger; }
        else if (mythic >= 0) { mark = Loc.L("✦ 신화", "✦ Mythic"); markColor = Myth; extra = StageText.Of(mythic, MythicRules.MaxLevel); extraColor = Myth; }
        else
        {
            var total = StatPatches.Registry.TotalLevel(id);
            mark = level >= 2 ? StarEvolution.Stars(level) : "★" + (total > 0 ? $" +{total}" : "");
            markColor = StarTint(level);
            if (StatPatches.Registry.TranscendTotal(id) > 0) extra = Loc.L("초월 ", "Transcend ") + TranscendPair(id);
        }
        var markStyle = mythic >= 0 && itemId != null ? mythMark : stars;
        var extraStyle = mythic >= 0 && itemId != null ? mythExtra : starsExtra;
        var markWidth = markStyle.CalcSize(new GUIContent(mark)).x;
        Label(x, area.y + 40, w, 34, mark, markStyle, markColor);
        if (extra.Length > 0) Label(x + markWidth + 10, area.y + 45, w - markWidth - 10, 26, extra, extraStyle, extraColor);
        Label(x, area.y + 74, w, 20, HeroLine(id), sub);
    }

    // "a/5 · b/5" of the 2-star and 3-star transcendence tracks.
    private static string TranscendPair(string id) =>
        StageText.Of(StatPatches.Registry.TranscendLevel(id, TranscendTrack.Second), TranscendRules.MaxLevel) + " · " + StageText.Of(StatPatches.Registry.TranscendLevel(id, TranscendTrack.Third), TranscendRules.MaxLevel);

    // Beside the name (mockup): the caliber of a weapon, "방어구 · 방탄판 N장" for armor.
    private string KindLine()
    {
        if (!weapon) return Loc.L("방어구", "Armor") + (preview?.InsertedParts > 0 ? Loc.L($" · 방탄판 {preview.InsertedParts}장", $" · {preview.InsertedParts} plates") : "");
        try { return CaliberText.Name(GameBridge.Get(selectedItem, "AmmoCaliber")?.ToString()) ?? Loc.L("무기", "Weapon"); }
        catch (Exception) { return Loc.L("무기", "Weapon"); }
    }

    // The line under the stars follows the tab, like the mockup's hero line.
    private string HeroLine(string id)
    {
        var tracks = weapon ? WeaponTracks : ArmorTracks;
        var parts = new List<string>();
        foreach (var path in tracks) parts.Add(EnhanceEffects.Name(path) + " +" + (StatPatches.Registry.Find(id, path)?.Level ?? 0));
        var levels = string.Join(" · ", parts);
        var stars = StarCount;
        switch (tab)
        {
            case UiTab.Star2:
            case UiTab.Star3:
                return stars >= EvolutionStars ? levels + Loc.L($" · {StarEvolution.Stars(EvolutionStars)} 완료", $" · {StarEvolution.Stars(EvolutionStars)} done")
                    : StatPatches.Registry.IsComplete(id) && stars == EvolutionStars - 1 ? levels + Loc.L($" · {StarEvolution.Stars(EvolutionStars)} 도전 가능", $" · {StarEvolution.Stars(EvolutionStars)} ready") : levels;
            case UiTab.Transcend:
                return Loc.L($"2성 초월 {StageText.Of(StatPatches.Registry.TranscendLevel(id, TranscendTrack.Second), TranscendRules.MaxLevel)} · 3성 초월 {StageText.Of(StatPatches.Registry.TranscendLevel(id, TranscendTrack.Third), TranscendRules.MaxLevel)}",
                    $"2★ track {StageText.Of(StatPatches.Registry.TranscendLevel(id, TranscendTrack.Second), TranscendRules.MaxLevel)} · 3★ track {StageText.Of(StatPatches.Registry.TranscendLevel(id, TranscendTrack.Third), TranscendRules.MaxLevel)}");
            case UiTab.Mythic when MythicLevel < 0:
                return stars >= 3 ? Loc.L("★★★ 달성 · ??? 도전 가능", "★★★ reached · ??? open") : levels;
            case UiTab.Mythic:
            {
                var tuning = StatPatches.Registry.MythicTuning;
                var head = Loc.L("신화 ", "Mythic ") + StageText.Of(MythicLevel, MythicRules.MaxLevel);
                if (weapon) return head + Loc.L($" · 탄 데미지·관통력 단계당 +{tuning.WeaponPercentPerLevel:0.#}% · 비용 단계마다 ×{tuning.CostGrowth:0.##}",
                    $" · damage & penetration +{tuning.WeaponPercentPerLevel:0.#}% per level · cost ×{tuning.CostGrowth:0.##} per level");
                var zones = new List<string>();
                foreach (var zone in StatPatches.Registry.MythicZones(id)) zones.Add(MythicText.ZoneName(zone));
                return head + Loc.L($" · 받는 피해 단계당 -{tuning.ArmorPercentPerLevel:0.#}%(머리 절반)", $" · damage taken -{tuning.ArmorPercentPerLevel:0.#}% per level (half on the head)")
                    + (zones.Count > 0 ? Loc.L(" · 추가 보호 부위 ", " · extra zones ") + string.Join(" · ", zones) : "");
            }
            default: return levels;
        }
    }

    private static void DrawSprite(Sprite sprite, Rect area, float alpha)
    {
        if (Event.current.type != EventType.Repaint) return;
        var source = sprite.rect;
        var fit = Math.Min(area.width / source.width, area.height / source.height);
        var rect = new Rect(area.center.x - source.width * fit / 2, area.center.y - source.height * fit / 2, source.width * fit, source.height * fit);
        var old = GUI.color; GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);
        GUI.DrawTextureWithTexCoords(rect, sprite.texture, new Rect(source.x / sprite.texture.width,
            source.y / sprite.texture.height, source.width / sprite.texture.width, source.height / sprite.texture.height));
        GUI.color = old;
    }

    private void DrawPathIcon(Vector2 center, EnhancePath path, Color color, float size = 32)
    {
        if (Event.current.type != EventType.Repaint) return;
        var previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(center.x - size / 2, center.y - size / 2, size, size), glyphs[path]);
        GUI.color = previous;
    }

    // Line with a label in the middle (mockup "강화 목록").
    private void SectionTitle(float y, string text, Color border, Color ink)
    {
        var width = bodyBoldCenter.CalcSize(new GUIContent(text)).x + 34;
        var x = (midWidth - width) / 2;
        Fill(new Rect(0, y + 11, x - 12, 1), Line);
        Fill(new Rect(x + width + 12, y + 11, midWidth - x - width - 12, 1), Line);
        Outline(new Rect(x, y, width, 24), border);
        Label(x, y, width, 24, text, bodyBoldCenter, ink);
    }
}
