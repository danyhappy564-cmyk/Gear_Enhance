using System;
using System.Collections.Generic;
using UnityEngine;

namespace GearEnhance;

public sealed partial class Plugin
{
    private const float WindowWidth = 760, WindowHeight = 870;
    private static readonly Color Gold = new Color(0.84f, 0.74f, 0.55f);
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
    private GUIStyle noteStyle, statusStyle;
    private GUI.WindowFunction drawWindow;
    private GUIStyle heading, body, small, centeredSmall, pathHeading, large, value, selectedValue, chanceValue, costValue;
    private GUIStyle card, primary, secondary, frame;
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
        uiFont = Font.CreateDynamicFontFromOSFont(
            new[] { "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "Arial Unicode MS" }, 16);
        if (uiFont != null) { uiFont.hideFlags = HideFlags.HideAndDontSave; windowSkin.font = uiFont; }
        heading = TextStyle(24, Color.white, FontStyle.Bold);
        body = TextStyle(17, new Color(0.88f, 0.89f, 0.90f));
        small = TextStyle(13, Muted);
        centeredSmall = TextStyle(13, Muted, alignment: TextAnchor.MiddleCenter);
        noteStyle = TextStyle(13, Gold); noteStyle.wordWrap = true;
        statusStyle = TextStyle(13, Muted); statusStyle.wordWrap = true;
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
        Label(23, 9, 200, 36, "장비 강화", heading);
        Label(162, 16, 190, 24, "Gear Enhance · " + EnhanceProtocol.ModVersion, small);
        GUI.enabled = !busy;
        if (!uncertain && itemId != null && GUI.Button(new Rect(608, 13, 92, 29), "새로고침", secondary)) main.Enqueue(RefreshQuote);
        if (GUI.Button(new Rect(712, 10, 32, 32), "×", secondary)) main.Enqueue(Close);
        GUI.enabled = true;

        DrawPreview();
        Label(24, 239, 300, 29, "강화 항목 · 각각 강화 가능", body);
        var options = weapon ? WeaponTracks : ArmorTracks;
        var width = (712f - (options.Length - 1) * 12) / options.Length;
        for (var index = 0; index < options.Length; index++)
            DrawPath(new Rect(24 + index * (width + 12), 276, width, 76), options[index]);
        DrawEffects(); DrawCosts(); DrawActions();
        GUI.DragWindow(new Rect(0, 0, 598, 54));
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
            Label(62, 127, 380, 30, itemId == null ? "장비가 파괴되었습니다" : "장비 이미지를 불러오는 중…", small);
        DrawResultEffect(area);
        Label(517, 76, 217, 38, itemName ?? "장비", heading);
        Label(518, 112, 216, 25, EnhanceEffects.Name(selectedPath) + " 강화", small);
        var current = preview?.CurrentLevel ?? StatPatches.Registry.Find(itemId ?? "", selectedPath)?.Level ?? 0;
        var next = preview?.NextLevel ?? Math.Max(current, Math.Min(current + 1, maxLevel.Value));
        Label(513, 145, 225, 64, itemId == null ? "파괴됨" : $"+{current} → +{next}", current >= 100 || next >= 100
            ? selectedValue : large);
        var complete = StatPatches.Registry.IsComplete(itemId ?? "");
        Label(517, 207, 217, 22, itemId == null ? "본체·부착물·탄약 소실" : complete ? $"전 항목 완성 · 효과 +{completionBonus.Value:0.##}%"
            : preview?.CompletesOnSuccess == true ? $"성공 시 완성 · 효과 +{completionBonus.Value:0.##}%" : "전 항목 완성 시 보너스", small);
        if (complete) Fill(new Rect(517, 230, 217, 2), Purple);
        Fill(new Rect(24, 231, 712, 1), Line);
    }

    private void DrawPath(Rect rect, EnhancePath path)
    {
        var selected = path == selectedPath;
        GUI.enabled = !busy && !uncertain && itemId != null;
        var hovered = GUI.enabled && rect.Contains(Event.current.mousePosition);
        Surface(rect, selected ? selectedGradient : cardGradient, selected ? Gold : hovered ? Muted : Line);
        if (selected) Fill(new Rect(rect.x + 2, rect.y + 1, rect.width - 4, 1), new Color(0.96f, 0.86f, 0.66f));
        if (GUI.Button(rect, GUIContent.none, card)) main.Enqueue(() => SelectPath(path));
        DrawPathIcon(new Vector2(rect.x + 34, rect.y + 38), path, selected ? Gold : Muted);
        var textX = rect.x + 63;
        var textWidth = rect.width - 73;
        Label(textX, rect.y + 12, textWidth, 26, EnhanceEffects.Name(path) + " +" + (StatPatches.Registry.Find(itemId ?? "", path)?.Level ?? 0), pathHeading);
        var description = path == EnhancePath.Recoil ? "수직·수평 반동 감소"
            : path == EnhancePath.Ergonomics ? "인체공학 증가" : path == EnhancePath.Precision ? "탄 퍼짐 감소"
            : path == EnhancePath.ArmorDurability ? "피격 시 내구도 소모 감소" : "장비 페널티 완화";
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
        Label(40, 370, 280, 29, $"강화 효과 · 증가량 ×{(preview?.NextGainMultiplier ?? 1):0.##}", body);
        Label(379, 370, 122, 29, $"현재 +{preview?.CurrentLevel ?? StatPatches.Registry.Find(itemId ?? "", selectedPath)?.Level ?? 0}", centeredSmall);
        Label(594, 370, 126, 29,  $"성공 시 +{preview?.NextLevel ?? Math.Max(StatPatches.Registry.Find(itemId ?? "", selectedPath)?.Level ?? 0, Math.Min(maxLevel.Value, (StatPatches.Registry.Find(itemId ?? "", selectedPath)?.Level ?? 0) + 1))}", centeredSmall);
        Fill(new Rect(25, 402, 710, 1), Line);
        Label(48, 412, 290, 36, EnhanceEffects.StatName(selectedPath), body);
        var current = preview?.CurrentEffect ?? StatPatches.Registry.Amount(itemId ?? "", selectedPath);
        var next = preview?.NextEffect ?? current;
        var cap = preview?.EffectCap ?? EnhanceEffects.Amount(selectedPath, maxLevel.Value, Tuning(), true);
        Label(379, 408, 122, 42, EnhanceEffects.Format(selectedPath, current), value);
        Label(532, 410, 40, 38, "→", value);
        Label(594, 408, 126, 42, EnhanceEffects.Format(selectedPath, next), selectedValue);
        Bar(new Rect(155, 467, 400, 10), current, cap, new Color(0.43f, 0.46f, 0.48f));
        Bar(new Rect(155, 492, 400, 10), next, cap, Gold);
        Label(48, 458, 102, 27, $"현재 +{preview?.CurrentLevel ?? StatPatches.Registry.Find(itemId ?? "", selectedPath)?.Level ?? 0}", small);
        Label(48, 483, 102, 27, $"성공 시 +{preview?.NextLevel ?? Math.Max(StatPatches.Registry.Find(itemId ?? "", selectedPath)?.Level ?? 0, Math.Min(maxLevel.Value, (StatPatches.Registry.Find(itemId ?? "", selectedPath)?.Level ?? 0) + 1))}", small);
        Label(575, 458, 143, 27, EnhanceEffects.Format(selectedPath, current), small);
        Label(575, 483, 143, 27, EnhanceEffects.Format(selectedPath, next), small);
        Label(155, 511, 563, 22, EnhanceEffects.Description(selectedPath), small);
    }

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
        Label(45, 558, 300, 28, "성공률", body);
        Label(44, 585, 300, 49, preview == null ? "—" : $"{preview.SuccessPercent:0.##}%", chanceValue);
        Bar(new Rect(48, 643, 296, 9), preview?.SuccessPercent ?? 0, 100, Gold);
        Label(405, 558, 310, 28, "강화 비용", body);
        Label(406, 587, 310, 40, preview == null ? "—" : $"{preview.Cost:N0} ₽", costValue);
        Label(407, 628, 309, 25, preview == null ? "" : $"보유 {preview.AvailableRoubles:N0} ₽", small);
        Label(24, 670, 515, 23, "가격 기준: " + (preview?.PriceSource ?? "견적 확인 중") + " · 장비 본체", small);
        Label(562, 670, 174, 23, "F12에서 수치 조절", centeredSmall);
    }

    private void DrawActions()
    {
        Box(new Rect(24, 704, 712, 44), new Color(0.10f, 0.085f, 0.055f), new Color(0.61f, 0.46f, 0.22f));
        Label(38, 711, 24, 27, "!", selectedValue);
        var note = preview?.BindLegacy == true ? "기존 단계 유지 · 첫 효과 선택은 무료 · 100% 적용"

            : preview?.HighRisk == true ? $"고위험 실패 시: 파괴 {preview.DestructionPercent:0.##}% / 선택 항목 -1 {100 - preview.DestructionPercent:0.##}% · 이번 시도 파괴 {(100 - preview.SuccessPercent) * preview.DestructionPercent / 100:0.##}%"
            : "실패 시: " + FailureText(preview?.FailureMode ?? failure.Value);
        Label(75, 709, 643, 34, note, noteStyle);
        if (attemptTimeline.Active)
        {
            var fraction = attemptTimeline.Fraction(Clock);
            Label(28, 750, 704, 27, fraction < 1 ? $"강화 진행 중… {fraction * 100:0}%" : "강화 판정 중…", statusStyle);
            Bar(new Rect(28, 784, 704, 9), fraction, 1, Gold);
        }
        else if (quote?.CanDestroy == true)
        {
            Label(28, 750, 704, 22, message, statusStyle);
            destructionConfirmed = GUI.Toggle(new Rect(28, 774, 704, 25), destructionConfirmed,
                $" 본체와 부착물 {quote.AttachedItems}개(탄약 포함)가 삭제될 수 있음을 확인했습니다.");
        }
        else
        {
            var seconds = quote == null ? "" : $"견적 유효 시간: {Math.Max(0, quote.ExpiresUnixSeconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds())}초";
            Label(28, 753, 704, 43, string.IsNullOrEmpty(message) ? seconds : message, statusStyle);
        }
        GUI.enabled = !busy;
        var closeRect = new Rect(24, 803, 233, 48);
        Surface(closeRect, cardGradient, Line);
        if (GUI.Button(closeRect, "닫기", secondary)) main.Enqueue(Close);
        var expired = quote != null && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= quote.ExpiresUnixSeconds;
        var canPay = quote != null && quote.AvailableRoubles >= quote.Cost && !expired
            && (!quote.CanDestroy || destructionConfirmed);
        var atMaximum = (StatPatches.Registry.Find(itemId ?? "", selectedPath)?.Level ?? 0) >= maxLevel.Value;
        GUI.enabled = !busy && !uncertain && itemId != null && !atMaximum && (quote == null || expired || canPay);
        var actionRect = new Rect(273, 803, 463, 48);
        Surface(actionRect, goldGradient, new Color(0.95f, 0.85f, 0.64f));
        var caption = busy ? attemptTimeline.Active ? "강화 진행 중…" : "처리 중…" : atMaximum ? "이 항목은 최대 단계입니다" : quote == null || expired ? "견적 다시 불러오기"
            : quote.BindLegacy ? "기존 단계에 효과 적용 · 무료" : $"강화 시도  {quote.Cost:N0} ₽";
        if (GUI.Button(actionRect, caption, primary))
        {
            if (quote == null || expired) main.Enqueue(RefreshQuote);
            else main.Enqueue(Execute);
        }
        GUI.enabled = true;
    }
}
