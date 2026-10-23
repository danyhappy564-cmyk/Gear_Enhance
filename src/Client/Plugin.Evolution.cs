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
        Label(23, 9, 380, 36, Loc.L($"{evolutionTargetStars}차 스타 강화 진화", $"{Loc.Tier(evolutionTargetStars)} Star Evolution"), heading);
        Label(285, 16, 265, 24, evolutionTargetStars == 3 ? Loc.L("2차 스타 완료 장비 전용", "Requires a completed 2nd star")
            : Loc.L("1차 스타 전 항목 최대 전용", "Requires all 1st star tracks maxed"), small);
        GUI.enabled = !busy && !resultVisible;
        if (UiButton(new Rect(594, 13, 106, 29), Loc.L("돌아가기", "Back"), secondary, UiInteraction.Close)) main.Enqueue(ReturnFromEvolution);
        if (UiButton(new Rect(712, 10, 32, 32), "×", secondary, UiInteraction.Close)) main.Enqueue(Close);
        GUI.enabled = true;
        DrawPreview();
        Label(24, 239, 712, 29, evolutionTargetStars == 3 ? Loc.L("진화 조건 · 2차 스타 진화 성공 + 모든 항목 최대", "Condition · 2nd star evolved + every track maxed")
            : Loc.L("진화 조건 · 1차 스타 모든 항목 최대 달성", "Condition · every 1st star track maxed"), body);
        var paths = weapon ? WeaponTracks : ArmorTracks;
        var width = (712f - (paths.Length - 1) * 12) / paths.Length;
        for (var index = 0; index < paths.Length; index++)
            DrawPath(new Rect(24 + index * (width + 12), 276, width, 76), paths[index]);

        Box(new Rect(24, 367, 712, 170), Panel, starTint);
        Label(40, 370, 330, 29, Loc.L("진화 효과 ×", "Evolution effect ×") + $"{StarMultiplier(evolutionTargetStars):0.##}", body);
        Label(379, 370, 122, 29, Now + $"{StarEvolution.Stars(Math.Min(StatPatches.Registry.StarLevel(itemId ?? ""), evolutionTargetStars))}", centeredSmall);
        Label(594, 370, 126, 29, Loc.L("진화 후 ", "After ") + StarEvolution.Stars(evolutionTargetStars), centeredSmall);
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
        Label(40, 747, 680, 38, Loc.L("진화 실패 시 장비·부착물·탄약 파괴 · 파괴/하락 보호 사용 불가", "Failed evolution destroys gear, mods and ammo · no destruction/downgrade protection"), noteStyle);
        if (attemptTimeline.Active)
        {
            var progress = attemptTimeline.Fraction(AttemptClock);
            Label(28, 790, 704, 27, progress < 1 ? Loc.L("스타 강화 진화 진행 중… ", "Star evolution… ") + $"{progress * 100:0}%" : Loc.L("진화 결과 판정 중…", "Rolling evolution result…"), statusStyle);
            Bar(new Rect(28, 824, 704, 9), progress, 1, starTint);
        }
        else if (quote?.CanDestroy == true)
        {
            GUI.enabled = !busy && !resultVisible;
            destructionConfirmed = UiToggle(new Rect(28, 799, 704, 35), destructionConfirmed,
                Loc.L($" 실패 시 본체와 부착물 {quote.AttachedItems}개(탄약 포함)가 파괴됨을 확인했습니다.",
                    $" I understand failure destroys the gear and {quote.AttachedItems} attachment(s) (incl. ammo)."));
            GUI.enabled = true;
        }
        else Label(28, 793, 704, 43, message, statusStyle);

        GUI.enabled = !busy && !resultVisible;
        var back = new Rect(24, 843, 233, 48);
        Surface(back, cardGradient, Line);
        if (UiButton(back, Loc.L("일반 강화로 돌아가기", "Back to enhance"), secondary, UiInteraction.Close)) main.Enqueue(ReturnFromEvolution);
        var expired = quote != null && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= quote.ExpiresUnixSeconds;
        var evolved = StatPatches.Registry.StarLevel(itemId ?? "") >= evolutionTargetStars;
        GUI.enabled = !busy && !resultVisible && !uncertain && itemId != null && !evolved
            && (quote == null || expired || quote.CanPay && (!quote.CanDestroy || destructionConfirmed));
        var action = new Rect(273, 843, 463, 48);
        Surface(action, goldGradient, starTint);
        var caption = busy ? Loc.L("스타 강화 진화 진행 중…", "Evolving…") : evolved ? StarEvolution.Stars(evolutionTargetStars) + Loc.L(" 진화 완료", " evolved")
            : quote == null || expired ? Loc.L("진화 견적 다시 불러오기", "Reload evolution quote") : Loc.L("스타 강화 진화  ", "Star evolution  ") + $"{quote.Cost:N0} ₽";
        if (UiButton(action, caption, primary))
        {
            if (quote == null || expired) main.Enqueue(RefreshQuote); else main.Enqueue(Execute);
        }
        GUI.enabled = true;
        if (!resultVisible) GUI.DragWindow(new Rect(0, 0, 580, 54));
        if (resultVisible) DrawResultPanel();
    }
}
