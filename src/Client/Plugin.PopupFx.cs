using System;
using UnityEngine;

namespace GearEnhance;

// 1.4.0 popup effects from the mockup CSS that IMGUI has no direct form for (gear-enhance-1.4.0.html
// 151-183), built once (P2: no per-frame textures): the rotating rays, glowing rings and spark bars,
// soft glows behind text (text-shadow) and around the mythic popup (box-shadow), and the gradient
// moving through the mythic title (background-clip: text). Scale and rotation go through GUI.matrix.
public sealed partial class Plugin
{
    private Texture2D raysTexture, haloTexture, sparkTexture, fxRingTexture;
    private GUIStyle glowNear, glowFar;
    private readonly GUIContent fxContent = new GUIContent();
    private static readonly Color SkyBlue = Hex(0x9fd0ff);
    // Mythic title gradient (90deg, 300 % wide): pink, gold, white, sky blue, pink.
    private static readonly Color[] TitleGradient = { Myth, MythGold, Color.white, SkyBlue, Myth };
    private static readonly Color[] SparkColors = { Myth, MythGold, Color.white, SkyBlue };
    // fxRing: 200 px circle, 3 px border, 24 px glow outside; the texture spans 280 px of it.
    private const float RingSpan = 280;
    private const int NearGlow = 40, FarGlow = 90;

    private void EnsureFx()
    {
        if (raysTexture != null) return;
        raysTexture = MakeTexture(512, 512, (x, y) =>
        {
            // repeating-conic-gradient from the top, clockwise: pink .26 0-5°, gold .2 18-22°, every 36°;
            // mask radial-gradient(circle, #000 22%, transparent 62%) of the farthest corner (1060.7 px).
            var dx = (x + 0.5f - 256) * 1500 / 512; var dy = (y + 0.5f - 256) * 1500 / 512;
            var r = Mathf.Sqrt(dx * dx + dy * dy);
            var mask = r <= 233.4f ? 1 : r >= 657.6f ? 0 : 1 - (r - 233.4f) / (657.6f - 233.4f);
            var angle = (Mathf.Atan2(dx, -dy) * Mathf.Rad2Deg + 360) % 36;
            return angle < 5 ? new Color(Myth.r, Myth.g, Myth.b, 0.26f * mask)
                : angle >= 18 && angle < 22 ? new Color(MythGold.r, MythGold.g, MythGold.b, 0.2f * mask) : Color.clear;
        });
        haloTexture = MakeTexture(64, 64, (x, y) =>
        {
            var dx = (x + 0.5f - 32) / 32; var dy = (y + 0.5f - 32) / 32;
            var r = Mathf.Sqrt(dx * dx + dy * dy);
            return new Color(1, 1, 1, Mathf.Exp(-0.5f * (r / 0.38f) * (r / 0.38f)) * Mathf.Clamp01((1 - r) * 8));
        });
        // fxspark i: a 30 × 6 px capsule with a 12 px glow, drawn at 2 texels per px (54 × 30 px).
        sparkTexture = MakeTexture(108, 60, (x, y) =>
        {
            var px = (x + 0.5f) / 2; var py = (y + 0.5f) / 2;
            var along = Mathf.Clamp(px, 15, 39);
            var d = Mathf.Sqrt((px - along) * (px - along) + (py - 15) * (py - 15)) - 3;
            return new Color(1, 1, 1, d <= 0 ? 1 : Mathf.Clamp01(1 - d * 2) * 0.4f + 0.6f * Mathf.Exp(-0.5f * (d / 6) * (d / 6)));
        });
        fxRingTexture = MakeTexture(256, 256, (x, y) =>
        {
            var dx = (x + 0.5f - 128) * RingSpan / 256; var dy = (y + 0.5f - 128) * RingSpan / 256;
            var r = Mathf.Sqrt(dx * dx + dy * dy);
            var border = Mathf.Clamp01(Mathf.Min(r - 96.5f, 100.5f - r));
            var glow = r > 100 ? 0.65f * Mathf.Exp(-0.5f * ((r - 100) / 12) * ((r - 100) / 12)) : 0;
            return new Color(1, 1, 1, Mathf.Max(border, glow));
        });
        glowNear = GlowStyle(NearGlow, 17);
        glowFar = GlowStyle(FarGlow, 40);
    }

    private Texture2D MakeTexture(int width, int height, Func<int, int, Color> pixel)
    {
        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            pixels[y * width + x] = pixel(x, y);
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        texture.SetPixels(pixels); texture.Apply(false, true);
        skinTextures.Add(texture);
        return texture;
    }

    // box-shadow 0 0 Npx: a 9-sliced falloff outside a rect, nothing inside it (border = edge texels).
    private GUIStyle GlowStyle(int edge, float sigma)
    {
        var size = edge * 2 + 2;
        var texture = MakeTexture(size, size, (x, y) =>
        {
            var dx = Math.Max(0, Math.Max(edge - (x + 0.5f), x + 0.5f - (size - edge)));
            var dy = Math.Max(0, Math.Max(edge - (y + 0.5f), y + 0.5f - (size - edge)));
            var d = Mathf.Sqrt(dx * dx + dy * dy);
            return new Color(1, 1, 1, dx == 0 && dy == 0 ? 0 : Mathf.Exp(-0.5f * (d / sigma) * (d / sigma)));
        });
        var style = new GUIStyle { border = new RectOffset(edge, edge, edge, edge) };
        style.normal.background = texture;
        return style;
    }

    // Scale and rotate what follows around `pivot` (window units); returns the matrix to restore.
    // The window's content starts at its position in the scaled screen (GUI.Window, no padding).
    private Matrix4x4 Transform(Vector2 pivot, float scale, float angle)
    {
        var old = GUI.matrix;
        if (Math.Abs(scale - 1) < 1e-4f && Math.Abs(angle) < 1e-3f) return old;
        var unclipped = (Vector3)(pivot + window.position);
        GUI.matrix = old * Matrix4x4.TRS(unclipped, Quaternion.Euler(0, 0, angle), new Vector3(scale, scale, 1))
            * Matrix4x4.TRS(-unclipped, Quaternion.identity, Vector3.one);
        return old;
    }

    // text-shadow 0 0 Npx: a soft halo behind the text and eight faint copies around it.
    private void GlowText(Rect rect, string text, GUIStyle style, Color color, float radius)
    {
        if (Event.current.type != EventType.Repaint || color.a <= 0.01f || string.IsNullOrEmpty(text)) return;
        EnsureFx();
        fxContent.text = text;
        var size = style.CalcSize(fxContent);
        var center = TextCenter(rect, size, style.alignment);
        var old = GUI.color;
        GUI.color = new Color(color.r, color.g, color.b, color.a * 0.5f * old.a);
        var halo = new Vector2(size.x + radius * 6, size.y + radius * 5);
        GUI.DrawTexture(new Rect(center.x - halo.x / 2, center.y - halo.y / 2, halo.x, halo.y), haloTexture);
        GUI.color = old;
        var faint = new Color(color.r, color.g, color.b, color.a * 0.28f);
        var step = Math.Max(1, radius * 0.5f);
        for (var index = 0; index < 8; index++)
        {
            var angle = index * Mathf.PI / 4;
            Label(rect.x + Mathf.Cos(angle) * step, rect.y + Mathf.Sin(angle) * step, rect.width, rect.height, text, style, faint);
        }
    }

    private static Vector2 TextCenter(Rect rect, Vector2 size, TextAnchor anchor)
    {
        var column = (int)anchor % 3; var row = (int)anchor / 3;
        var x = column == 0 ? rect.x + size.x / 2 : column == 1 ? rect.center.x : rect.xMax - size.x / 2;
        var y = row == 0 ? rect.y + size.y / 2 : row == 1 ? rect.center.y : rect.yMax - size.y / 2;
        return new Vector2(x, y);
    }

    // background: linear-gradient(90deg, …) 300 % wide, clipped to the text, moving 300 % per 2.4 s:
    // the text is drawn in thin vertical slices, each in the gradient's colour at that point.
    private void GradientLabel(Rect rect, string text, GUIStyle style, double elapsed)
    {
        if (Event.current.type != EventType.Repaint) { Label(rect.x, rect.y, rect.width, rect.height, text, style); return; }
        var size = style.fontSize;
        style.fontSize = FitSize(text, style, rect.width);
        try { GradientSlices(rect, text, style, elapsed); }
        finally { style.fontSize = size; }
    }

    private void GradientSlices(Rect rect, string text, GUIStyle style, double elapsed)
    {
        fxContent.text = text;
        var width = style.CalcSize(fxContent).x;
        var left = TextCenter(rect, new Vector2(width, 0), style.alignment).x - width / 2;
        const int slices = 32;
        var slice = width / slices;
        var shift = elapsed / 2.4 % 1 * 2;
        for (var index = 0; index < slices; index++)
        {
            var x = left + index * slice;
            var at = (float)(((index + 0.5) / slices / 3 + shift) % 1) * 4;
            var segment = Math.Min(3, (int)at);
            var color = Color.Lerp(TitleGradient[segment], TitleGradient[segment + 1], at - segment);
            // Taller than the title box: descenders (the "y" of Mythic) are not cut off.
            var pad = Mathf.Ceil(style.fontSize * 0.35f);
            GUI.BeginClip(new Rect(x, rect.y - pad, slice + 0.6f, rect.height + 2 * pad));
            Label(rect.x - x, pad, rect.width, rect.height, text, style, color);
            GUI.EndClip();
        }
    }

    // mythGlow: 0 0 0 1px pink + 0 0 34px pink .4  ↔  0 0 0 2px gold + 0 0 80px pink .8.
    private void MythicCardGlow(Rect card, float pulse)
    {
        if (Event.current.type != EventType.Repaint) return;
        EnsureFx();
        var old = GUI.backgroundColor;
        GUI.backgroundColor = new Color(Myth.r, Myth.g, Myth.b, 0.4f * (1 - pulse));
        glowNear.Draw(new Rect(card.x - NearGlow, card.y - NearGlow, card.width + 2 * NearGlow, card.height + 2 * NearGlow), false, false, false, false);
        GUI.backgroundColor = new Color(Myth.r, Myth.g, Myth.b, 0.8f * pulse);
        glowFar.Draw(new Rect(card.x - FarGlow, card.y - FarGlow, card.width + 2 * FarGlow, card.height + 2 * FarGlow), false, false, false, false);
        GUI.backgroundColor = old;
        var width = 1 + pulse;
        Outline(new Rect(card.x - width, card.y - width, card.width + 2 * width, card.height + 2 * width), Color.Lerp(Myth, MythGold, pulse), width);
    }

    // fxstar: gold ✦ with text-shadow 0 0 18px pink, 0 0 36px gold, spinning and growing in.
    private void DrawStar(Rect rect, GUIStyle style, double elapsed)
    {
        var (scale, angle, alpha) = UiMotion.StarIn(elapsed, Reduced);
        if (alpha <= 0 || scale <= 0.01f) return;
        var restore = Transform(rect.center, scale, angle);
        var old = GUI.color;
        GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);
        try
        {
            if (Event.current.type == EventType.Repaint)
            {
                EnsureFx();
                var size = style.fontSize;
                var halo = GUI.color;
                GUI.color = new Color(MythGold.r, MythGold.g, MythGold.b, 0.45f * halo.a);
                GUI.DrawTexture(new Rect(rect.center.x - size * 1.6f, rect.center.y - size * 1.6f, size * 3.2f, size * 3.2f), haloTexture);
                GUI.color = new Color(Myth.r, Myth.g, Myth.b, 0.6f * halo.a);
                GUI.DrawTexture(new Rect(rect.center.x - size * 0.95f, rect.center.y - size * 0.95f, size * 1.9f, size * 1.9f), haloTexture);
                GUI.color = halo;
            }
            Label(rect.x, rect.y, rect.width, rect.height, "✦", style, MythGold);
        }
        finally { GUI.color = old; GUI.matrix = restore; }
    }
}
