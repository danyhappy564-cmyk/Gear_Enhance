using System;
using UnityEngine;

namespace GearEnhance;

public sealed partial class Plugin
{
    private static readonly Color Gold = new Color(0.83f, 0.73f, 0.53f);
    private static readonly Color Muted = new Color(0.61f, 0.64f, 0.63f);
    private static readonly Color Line = new Color(0.29f, 0.31f, 0.30f);
    private static readonly Color Panel = new Color(0.075f, 0.085f, 0.08f, 0.99f);
    private GUIStyle heading, body, small, large, value, selectedValue, card, primary, secondary, frame;
    private float UiScale => Mathf.Max(0.25f, Mathf.Min(1.25f,
        Mathf.Min((Screen.height - 60f) / 950f, (Screen.width - 40f) / 820f)));

    private void ResetWindow()
    {
        var scale = UiScale;
        window = new Rect(Math.Max(0, (Screen.width / scale - 820) / 2 + 24),
            Math.Max(0, (Screen.height / scale - 950) / 2), 820, 950);
    }

    private void EnsureSkin()
    {
        if (windowSkin != null) return;
        windowSkin = Instantiate(GUI.skin);
        windowSkin.hideFlags = HideFlags.HideAndDontSave;
        uiFont = Font.CreateDynamicFontFromOSFont(
            new[] { "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "Arial Unicode MS" }, 18);
        if (uiFont != null) { uiFont.hideFlags = HideFlags.HideAndDontSave; windowSkin.font = uiFont; }
        heading = TextStyle(25, Color.white, FontStyle.Bold);
        body = TextStyle(19, new Color(0.91f, 0.92f, 0.90f));
        small = TextStyle(15, Muted);
        large = TextStyle(43, Gold, FontStyle.Bold);
        value = TextStyle(27, Color.white, FontStyle.Bold);
        selectedValue = TextStyle(27, Gold, FontStyle.Bold);
        card = new GUIStyle(windowSkin.button) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = false };
        card.normal.background = card.hover.background = card.active.background = null;
        card.normal.textColor = card.hover.textColor = card.active.textColor = Color.white;
        primary = new GUIStyle(card) { fontSize = 23 };
        primary.normal.textColor = primary.hover.textColor = primary.active.textColor = new Color(0.08f, 0.08f, 0.07f);
        secondary = new GUIStyle(card) { fontSize = 20 };
        frame = new GUIStyle(windowSkin.window) { padding = new RectOffset(), border = new RectOffset() };
        frame.normal.background = frame.onNormal.background = null;
        windowSkin.toggle.fontSize = 16;
        windowSkin.toggle.wordWrap = true;
    }

    private GUIStyle TextStyle(int size, Color color, FontStyle weight = FontStyle.Normal)
    {
        var style = new GUIStyle(windowSkin.label)
            { fontSize = size, fontStyle = weight, wordWrap = false, richText = false, clipping = TextClipping.Clip };
        style.normal.textColor = color;
        return style;
    }
    private static void Fill(Rect rect, Color color)
    {
        var old = GUI.color; GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old;
    }
    private static void Box(Rect rect, Color fill, Color border)
    {
        Fill(rect, border); Fill(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), fill);
    }
    private void Label(float x, float y, float width, float height, string text, GUIStyle style) =>
        GUI.Label(new Rect(x, y, width, height), new GUIContent(text, text), style);

    private void OnGUI()
    {
        if (!visible) return;
        EnsureSkin();
        var matrix = GUI.matrix; var skin = GUI.skin; var depth = GUI.depth; var active = GUI.enabled;
        try
        {
            GUI.depth = -1000; GUI.skin = windowSkin;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.55f));
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * UiScale);
            window.x = Mathf.Clamp(window.x, 0, Math.Max(0, Screen.width / UiScale - window.width));
            window.y = Mathf.Clamp(window.y, 0, Math.Max(0, Screen.height / UiScale - window.height));
            window = GUI.Window(GetInstanceID(), window, DrawWindow, "", frame);
        }
        finally { GUI.matrix = matrix; GUI.skin = skin; GUI.depth = depth; GUI.enabled = active; }
    }

    private void DrawWindow(int id)
    {
        Box(new Rect(0, 0, 820, 950), Panel, Line);
        Fill(new Rect(1, 1, 818, 60), new Color(0.14f, 0.15f, 0.14f));
        Label(24, 13, 420, 40, "장비 강화", heading);
        Label(448, 22, 185, 26, "Gear Enhance · 0.2.0", small);
        GUI.enabled = !busy;
        if (!uncertain && itemId != null && GUI.Button(new Rect(638, 17, 118, 31), "새로고침", secondary))
            main.Enqueue(RefreshQuote);
        if (GUI.Button(new Rect(768, 14, 34, 34), "×", secondary)) main.Enqueue(Close);
        GUI.enabled = true;

        DrawPreview();
        Label(24, 263, 300, 32, "강화 방향", body);
        var options = weapon ? new[] { EnhancePath.Recoil, EnhancePath.Ergonomics, EnhancePath.Precision }
            : new[] { EnhancePath.ArmorDurability, EnhancePath.Mobility };
        var width = (772f - (options.Length - 1) * 12) / options.Length;
        for (var index = 0; index < options.Length; index++)
            DrawPath(new Rect(24 + index * (width + 12), 301, width, 78), options[index]);
        DrawEffects();
        DrawCosts();
        DrawActions();
        GUI.DragWindow(new Rect(0, 0, 750, 60));
    }

    private void DrawPreview()
    {
        Box(new Rect(24, 76, 772, 172), new Color(0.055f, 0.065f, 0.06f), new Color(0.18f, 0.20f, 0.18f));
        var area = new Rect(38, 90, 432, 142);
        var sprite = GameBridge.Get(itemIcon, "Sprite") as Sprite;
        if (sprite != null && sprite.texture != null)
        {
            var source = sprite.rect;
            var fit = Math.Min(area.width / source.width, area.height / source.height);
            var rect = new Rect(area.center.x - source.width * fit / 2, area.center.y - source.height * fit / 2,
                source.width * fit, source.height * fit);
            GUI.DrawTextureWithTexCoords(rect, sprite.texture, new Rect(source.x / sprite.texture.width,
                source.y / sprite.texture.height, source.width / sprite.texture.width, source.height / sprite.texture.height));
        }
        else Label(70, 145, 380, 32, "장비 이미지를 불러오는 중…", small);
        Label(492, 91, 285, 40, itemName ?? "장비", heading);
        Label(492, 132, 285, 27, weapon ? "무기 강화" : "방어구 강화", small);
        var current = preview?.CurrentLevel ?? StatPatches.Registry.Find(itemId ?? "")?.Level ?? 0;
        var next = preview?.NextLevel ?? Math.Min(current + 1, maxLevel.Value);
        Label(489, 169, 300, 64, $"+{current} → +{next}", large);
    }

    private void DrawPath(Rect rect, EnhancePath path)
    {
        var selected = path == selectedPath;
        Box(rect, selected ? new Color(0.20f, 0.20f, 0.16f) : new Color(0.10f, 0.12f, 0.11f), selected ? Gold : Line);
        GUI.enabled = !busy && !uncertain && itemId != null;
        if (GUI.Button(new Rect(rect.x, rect.y + 5, rect.width, 36), EnhanceEffects.Name(path), card))
            main.Enqueue(() => SelectPath(path));
        GUI.enabled = true;
        var description = path == EnhancePath.Recoil ? "수직·수평 반동 감소"
            : path == EnhancePath.Ergonomics ? "인체공학 증가" : path == EnhancePath.Precision ? "탄 퍼짐 감소"
            : path == EnhancePath.ArmorDurability ? "피격 시 내구도 소모 감소" : "장비 페널티 완화";
        Label(rect.x + 12, rect.y + 45, rect.width - 24, 28, description, small);
    }

    private void DrawEffects()
    {
        Box(new Rect(24, 395, 772, 187), new Color(0.065f, 0.075f, 0.07f), Line);
        Label(40, 405, 310, 32, "강화 효과", body);
        Label(450, 408, 135, 28, $"현재 +{preview?.CurrentLevel ?? 0}", small);
        Label(642, 408, 140, 28, $"{(preview?.ChangePath == true ? "변경 시" : "성공 시")} +{preview?.NextLevel ?? 1}", small);
        Fill(new Rect(25, 443, 770, 1), Line);
        Label(40, 456, 360, 37, EnhanceEffects.StatName(selectedPath), body);
        var current = preview?.CurrentEffect ?? 0;
        var next = preview?.NextEffect ?? 0;
        var cap = preview?.EffectCap ?? EnhanceEffects.Cap(selectedPath, Tuning());
        Label(449, 452, 145, 45, EnhanceEffects.Format(selectedPath, current), value);
        Label(602, 454, 42, 40, "→", value);
        Label(652, 452, 138, 45, EnhanceEffects.Format(selectedPath, next), selectedValue);
        Bar(new Rect(188, 510, 440, 12), current, cap, new Color(0.43f, 0.46f, 0.44f));
        Bar(new Rect(188, 538, 440, 12), next, cap, Gold);
        Label(40, 502, 140, 28, "현재 효과", small);
        Label(40, 530, 140, 28, "선택한 효과", small);
        Label(646, 502, 140, 28, EnhanceEffects.Format(selectedPath, current), small);
        Label(646, 530, 140, 28, EnhanceEffects.Format(selectedPath, next), small);
        Label(40, 555, 733, 25, EnhanceEffects.Description(selectedPath), small);
    }

    private static void Bar(Rect rect, double amount, double cap, Color color)
    {
        Fill(rect, new Color(0.17f, 0.19f, 0.18f));
        if (cap > 0) Fill(new Rect(rect.x, rect.y, rect.width * (float)Math.Max(0, Math.Min(1, amount / cap)), rect.height), color);
    }
    private void DrawCosts()
    {
        Box(new Rect(24, 597, 372, 116), new Color(0.09f, 0.10f, 0.09f), Line);
        Box(new Rect(410, 597, 386, 116), new Color(0.09f, 0.10f, 0.09f), Line);
        Label(44, 607, 330, 29, "성공률", body);
        Label(42, 639, 335, 56, preview == null ? "—" : $"{preview.SuccessPercent:0.##}%", large);
        Bar(new Rect(44, 696, 327, 6), preview?.SuccessPercent ?? 0, 100, Gold);
        Label(430, 607, 340, 29, "강화 비용", body);
        Label(430, 641, 350, 43, preview == null ? "—" : $"{preview.Cost:N0} ₽", selectedValue);
        Label(432, 681, 343, 26, preview == null ? "" : $"보유 {preview.AvailableRoubles:N0} ₽", small);
        Label(24, 719, 550, 26, "가격 기준: " + (preview?.PriceSource ?? "견적 확인 중") + " · 장비 본체", small);
        Label(604, 719, 190, 26, "F12에서 수치 조절", small);
    }

    private void DrawActions()
    {
        Box(new Rect(24, 754, 772, 61), new Color(0.14f, 0.12f, 0.07f), new Color(0.55f, 0.43f, 0.21f));
        var note = preview?.BindLegacy == true ? "기존 프로토타입 단계 유지 · 첫 효과 선택은 무료 · 100% 적용"
            : preview?.ChangePath == true ? $"방향 변경: 기존 {EnhanceEffects.Name(preview.PreviousPath.Value)} 효과 대체 · 단계 유지 · 100% 적용"
            : "실패 시: " + FailureText(preview?.FailureMode ?? failure.Value);
        var noteStyle = new GUIStyle(small) { wordWrap = true }; noteStyle.normal.textColor = Gold;
        Label(40, 764, 740, 43, note, noteStyle);
        if (quote != null && quote.FailureMode == FailureMode.Destroy && quote.SuccessPercent < 100)
            destructionConfirmed = GUI.Toggle(new Rect(28, 821, 760, 27), destructionConfirmed,
                $" 본체와 부착물 {quote.AttachedItems}개(탄약 포함)가 삭제될 수 있음을 확인했습니다.");
        else
        {
            var statusStyle = new GUIStyle(small) { wordWrap = true };
            var seconds = quote == null ? "" : $"견적 유효 시간: {Math.Max(0, quote.ExpiresUnixSeconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds())}초";
            Label(28, 821, 760, 46, string.IsNullOrEmpty(message) ? seconds : message, statusStyle);
        }
        GUI.enabled = !busy;
        Box(new Rect(24, 882, 234, 49), new Color(0.12f, 0.14f, 0.13f), Line);
        if (GUI.Button(new Rect(24, 882, 234, 49), "닫기", secondary)) main.Enqueue(Close);
        var expired = quote != null && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= quote.ExpiresUnixSeconds;
        var canPay = quote != null && quote.AvailableRoubles >= quote.Cost && !expired
            && (quote.FailureMode != FailureMode.Destroy || quote.SuccessPercent == 100 || destructionConfirmed);
        GUI.enabled = !busy && !uncertain && itemId != null && (quote == null || expired || canPay);
        Box(new Rect(274, 882, 522, 49), Gold, new Color(0.96f, 0.86f, 0.65f));
        var caption = busy ? "처리 중…" : quote == null || expired ? "견적 다시 불러오기"
            : quote.BindLegacy ? "기존 단계에 효과 적용 · 무료" : quote.ChangePath ? $"방향 변경  {quote.Cost:N0} ₽" : $"강화 시도  {quote.Cost:N0} ₽";
        if (GUI.Button(new Rect(274, 882, 522, 49), caption, primary))
        {
            if (quote == null || expired) main.Enqueue(RefreshQuote);
            else main.Enqueue(Execute);
        }
        GUI.enabled = true;
    }
}
