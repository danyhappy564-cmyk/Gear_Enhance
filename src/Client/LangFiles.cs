using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GearEnhance;

// 1.4.1 translations. The DLL carries the master table (Lang/strings.tsv: ko, en, ru, zh, where);
// BepInEx/plugins/GearEnhance/lang/<file>.tsv (ko, en, text) lets players fix or add a language
// without a rebuild: its filled rows win, an empty or missing row falls back to the DLL, then English.
internal static class LangFiles
{
    internal const string Folder = "lang";
    private static Dictionary<string, string>[] master;

    internal static string FileOf(UiLang language) => language == UiLang.Russian ? "ru.tsv" : language == UiLang.Chinese ? "zh.tsv" : null;

    // Rows for one language; `note` names a file that could not be read (null when fine).
    internal static Dictionary<string, string> Load(UiLang language, string pluginFolder, string customFile, out string note)
    {
        note = null;
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        if (language == UiLang.Korean || language == UiLang.English) return rows;
        if (language != UiLang.Custom)
            foreach (var row in Master()[language == UiLang.Russian ? 0 : 1]) rows[row.Key] = row.Value;
        var file = language == UiLang.Custom ? customFile : FileOf(language);
        if (string.IsNullOrWhiteSpace(file)) return rows;
        try
        {
            // Only a plain file name inside the lang folder.
            var path = Path.Combine(pluginFolder ?? "", Folder, Path.GetFileName(file.Trim()));
            if (!File.Exists(path))
            {
                if (language == UiLang.Custom) note = path + ": 없음/missing";
                return rows;
            }
            foreach (var row in Parse(File.ReadAllText(path, Encoding.UTF8), 2)) rows[row.Key] = row.Value;
        }
        catch (Exception ex) { note = file + ": " + ex.GetBaseException().Message; }
        return rows;
    }

    // The DLL's ru and zh columns, read once.
    private static Dictionary<string, string>[] Master()
    {
        if (master != null) return master;
        using (var stream = typeof(LangFiles).Assembly.GetManifestResourceStream("GearEnhance.Lang.strings.tsv"))
        using (var reader = new StreamReader(stream ?? throw new FileNotFoundException("GearEnhance.Lang.strings.tsv"), Encoding.UTF8))
        {
            var text = reader.ReadToEnd();
            return master = new[] { Parse(text, 2), Parse(text, 3) };
        }
    }

    // Tab-separated rows after the header; '#' lines are notes. Cell escapes: \n \t \r \\.
    // "-" or an empty cell means "show English".
    internal static Dictionary<string, string> Parse(string text, int column)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        var header = true;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            if (header) { header = false; continue; }
            var cells = line.Split('\t');
            if (cells.Length <= column) continue;
            var value = Unescape(cells[column]);
            if (value.Length == 0 || value == "-") continue;
            rows[Unescape(cells[0])] = value;
        }
        return rows;
    }

    internal static string Unescape(string cell)
    {
        if (cell.IndexOf('\\') < 0) return cell;
        var text = new StringBuilder(cell.Length);
        for (var index = 0; index < cell.Length; index++)
        {
            var c = cell[index];
            if (c != '\\' || index + 1 == cell.Length) { text.Append(c); continue; }
            var next = cell[++index];
            text.Append(next == 'n' ? '\n' : next == 't' ? '\t' : next == 'r' ? '\r' : next);
        }
        return text.ToString();
    }

    // Chinese, Japanese or another CJK custom file needs the Windows CJK font (GE Sans has Hangul only).
    internal static bool NeedsCjkFont(UiLang language, IReadOnlyDictionary<string, string> rows)
    {
        if (language == UiLang.Chinese) return true;
        if (language != UiLang.Custom) return false;
        foreach (var value in rows.Values)
            foreach (var c in value)
                if (c >= '぀' && c <= '鿿' && !(c >= '㄰' && c <= '㆏')) return true;
        return false;
    }
}
