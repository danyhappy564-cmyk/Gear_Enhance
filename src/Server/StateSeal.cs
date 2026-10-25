using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace GearEnhance;

// 1.4.0: the enhancement records in the profile file carry a seal (HMAC of their content) and a
// vault (a compressed copy with its own seal). A level changed by hand in a text editor no longer
// matches the seal, so the next server start reads the vault back instead. A deterrent for text
// editing, not protection against someone rebuilding the mod: the key ships in the DLL.
public enum SealVerdict { Valid, Legacy, Restored, Reset }

public static class StateSeal
{
    public const string SealKey = "Seal";
    public const string VaultKey = "Vault";
    // Never change: every sealed profile would read as tampered.
    private static readonly byte[] Key = SHA256.HashData(Encoding.UTF8.GetBytes("rf.gearenhance.records.seal.v1:7d1c9e42-b58a-4f3e-a0c6-2e91f5b8d374"));
    // Where the last verified vault of each profile is kept (outside the profile file), so a
    // file whose seal and vault were both deleted is not accepted as an old unsealed one.
    // Null turns it off (tests that do not exercise it).
    public static string? StoreDirectory { get; set; } = DefaultStore();

    private static string? DefaultStore()
    {
        try
        {
            var folder = Path.GetDirectoryName(typeof(StateSeal).Assembly.Location);
            return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "data", "seals");
        }
        catch (Exception) { return null; }
    }

    // Order and number spelling do not matter (dictionaries, sets, 5 vs 5.0); values do.
    public static string Canonical(JsonNode? node)
    {
        var text = new StringBuilder();
        Write(node, text, 0);
        return text.ToString();
    }

    private static void Write(JsonNode? node, StringBuilder text, int depth)
    {
        if (depth > 64) throw new InvalidOperationException("Records nest too deep.");
        switch (node)
        {
            case null: text.Append("null"); return;
            case JsonObject map:
                text.Append('{');
                var first = true;
                foreach (var (name, value) in map.Where(p => p.Value != null && !(depth == 0 && (p.Key == SealKey || p.Key == VaultKey)))
                    .OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (!first) text.Append(',');
                    first = false;
                    text.Append(JsonSerializer.Serialize(name)).Append(':');
                    Write(value, text, depth + 1);
                }
                text.Append('}');
                return;
            case JsonArray list:
                var items = list.Select(item => { var part = new StringBuilder(); Write(item, part, depth + 1); return part.ToString(); })
                    .OrderBy(item => item, StringComparer.Ordinal);
                text.Append('[').Append(string.Join(",", items)).Append(']');
                return;
            case JsonValue value:
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String: text.Append(JsonSerializer.Serialize(value.GetValue<string>())); return;
                    case JsonValueKind.Number:
                        var number = double.Parse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                        text.Append(number.ToString("R", CultureInfo.InvariantCulture)); return;
                    case JsonValueKind.True: text.Append("true"); return;
                    case JsonValueKind.False: text.Append("false"); return;
                    default: text.Append("null"); return;
                }
        }
    }

    public static string Sign(JsonNode? node) => Convert.ToHexString(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(Canonical(node))));

    private static bool Matches(JsonNode? node, string? seal)
    {
        if (string.IsNullOrEmpty(seal)) return false;
        try { return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(node)), Encoding.ASCII.GetBytes(seal.ToUpperInvariant())); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException) { return false; }
    }

    // Seal line + the records JSON, gzip, base64. It verifies on its own.
    public static string Pack(string seal, string json)
    {
        using var buffer = new MemoryStream();
        using (var zip = new GZipStream(buffer, CompressionLevel.Optimal, true))
        {
            var bytes = Encoding.UTF8.GetBytes(seal + "\n" + json);
            zip.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(buffer.ToArray());
    }

    // The records JSON of a vault whose seal matches its content, or null.
    public static string? Unpack(string? vault)
    {
        if (string.IsNullOrEmpty(vault)) return null;
        try
        {
            using var zip = new GZipStream(new MemoryStream(Convert.FromBase64String(vault)), CompressionMode.Decompress);
            using var reader = new StreamReader(zip, Encoding.UTF8);
            // Bounded: a vault is a few hundred KB at most.
            var buffer = new char[8 * 1024 * 1024];
            var length = reader.ReadBlock(buffer, 0, buffer.Length);
            if (length == buffer.Length) return null;
            var text = new string(buffer, 0, length);
            var split = text.IndexOf('\n');
            if (split <= 0) return null;
            var json = text[(split + 1)..];
            return Matches(JsonNode.Parse(json), text[..split]) ? json : null;
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or JsonException or IOException or InvalidOperationException) { return null; }
    }

    // The records to load from a profile file's JSON and how they were found.
    public static (string Json, SealVerdict Verdict) Open(string json, string profileId, string? store)
    {
        JsonObject? node;
        try { node = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return (json, SealVerdict.Legacy); }
        // Not an object: let the normal reader report the broken format.
        if (node == null) return (json, SealVerdict.Legacy);
        var seal = (node[SealKey] as JsonValue)?.GetValueKind() == JsonValueKind.String ? node[SealKey]!.GetValue<string>() : null;
        var vault = (node[VaultKey] as JsonValue)?.GetValueKind() == JsonValueKind.String ? node[VaultKey]!.GetValue<string>() : null;
        node.Remove(SealKey);
        node.Remove(VaultKey);
        if (seal != null && Matches(node, seal))
        {
            Remember(store, profileId, vault);
            return (node.ToJsonString(), SealVerdict.Valid);
        }
        if (Unpack(vault) is { } kept)
        {
            Remember(store, profileId, vault);
            return (kept, SealVerdict.Restored);
        }
        // Files from before 1.4.0 have neither. A profile this server has seen sealed must not
        // turn back into an unsealed one by deleting both lines.
        var stored = Stored(store, profileId);
        if (seal == null && vault == null && stored == null) return (json, SealVerdict.Legacy);
        return Unpack(stored) is { } previous ? (previous, SealVerdict.Restored) : ("{}", SealVerdict.Reset);
    }

    private static string? StorePath(string? store, string profileId)
    {
        if (store == null || profileId.Length == 0 || profileId.Any(c => !char.IsLetterOrDigit(c))) return null;
        return Path.Combine(store, profileId + ".vault");
    }

    private static string? Stored(string? store, string profileId)
    {
        try { return StorePath(store, profileId) is { } path && File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    // Runs when a profile's records load (server start), not on every save.
    private static void Remember(string? store, string profileId, string? vault)
    {
        if (vault == null || StorePath(store, profileId) is not { } path) return;
        try
        {
            if (File.Exists(path) && File.ReadAllText(path) == vault) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, vault);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static string Notice(SealVerdict verdict) => verdict switch
    {
        SealVerdict.Restored => "Gear Enhance: 프로필 파일에서 강화 기록이 직접 수정된 흔적을 찾아 마지막 정상 기록으로 되돌렸습니다. "
            + "The enhancement records in the profile file were edited by hand; the last valid records were restored.",
        SealVerdict.Reset => "Gear Enhance: 프로필 파일의 강화 기록이 직접 수정됐고 되돌릴 정상 기록이 없어 강화 기록을 비웠습니다. "
            + "The enhancement records in the profile file were edited by hand and no valid copy was left; they were cleared.",
        _ => ""
    };
}

// Writes EnhanceState with its seal and vault, under the service lock so a save never sees the
// records halfway through a change. Reads ignore both (State() checks them first).
public sealed class SealedStateConverter : JsonConverter<EnhanceState>
{
    private static readonly System.Reflection.PropertyInfo[] Fields = typeof(EnhanceRecords).GetProperties()
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0).ToArray();

    public override EnhanceState? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (JsonNode.Parse(ref reader) is not JsonObject node) throw new JsonException("Enhancement records must be an object.");
        node.Remove(StateSeal.SealKey);
        node.Remove(StateSeal.VaultKey);
        var records = node.Deserialize<EnhanceRecords>(options);
        if (records == null) return null;
        var state = new EnhanceState();
        Copy(records, state);
        return state;
    }

    public override void Write(Utf8JsonWriter writer, EnhanceState value, JsonSerializerOptions options)
    {
        lock (EnhanceService.Gate)
        {
            JsonObject? node = null;
            try
            {
                node = JsonSerializer.SerializeToNode<EnhanceRecords>(value, options) as JsonObject;
                if (node != null)
                {
                    var seal = StateSeal.Sign(node);
                    var json = node.ToJsonString();
                    node[StateSeal.SealKey] = seal;
                    node[StateSeal.VaultKey] = StateSeal.Pack(seal, json);
                }
            }
            // A save must never fail because of the seal; the next load reads it as unsealed.
            catch (Exception ex) when (ex is not OutOfMemoryException) { node = null; }
            if (node != null) node.WriteTo(writer, options);
            else JsonSerializer.Serialize<EnhanceRecords>(writer, value, options);
        }
    }

    internal static void Copy(EnhanceRecords from, EnhanceRecords to)
    {
        foreach (var field in Fields) field.SetValue(to, field.GetValue(from));
    }
}
