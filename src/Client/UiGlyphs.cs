using System;

namespace GearEnhance;

// Baked once into small textures. No GUI matrix changes or geometry in OnGUI.
internal static class UiGlyphs
{
    internal const int Size = 48;
    internal static byte[] Rasterize(EnhancePath path)
    {
        var pixels = new byte[Size * Size];
        switch (path)
        {
            case EnhancePath.Recoil:
                for (var arrow = 0; arrow < 3; arrow++)
                {
                    var x = 12 + arrow * 12;
                    var top = arrow == 1 ? 5 : 12;
                    Line(pixels, x, 41, x, top, 2.5);
                    Line(pixels, x, top, x - 4, top + 7, 2);
                    Line(pixels, x, top, x + 4, top + 7, 2);
                }
                break;
            case EnhancePath.Ergonomics:
                for (var finger = 0; finger < 4; finger++)
                    Line(pixels, 15 + finger * 6, 32, 12 + finger * 7, finger == 0 || finger == 3 ? 12 : 5, 4);
                Line(pixels, 18, 37, 5, 23, 5);
                for (var x = 14; x <= 34; x++) Line(pixels, x, 25, x, 41, 1);
                break;
            case EnhancePath.Precision:
                for (var segment = 0; segment < 32; segment++)
                {
                    var a = segment * Math.PI / 16;
                    var b = (segment + 1) * Math.PI / 16;
                    Line(pixels, 24 + Math.Cos(a) * 15, 24 + Math.Sin(a) * 15,
                        24 + Math.Cos(b) * 15, 24 + Math.Sin(b) * 15, 1.6);
                }
                Line(pixels, 2, 24, 16, 24, 2); Line(pixels, 32, 24, 46, 24, 2);
                Line(pixels, 24, 2, 24, 16, 2); Line(pixels, 24, 32, 24, 46, 2);
                Line(pixels, 24, 23, 24, 25, 3);
                break;
            case EnhancePath.ArmorDurability:
                var points = new[] { 7, 10, 24, 4, 41, 10, 38, 31, 24, 44, 10, 31, 7, 10 };
                for (var edge = 2; edge < points.Length; edge += 2)
                    Line(pixels, points[edge - 2], points[edge - 1], points[edge], points[edge + 1], 2);
                Line(pixels, 16, 24, 32, 24, 2); Line(pixels, 24, 16, 24, 32, 2);
                break;
            case EnhancePath.Mobility:
                for (var chevron = 0; chevron < 2; chevron++)
                {
                    var x = 10 + chevron * 15;
                    Line(pixels, x, 9, x + 12, 24, 3); Line(pixels, x + 12, 24, x, 39, 3);
                }
                break;
            default: throw new ArgumentOutOfRangeException(nameof(path));
        }
        return pixels;
    }

    private static void Line(byte[] pixels, double x0, double y0, double x1, double y1, double width)
    {
        var radius = width / 2;
        var minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - radius - 1));
        var maxX = Math.Min(Size - 1, (int)Math.Ceiling(Math.Max(x0, x1) + radius + 1));
        var minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - radius - 1));
        var maxY = Math.Min(Size - 1, (int)Math.Ceiling(Math.Max(y0, y1) + radius + 1));
        var dx = x1 - x0; var dy = y1 - y0;
        var lengthSquared = dx * dx + dy * dy;
        // Every loop is clipped to the 48x48 icon, even with outlying geometry.
        for (var y = minY; y <= maxY; y++) for (var x = minX; x <= maxX; x++)
        {
            var t = lengthSquared == 0 ? 0 : Math.Max(0, Math.Min(1, ((x - x0) * dx + (y - y0) * dy) / lengthSquared));
            var distance = Math.Sqrt(Math.Pow(x - x0 - t * dx, 2) + Math.Pow(y - y0 - t * dy, 2));
            var alpha = (byte)(255 * Math.Max(0, Math.Min(1, radius + 0.5 - distance)));
            var index = y * Size + x;
            if (alpha > pixels[index]) pixels[index] = alpha;
        }
    }
}
