using System;
using System.Collections.Generic;
using UnityEngine;

namespace GearEnhance;

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
    private bool UiToggle(Rect rect, bool current, string text)
    {
        if (InputLocked) { Locked(() => GUI.Toggle(rect, current, text)); return current; }
        var selected = GUI.Toggle(rect, current, text);
        if (selected != current) PlayInteraction(UiInteraction.CheckBox);
        return selected;
    }
    // Drawn but inert under the insurance overlay.
    private static void Locked(Func<bool> draw)
    {
        var enabled = GUI.enabled;
        GUI.enabled = false;
        draw();
        GUI.enabled = enabled;
    }
    private const float WindowWidth = 760, WindowHeight = 950;
    private static readonly Color Gold = new Color(0.84f, 0.74f, 0.55f);
    private double StarMultiplier(int stars) => 1 + (stars >= 3 ? thirdStarBonus.Value : completionBonus.Value) / 100;
    private static Color StarTint(int stars) => stars >= 3 ? new Color(0.94f, 0.34f, 0.34f) : Purple;
    private static readonly Color Purple = new Color(0.73f, 0.54f, 0.96f);
    private static readonly Color Danger = new Color(0.94f, 0.30f, 0.24f);
    private static readonly Color Muted = new Color(0.62f, 0.65f, 0.67f);
    private static readonly Color Line = new Color(0.25f, 0.28f, 0.30f);
    private static readonly Color Panel = new Color(0.045f, 0.055f, 0.065f, 0.98f);
    private readonly List<Texture2D> skinTextures = new List<Texture2D>();
    private Texture2D panelGradient, headerGradient, selectedGradient, cardGradient, goldGradient, previewGrid;
    private readonly Dictionary<EnhancePath, Texture2D> glyphs = new Dictionary<EnhancePath, Texture2D>();
    private static readonly EnhancePath[] WeaponTracks = { EnhancePath.Recoil, EnhancePath.Ergonomics, EnhancePath.Precision };
    private static readonly EnhancePath[] ArmorTracks = { EnhancePath.ArmorDurability, EnhancePath.Mobility };
    private GUIStyle noteStyle, statusStyle, noteStyleCentered;
    private GUIStyle resultTitleStyle, resultDetailStyle;
    private GUI.WindowFunction drawWindow, drawEvolutionWindow;
    private float ActiveWindowHeight => evolutionMode ? 910 : WindowHeight;
    private GUIStyle heading, body, small, centeredSmall, pathHeading, large, value, selectedValue, chanceValue, costValue;
    private GUIStyle card, primary, secondary, frame;
    private float UiScale => Mathf.Max(0.1f, Mathf.Min(1.1f,
        Mathf.Min((Screen.height - 64f) / ActiveWindowHeight, (Screen.width - 48f) / WindowWidth)));

    private void ResetWindow()
    {
        var scale = UiScale;
        window = new Rect(Math.Max(0, (Screen.width / scale - WindowWidth) / 2),
            Math.Max(0, (Screen.height / scale - ActiveWindowHeight) / 2), WindowWidth, ActiveWindowHeight);
    }

    private void EnsureSkin()
    {
        if (windowSkin != null) return;
        windowSkin = Instantiate(GUI.skin);
        drawWindow = DrawWindow;
        drawEvolutionWindow = DrawEvolutionWindow;
        windowSkin.hideFlags = HideFlags.HideAndDontSave;
        uiFont = Font.CreateDynamicFontFromOSFont(
            new[] { "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "Arial Unicode MS" }, 16);
        if (uiFont != null) { uiFont.hideFlags = HideFlags.HideAndDontSave; windowSkin.font = uiFont; }
        heading = TextStyle(24, Color.white, FontStyle.Bold);
        body = TextStyle(17, new Color(0.88f, 0.89f, 0.90f));
        small = TextStyle(13, Muted);
        centeredSmall = TextStyle(13, Muted, alignment: TextAnchor.MiddleCenter);
        noteStyle = TextStyle(13, Gold); noteStyle.wordWrap = true;
        noteStyleCentered = TextStyle(17, Gold, FontStyle.Bold, TextAnchor.MiddleCenter);
        statusStyle = TextStyle(13, Muted); statusStyle.wordWrap = true;
        resultTitleStyle = TextStyle(40, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
        resultDetailStyle = TextStyle(17, Color.white, alignment: TextAnchor.MiddleCenter); resultDetailStyle.wordWrap = true;
        pathHeading = TextStyle(16, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
        large = TextStyle(39, Gold, FontStyle.Bold);
        value = TextStyle(26, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
        selectedValue = TextStyle(26, Gold, FontStyle.Bold, TextAnchor.MiddleCenter);
        chanceValue = TextStyle(35, Color.white, FontStyle.Bold);
        costValue = TextStyle(29, Color.white, FontStyle.Bold);
        panelGradient = Gradient(new Color(0.075f, 0.085f, 0.095f, 0.98f), Panel);
        headerGradient = Gradient(new Color(0.16f, 0.17f, 0.18f), new Color(0.075f, 0.085f, 0.095f));
        cardGradient = Gradient(new Color(0.12f, 0.135f, 0.15f), new Color(0.055f, 0.065f, 0.075f));
        selectedGradient = Gradient(new Color(0.20f, 0.19f, 0.16f), new Color(0.10f, 0.105f, 0.105f));
        goldGradient = Gradient(new Color(0.88f, 0.78f, 0.59f), new Color(0.66f, 0.55f, 0.37f));
        card = new GUIStyle(windowSkin.button) { padding = new RectOffset(), margin = new RectOffset() };
        foreach (var state in new[] { card.normal, card.hover, card.active, card.focused,
            card.onNormal, card.onHover, card.onActive, card.onFocused }) state.background = null;
        secondary = new GUIStyle(card) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        secondary.normal.textColor = secondary.hover.textColor = secondary.active.textColor = Color.white;
        primary = new GUIStyle(secondary) { fontSize = 20 };
        primary.normal.textColor = primary.hover.textColor = primary.active.textColor = new Color(0.055f, 0.05f, 0.04f);
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

    private Texture2D Gradient(Color top, Color bottom)
    {
        var texture = new Texture2D(1, 64, TextureFormat.RGBA32, false)
            { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        var colors = new Color[64];
        for (var row = 0; row < colors.Length; row++) colors[row] = Color.Lerp(bottom, top, row / 63f);
        texture.SetPixels(colors); texture.Apply(false, true); skinTextures.Add(texture);
        return texture;
    }

    private GUIStyle TextStyle(int size, Color color, FontStyle weight = FontStyle.Normal,
        TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var style = new GUIStyle(windowSkin.label)
        {
            fontSize = size, fontStyle = weight, alignment = alignment,
            padding = new RectOffset(), margin = new RectOffset(),
            wordWrap = false, richText = false, clipping = TextClipping.Clip
        };
        style.normal.textColor = color;
        return style;
    }
    private static void Fill(Rect rect, Color color)
    {
        if (Event.current.type != EventType.Repaint) return;
        var old = GUI.color; GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old;
    }
    private static void Box(Rect rect, Color fill, Color border)
    {
        Fill(rect, border); Fill(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), fill);
    }
    private void Surface(Rect rect, Texture2D texture, Color border)
    {
        if (Event.current.type != EventType.Repaint) return;
        Fill(rect, border);
        GUI.DrawTexture(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), texture);
    }
    private void Label(float x, float y, float width, float height, string text, GUIStyle style)
    {
        // Labels retain their control IDs on every event; Unity draws them only on Repaint.
        GUI.Label(new Rect(x, y, width, height), text, style);
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
            window.x = Mathf.Clamp(window.x, 0, Math.Max(0, Screen.width / scale - WindowWidth));
            window.y = Mathf.Clamp(window.y, 0, Math.Max(0, Screen.height / scale - ActiveWindowHeight));
            window = GUI.Window(GetInstanceID() + (evolutionMode ? 1 : 0), window, evolutionMode ? drawEvolutionWindow : drawWindow, "", frame);
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
        Surface(new Rect(0, 0, WindowWidth, WindowHeight), panelGradient, Line);
        if (Event.current.type == EventType.Repaint)
            GUI.DrawTexture(new Rect(1, 1, WindowWidth - 2, 53), headerGradient);
        Fill(new Rect(1, 54, WindowWidth - 2, 1), Line);
        // Small metal corner accents follow the approved mockup's frame.
        var accent = new Color(0.55f, 0.58f, 0.59f);
        Fill(new Rect(0, 0, 14, 1), accent); Fill(new Rect(0, 0, 1, 14), accent);
        Fill(new Rect(WindowWidth - 14, 0, 14, 1), accent); Fill(new Rect(WindowWidth - 1, 0, 1, 14), accent);
        Fill(new Rect(0, WindowHeight - 1, 14, 1), accent); Fill(new Rect(0, WindowHeight - 14, 1, 14), accent);
        Fill(new Rect(WindowWidth - 14, WindowHeight - 1, 14, 1), accent);
        Fill(new Rect(WindowWidth - 1, WindowHeight - 14, 1, 14), accent);
        Label(23, 9, 200, 36, Loc.L("장비 강화", "Enhance"), heading);
        Label(162, 16, 190, 24, "Gear Enhance · " + EnhanceProtocol.ModVersion, small);
        GUI.enabled = !busy && !resultVisible;
        if (!uncertain && itemId != null && UiButton(new Rect(608, 13, 92, 29), Loc.L("새로고침", "Refresh"), secondary)) main.Enqueue(RefreshQuote);
        if (!uncertain && itemId != null && UiButton(new Rect(486, 13, 112, 29), Loc.L("보험·방탄판", "Insure·plates"), secondary, UiInteraction.Open)) main.Enqueue(OpenInsurance);
        if (UiButton(new Rect(712, 10, 32, 32), "×", secondary, UiInteraction.Close)) main.Enqueue(Close);
        GUI.enabled = true;

        DrawPreview();
        Label(24, 239, 300, 29, Loc.L("강화 항목 · 각각 강화 가능", "Tracks · upgraded separately"), body);
        var options = weapon ? WeaponTracks : ArmorTracks;
        var width = (712f - (options.Length - 1) * 12) / options.Length;
        for (var index = 0; index < options.Length; index++)
            DrawPath(new Rect(24 + index * (width + 12), 276, width, 76), options[index]);
        DrawEffects(); DrawCosts(); DrawProtection(); DrawActions();
        if (resultVisible) DrawResultPanel();
        else if (insuranceVisible) DrawInsurancePanel();
        if (!resultVisible) GUI.DragWindow(new Rect(0, 0, 480, 54));
    }

    private void DrawPreview()
    {
        if (Event.current.type == EventType.Repaint)
            GUI.DrawTextureWithTexCoords(new Rect(24, 62, 480, 166), previewGrid, new Rect(0, 0, 20, 166f / 24));
        var area = new Rect(38, 69, 449, 151);
        var sprite = GameBridge.Get(itemIcon, "Sprite") as Sprite;
        var shattering = resultCue == FeedbackCue.Destroy && Clock - resultStarted < ResultDuration;
        if (!shattering && sprite != null && sprite.texture != null && Event.current.type == EventType.Repaint)
        {
            var source = sprite.rect;
            var fit = Math.Min(area.width / source.width, area.height / source.height);
            var rect = new Rect(area.center.x - source.width * fit / 2, area.center.y - source.height * fit / 2,
                source.width * fit, source.height * fit);
            GUI.DrawTextureWithTexCoords(rect, sprite.texture, new Rect(source.x / sprite.texture.width,
                source.y / sprite.texture.height, source.width / sprite.texture.width, source.height / sprite.texture.height));
        }
        if (!shattering && (sprite == null || sprite.texture == null))
            Label(62, 127, 380, 30, itemId == null ? Loc.L("장비가 파괴되었습니다", "Gear destroyed") : Loc.L("장비 이미지를 불러오는 중…", "Loading gear image…"), small);
        DrawResultEffect(area);
        Label(517, 76, 217, 38, itemName ?? Loc.L("장비", "Gear"), heading);
        Label(518, 112, 216, 25, evolutionMode ? Loc.L($"{evolutionTargetStars}차 스타 진화", $"{Loc.Tier(evolutionTargetStars)} star evolution")
            : EnhanceEffects.Name(selectedPath) + Loc.L(" 강화", " upgrade"), small);
        var current = preview?.CurrentLevel ?? StatPatches.Registry.Find(itemId ?? "", selectedPath)?.Level ?? 0;
        var next = preview?.NextLevel ?? Math.Max(current, Math.Min(current + 1, maxLevel.Value));
        var stars = StatPatches.Registry.StarLevel(itemId ?? "");
        var finishedEvolution = stars >= evolutionTargetStars;
        Label(513, 145, 225, 64, itemId == null ? Loc.L("파괴됨", "Destroyed") : evolutionMode
            ? finishedEvolution ? StarEvolution.Stars(stars) : $"{StarEvolution.Stars(stars)} → {StarEvolution.Stars(evolutionTargetStars)}"
            : $"+{current} → +{next}", evolutionMode || current >= 100 || next >= 100 ? selectedValue : large);
        var complete = stars >= 2;
        Label(517, 207, 217, 22, itemId == null ? Loc.L("본체·부착물·탄약 소실", "Gear, mods and ammo lost") : evolutionMode && !finishedEvolution
            ? Loc.L($"진화 성공 시 효과 ×{StarMultiplier(evolutionTargetStars):0.##}", $"On success: effect ×{StarMultiplier(evolutionTargetStars):0.##}") : complete
            ? Loc.L($"{StarEvolution.Stars(stars)} 진화 완료 · 효과 ×{StarMultiplier(stars):0.##}", $"{StarEvolution.Stars(stars)} evolved · effect ×{StarMultiplier(stars):0.##}")
            : Loc.L("1차 스타 완성 후 진화 가능", "Evolve after maxing 1st star"), small);
        if (complete) Fill(new Rect(517, 230, 217, 2), StarTint(stars));
        Fill(new Rect(24, 231, 712, 1), Line);
    }

    private void DrawPath(Rect rect, EnhancePath path)
    {
        var selected = path == selectedPath && !evolutionMode;
        GUI.enabled = !evolutionMode && !busy && !resultVisible && !uncertain && itemId != null;
        var hovered = GUI.enabled && rect.Contains(Event.current.mousePosition);
        Surface(rect, selected ? selectedGradient : cardGradient, selected ? Gold : hovered ? Muted : Line);
        if (selected) Fill(new Rect(rect.x + 2, rect.y + 1, rect.width - 4, 1), new Color(0.96f, 0.86f, 0.66f));
        if (UiButton(rect, GUIContent.none, card)) main.Enqueue(() => SelectPath(path));
        DrawPathIcon(new Vector2(rect.x + 34, rect.y + 38), path, selected ? Gold : Muted);
        var textX = rect.x + 63;
        var textWidth = rect.width - 73;
        Label(textX, rect.y + 12, textWidth, 26, EnhanceEffects.Name(path) + " +" + (StatPatches.Registry.Find(itemId ?? "", path)?.Level ?? 0), pathHeading);
        var description = path == EnhancePath.Recoil ? Loc.L("수직·수평 반동 감소", "Lower V/H recoil")
            : path == EnhancePath.Ergonomics ? Loc.L("인체공학 증가", "More ergonomics") : path == EnhancePath.Precision ? Loc.L("탄 퍼짐 감소", "Less dispersion")
            : path == EnhancePath.ArmorDurability ? Loc.L("피격 시 내구도 소모 감소", "Less durability loss on hit") : Loc.L("장비 페널티 완화", "Lighter gear penalties");
        Label(textX, rect.y + 39, textWidth, 24, description, centeredSmall);
        GUI.enabled = true;
    }

    private void DrawPathIcon(Vector2 center, EnhancePath path, Color color)
    {
        if (Event.current.type != EventType.Repaint) return;
        var previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(center.x - 24, center.y - 24, 48, 48), glyphs[path]);
        GUI.color = previous;
    }

    private void DrawEffects()
    {
        Box(new Rect(24, 367, 712, 170), Panel, Line);
        Fill(new Rect(25, 368, 710, 34), new Color(0.12f, 0.135f, 0.15f, 0.65f));
        Label(40, 370, 280, 29, Loc.L("강화 효과 · 증가량 ×", "Effect · gain ×") + $"{(preview?.NextGainMultiplier ?? 1):0.##}", body);
        Label(379, 370, 122, 29, Now + $"+{preview?.CurrentLevel ?? StatPatches.Registry.Find(itemId ?? "", selectedPath)?.Level ?? 0}", centeredSmall);
        Label(594, 370, 126, 29, Next + $"+{preview?.NextLevel ?? Math.Max(StatPatches.Registry.Find(itemId ?? "", selectedPath)?.Level ?? 0, Math.Min(maxLevel.Value, (StatPatches.Registry.Find(itemId ?? "", selectedPath)?.Level ?? 0) + 1))}", centeredSmall);
        Fill(new Rect(25, 402, 710, 1), Line);
        Label(48, 412, 290, 36, EnhanceEffects.StatName(selectedPath), body);
        var current = preview?.CurrentEffect ?? StatPatches.Registry.Amount(itemId ?? "", selectedPath);
        var next = preview?.NextEffect ?? current;
        var cap = preview?.EffectCap ?? EnhanceEffects.Amount(selectedPath, maxLevel.Value, Tuning(), StatPatches.Registry.StarLevel(itemId ?? ""));
        Label(379, 408, 122, 42, EnhanceEffects.Format(selectedPath, current), value);
        Label(532, 410, 40, 38, "→", value);
        Label(594, 408, 126, 42, EnhanceEffects.Format(selectedPath, next), selectedValue);
        Bar(new Rect(155, 467, 400, 10), current, cap, new Color(0.43f, 0.46f, 0.48f));
        Bar(new Rect(155, 492, 400, 10), next, cap, Gold);
        Label(48, 458, 102, 27, Now + $"+{preview?.CurrentLevel ?? StatPatches.Registry.Find(itemId ?? "", selectedPath)?.Level ?? 0}", small);
        Label(48, 483, 102, 27, Next + $"+{preview?.NextLevel ?? Math.Max(StatPatches.Registry.Find(itemId ?? "", selectedPath)?.Level ?? 0, Math.Min(maxLevel.Value, (StatPatches.Registry.Find(itemId ?? "", selectedPath)?.Level ?? 0) + 1))}", small);
        Label(575, 458, 143, 27, EnhanceEffects.Format(selectedPath, current), small);
        Label(575, 483, 143, 27, EnhanceEffects.Format(selectedPath, next), small);
        Label(155, 511, 563, 22, EnhanceEffects.Description(selectedPath), small);
    }

    private static string Now => Loc.L("현재 ", "Now ");
    private static string Next => Loc.L("성공 시 ", "Next ");

    private void Bar(Rect rect, double amount, double cap, Color color)
    {
        Box(new Rect(rect.x - 1, rect.y - 1, rect.width + 2, rect.height + 2),
            new Color(0.13f, 0.15f, 0.17f), Line);
        if (cap > 0)
        {
            var fill = new Rect(rect.x, rect.y, rect.width * (float)Math.Max(0, Math.Min(1, amount / cap)), rect.height);
            Fill(fill, color);
            Fill(new Rect(fill.x, fill.y, fill.width, 1), new Color(0.95f, 0.91f, 0.80f, 0.35f));
        }
    }
    private void DrawCosts()
    {
        Surface(new Rect(24, 550, 347, 112), cardGradient, Line);
        Surface(new Rect(385, 550, 351, 112), cardGradient, Line);
        Label(45, 558, 300, 28, Loc.L("성공률", "Success chance") + (preview?.ChanceBoost == true ? $" (+{preview.BoostPoints:0.##}%p)" : ""), body);
        Label(44, 585, 300, 49, preview == null ? "—" : $"{preview.SuccessPercent:0.##}%", chanceValue);
        Bar(new Rect(48, 643, 296, 9), preview?.SuccessPercent ?? 0, 100, Gold);
        Label(405, 558, 310, 28, evolutionMode ? Loc.L("진화 비용", "Evolution cost") : Loc.L("총 강화 비용", "Total cost"), body);
        Label(406, 587, 310, 40, preview == null ? "—" : $"{preview.Cost:N0} ₽", costValue);
        Label(407, 628, 309, 25, preview == null ? "" : Loc.L("보유 ", "Have ") + $"{preview.AvailableRoubles:N0} ₽ · BTC {preview.AvailableBitcoins:N0}", small);
        Label(24, 670, 515, 23, evolutionMode && evolutionTargetStars == 3 ? Loc.L("가격 기준: 3차 진화 4,000,000 ₽ 고정", "Price: 3rd evolution fixed at 4,000,000 ₽")
            : Loc.L("가격 기준: ", "Price: ") + (preview == null ? Loc.L("견적 확인 중", "checking quote") : Loc.PriceSource(preview.PriceSource))
              + (preview?.PartsPrice > 0 ? Loc.L(" · 본체+끼운 방탄판", " · body + inserted plates") : Loc.L(" · 장비 본체", " · gear body only"))
              + (preview?.InsertSurcharge > 0 ? Loc.L($" · 방탄판 차액 +{preview.InsertSurcharge:N0} ₽", $" · plate catch-up +{preview.InsertSurcharge:N0} ₽") : ""), small);
        Label(562, 670, 174, 23, evolutionMode ? Loc.L("진화 전용 판정", "Evolution roll") : Loc.L("선택 항목 +6 시도부터 보호", "Protect from +6 try"), centeredSmall);
    }

    private void DrawActions()
    {
        Box(new Rect(24, 784, 712, 44), new Color(0.10f, 0.085f, 0.055f), new Color(0.61f, 0.46f, 0.22f));
        Label(38, 791, 24, 27, "!", selectedValue);
        var note = preview?.BindLegacy == true ? Loc.L("기존 단계 유지 · 첫 효과 선택은 무료 · 100% 적용", "Keeps old level · first effect choice is free · 100%")
            : preview?.HighRisk == true ? RiskNote(preview)
            : preview?.Protection.Destruction == true ? Loc.L("파괴 방지 적용 · 실패 시 비용만 소모", "Destruction protected · failure costs only the fee")
            : preview?.Protection.Downgrade == true ? Loc.L("단계 하락 방지 적용 · 실패 시 비용만 소모", "Downgrade protected · failure costs only the fee")
            : Loc.L("실패 시: ", "On failure: ") + FailureText(preview?.FailureMode ?? failure.Value);
        Label(75, 789, 643, 34, note, noteStyle);
        if (attemptTimeline.Active)
        {
            var fraction = attemptTimeline.Fraction(AttemptClock);
            Label(28, 830, 704, 27, fraction < 1 ? Loc.L("강화 진행 중… ", "Enhancing… ") + $"{fraction * 100:0}%" : Loc.L("강화 판정 중…", "Rolling result…"), statusStyle);
            Bar(new Rect(28, 864, 704, 9), fraction, 1, Gold);
        }
        else if (quote?.CanDestroy == true)
        {
            Label(28, 830, 704, 22, message, statusStyle);
            GUI.enabled = !busy && !resultVisible;
            destructionConfirmed = UiToggle(new Rect(28, 854, 704, 25), destructionConfirmed,
                Loc.L($" 본체와 부착물 {quote.AttachedItems}개(탄약 포함)가 삭제될 수 있음을 확인했습니다.",
                    $" I understand the gear and {quote.AttachedItems} attachment(s) (incl. ammo) may be deleted."));
            GUI.enabled = true;
        }
        else
        {
            var left = quote == null ? 0 : Math.Max(0, quote.ExpiresUnixSeconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var seconds = quote == null ? "" : Loc.L($"견적 유효 시간: {left}초", $"Quote valid for {left}s");
            Label(28, 833, 704, 43, string.IsNullOrEmpty(message) ? seconds : message, statusStyle);
        }
        GUI.enabled = !busy && !resultVisible;
        var closeRect = new Rect(24, 883, 233, 48);
        Surface(closeRect, cardGradient, Line);
        if (UiButton(closeRect, Loc.L("닫기", "Close"), secondary, UiInteraction.Close)) main.Enqueue(Close);
        var expired = quote != null && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= quote.ExpiresUnixSeconds;
        var canPay = quote != null && quote.CanPay && !expired
            && (!quote.CanDestroy || destructionConfirmed);
        var atMaximum = (StatPatches.Registry.Find(itemId ?? "", selectedPath)?.Level ?? 0) >= maxLevel.Value;
        var canEvolve = itemId != null && StatPatches.Registry.IsComplete(itemId) && StatPatches.Registry.StarLevel(itemId) < StarEvolution.Maximum;
        GUI.enabled = !busy && !resultVisible && !uncertain && itemId != null && (canEvolve || !atMaximum && (quote == null || expired || canPay));
        var actionRect = new Rect(273, 883, 463, 48);
        Surface(actionRect, goldGradient, new Color(0.95f, 0.85f, 0.64f));
        var caption = busy ? attemptTimeline.Active ? Loc.L("강화 진행 중…", "Enhancing…") : Loc.L("처리 중…", "Working…")
            : canEvolve ? Loc.L("스타 진화 열기  ", "Open star evolution  ") + $"{StarEvolution.Stars(StatPatches.Registry.StarLevel(itemId))} → {StarEvolution.Stars(StatPatches.Registry.StarLevel(itemId) + 1)}"
            : atMaximum ? Loc.L("이 항목은 최대 단계입니다", "This track is maxed") : quote == null || expired ? Loc.L("견적 다시 불러오기", "Reload quote")
            : quote.BindLegacy ? Loc.L("기존 단계에 효과 적용 · 무료", "Apply effect to old level · free") : Loc.L("강화 시도  ", "Enhance  ") + $"{quote.Cost:N0} ₽";
        if (UiButton(actionRect, caption, primary, canEvolve ? UiInteraction.Open : UiInteraction.Button))
        {
            if (canEvolve) main.Enqueue(OpenEvolution);
            else if (quote == null || expired) main.Enqueue(RefreshQuote);
            else main.Enqueue(Execute);
        }
        GUI.enabled = true;
    }

    private void DrawProtection()
    {
        Surface(new Rect(24, 700, 712, 74), cardGradient, Line);
        GUI.enabled = !busy && !resultVisible && !uncertain && quote?.DestructionProtectionAvailable == true;
        var destruction = UiToggle(new Rect(38, 705, 335, 27), protectDestruction,
            Loc.L(" 파괴 방지", " Prevent destruction") + $" (+{preview?.DestructionProtectionFee ?? 0:N0} ₽)");
        if (destruction != protectDestruction) main.Enqueue(() => SetProtection(true, destruction));
        GUI.enabled = !busy && !resultVisible && !uncertain && quote?.DowngradeProtectionAvailable == true;
        var downgrade = UiToggle(new Rect(390, 705, 335, 27), protectDowngrade,
            Loc.L(" 단계 하락 방지", " Prevent downgrade") + $" (+{preview?.DowngradeProtectionFee ?? 0:N0} ₽)");
        if (downgrade != protectDowngrade) main.Enqueue(() => SetProtection(false, downgrade));
        GUI.enabled = true;
        DrawChanceBoost(38, 740, 684);
    }
    private void DrawChanceBoost(float x, float y, float width)
    {
        var stars = evolutionMode ? evolutionTargetStars : 1;
        var coins = SuccessBoost.Bitcoins(evolutionMode, stars);
        var fee = SuccessBoost.Roubles(evolutionMode, stars);
        var points = SuccessBoost.Points(evolutionMode);
        GUI.enabled = !busy && !resultVisible && !uncertain && (chanceBoost || quote?.ChanceBoostAvailable == true);
        var enabled = UiToggle(new Rect(x, y, width, 28), chanceBoost,
            Loc.L($" 성공률 +{points}%p · BTC {coins}개 + {fee:N0} ₽ (보유 {preview?.AvailableBitcoins ?? 0:N0}개)",
                $" Chance +{points}%p · {coins} BTC + {fee:N0} ₽ (have {preview?.AvailableBitcoins ?? 0:N0})"));
        if (enabled != chanceBoost) main.Enqueue(() => SetChanceBoost(enabled));
        GUI.enabled = true;
    }
    private static string RiskNote(EnhanceQuote view)
    {
        var destroy = view.Protection.Destruction ? 0 : view.DestructionPercent;
        var downgrade = view.Protection.Downgrade ? 0 : 100 - view.DestructionPercent;
        var attempt = (100 - view.SuccessPercent) * destroy / 100;
        return Loc.L($"고위험 실패 시: 파괴 {destroy:0.##}% · 하락 {downgrade:0.##}% · 보호 {100 - destroy - downgrade:0.##}% / 이번 시도 파괴 {attempt:0.##}%",
            $"High-risk failure: destroy {destroy:0.##}% · downgrade {downgrade:0.##}% · protected {100 - destroy - downgrade:0.##}% / destroy this try {attempt:0.##}%");
    }
}
