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

    private void ShowResultFeedback(EnhanceReceipt receipt)
    {
        // A cosmetic failure must never turn a confirmed payment into an unknown result.
        try
        {
            resultCue = receipt.Success ? FeedbackCue.Success
                : receipt.FailureMode == FailureMode.Destroy ? FeedbackCue.Destroy : FeedbackCue.Failure;
            resultSprite = GameBridge.Get(itemIcon, "Sprite") as Sprite;
            resultComplete = receipt.Success && StatPatches.Registry.IsComplete(receipt.ItemId);
            resultStarted = Clock;
        }
        catch (Exception ex) { Logger.LogWarning("강화 결과 연출 생략: " + ex.GetBaseException().Message); }
    }

    private void DrawResultEffect(Rect area)
    {
        var elapsed = Clock - resultStarted;
        if (elapsed < 0 || elapsed >= ResultDuration || Event.current.type != EventType.Repaint) return;
        var progress = (float)(elapsed / ResultDuration);
        var tint = resultCue == FeedbackCue.Success ? resultComplete ? Purple : Gold : Danger;
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
