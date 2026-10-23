using System;
using UnityEngine;

namespace GearEnhance;

public sealed partial class Plugin
{
    private const double ResultDuration = 1.2;
    private double resultStarted = -100;
    private FeedbackCue resultCue;
    private Sprite resultSprite;
    private bool resultComplete;
    private bool resultVisible;
    private EnhanceReceipt resultReceipt;

    private void ShowResultFeedback(EnhanceReceipt receipt)
    {
        resultReceipt = receipt;
        resultVisible = true;
        resultComplete = receipt.Success && receipt.IsEvolution;
        // A cosmetic failure must never turn a confirmed payment into an unknown result.
        try
        {
            resultCue = receipt.Success ? FeedbackCue.Success
                : receipt.FailureMode == FailureMode.Destroy ? FeedbackCue.Destroy : FeedbackCue.Failure;
            resultSprite = GameBridge.Get(itemIcon, "Sprite") as Sprite;
            resultStarted = Clock;
        }
        catch (Exception ex) { Logger.LogWarning("강화 결과 연출 생략: " + ex.GetBaseException().Message); }
    }

    private void DismissResult()
    {
        if (busy) return;
        resultVisible = false; resultReceipt = null;
        if (itemId == null) Close();
    }
    private void DrawResultPanel()
    {
        if (resultReceipt == null) return;
        var receipt = resultReceipt;
        var tint = receipt.Success ? resultComplete ? StarTint(StarEvolution.Level(true, receipt.StarLevel)) : new Color(0.36f, 0.86f, 0.58f)
            : receipt.DestructionPrevented || receipt.DowngradePrevented ? Gold : Danger;
        Fill(new Rect(1, 55, WindowWidth - 2, ActiveWindowHeight - 56), new Color(0, 0, 0, 0.65f));
        Surface(new Rect(70, 295, 620, 305), cardGradient, tint);
        Fill(new Rect(72, 297, 616, 5), tint);
        var previous = GUI.color;
        GUI.color = tint;
        Label(90, 315, 580, 70, ResultText.Title(receipt, resultComplete), resultTitleStyle);
        GUI.color = previous;
        Label(100, 393, 560, 66, ResultText.Detail(receipt), resultDetailStyle);
        Label(100, 467, 560, 27, Loc.L($"{EnhanceEffects.Name(receipt.Path)} · {receipt.Paid:N0} ₽ + BTC {receipt.BitcoinsPaid}개 소모",
            $"{EnhanceEffects.Name(receipt.Path)} · spent {receipt.Paid:N0} ₽ + {receipt.BitcoinsPaid} BTC"), centeredSmall);
        if (resultComplete) Label(100, 493, 560, 27, Loc.L($"{StarEvolution.Stars(receipt.StarLevel)} 진화 효과 ×{StarMultiplier(receipt.StarLevel):0.##} · {StarEvolution.ColorName(receipt.StarLevel)} 표식 적용",
            $"{StarEvolution.Stars(receipt.StarLevel)} evolution effect ×{StarMultiplier(receipt.StarLevel):0.##} · {StarEvolution.ColorName(receipt.StarLevel)} marker applied"), centeredSmall);
        GUI.enabled = !busy;
        var button = new Rect(215, 536, 330, 46);
        Surface(button, goldGradient, tint);
        if (UiButton(button, busy ? Loc.L("기록 동기화 중…", "Syncing records…") : Loc.L("확인", "OK"), primary)) main.Enqueue(DismissResult);
        GUI.enabled = true;
    }

    private void DrawResultEffect(Rect area)
    {
        var elapsed = Clock - resultStarted;
        if (elapsed < 0 || elapsed >= ResultDuration || Event.current.type != EventType.Repaint) return;
        var progress = (float)(elapsed / ResultDuration);
        var tint = resultCue == FeedbackCue.Success ? resultComplete ? StarTint(StarEvolution.Level(true, resultReceipt?.StarLevel ?? 0)) : Gold : Danger;
        Fill(new Rect(24, 62, 712, 166), new Color(tint.r, tint.g, tint.b, 0.20f * (1 - progress)));
        if (resultCue != FeedbackCue.Destroy || resultSprite == null || resultSprite.texture == null) return;
        // Nine pieces of the cached preview, clipped inside its own area. No new
        // textures, particles, transforms or GUI matrix rotations per frame.
        var source = resultSprite.rect;
        var fit = Math.Min(area.width / source.width, area.height / source.height);
        var w = source.width * fit / 3;
        var h = source.height * fit / 3;
        var previous = GUI.color;
        try
        {
            GUI.color = new Color(1, 0.55f, 0.43f, 1 - progress);
            for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
            {
                var x = area.x + (area.width - w * 3) / 2 + col * w + (col - 1) * progress * 95;
                var y = area.y + (area.height - h * 3) / 2 + row * h + (row - 1) * progress * 45 + progress * progress * 60;
                var rect = Rect.MinMaxRect(Math.Max(area.x, x), Math.Max(area.y, y),
                    Math.Min(area.xMax, x + w), Math.Min(area.yMax, y + h));
                if (rect.width <= 0 || rect.height <= 0) continue;
                GUI.DrawTextureWithTexCoords(rect, resultSprite.texture,
                    new Rect((source.x + (col + (rect.x - x) / w) * source.width / 3) / resultSprite.texture.width,
                        (source.y + (2 - row + 1 - (rect.yMax - y) / h) * source.height / 3) / resultSprite.texture.height,
                        source.width / 3 * rect.width / w / resultSprite.texture.width,
                        source.height / 3 * rect.height / h / resultSprite.texture.height));
            }
        }
        finally { GUI.color = previous; }
    }
}
