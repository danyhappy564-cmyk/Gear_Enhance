using System;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Configuration;

namespace GearEnhance;

// F12 developer options lock (1.2.0). Only a salted SHA-256 of the password ships;
// the password itself is never in the source. A deterrent for casual F12 use, not
// protection against someone rebuilding the mod.
internal static class DeveloperLock
{
    private const string Salt = "rf.gearenhance.developer:";
    internal const string PasswordHash = "f64ead0944b6c0cc9684a11869713215fe3f2425d75609c16e8c554896f7787a";

    internal static string HashOf(string password)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(Salt + (password ?? "").Trim()));
        var text = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes) text.Append(value.ToString("x2"));
        return text.ToString();
    }

    internal static bool Matches(string password, string hash = PasswordHash) =>
        !string.IsNullOrWhiteSpace(password) && string.Equals(HashOf(password), hash, StringComparison.Ordinal);

    // Save password (user 2026-10-26): with it, what is enhanced while the developer options are open
    // stays. PBKDF2-SHA256 over 100,000 rounds of a random 16-character password: the shipped hash
    // cannot practically be turned back into it.
    private static readonly byte[] SaveSalt = { 0x4f, 0x3b, 0x77, 0x2b, 0x46, 0x2a, 0x76, 0x23, 0x7e, 0xd8, 0x34, 0x98, 0x68, 0xbd, 0x44, 0x53 };
    internal const string SaveHash = "ac927d8fc8424dd7e86ac42e258946c8d760ed48ad1c46444ab5902ac0978980";

    internal static bool MatchesSave(string password, string hash = SaveHash)
    {
        if (string.IsNullOrWhiteSpace(password)) return false;
        using var kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password.Trim()), SaveSalt, 100000, HashAlgorithmName.SHA256);
        var bytes = kdf.GetBytes(32);
        var text = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes) text.Append(value.ToString("x2"));
        return string.Equals(text.ToString(), hash, StringComparison.Ordinal);
    }
}

// A developer setting: its saved value while unlocked, its shipped default while locked.
// Locking never rewrites the saved value, so unlocking again restores it.
internal sealed class DevSetting<T>
{
    private readonly Func<bool> unlocked;
    internal ConfigEntry<T> Entry { get; }
    internal DevSetting(ConfigEntry<T> entry, Func<bool> unlocked) { Entry = entry; this.unlocked = unlocked; }
    internal T Value => unlocked() ? Entry.Value : (T)Entry.DefaultValue;
}
