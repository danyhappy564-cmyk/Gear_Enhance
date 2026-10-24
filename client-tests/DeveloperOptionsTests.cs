using GearEnhance;
using Xunit;

namespace GearEnhanceClientTests;

public sealed class DeveloperOptionsTests
{
    [Fact]
    public void Balance_entries_are_hidden_by_default_and_only_visibility_changes_when_developer_mode_is_toggled()
    {
        var policy = new DeveloperOptions();
        var chance = policy.Describe("2. 확률", "항상 성공 (100%)");
        var cost = policy.Describe("3. 비용", "기준 가격 대비 비용 (%)");
        Assert.False(chance.Browsable); Assert.False(cost.Browsable);
        Assert.Equal("99. 개발자 옵션", chance.Category);
        policy.SetVisible(true);
        Assert.True(chance.Browsable); Assert.True(cost.Browsable);
        Assert.True(policy.Describe("10. 스타 강화 진화", "진화 성공률 (%)").Browsable);
        policy.SetVisible(false);
        Assert.False(chance.Browsable); Assert.False(cost.Browsable);
        Assert.Contains("항상 성공", chance.DispName);
    }
}

public sealed class DeveloperLockTests
{
    // The real password is never in the repository; only its hash is.
    [Fact]
    public void Only_the_matching_password_unlocks_and_the_hash_is_salted()
    {
        var hash = GearEnhance.DeveloperLock.HashOf("test-password");
        Assert.Equal(64, GearEnhance.DeveloperLock.PasswordHash.Length);
        Assert.True(GearEnhance.DeveloperLock.Matches("test-password", hash));
        Assert.True(GearEnhance.DeveloperLock.Matches("  test-password ", hash));
        Assert.False(GearEnhance.DeveloperLock.Matches("Test-password", hash));
        Assert.False(GearEnhance.DeveloperLock.Matches("", hash));
        Assert.False(GearEnhance.DeveloperLock.Matches(null, hash));
        Assert.False(GearEnhance.DeveloperLock.Matches("test-password"));
        Assert.NotEqual(hash, System.BitConverter.ToString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("test-password"))).Replace("-", "").ToLowerInvariant());
    }

    [Fact]
    public void Locked_settings_use_the_default_and_keep_the_saved_value()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gear-lock-" + System.Guid.NewGuid().ToString("N") + ".cfg");
        try
        {
            var file = new BepInEx.Configuration.ConfigFile(path, false);
            var entry = file.Bind("2. 확률", "항상 성공 (100%)", false);
            entry.Value = true;
            var unlocked = false;
            var setting = new GearEnhance.DevSetting<bool>(entry, () => unlocked);
            Assert.False(setting.Value);
            Assert.True(entry.Value);
            unlocked = true;
            Assert.True(setting.Value);
        }
        finally { System.IO.File.Delete(path); }
    }
}
