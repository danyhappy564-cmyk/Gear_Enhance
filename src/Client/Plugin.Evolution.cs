using System;
using UnityEngine;

namespace GearEnhance;

public sealed partial class Plugin
{
    private void OpenEvolution()
    {
        if (busy || resultVisible || itemId == null || !StatPatches.Registry.IsComplete(itemId)
            || StatPatches.Registry.StarLevel(itemId) >= StarEvolution.Maximum) return;
        evolutionTargetStars = StatPatches.Registry.StarLevel(itemId) + 1;
        evolutionMode = true; protectDestruction = protectDowngrade = chanceBoost = false;
        selectedPath = weapon ? EnhancePath.Recoil : EnhancePath.ArmorDurability;
        quote = preview = null; generation++; ResetWindow(); RefreshQuote();
    }
    private void ReturnFromEvolution()
    {
        if (busy || resultVisible) return;
        evolutionMode = false; chanceBoost = false; quote = preview = null; generation++;
        ResetWindow(); RefreshQuote();
    }
    private void DrawEvolutionWindow(int id)
    {
        var starTint = StarTint(evolutionTargetStars);
        Surface(new Rect(0, 0, WindowWidth, ActiveWindowHeight), panelGradient, starTint);
        if (Event.current.type == EventType.Repaint)
            GUI.DrawTexture(new Rect(1, 1, WindowWidth - 2, 53), headerGradient);
        Label(23, 9, 380, 36, $"{evolutionTargetStars}차 스타 강화 진화", heading);
        Label(285, 16, 265, 24, evolutionTargetStars == 3 ? "2차 스타 완료 장비 전용" : "1차 스타 전 항목 최대 전용", small);
        GUI.enabled = !busy && !resultVisible;
        if (UiButton(new Rect(594, 13, 106, 29), "돌아가기", secondary, UiInteraction.Close)) main.Enqueue(ReturnFromEvolution);
        if (UiButton(new Rect(712, 10, 32, 32), "×", secondary, UiInteraction.Close)) main.Enqueue(Close);
        GUI.enabled = true;
        DrawPreview();
        Label(24, 239, 712, 29, evolutionTargetStars == 3 ? "진화 조건 · 2차 스타 진화 성공 + 모든 항목 최대" : "진화 조건 · 1차 스타 모든 항목 최대 달성", body);
        var paths = weapon ? WeaponTracks : ArmorTracks;
        var width = (712f - (paths.Length - 1) * 12) / paths.Length;
        for (var index = 0; index < paths.Length; index++)
            DrawPath(new Rect(24 + index * (width + 12), 276, width, 76), paths[index]);

        Box(new Rect(24, 367, 712, 170), Panel, starTint);
        Label(40, 370, 330, 29, $"진화 효과 ×{StarMultiplier(evolutionTargetStars):0.##}", body);
        Label(379, 370, 122, 29, $"현재 {StarEvolution.Stars(Math.Min(StatPatches.Registry.StarLevel(itemId ?? ""), evolutionTargetStars))}", centeredSmall);
        Label(594, 370, 126, 29, $"진화 후 {StarEvolution.Stars(evolutionTargetStars)}", centeredSmall);
        Fill(new Rect(25, 402, 710, 1), Line);
        for (var index = 0; index < paths.Length; index++)
        {
            var path = paths[index];
            var level = StatPatches.Registry.Find(itemId ?? "", path)?.Level ?? 0;
            var current = StatPatches.Registry.Amount(itemId ?? "", path);
            var next = EnhanceEffects.Amount(path, level, Tuning(), evolutionTargetStars);
            var y = 408 + index * 40;
            Label(48, y, 290, 34, EnhanceEffects.StatName(path), body);
            Label(379, y, 122, 34, EnhanceEffects.Format(path, current), value);
            Label(532, y, 40, 34, "→", value);
            Label(594, y, 126, 34, EnhanceEffects.Format(path, next), selectedValue);
        }
        DrawCosts();
        DrawChanceBoost(38, 705, 684);
        Box(new Rect(24, 744, 712, 44), new Color(0.14f, 0.035f, 0.035f), Danger);
        Label(40, 747, 680, 38, "진화 실패 시 장비·부착물·탄약 파괴 · 파괴/하락 보호 사용 불가", noteStyle);
        if (attemptTimeline.Active)
        {
            var progress = attemptTimeline.Fraction(AttemptClock);
            Label(28, 790, 704, 27, progress < 1 ? $"스타 강화 진화 진행 중… {progress * 100:0}%" : "진화 결과 판정 중…", statusStyle);
            Bar(new Rect(28, 824, 704, 9), progress, 1, starTint);
        }
        else if (quote?.CanDestroy == true)
        {
            GUI.enabled = !busy && !resultVisible;
            destructionConfirmed = UiToggle(new Rect(28, 799, 704, 35), destructionConfirmed,
                $" 실패 시 본체와 부착물 {quote.AttachedItems}개(탄약 포함)가 파괴됨을 확인했습니다.");
            GUI.enabled = true;
        }
        else Label(28, 793, 704, 43, message, statusStyle);

        GUI.enabled = !busy && !resultVisible;
        var back = new Rect(24, 843, 233, 48);
        Surface(back, cardGradient, Line);
        if (UiButton(back, "일반 강화로 돌아가기", secondary, UiInteraction.Close)) main.Enqueue(ReturnFromEvolution);
        var expired = quote != null && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= quote.ExpiresUnixSeconds;
        var evolved = StatPatches.Registry.StarLevel(itemId ?? "") >= evolutionTargetStars;
        GUI.enabled = !busy && !resultVisible && !uncertain && itemId != null && !evolved
            && (quote == null || expired || quote.CanPay && (!quote.CanDestroy || destructionConfirmed));
        var action = new Rect(273, 843, 463, 48);
        Surface(action, goldGradient, starTint);
        var caption = busy ? "스타 강화 진화 진행 중…" : evolved ? StarEvolution.Stars(evolutionTargetStars) + " 진화 완료" : quote == null || expired ? "진화 견적 다시 불러오기" : $"스타 강화 진화  {quote.Cost:N0} ₽";
        if (UiButton(action, caption, primary))
        {
            if (quote == null || expired) main.Enqueue(RefreshQuote); else main.Enqueue(Execute);
        }
        GUI.enabled = true;
        if (!resultVisible) GUI.DragWindow(new Rect(0, 0, 580, 54));
        if (resultVisible) DrawResultPanel();
    }
}
