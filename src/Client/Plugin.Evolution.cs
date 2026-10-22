using System;
using UnityEngine;

namespace GearEnhance;

public sealed partial class Plugin
{
    private void OpenEvolution()
    {
        if (busy || resultVisible || itemId == null || !StatPatches.Registry.IsComplete(itemId)
            || StatPatches.Registry.IsEvolved(itemId)) return;
        evolutionMode = true; protectDestruction = protectDowngrade = false;
        selectedPath = weapon ? EnhancePath.Recoil : EnhancePath.ArmorDurability;
        quote = preview = null; generation++; ResetWindow(); RefreshQuote();
    }
    private void ReturnFromEvolution()
    {
        if (busy || resultVisible) return;
        evolutionMode = false; quote = preview = null; generation++;
        ResetWindow(); RefreshQuote();
    }
    private void DrawEvolutionWindow(int id)
    {
        Surface(new Rect(0, 0, WindowWidth, ActiveWindowHeight), panelGradient, Purple);
        if (Event.current.type == EventType.Repaint)
            GUI.DrawTexture(new Rect(1, 1, WindowWidth - 2, 53), headerGradient);
        Label(23, 9, 380, 36, "스타 강화 진화", heading);
        Label(285, 16, 265, 24, "1차 스타 전 항목 최대 전용", small);
        GUI.enabled = !busy && !resultVisible;
        if (GUI.Button(new Rect(594, 13, 106, 29), "돌아가기", secondary)) main.Enqueue(ReturnFromEvolution);
        if (GUI.Button(new Rect(712, 10, 32, 32), "×", secondary)) main.Enqueue(Close);
        GUI.enabled = true;
        DrawPreview();
        Label(24, 239, 712, 29, "진화 조건 · 1차 스타 모든 항목 최대 달성", body);
        var paths = weapon ? WeaponTracks : ArmorTracks;
        var width = (712f - (paths.Length - 1) * 12) / paths.Length;
        for (var index = 0; index < paths.Length; index++)
            DrawPath(new Rect(24 + index * (width + 12), 276, width, 76), paths[index]);

        Box(new Rect(24, 367, 712, 170), Panel, Purple);
        Label(40, 370, 330, 29, $"진화 보너스 +{completionBonus.Value:0.##}%", body);
        Label(379, 370, 122, 29, "현재 ★", centeredSmall);
        Label(594, 370, 126, 29, "진화 후 ★★", centeredSmall);
        Fill(new Rect(25, 402, 710, 1), Line);
        for (var index = 0; index < paths.Length; index++)
        {
            var path = paths[index];
            var level = StatPatches.Registry.Find(itemId ?? "", path)?.Level ?? 0;
            var current = StatPatches.Registry.Amount(itemId ?? "", path);
            var next = EnhanceEffects.Amount(path, level, Tuning(), true);
            var y = 408 + index * 40;
            Label(48, y, 290, 34, EnhanceEffects.StatName(path), body);
            Label(379, y, 122, 34, EnhanceEffects.Format(path, current), value);
            Label(532, y, 40, 34, "→", value);
            Label(594, y, 126, 34, EnhanceEffects.Format(path, next), selectedValue);
        }
        DrawCosts();
        Box(new Rect(24, 704, 712, 44), new Color(0.14f, 0.035f, 0.035f), Danger);
        Label(40, 707, 680, 38, "진화 실패 시 장비·부착물·탄약 파괴 · 파괴/하락 보호 사용 불가", noteStyle);
        if (attemptTimeline.Active)
        {
            var progress = attemptTimeline.Fraction(AttemptClock);
            Label(28, 750, 704, 27, progress < 1 ? $"스타 강화 진화 진행 중… {progress * 100:0}%" : "진화 결과 판정 중…", statusStyle);
            Bar(new Rect(28, 784, 704, 9), progress, 1, Purple);
        }
        else if (quote?.CanDestroy == true)
        {
            GUI.enabled = !busy && !resultVisible;
            destructionConfirmed = GUI.Toggle(new Rect(28, 759, 704, 35), destructionConfirmed,
                $" 실패 시 본체와 부착물 {quote.AttachedItems}개(탄약 포함)가 파괴됨을 확인했습니다.");
            GUI.enabled = true;
        }
        else Label(28, 753, 704, 43, message, statusStyle);

        GUI.enabled = !busy && !resultVisible;
        var back = new Rect(24, 803, 233, 48);
        Surface(back, cardGradient, Line);
        if (GUI.Button(back, "일반 강화로 돌아가기", secondary)) main.Enqueue(ReturnFromEvolution);
        var expired = quote != null && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= quote.ExpiresUnixSeconds;
        var evolved = StatPatches.Registry.IsEvolved(itemId ?? "");
        GUI.enabled = !busy && !resultVisible && !uncertain && itemId != null && !evolved
            && (quote == null || expired || quote.AvailableRoubles >= quote.Cost && (!quote.CanDestroy || destructionConfirmed));
        var action = new Rect(273, 803, 463, 48);
        Surface(action, goldGradient, Purple);
        var caption = busy ? "스타 강화 진화 진행 중…" : evolved ? "★★ 진화 완료" : quote == null || expired ? "진화 견적 다시 불러오기" : $"스타 강화 진화  {quote.Cost:N0} ₽";
        if (GUI.Button(action, caption, primary))
        {
            if (quote == null || expired) main.Enqueue(RefreshQuote); else main.Enqueue(Execute);
        }
        GUI.enabled = true;
        if (!resultVisible) GUI.DragWindow(new Rect(0, 0, 580, 54));
        if (resultVisible) DrawResultPanel();
    }
}
