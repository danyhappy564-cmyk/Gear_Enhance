"""화면·F12·서버 오류 문구를 모아 번역표(src/Client/Lang/strings.tsv)를 만들고 빠진 번역을 찾습니다.

한국어 원문이 열쇠입니다. 코드의 Loc.L("한", "영") · Loc.F($"한 {x}", $"영 {x}") · Loc.Argument/Invalid
와 F12 사전(DeveloperOptions.cs의 ["한"] = "영")을 읽어, 표에 없는 줄은 끝에 붙이고(러·중 칸은 빈칸),
있는 줄의 러·중 번역은 그대로 둡니다. 보간 문자열은 C# FormattableString.Format과 같은 틀({0}, {1:N0})로 적습니다.

    python3 tools/lang.py            # 표 갱신 + 빠진 번역·코드에서 못 읽은 곳 보고
    python3 tools/lang.py --check    # 갱신 없이 검사만 (빠진 번역이 있으면 종료 코드 1)
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TABLE = ROOT / "src/Client/Lang/strings.tsv"
# Files players edit (shipped in BepInEx/plugins/GearEnhance/lang): ko, en, text.
PLAYER_FILES = {"ru.tsv": (2, "Русский"), "zh.tsv": (3, "简体中文"), "template.tsv": (None, "new language")}
PLAYER_NOTE = """# Gear Enhance translation file / 번역 파일. UTF-8, tab-separated: ko, en, text.
# Fill the third column ("text"). Keep {0}, {1:N0} ... and the leading/trailing spaces of a cell.
# \\n is a line break. An empty cell or "-" shows English. Lines starting with # are notes.
# Your own language: copy template.tsv to a new name (for example de.tsv), fill it, then pick
# F12 → Gear Enhance → Language = Custom and type the file name in "Custom language file".
# Files shipped with the mod (ru.tsv, zh.tsv) are overwritten by updates; keep your edits under another name.
"""
HEADER = ["ko", "en", "ru", "zh", "where"]
CALLS = re.compile(r"(?<![\w.])(?:Loc\.)?(L|F|Argument|Invalid|ArgumentF|InvalidF)\(")
DICT = re.compile(r'\[\s*(?=")')


def tsv_escape(text):
    return text.replace("\\", "\\\\").replace("\t", "\\t").replace("\r", "\\r").replace("\n", "\\n")


def tsv_unescape(text):
    return re.sub(r"\\(.)", lambda m: {"n": "\n", "t": "\t", "r": "\r"}.get(m.group(1), m.group(1)), text)


def read_regular(src, i):
    """C# "..." at src[i] == '"' → (value, end index after closing quote)."""
    out, i = [], i + 1
    while src[i] != '"':
        if src[i] == "\\":
            c = src[i + 1]
            if c == "u":
                out.append(chr(int(src[i + 2:i + 6], 16))); i += 6; continue
            out.append({"n": "\n", "t": "\t", "r": "\r", "0": "\0"}.get(c, c)); i += 2; continue
        out.append(src[i]); i += 1
    return "".join(out), i + 1


def skip_expr(src, i, stops):
    """Skips a C# expression from i until a top-level char in stops; returns that index."""
    depth = 0
    while True:
        c = src[i]
        if c == '"' or (c == "$" and src[i + 1] == '"'):
            i = read_interpolated(src, i)[1] if c == "$" else read_regular(src, i)[1]; continue
        if c == "@" and src[i + 1] == '"':
            i += 2
            while not (src[i] == '"' and src[i + 1] != '"'):
                i += 2 if src[i] == '"' else 1
            i += 1; continue
        if c == "'":
            i += 4 if src[i + 1] == "\\" else 3; continue
        if depth == 0 and c in stops:
            return i
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        i += 1


def read_interpolated(src, i):
    """C# $"..." at src[i] == '$' → (FormattableString.Format text, end index)."""
    out, i, hole = [], i + 2, 0
    while src[i] != '"':
        c = src[i]
        if c == "\\":
            v, _ = read_regular('"' + src[i:i + 6] + '"', 0) if src[i + 1] == "u" else ({"n": "\n", "t": "\t", "r": "\r"}.get(src[i + 1], src[i + 1]), 0)
            out.append(v); i += 6 if src[i + 1] == "u" else 2; continue
        if c in "{}" and src[i + 1] == c:
            out.append(c * 2); i += 2; continue
        if c == "{":
            end = skip_expr(src, i + 1, ",:}")
            spec = ""
            if src[end] == ",":
                close = skip_expr(src, end + 1, ":}")
                spec += "," + src[end + 1:close].strip(); end = close
            if src[end] == ":":
                close = src.index("}", end)
                spec += src[end:close]; end = close
            out.append("{%d%s}" % (hole, spec)); hole += 1; i = end + 1; continue
        out.append(c); i += 1
    return "".join(out), i + 1


def literal(src, i):
    """A string argument at i (after spaces) → (text or None if not a single literal, end of argument)."""
    while src[i].isspace():
        i += 1
    end = skip_expr(src, i, ",)")
    text = None
    if src[i] == '"':
        value, after = read_regular(src, i)
        if src[after:end].strip() == "": text = value
    elif src[i] == "$" and src[i + 1] == '"':
        value, after = read_interpolated(src, i)
        if src[after:end].strip() == "": text = value
    return text, end


def strip_comments(src):
    # Keeps offsets: comments become spaces (strings are respected).
    out, i = list(src), 0
    while i < len(src):
        c = src[i]
        if c == '"' or (c == "$" and src[i + 1:i + 2] == '"'):
            i = read_interpolated(src, i)[1] if c == "$" else read_regular(src, i)[1]; continue
        if c == "'":
            i += 4 if src[i + 1] == "\\" else 3; continue
        if src.startswith("//", i):
            end = src.find("\n", i)
            end = len(src) if end < 0 else end
            for k in range(i, end): out[k] = " "
            i = end; continue
        if src.startswith("/*", i):
            end = src.index("*/", i) + 2
            for k in range(i, end):
                if out[k] != "\n": out[k] = " "
            i = end; continue
        i += 1
    return "".join(out)


def collect():
    rows, dynamic, interpolated = [], [], []
    for path in sorted((ROOT / "src").rglob("*.cs")):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        rel = path.relative_to(ROOT).as_posix()
        src = strip_comments(path.read_text(encoding="utf-8"))
        line_of = lambda k: rel + ":" + str(src.count("\n", 0, k) + 1)
        for m in CALLS.finditer(src):
            if m.group(1) == "L" and not m.group(0).startswith("Loc.") and path.name != "Loc.cs":
                continue
            # Declarations (string L(string korean, ...)) are not calls.
            if re.search(r"(string|Exception|static)\s+$", src[max(0, m.start() - 30):m.start()]):
                continue
            start = m.end()
            while src[start].isspace(): start += 1
            if m.group(1) in ("L", "Argument", "Invalid") and src.startswith('$"', start):
                interpolated.append(line_of(m.start()))
            ko, end = literal(src, m.end())
            if src[end] != ",":
                continue
            en, _ = literal(src, end + 1)
            if ko is None or en is None:
                if path.name != "Loc.cs" and not (path.name == "Mythic.cs" and "Capped" in src[max(0, m.start() - 400):m.start()])                         and "Names.TryGetValue" not in src[max(0, m.start() - 120):m.start()]:
                    dynamic.append(line_of(m.start()))
                continue
            if re.search("[가-힣]", ko):
                rows.append((ko, en, line_of(m.start())))
        # Fixed (Korean, English) pairs passed on to Loc.L later: dictionary tuples and Mythic.Capped.
        for m in re.finditer(r'(?:\]\s*=\s*\(|Capped\([^;]*?,\s*)(?=")', src):
            ko, end = read_regular(src, m.end())
            rest = re.match(r'\s*,\s*', src[end:])
            if rest and src[end + rest.end()] == '"' and re.search("[가-힣]", ko):
                en, _ = read_regular(src, end + rest.end())
                rows.append((ko, en, line_of(m.start())))
        if path.name == "DeveloperOptions.cs":
            for m in re.finditer(r'\[\s*"', src):
                ko, end = read_regular(src, m.end() - 1)
                rest = re.match(r'\s*\]\s*=\s*', src[end:])
                if rest and src[end + rest.end()] == '"':
                    en, _ = read_regular(src, end + rest.end())
                    if re.search("[가-힣]", ko): rows.append((ko, en, line_of(m.start())))
    return rows, dynamic, interpolated


def load():
    if not TABLE.exists():
        return {}
    table = {}
    for line in TABLE.read_text(encoding="utf-8").splitlines()[1:]:
        if not line.strip():
            continue
        cells = (line.split("\t") + [""] * 5)[:5]
        table[tsv_unescape(cells[0])] = [tsv_unescape(c) for c in cells]
    return table


def main():
    check = "--check" in sys.argv
    rows, dynamic, interpolated = collect()
    table = load()
    order = list(table)
    conflicts = {}
    for ko, en, where in rows:
        if ko not in table:
            table[ko] = [ko, en, "", "", where]; order.append(ko)
        elif table[ko][1] != en:
            conflicts.setdefault(ko, {table[ko][1]}).add(en)
    used = {ko for ko, _, _ in rows}
    missing = [k for k in order if k in used and not (table[k][2] and table[k][3])]
    # "-" in a cell: English on purpose (developer-only F12 text).
    unused = [k for k in order if k not in used and not table[k][4].startswith("manual")]
    if not check:
        TABLE.parent.mkdir(parents=True, exist_ok=True)
        lines = ["\t".join(HEADER)] + ["\t".join(tsv_escape(c) for c in table[k]) for k in order]
        TABLE.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
        for name, (column, title) in PLAYER_FILES.items():
            body = [PLAYER_NOTE + "# " + title, "ko\ten\ttext"]
            for k in order:
                if k not in used:
                    continue
                cell = "" if column is None or table[k][column] == "-" else table[k][column]
                body.append("\t".join(tsv_escape(c) for c in (table[k][0], table[k][1], cell)))
            (TABLE.parent / name).write_text("\n".join(body) + "\n", encoding="utf-8-sig", newline="\n")
    print(f"문구 {len(used)}개 · 표 {len(order)}줄 · 번역 빠짐 {len(missing)} · 코드에 없음 {len(unused)} · 영문 다름 {len(conflicts)}")
    for k in missing[:20]: print("  빠짐:", tsv_escape(k))
    for k in unused[:20]: print("  코드에 없음:", tsv_escape(k))
    for site in dynamic: print("  원문이 상수가 아님(직접 확인):", site)
    for site in interpolated: print("  보간 문자열은 F/ArgumentF/InvalidF로:", site)
    return 1 if check and (missing or interpolated) else 0


if __name__ == "__main__":
    sys.exit(main())
