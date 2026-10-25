"""목업 글꼴(Noto Sans KR · Rajdhani, 둘 다 SIL OFL)을 게임용으로 만듭니다.

- Noto Sans KR 가변 글꼴 → 400/500/700 고정 굵기, 한자를 뺀 한글·라틴·기호만 남김(크기 절약).
- Rajdhani(숫자·영문) → 없는 ₽ → ★ ◆ · 를 Noto에서 옮기고, 두 글꼴 모두 없는 ✦ ◈ 와 ₽ 를 직접 그림.
결과: src/Client/Fonts/*.ttf (DLL에 넣어 실행 때 풀어 씀). 같은 입력이면 같은 결과.

    python3 tools/make_fonts.py <원본 폴더: NotoSansKR[wght].ttf, Rajdhani-SemiBold.ttf, Rajdhani-Bold.ttf>
"""
import sys
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer
from fontTools import subset
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.pens.transformPen import TransformPen

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "src/Client/Fonts"
# Hangul syllables + jamo, Latin, Cyrillic (item names), punctuation, arrows, shapes, symbols, CJK punctuation, fullwidth.
RANGES = [(0x20, 0x7E), (0xA0, 0x24F), (0x370, 0x3FF), (0x400, 0x4FF), (0x1100, 0x11FF), (0x2000, 0x206F), (0x2070, 0x209F),
          (0x20A0, 0x20CF), (0x2100, 0x214F), (0x2150, 0x218F), (0x2190, 0x21FF), (0x2200, 0x22FF), (0x2460, 0x24FF),
          (0x2500, 0x257F), (0x25A0, 0x25FF), (0x2600, 0x26FF), (0x2700, 0x27BF), (0x3000, 0x303F), (0x3130, 0x318F),
          (0x3200, 0x32FF), (0xAC00, 0xD7A3), (0xFF00, 0xFFEF)]
EXTRA = "₽✦◈★◆→×·…—"


def unicodes():
    return [c for a, b in RANGES for c in range(a, b + 1)] + [ord(c) for c in EXTRA]


def add_glyph(font, name, code, draw, width):
    pen = TTGlyphPen(font.getGlyphSet())
    draw(pen)
    glyph = pen.glyph()
    font["glyf"][name] = glyph
    if name not in font.getGlyphOrder():
        font.setGlyphOrder(font.getGlyphOrder() + [name])
    glyph.recalcBounds(font["glyf"])
    font["hmtx"][name] = (width, glyph.xMin if hasattr(glyph, "xMin") else 0)
    # CJK fonts also carry vertical metrics; every glyph needs an entry there too.
    if "vmtx" in font:
        ascent = font["vhea"].ascent if "vhea" in font else font["head"].unitsPerEm
        font["vmtx"][name] = (font["head"].unitsPerEm, ascent - getattr(glyph, "yMax", 0))
    for table in font["cmap"].tables:
        if table.isUnicode():
            table.cmap[code] = name


def poly(pen, points):
    pen.moveTo(points[0])
    for p in points[1:]:
        pen.lineTo(p)
    pen.closePath()


def sparkle(pen, cx, cy, r, waist):
    # Four-point star ✦ (clockwise outer contour).
    poly(pen, [(cx, cy + r), (cx + waist, cy + waist), (cx + r, cy), (cx + waist, cy - waist),
               (cx, cy - r), (cx - waist, cy - waist), (cx - r, cy), (cx - waist, cy + waist)])


def diamond_ring(pen, cx, cy, r, t, inner):
    # ◈: diamond outline with a small solid diamond inside (holes run the other way).
    poly(pen, [(cx, cy + r), (cx + r, cy), (cx, cy - r), (cx - r, cy)])
    s = r - t * 1.41
    poly(pen, [(cx, cy + s), (cx - s, cy), (cx, cy - s), (cx + s, cy)])
    poly(pen, [(cx, cy + inner), (cx + inner, cy), (cx, cy - inner), (cx - inner, cy)])


def copy_glyph(target, source, code, scale=1.0, dy=0):
    src_name = source.getBestCmap()[code]
    glyph_set = source.getGlyphSet()

    def draw(pen):
        glyph_set[src_name].draw(TransformPen(pen, (scale, 0, 0, scale, 0, dy)))
    add_glyph(target, "uni%04X" % code, code, draw, int(source["hmtx"][src_name][0] * scale))


def rouble(font, base):
    # ₽ = the font's own P with a crossbar through the stem.
    p_name = font.getBestCmap()[ord("P")]
    glyph_set = font.getGlyphSet()
    width = font["hmtx"][p_name][0]
    p = font["glyf"][p_name]
    p.recalcBounds(font["glyf"])
    stem = p.xMin
    bar = max(50, int((p.yMax - p.yMin) * 0.09))
    y = int(p.yMin + (p.yMax - p.yMin) * 0.24)

    def draw(pen):
        glyph_set[p_name].draw(pen)
        poly(pen, [(stem - int(width * 0.12), y + bar), (stem + int(width * 0.55), y + bar), (stem + int(width * 0.55), y), (stem - int(width * 0.12), y)])
    add_glyph(font, "uni20BD", 0x20BD, draw, width)


def rename(font, family, style):
    for record in font["name"].names:
        if record.nameID in (1, 16):
            record.string = family
        elif record.nameID in (2, 17):
            record.string = style
        elif record.nameID == 4:
            record.string = f"{family} {style}"
        elif record.nameID == 6:
            record.string = f"{family}-{style}".replace(" ", "")


def main():
    source = Path(sys.argv[1])
    OUT.mkdir(parents=True, exist_ok=True)
    variable = TTFont(source / "NotoSansKR[wght].ttf")
    nanos = {}
    for weight, style in ((400, "Regular"), (700, "Bold")):
        font = instancer.instantiateVariableFont(TTFont(source / "NotoSansKR[wght].ttf"), {"wght": weight})
        options = subset.Options()
        options.layout_features = ["kern", "liga", "ccmp", "locl", "ljmo", "vjmo", "tjmo"]
        options.name_IDs = ["*"]
        options.notdef_outline = True
        sub = subset.Subsetter(options)
        sub.populate(unicodes=unicodes())
        sub.subset(font)
        size = font["head"].unitsPerEm
        add_glyph(font, "uni2726", 0x2726, lambda pen: sparkle(pen, 500, 380, 420, 90), 1000)
        add_glyph(font, "uni25C8", 0x25C8, lambda pen: diamond_ring(pen, 500, 380, 400, 70, 150), 1000)
        rouble(font, size)
        rename(font, "GE Sans", style)
        font.save(OUT / f"GESans-{style}.ttf")
        nanos[style] = font
    for style in ("SemiBold", "Bold"):
        font = TTFont(source / f"Rajdhani-{style}.ttf")
        noto = nanos["Bold" if style == "Bold" else "Regular"]
        # Rajdhani's em is 1000 like Noto; Noto shapes sit a little high next to its digits.
        for code in (0x2192, 0x2605, 0x25C6, 0x00B7):
            copy_glyph(font, noto, code, 0.92, -20)
        add_glyph(font, "uni2726", 0x2726, lambda pen: sparkle(pen, 380, 330, 360, 80), 760)
        add_glyph(font, "uni25C8", 0x25C8, lambda pen: diamond_ring(pen, 380, 330, 340, 60, 130), 760)
        rouble(font, 1000)
        rename(font, "GE Num", style)
        font.save(OUT / f"GENum-{style}.ttf")
    for path in sorted(OUT.glob("*.ttf")):
        print(path.name, path.stat().st_size)


if __name__ == "__main__":
    main()
